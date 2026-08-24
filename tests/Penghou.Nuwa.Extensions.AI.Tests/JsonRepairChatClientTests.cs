using FluentAssertions;
using Microsoft.Extensions.AI;
using Penghou.Nuwa.Extensions.AI;
using System.Text.Json;

namespace Penghou.Nuwa.Extensions.AI.Tests;

public sealed class JsonRepairChatClientTests
{
    private static readonly JsonElement _applyPatchSchema = JsonDocument.Parse(
        """
        {"type":"object","properties":{"files":{"type":"array","items":{"type":"string"}}}}
        """).RootElement.Clone();

    private static ChatOptions ToolsWithApplyPatch =>
        new()
        {
            Tools =
            [
                AIFunctionFactory.CreateDeclaration(
                    "apply_patch",
                    "Applies a list of file patches.",
                    _applyPatchSchema,
                    returnJsonSchema: null)
            ]
        };

    private static ChatOptions StructuredFilesResponse =>
        new()
        {
            ResponseFormat = new ChatResponseFormatJson(_applyPatchSchema)
        };

    [Fact]
    public async Task GetResponseAsync_RepairsWrongShapedFunctionArguments()
    {
        var fcc = new FunctionCallContent(
            "call_1",
            "apply_patch",
            new Dictionary<string, object?>
            {
                ["files"] = "[\"a.txt\",\"b.txt\"]"
            });
        var inner = new FakeChatClient(
            new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [fcc])));

        var client = inner.UseJsonRepair();
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "apply a patch")],
            ToolsWithApplyPatch,
            TestContext.Current.CancellationToken);

        var repaired = response.Messages[0].Contents[0]
            .Should().BeOfType<FunctionCallContent>().Which;
        var files = ((JsonElement)repaired.Arguments!["files"]!).ValueKind;
        files.Should().Be(JsonValueKind.Array);
        repaired.Exception.Should().BeNull();
    }

    [Fact]
    public async Task GetResponseAsync_AlreadyValidArguments_AreUntouched()
    {
        var original = new Dictionary<string, object?>
        {
            ["files"] = new[] { "a.txt" }
        };
        var fcc = new FunctionCallContent(
            "call_1",
            "apply_patch",
            original);
        var inner = new FakeChatClient(
            new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [fcc])));

        var client = inner.UseJsonRepair();
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "apply a patch")],
            ToolsWithApplyPatch,
            TestContext.Current.CancellationToken);

        var untouched = response.Messages[0].Contents[0]
            .Should().BeOfType<FunctionCallContent>().Which;
        untouched.Arguments.Should().BeSameAs(original);
    }

    [Fact]
    public async Task GetResponseAsync_RepairsMalformedTextContent()
    {
        var inner = new FakeChatClient(
            new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [new TextContent("{\"files\":[\"a.txt\",\"b.txt\" ")])));

        var client = inner.UseJsonRepair();
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "list files")],
            StructuredFilesResponse,
            TestContext.Current.CancellationToken);

        var text = response.Messages[0].Contents[0]
            .Should().BeOfType<TextContent>().Which;
        using var parsed = JsonDocument.Parse(text.Text);
        parsed.RootElement.GetProperty("files").GetArrayLength()
            .Should().Be(2);
    }

    [Fact]
    public async Task GetResponseAsync_ProseText_IsUntouched()
    {
        const string prose = "Hello! How can I help you today?";
        var inner = new FakeChatClient(
            new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [new TextContent(prose)])));

        var client = inner.UseJsonRepair();
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")],
            cancellationToken: TestContext.Current.CancellationToken);

        response.Messages[0].Contents[0]
            .Should().BeOfType<TextContent>()
            .Which.Text.Should().Be(prose);
    }

    [Fact]
    public async Task GetResponseAsync_JsonLookingTextWithoutStructuredRequest_IsUntouched()
    {
        const string malformed = "{\"files\":[\"a.txt\" ";
        var inner = new FakeChatClient(
            new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [new TextContent(malformed)])));

        var response = await inner.UseJsonRepair().GetResponseAsync(
            [new ChatMessage(ChatRole.User, "show malformed JSON")],
            cancellationToken: TestContext.Current.CancellationToken);

        response.Messages[0].Contents[0]
            .Should().BeOfType<TextContent>()
            .Which.Text.Should().Be(malformed);
    }

    [Fact]
    public async Task GetResponseAsync_ScalarProseUnderJsonResponseFormat_IsNotRewritten()
    {
        // Salvage quoting can turn free-form assistant prose into valid JSON
        // scalars ("True" -> true). Without a schema contract that mutation is
        // data loss and must be suppressed.
        const string prose = "True";
        var inner = new FakeChatClient(
            new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [new TextContent(prose)])));

        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json
        };

        var response = await inner.UseJsonRepair().GetResponseAsync(
            [new ChatMessage(ChatRole.User, "is it on?")],
            options,
            TestContext.Current.CancellationToken);

        response.Messages[0].Contents[0]
            .Should().BeOfType<TextContent>()
            .Which.Text.Should().Be(prose);
    }

    [Fact]
    public async Task GetResponseAsync_RepairsMarkdownFenceForStructuredRequest()
    {
        var inner = new FakeChatClient(
            new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [new TextContent("```json\n{\"files\":[\"a.txt\"]}\n```")])));

        var response = await inner.UseJsonRepair().GetResponseAsync(
            [new ChatMessage(ChatRole.User, "list files")],
            StructuredFilesResponse,
            TestContext.Current.CancellationToken);

        var repaired = response.Messages[0].Contents[0]
            .Should().BeOfType<TextContent>().Which.Text;
        using var document = JsonDocument.Parse(repaired);
        document.RootElement.GetProperty("files").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetResponseAsync_NotifiesCallerAboutRepair()
    {
        JsonRepairNotification? notification = null;
        var inner = new FakeChatClient(
            new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [new TextContent("{\"files\":[\"a.txt\" ")])));
        var client = inner.UseJsonRepair(
            new JsonRepairChatClientOptions
            {
                RepairCompleted = (value, _) =>
                {
                    notification = value;
                    return ValueTask.CompletedTask;
                }
            });

        await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "list files")],
            StructuredFilesResponse,
            TestContext.Current.CancellationToken);

        notification.Should().NotBeNull();
        notification!.Target.Should().Be("response-text");
        notification.WasRepaired.Should().BeTrue();
        notification.ShapeStatus.Should().Be(JsonRepairShapeStatus.Matched);
        notification.TolerantRecovery.Should().NotBeNull();
    }

    [Fact]
    public async Task GetResponseAsync_RepairsUnparsedArgumentsFromRawRepresentation()
    {
        var fcc = new FunctionCallContent(
            "call_1",
            "apply_patch",
            arguments: null)
        {
            Exception = new InvalidOperationException(
                "Error parsing function call arguments."),
            RawRepresentation = """{"files":["a.txt" """
        };
        var inner = new FakeChatClient(
            new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [fcc])));

        var client = inner.UseJsonRepair();
        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "apply a patch")],
            ToolsWithApplyPatch,
            TestContext.Current.CancellationToken);

        var repaired = response.Messages[0].Contents[0]
            .Should().BeOfType<FunctionCallContent>().Which;
        repaired.Arguments.Should().NotBeNull();
        repaired.Exception.Should().BeNull();
        var files = ((JsonElement)repaired.Arguments!["files"]!).ValueKind;
        files.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RepairsAccumulatedFunctionCall()
    {
        var fcc = new FunctionCallContent(
            "call_1",
            "apply_patch",
            new Dictionary<string, object?>
            {
                ["files"] = "[\"a.txt\",\"b.txt\"]"
            });
        var inner = new FakeChatClient(
            [
                new ChatResponseUpdate
                {
                    Contents = [fcc]
                }
            ]);

        var client = inner.UseJsonRepair();
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "apply a patch")],
                           ToolsWithApplyPatch,
                           TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        var repaired = updates[0].Contents[0]
            .Should().BeOfType<FunctionCallContent>().Which;
        ((JsonElement)repaired.Arguments!["files"]!).ValueKind
            .Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RepairsRawFunctionArguments()
    {
        var fcc = new FunctionCallContent("call_1", "apply_patch", arguments: null)
        {
            Exception = new InvalidOperationException("parse failed"),
            RawRepresentation = "{\"files\":[\"a.txt\" "
        };
        var inner = new FakeChatClient(
            [new ChatResponseUpdate { Contents = [fcc] }]);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in inner.UseJsonRepair().GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "apply")],
                           ToolsWithApplyPatch,
                           TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        var repaired = updates[0].Contents[0]
            .Should().BeOfType<FunctionCallContent>().Which;
        repaired.Arguments.Should().NotBeNull();
        repaired.Exception.Should().BeNull();
    }

    [Fact]
    public async Task GetStreamingResponseAsync_LeavesTextUpdatesUntouched()
    {
        const string fragment = "{\"files\":[\"a.txt\",\"b.txt\" ";
        var inner = new FakeChatClient(
            [
                new ChatResponseUpdate
                {
                    Contents = [new TextContent(fragment)]
                }
            ]);

        var client = inner.UseJsonRepair();
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "list files")],
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        updates[0].Contents[0]
            .Should().BeOfType<TextContent>()
            .Which.Text.Should().Be(fragment);
    }

    [Fact]
    public async Task UseJsonRepair_OnChatClientBuilder_RepairsResponses()
    {
        var fcc = new FunctionCallContent(
            "call_1",
            "apply_patch",
            new Dictionary<string, object?>
            {
                ["files"] = "[\"a.txt\",\"b.txt\"]"
            });
        var inner = new FakeChatClient(
            new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [fcc])));

        var client = new ChatClientBuilder(inner)
            .UseJsonRepair()
            .Build();

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "apply a patch")],
            ToolsWithApplyPatch,
            TestContext.Current.CancellationToken);

        var repaired = response.Messages[0].Contents[0]
            .Should().BeOfType<FunctionCallContent>().Which;
        ((JsonElement)repaired.Arguments!["files"]!).ValueKind
            .Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task UseJsonRepair_WithOptions_RepairsResponses()
    {
        var fcc = new FunctionCallContent(
            "call_1",
            "apply_patch",
            new Dictionary<string, object?>
            {
                ["files"] = "[\"a.txt\",\"b.txt\"]"
            });
        var inner = new FakeChatClient(
            new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [fcc])));

        var client = inner.UseJsonRepair(
            new JsonRepairChatClientOptions
            {
                Configure = options =>
                    options.DisableSalvageFallback()
            });

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "apply a patch")],
            ToolsWithApplyPatch,
            TestContext.Current.CancellationToken);

        var repaired = response.Messages[0].Contents[0]
            .Should().BeOfType<FunctionCallContent>().Which;
        ((JsonElement)repaired.Arguments!["files"]!).ValueKind
            .Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public void GetService_PassesThroughToInnerClient()
    {
        var inner = new FakeChatClient(
            new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [new TextContent("hello")])));

        var client = inner.UseJsonRepair();

        client.GetService(typeof(JsonRepairChatClient))
            .Should().BeSameAs(client);
        client.GetService(typeof(IFakeService))
            .Should().BeSameAs(inner.Service);
    }

    [Fact]
    public void Dispose_DisposesInnerClient()
    {
        var inner = new FakeChatClient(
            new ChatResponse(
                new ChatMessage(
                    ChatRole.Assistant,
                    [new TextContent("hello")])));

        using (inner.UseJsonRepair())
        {
        }

        inner.Disposed.Should().BeTrue();
    }

    private interface IFakeService;

    private sealed class FakeChatClient : IChatClient
    {
        private readonly ChatResponse? _response;
        private readonly IReadOnlyList<ChatResponseUpdate>? _updates;

        public FakeChatClient(ChatResponse response)
        {
            _response = response;
        }

        public FakeChatClient(IReadOnlyList<ChatResponseUpdate> updates)
        {
            _updates = updates;
        }

        public object? Service { get; } = new object();

        public bool Disposed { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_response!);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            EnumerateAsync();

        private async IAsyncEnumerable<ChatResponseUpdate> EnumerateAsync()
        {
            if (_updates is null)
            {
                yield break;
            }

            foreach (var update in _updates)
            {
                yield return update;
            }

            await Task.CompletedTask;
        }

        public object? GetService(
            Type serviceType,
            object? serviceKey = null) =>
            Service;

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
