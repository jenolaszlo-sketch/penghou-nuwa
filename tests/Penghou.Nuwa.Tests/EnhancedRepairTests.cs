using System.Text.Json;
using FluentAssertions;
using Penghou.Nuwa.Strategies;

namespace Penghou.Nuwa.Tests;

/// <summary>
/// Coverage for the 0.6 capability batch: truncation salvage, payload
/// extraction, Unicode delimiter normalization, schema-guided coercions,
/// repair confidence, and the streaming API.
/// </summary>
public sealed class EnhancedRepairTests
{
    // ---------- #1 truncation-aware partial salvage ----------

    [Fact]
    public async Task TruncatedObject_DropsIncompleteProperty_KeepsCompleteOnes()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = await pipeline.RepairAsync(
            """{"count": 42, "note": """,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.Root!["count"]!.GetValue<long>().Should().Be(42);
        result.Root!.AsObject().ContainsKey("note").Should().BeFalse();
        result.TolerantRecovery!.Corrections
            .Should().Contain(c => c.Contains("dropped incomplete property 'note'"));
    }

    [Fact]
    public async Task TruncatedArray_KeepsCompletedElements()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = await pipeline.RepairAsync(
            """{"items": [1, 2""",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Root!["items"]!.AsArray().Should().HaveCount(2);
    }

    [Fact]
    public async Task TruncationSalvage_CanBeDisabled()
    {
        var pipeline = JsonRepairPipeline.Create(options =>
        {
            options.AllowTruncationSalvage = false;
            options.DisableSalvageFallback();
        });

        using var result = await pipeline.RepairAsync(
            """{"count": 42, "note": """,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
    }

    // ---------- #3 payload extraction ----------

    [Fact]
    public async Task ProseWrappedPayload_IsExtracted()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = await pipeline.RepairAsync(
            """Here is the JSON you requested: {"city": "Budapest"} hope that helps!""",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Root!["city"]!.GetValue<string>().Should().Be("Budapest");
    }

    [Fact]
    public async Task XmlWrappedPayload_IsExtracted()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = await pipeline.RepairAsync(
            """<answer>{"ok": true}</answer>""",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Root!["ok"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task CdataWrappedPayload_IsExtracted()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = await pipeline.RepairAsync(
            """<result><![CDATA[{"n": 7}]]></result>""",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Root!["n"]!.GetValue<int>().Should().Be(7);
    }

    [Fact]
    public async Task ConcatenatedObjects_FirstValueWins()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = await pipeline.RepairAsync(
            """{"first": 1}{"second": 2}""",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Root!["first"].Should().NotBeNull();
        result.Root!.AsObject().Should().ContainSingle();
    }

    [Fact]
    public async Task FenceStrategy_AcceptsArbitraryPayloadTagsAsync()
    {
        var strategy = new MarkdownJsonFenceRepairStrategy();

        var attempt = await strategy.RepairAsync(
            "```tool_call\n{\"a\": 1}\n```",
            cancellationToken: TestContext.Current.CancellationToken);

        attempt.Outcome.Should().Be(RepairOutcome.Repaired);
        attempt.Repaired.Should().Be("""{"a": 1}""");
    }

    [Fact]
    public async Task FenceStrategy_RejectsCodeLanguageTagsAsync()
    {
        var strategy = new MarkdownJsonFenceRepairStrategy();

        const string code = "```csharp\r\nConsole.WriteLine(\"Hello\");\r\n```";
        var attempt = await strategy.RepairAsync(
            code,
            cancellationToken: TestContext.Current.CancellationToken);

        attempt.Outcome.Should().Be(RepairOutcome.NotApplicable);
    }

    // ---------- #4 Unicode delimiters ----------

    [Fact]
    public async Task CurlyQuotesAndFullwidthPunctuation_AreNormalized()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = await pipeline.RepairAsync(
            "{\u201Ca\u201D\uFF1Atrue\uFF0C\u201Cb\u201D\uFF1Anull}",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.Root!["a"]!.GetValue<bool>().Should().BeTrue();
        result.Root!["b"].Should().BeNull();
    }

    [Fact]
    public async Task UnicodeStrategy_StripsBomAndZeroWidthCharactersAsync()
    {
        var strategy = new UnicodeDelimiterNormalizationStrategy();

        var attempt = await strategy.RepairAsync(
            "\uFEFF{}\u200B",
            cancellationToken: TestContext.Current.CancellationToken);

        attempt.Outcome.Should().Be(RepairOutcome.Repaired);
        attempt.Repaired.Should().Be("{}");
    }

    // ---------- #5 schema-guided coercions ----------

    private static JsonSchemaExpectation Expectation(string schemaJson) =>
        JsonSchemaExpectation.FromSchemaJson(schemaJson)!;

    [Fact]
    public async Task Coercions_QuotedNumber_BecomesNumber()
    {
        var pipeline = JsonRepairPipeline.Create(options =>
            options.EnableSchemaCoercions());

        using var result = await pipeline.RepairAsync(
            """{"count": "42"}""",
            Expectation(
                """{"type":"object","properties":{"count":{"type":"number"}},"required":["count"]}"""),
            cancellationToken: TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue();
        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Matched);
        result.Root!["count"]!.GetValue<long>().Should().Be(42);
    }

    [Fact]
    public async Task Coercions_QuotedBoolean_BecomesBoolean()
    {
        var pipeline = JsonRepairPipeline.Create(options =>
            options.EnableSchemaCoercions());

        using var result = await pipeline.RepairAsync(
            """{"flag": "True"}""",
            Expectation(
                """{"type":"object","properties":{"flag":{"type":"boolean"}}}"""),
            TestContext.Current.CancellationToken);

        result.Root!["flag"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Coercions_Scalar_WrappedIntoArray()
    {
        var pipeline = JsonRepairPipeline.Create(options =>
            options.EnableSchemaCoercions());

        using var result = await pipeline.RepairAsync(
            """{"tags": "red"}""",
            Expectation(
                """{"type":"object","properties":{"tags":{"type":"array","items":{"type":"string"}}}}"""),
            TestContext.Current.CancellationToken);

        result.Root!["tags"]!.AsArray().Should().ContainSingle("red");
    }

    [Fact]
    public async Task Coercions_TypoedEnum_MatchedByEditDistance()
    {
        var pipeline = JsonRepairPipeline.Create(options =>
            options.EnableSchemaCoercions());

        using var result = await pipeline.RepairAsync(
            """{"status": "Actve"}""",
            Expectation(
                """{"type":"object","properties":{"status":{"type":"string","enum":["Active","Inactive"]}}}"""),
            TestContext.Current.CancellationToken);

        result.Root!["status"]!.GetValue<string>().Should().Be("Active");
    }

    [Fact]
    public async Task Coercions_UnknownProperties_PrunedForStrictSchemas()
    {
        var pipeline = JsonRepairPipeline.Create(options =>
            options.EnableSchemaCoercions());

        using var result = await pipeline.RepairAsync(
            """{"known": 1, "invented": true}""",
            Expectation(
                """{"type":"object","properties":{"known":{"type":"number"}},"required":["known"],"additionalProperties":false}"""),
            TestContext.Current.CancellationToken);

        result.Root!.AsObject().Should().ContainSingle();
    }

    [Fact]
    public async Task Coercions_Off_ByDefault()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = await pipeline.RepairAsync(
            """{"count": "42"}""",
            Expectation(
                """{"type":"object","properties":{"count":{"type":"number"}}}"""),
            TestContext.Current.CancellationToken);

        result.Root!["count"]!.GetValue<string>().Should().Be("42");
    }

    // ---------- #6 confidence ----------

    [Fact]
    public async Task Confidence_PerfectInputScoresOne()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = await pipeline.RepairAsync(
            """{"a": 1}""",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Confidence.Should().Be(1);
        result.IsConfident(0.9).Should().BeTrue();
    }

    [Fact]
    public async Task Confidence_FailureScoresZero()
    {
        var pipeline = JsonRepairPipeline.Create(options =>
        {
            options.AllowTruncationSalvage = false;
            options.DisableSalvageFallback();
        });

        using var result = await pipeline.RepairAsync(
            "not json at all",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Confidence.Should().Be(0);
    }

    [Fact]
    public async Task Confidence_RepairedInputIsReducedButPositive()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = await pipeline.RepairAsync(
            """{"count": 42, "note": """,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Confidence.Should().BeGreaterThan(0.5).And.BeLessThan(1);
    }

    // ---------- #2 streaming ----------

    [Fact]
    public async Task StreamRepair_EmitsDeltasThenCompletedResult()
    {
        var pipeline = JsonRepairPipeline.Create();
        var chunks = new[]
        {
            """Here is the payload: {"na""",
            """me": "svc-1", "repl""",
            """icas": [1, 2]} done."""
        };

        var events = new List<JsonRepairStreamEvent>();
        await foreach (var streamEvent in pipeline.RepairStreamAsync(
                           ToChunks(chunks),
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            events.Add(streamEvent);
        }

        var deltas = events.OfType<JsonRepairStreamDelta>().ToArray();
        deltas.Should().NotBeEmpty();

        // Deltas are contiguous, verbatim slices of the accumulated input.
        var offset = 0;
        foreach (var delta in deltas)
        {
            delta.Offset.Should().Be(offset);
            offset += delta.Text.Length;
        }

        var completed = events
            .OfType<JsonRepairStreamCompleted>()
            .Should().ContainSingle().Which;
        using var result = completed.Result;
        result.Succeeded.Should().BeTrue();
        result.Root!["name"]!.GetValue<string>().Should().Be("svc-1");
        result.Root!["replicas"]!.AsArray().Should().HaveCount(2);
    }

    [Fact]
    public void StablePrefixScanner_NeverEmitsInsideOpenString()
    {
        const string buffer = """{"a": "open stri""";

        var stable = JsonRepairPipeline.StablePrefixScanner.FindStableLength(
            buffer,
            0);

        buffer[stable].Should().NotBe('e');
        buffer[..stable].Should().NotEndWith("open stri");
    }

    private static async IAsyncEnumerable<string> ToChunks(
        IReadOnlyList<string> chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }
}
