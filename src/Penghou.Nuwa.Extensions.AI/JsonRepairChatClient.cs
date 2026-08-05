using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Penghou.Nuwa.Extensions.AI;

/// <summary>
/// A <see cref="IChatClient"/> middleware that drops Penghou.Nuwa JSON repair
/// into any Microsoft.Extensions.AI pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Tool-call arguments carried by <see cref="FunctionCallContent"/> are
/// re-serialized and schema-repaired against the matching tool's JSON schema,
/// then re-parsed, so a model that emits valid-but-wrong-shaped arguments
/// (double-serialized fields, optional nulls, wrong property kinds) no longer
/// breaks your tool invocation.
/// </para>
/// <para>
/// Assistant <see cref="TextContent"/> that looks like JSON is repaired too,
/// covering structured-output responses returned as text. When a JSON
/// response format with a schema is configured, that schema guides repair;
/// otherwise repair is schema-less best-effort.
/// </para>
/// <para>
/// Wire the client through <c>UseJsonRepair()</c> on <see cref="ChatClientBuilder"/>
/// (or on any <see cref="IChatClient"/>). For example, on top of
/// <c>openAi.AsIChatClient()</c> or <c>anthropic.AsIChatClient(modelId)</c>.
/// </para>
/// </remarks>
public class JsonRepairChatClient : DelegatingChatClient
{
    private readonly JsonRepairChatClientOptions _options;
    private readonly JsonRepairPipeline _pipeline;

    /// <summary>
    /// Creates a client that wraps <paramref name="innerClient"/> and repairs
    /// JSON before returning responses.
    /// </summary>
    /// <param name="innerClient">The client to wrap.</param>
    /// <param name="configure">
    /// Configures the underlying Nuwa repair pipeline. When null, the default
    /// strategy set is used.
    /// </param>
    public JsonRepairChatClient(
        IChatClient innerClient,
        Action<JsonRepairOptions>? configure = null)
        : this(innerClient, new JsonRepairChatClientOptions
        {
            Configure = configure
        })
    {
    }

    /// <summary>
    /// Creates a client that wraps <paramref name="innerClient"/> and repairs
    /// JSON according to <paramref name="options"/>.
    /// </summary>
    /// <param name="innerClient">The client to wrap.</param>
    /// <param name="options">Repair configuration.</param>
    public JsonRepairChatClient(
        IChatClient innerClient,
        JsonRepairChatClientOptions options)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _pipeline = JsonRepairPipeline.Create(
            options.Configure);
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(
                messages,
                options,
                cancellationToken)
            .ConfigureAwait(false);

        await RepairResponseAsync(
                response,
                options,
                cancellationToken)
            .ConfigureAwait(false);

        return response;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Streaming text arrives fragmented, so response text is not repaired on
    /// this path. Completed function-call arguments (the accumulated update a
    /// connector emits at the end of a tool call) are repaired.
    /// </remarks>
    public override IAsyncEnumerable<ChatResponseUpdate>
        GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
    {
        var inner = base.GetStreamingResponseAsync(
            messages,
            options,
            cancellationToken);

        return StreamAndRepairAsync(
            inner,
            options,
            cancellationToken);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> StreamAndRepairAsync(
        IAsyncEnumerable<ChatResponseUpdate> inner,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in inner
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (_options.RepairFunctionCallArguments &&
                update.Contents.Count > 0)
            {
                foreach (var content in update.Contents)
                {
                    if (content is FunctionCallContent fcc &&
                        !fcc.InformationalOnly &&
                        fcc.Arguments is not null)
                    {
                        await RepairFunctionCallAsync(
                                fcc,
                                options,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }

            yield return update;
        }
    }

    private async Task RepairResponseAsync(
        ChatResponse response,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        if (response.Messages.Count == 0)
        {
            return;
        }

        foreach (var message in response.Messages)
        {
            if (message.Contents.Count == 0)
            {
                continue;
            }

            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent fcc
                        when _options.RepairFunctionCallArguments:
                        await RepairFunctionCallAsync(
                                fcc,
                                options,
                                cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    case TextContent text
                        when _options.RepairResponseText:
                        await RepairTextAsync(
                                text,
                                options,
                                cancellationToken)
                            .ConfigureAwait(false);
                        break;
                }
            }
        }
    }

    private async ValueTask RepairFunctionCallAsync(
        FunctionCallContent fcc,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        if (fcc.InformationalOnly)
        {
            return;
        }

        string? json = null;

        if (fcc.Arguments is not null)
        {
            // The connector parsed the arguments; re-serialize so schema-
            // guided node repair can fix valid-but-wrong-shaped payloads.
            json = JsonSerializer.Serialize(
                fcc.Arguments);
        }
        else if (fcc.RawRepresentation is string raw)
        {
            // The connector could not parse the arguments, but the provider
            // preserved the raw arguments text; attempt full recovery.
            json = raw;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var expectation =
            _options.FunctionCallExpectationResolver?.Invoke(fcc) ??
            ResolveFunctionCallExpectation(
                options,
                fcc.Name);

        using var result = await _pipeline.RepairAsync(
                json,
                expectation,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded ||
            !result.WasRepaired ||
            result.RepairedText is not { } repairedText)
        {
            return;
        }

        try
        {
            var reparsed = JsonSerializer.Deserialize<
                Dictionary<string, object?>>(
                repairedText);

            if (reparsed is not null)
            {
                fcc.Arguments = reparsed;
                fcc.Exception = null;
            }
        }
        catch (JsonException)
        {
            // Keep the original arguments; the pipeline result is best-effort.
        }
    }

    private async ValueTask RepairTextAsync(
        TextContent text,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text.Text))
        {
            return;
        }

        var trimmed = text.Text.TrimStart();

        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
        {
            return;
        }

        var expectation =
            _options.TextExpectationResolver?.Invoke(options) ??
            ResolveResponseFormatExpectation(options);

        using var result = await _pipeline.RepairAsync(
                text.Text,
                expectation,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded &&
            result.WasRepaired &&
            result.RepairedText is { } repaired)
        {
            text.Text = repaired;
        }
    }

    private static JsonSchemaExpectation? ResolveFunctionCallExpectation(
        ChatOptions? options,
        string? functionName)
    {
        if (options?.Tools is null ||
            string.IsNullOrWhiteSpace(functionName))
        {
            return null;
        }

        foreach (var tool in options.Tools)
        {
            if (tool is AIFunctionDeclaration declaration &&
                string.Equals(
                    declaration.Name,
                    functionName,
                    StringComparison.Ordinal) &&
                declaration.JsonSchema.ValueKind ==
                JsonValueKind.Object)
            {
                return JsonSchemaExpectation.FromSchemaJson(
                    declaration.JsonSchema.GetRawText());
            }
        }

        return null;
    }

    private static JsonSchemaExpectation? ResolveResponseFormatExpectation(
        ChatOptions? options)
    {
        if (options?.ResponseFormat is ChatResponseFormatJson json &&
            json.Schema is { } schema &&
            schema.ValueKind == JsonValueKind.Object)
        {
            return JsonSchemaExpectation.FromSchemaJson(
                schema.GetRawText());
        }

        return null;
    }
}
