using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Penghou.Nuwa;
using Penghou.Nuwa.Extensions.AI;
using Penghou.Nuwa.Strategies;
using System.Text.Json;

// Exercises the AOT-safe usage path end to end: an explicitly constructed
// pipeline (no type-based activation, no FromType reflection), the
// source-generated string serialization used by the text strategies, and the
// JsonNode-based tool-call argument round-trip in the AI middleware.

var pipeline = new JsonRepairPipeline(
    textRepairs:
    [
        new MarkdownJsonFenceRepairStrategy(),
        new UnicodeDelimiterNormalizationStrategy(),
        new XmlWrappedExtractionStrategy(),
        new ConcatenatedJsonExtractionStrategy(),
        new ProseWrapperExtractionStrategy(),
        new PseudoCSharpVerbatimStringRepairStrategy(),
        new PseudoJavaScriptTemplateStringRepairStrategy(),
    ],
    salvageRepairs: [new SalvageRepairStrategy()],
    nodeRepairs:
    [
        new SchemaGuidedOptionalNullRemovalStrategy(),
        new SchemaGuidedJsonStringExpansionStrategy(),
        new SchemaGuidedScalarToStringCoercionStrategy(),
    ],
    logger: NullLogger<JsonRepairPipeline>.Instance,
    limits: JsonRepairLimits.Default,
    allowTruncationSalvage: true);

var failures = 0;

// 1. Core schema-guided node repair (FromSchemaJson is AOT-safe).
var expectation = JsonSchemaExpectation.FromSchemaJson(
    """{"type":"object","required":["files"],"properties":{"files":{"type":"array","items":{"type":"string"}}}}""");

using (var result = await pipeline.RepairAsync("{\"files\":\"[1, 2]\"}", expectation))
{
    failures += Check(result.Succeeded, "core repair succeeded");
    failures += Check(result.IsRepairAccepted, "core repair accepted");
    failures += Check(result.RepairedText == "{\"files\":[\"1\",\"2\"]}", "core repair output");
}

// 2. Text strategy that serializes a string through the source-generated context.
using (var result = await pipeline.RepairAsync("{\"s\": `hello`}"))
{
    failures += Check(result.RepairedText == "{\"s\":\"hello\"}", "template literal output");
}

// 3. AI middleware tool-call argument round-trip through JsonNode.
var fcc = new FunctionCallContent(
    "call_1",
    "apply_patch",
    new Dictionary<string, object?> { ["files"] = "[\"a.txt\",\"b.txt\"]" });
var inner = new FakeChatClient(new ChatResponse(
    new ChatMessage(ChatRole.Assistant, [fcc])));
var client = inner.UseJsonRepair(
    pipeline,
    new JsonRepairChatClientOptions
    {
        FunctionCallExpectationResolver = _ => expectation,
    });

var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "patch")]);
var repairedFcc = (FunctionCallContent)response.Messages[0].Contents[0];
failures += Check(
    repairedFcc.Arguments?["files"] is JsonElement { ValueKind: JsonValueKind.Array },
    "middleware argument round-trip");

if (failures > 0)
{
    Console.WriteLine($"AOT-SMOKE-FAILED ({failures})");
    return 1;
}

Console.WriteLine("AOT-SMOKE-OK");
return 0;

static int Check(bool condition, string label)
{
    Console.WriteLine($"{(condition ? "ok  " : "FAIL")} {label}");
    return condition ? 0 : 1;
}

internal sealed class FakeChatClient : IChatClient
{
    private readonly ChatResponse _response;

    public FakeChatClient(ChatResponse response) => _response = response;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_response);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
