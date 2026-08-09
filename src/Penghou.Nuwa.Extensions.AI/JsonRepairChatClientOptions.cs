using Microsoft.Extensions.AI;

namespace Penghou.Nuwa.Extensions.AI;

/// <summary>
/// Configuration for <see cref="JsonRepairChatClient"/>.
/// </summary>
public sealed class JsonRepairChatClientOptions
{
    /// <summary>
    /// Configures the underlying Nuwa repair pipeline (strategy lists, etc).
    /// When null, the default <see cref="JsonRepairOptions"/> are used.
    /// </summary>
    public Action<JsonRepairOptions>? Configure { get; set; }

    /// <summary>
    /// Whether tool-call arguments carried by <see cref="FunctionCallContent"/>
    /// are re-serialized, schema-repaired, and re-parsed before they reach the
    /// caller. Defaults to <see langword="true"/>.
    /// </summary>
    public bool RepairFunctionCallArguments { get; set; } = true;

    /// <summary>
    /// Whether assistant <see cref="TextContent"/> that looks like JSON
    /// (structured output returned as text) is repaired. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool RepairResponseText { get; set; } = true;

    /// <summary>
    /// Whether JSON-looking assistant text should be repaired when the request
    /// did not declare a JSON response format and no expectation resolver
    /// supplied a schema. Defaults to false to avoid rewriting ordinary chat.
    /// </summary>
    public bool RepairJsonLookingTextWithoutResponseFormat { get; set; }

    /// <summary>
    /// Receives a compact audit after each attempted repair. Payload text is
    /// deliberately excluded.
    /// </summary>
    public Func<JsonRepairNotification, CancellationToken, ValueTask>?
        RepairCompleted
    { get; set; }

    /// <summary>
    /// Resolves the schema expectation used to repair a specific function call.
    /// When null, the matching <see cref="AIFunctionDeclaration"/> from
    /// <c>ChatOptions.Tools</c> (by name) supplies the schema.
    /// </summary>
    public Func<FunctionCallContent, JsonSchemaExpectation?>?
        FunctionCallExpectationResolver
    { get; set; }

    /// <summary>
    /// Resolves the schema expectation used to repair response text.
    /// When null, the JSON schema from <see cref="ChatResponseFormatJson"/>
    /// (if set on <c>ChatOptions.ResponseFormat</c>) is used; otherwise repair
    /// falls back to schema-less best-effort recovery.
    /// </summary>
    public Func<ChatOptions?, JsonSchemaExpectation?>?
        TextExpectationResolver
    { get; set; }
}
