using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Penghou.Nuwa.Strategies;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Tests;

public sealed class JsonRepairPipelineTests
{
    [Fact]
    public void RepairAsync_AppliesStrategiesInInjectedOrderAndReportsAttempts()
    {
        var applied = new List<string>();
        ITextRepair[] strategies =
        [
            new RecordingStrategy(
                "first",
                "broken",
                "still broken",
                applied),
            new RecordingStrategy(
                "second",
                "still broken",
                """{"ok":true}""",
                applied)
        ];

        var pipeline = new JsonRepairPipeline(
            strategies,
            [],
            [],
            NullLogger<JsonRepairPipeline>.Instance);

        using var result = Repair(pipeline, "broken");

        result.Document.Should().NotBeNull();
        result.WasRepaired.Should().BeTrue();
        result.TextRepairs.Should().Contain(
            report =>
                report.Name == "first" &&
                report.Status == StrategyStatus.Succeeded);
        result.TextRepairs.Should().Contain(
            report =>
                report.Name == "second" &&
                report.Status == StrategyStatus.Succeeded);
        applied.Should().Equal("first", "second");
    }

    [Fact]
    public void RepairAsync_ReportsLaterStrategiesAsSkippedAfterSuccess()
    {
        var applied = new List<string>();
        ITextRepair[] strategies =
        [
            new RecordingStrategy(
                "winner",
                "broken",
                """{"ok":true}""",
                applied),
            new RecordingStrategy(
                "never-reached",
                "broken",
                """{"nope":true}""",
                applied)
        ];

        var pipeline = new JsonRepairPipeline(
            strategies,
            [],
            [],
            NullLogger<JsonRepairPipeline>.Instance);

        using var result = Repair(pipeline, "broken");

        result.Succeeded.Should().BeTrue();
        result.TextRepairs.Should().Contain(
            report =>
                report.Name == "winner" &&
                report.Status == StrategyStatus.Succeeded);
        result.TextRepairs.Should().Contain(
            report =>
                report.Name == "never-reached" &&
                report.Status == StrategyStatus.Skipped);
        applied.Should().Equal("winner");
    }

    [Fact]
    public void RepairAsync_UsesSalvagePhaseWhenRecoveryFails()
    {
        var pipeline = new JsonRepairPipeline(
            [],
            [new SalvageRepairStrategy()],
            [],
            NullLogger<JsonRepairPipeline>.Instance);

        using var result = Repair(pipeline, "{name: None}");

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.TextRepairs.Should().Contain(
            report =>
                report.Name == "salvage" &&
                report.Status == StrategyStatus.Succeeded);
    }

    [Fact]
    public void RepairAsync_StrategyException_IsReportedAndPipelineContinues()
    {
        var applied = new List<string>();
        ITextRepair[] strategies =
        [
            new ThrowingStrategy(),
            new RecordingStrategy(
                "second",
                "still broken",
                """{"ok":true}""",
                applied)
        ];

        var pipeline = new JsonRepairPipeline(
            strategies,
            [],
            [],
            NullLogger<JsonRepairPipeline>.Instance);

        using var result = Repair(pipeline, "still broken");

        result.Succeeded.Should().BeTrue();
        result.TextRepairs.Should().Contain(
            report =>
                report.Name == "throws" &&
                report.Status == StrategyStatus.Failed &&
                report.Note != null);
        result.TextRepairs.Should().Contain(
            report =>
                report.Name == "second" &&
                report.Status == StrategyStatus.Succeeded);
        applied.Should().Equal("second");
    }

    [Fact]
    public void RepairAsync_SalvageSuccessMarksRemainingSalvageSkipped()
    {
        var pipeline = new JsonRepairPipeline(
            [],
            [new SalvageRepairStrategy(), new SalvageRepairStrategy()],
            [],
            NullLogger<JsonRepairPipeline>.Instance);

        using var result = Repair(pipeline, "{name: None}");

        result.Succeeded.Should().BeTrue();
        result.TextRepairs.Where(
            report => report.Name == "salvage")
            .Count(report => report.Status == StrategyStatus.Succeeded)
            .Should().Be(1);
        result.TextRepairs.Where(
            report => report.Name == "salvage")
            .Count(report => report.Status == StrategyStatus.Skipped)
            .Should().Be(1);
    }

    [Fact]
    public void RepairAsync_CarriesStrategyNote()
    {
        ITextRepair[] strategies = [new NotingStrategy()];

        var pipeline = new JsonRepairPipeline(
            strategies,
            [],
            [],
            NullLogger<JsonRepairPipeline>.Instance);

        using var result = Repair(pipeline, "anything");

        result.TextRepairs.Should().Contain(
            report =>
                report.Name == "noting" &&
                report.Status == StrategyStatus.NotApplicable &&
                report.Note == "declined");
    }

    [Fact]
    public void RepairAsync_ValidInput_ReportsAllStrategiesSkipped()
    {
        var applied = new List<string>();
        ITextRepair[] strategies =
        [
            new RecordingStrategy(
                "first",
                "never",
                "never",
                applied)
        ];

        var pipeline = new JsonRepairPipeline(
            strategies,
            [new SalvageRepairStrategy()],
            [],
            NullLogger<JsonRepairPipeline>.Instance);

        using var result = Repair(pipeline, """{"ok":true}""");

        result.Succeeded.Should().BeTrue();
        result.TextRepairs.Should().Contain(
            report =>
                report.Name == "first" &&
                report.Status == StrategyStatus.Skipped);
        result.TextRepairs.Should().Contain(
            report =>
                report.Name == "salvage" &&
                report.Status == StrategyStatus.Skipped);
        applied.Should().BeEmpty();
    }

    [Fact]
    public void RepairAsync_LogsWinnerAndElapsedTime()
    {
        var logger = new CapturingLogger();
        var pipeline = new JsonRepairPipeline(
            [],
            [new SalvageRepairStrategy()],
            [],
            logger);

        using var result = Repair(pipeline, "{name: None}");

        result.Succeeded.Should().BeTrue();
        logger.Messages.Should().Contain(
            entry =>
                entry.Level == LogLevel.Warning &&
                entry.Message.Contains("salvage") &&
                entry.Message.Contains("ms"));
    }

    [Fact]
    public void RepairAsync_RecoveryOnlyRepair_LogsTolerantRecoveryAsWinner()
    {
        var logger = new CapturingLogger();
        var pipeline = new JsonRepairPipeline(
            [],
            [],
            [],
            logger);

        using var result = Repair(pipeline, """{"value":[1,2""");

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.SucceededBy.Should().BeNull();
        logger.Messages.Should().Contain(
            entry =>
                entry.Level == LogLevel.Warning &&
                entry.Message.Contains(
                    "Winner: tolerant-recovery"));
    }

    [Fact]
    public async Task RepairAsync_PropagatesCancellationFromStrategy()
    {
        using var cts = new CancellationTokenSource();
        var pipeline = new JsonRepairPipeline(
            [new CancelingStrategy(cts)],
            [],
            [],
            NullLogger<JsonRepairPipeline>.Instance);

        Func<Task> act = async () =>
        {
            using var result = await pipeline.RepairAsync("broken", cancellationToken: cts.Token);
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void RepairAsync_RejectsInputPastConfiguredLimit()
    {
        var pipeline = new JsonRepairPipeline(
            [],
            [],
            [],
            NullLogger<JsonRepairPipeline>.Instance,
            new JsonRepairLimits { MaxInputLength = 4 });

        var act = () => Repair(pipeline, "12345");

        act.Should().Throw<JsonRepairLimitException>();
    }

    [Fact]
    public void RepairAsync_AppliesOutputLimitToFailedRepairText()
    {
        var pipeline = new JsonRepairPipeline(
            [new RecordingStrategy("expand", "broken", new string('x', 20), [])],
            [],
            [],
            NullLogger<JsonRepairPipeline>.Instance,
            new JsonRepairLimits
            {
                MaxInputLength = 100,
                MaxOutputLength = 10
            });

        var act = () => Repair(pipeline, "broken");

        act.Should().Throw<JsonRepairLimitException>()
            .WithMessage("*output length 20*");
    }

    [Fact]
    public void RepairAsync_RejectsRecoveryPastConfiguredDepth()
    {
        var pipeline = new JsonRepairPipeline(
            [],
            [],
            [],
            NullLogger<JsonRepairPipeline>.Instance,
            new JsonRepairLimits { MaxDepth = 3 });

        var act = () => Repair(pipeline, "[[[[1");

        act.Should().Throw<JsonRepairLimitException>();
    }

    [Fact]
    public void RepairAsync_AppliesCorrectionBudgetToNodeRepairs()
    {
        var pipeline = JsonRepairPipeline.Create(options =>
        {
            options.Limits = options.Limits with { MaxCorrections = 1 };
            options.EnableSchemaCoercions();
        });
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """{"type":"object","properties":{},"additionalProperties":false}""")!;

        var act = () => Repair(
            pipeline,
            """{"first":1,"second":2}""",
            expectation);

        act.Should().Throw<JsonRepairLimitException>()
            .WithMessage("*maximum of 1 corrections*");
    }

    [Fact]
    public void RepairAsync_ReportsShapeMismatchSeparatelyFromSyntaxSuccess()
    {
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """{"type":"object","properties":{"files":{"type":"array"}},"required":["files"]}""")!;
        var pipeline = JsonRepairPipeline.Create();

        using var result = Repair(pipeline, """{"other":true}""", expectation);

        result.Succeeded.Should().BeTrue();
        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.ShapeErrors.Should().NotBeEmpty();
    }

    [Fact]
    public void RepairAsync_ExposesTolerantRecoveryCorrections()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = Repair(pipeline, """{"items":[1,2""");

        result.TolerantRecovery.Should().NotBeNull();
        result.TolerantRecovery!.Succeeded.Should().BeTrue();
        result.TolerantRecovery.CorrectionCount.Should().BeGreaterThan(0);
        result.TolerantRecovery.Corrections.Should().NotBeEmpty();
    }

    [Fact]
    public void RepairAsync_UsesInjectedTolerantParser()
    {
        var parser = new RecordingTolerantParser();
        var pipeline = new JsonRepairPipeline(
            [],
            [],
            [],
            NullLogger<JsonRepairPipeline>.Instance,
            JsonRepairLimits.Default,
            allowTruncationSalvage: true,
            parser);

        using var result = Repair(pipeline, "broken");

        parser.CallCount.Should().Be(1);
        result.Root!["fromParser"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void RepairAsync_DoesNotCreditNodeStrategyThatReturnsUnchangedTree()
    {
        var pipeline = new JsonRepairPipeline(
            [],
            [],
            [new UnchangedNodeStrategy()],
            NullLogger<JsonRepairPipeline>.Instance);
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """{"type":"object"}""")!;

        using var result = Repair(pipeline, """{"value":true}""", expectation);

        result.WasRepaired.Should().BeFalse();
        result.SucceededBy.Should().BeNull();
        result.NodeRepairs.Should().ContainSingle()
            .Which.Status.Should().Be(StrategyStatus.Failed);
    }

    [Fact]
    public void RepairAsync_ValidInputPreservesOriginalText()
    {
        const string input = "{ \"ok\" : true }";
        var pipeline = JsonRepairPipeline.Create();

        using var result = Repair(pipeline, input);

        result.WasRepaired.Should().BeFalse();
        result.RepairedText.Should().Be(input);
    }

    [Fact]
    public void RepairAsync_LogsNoRepairedPayload()
    {
        const string secret = "do-not-log-this-value";
        var logger = new CapturingLogger();
        var pipeline = new JsonRepairPipeline(
            [new RecordingStrategy("repair", "broken", $"{{\"secret\":\"{secret}\"}}", [])],
            [],
            [],
            logger);

        using var result = Repair(pipeline, "broken");

        logger.Messages.Should().NotContain(entry => entry.Message.Contains(secret));
        result.TextRepairs.Should().OnlyContain(report => report.Repaired == null);
    }

    private static JsonRepairResult Repair(
        IJsonRepairPipeline pipeline,
        string input,
        JsonSchemaExpectation? expectation = null) =>
        pipeline.RepairAsync(input, expectation)
            .GetAwaiter()
            .GetResult();

    private sealed class RecordingStrategy(
        string name,
        string expectedInput,
        string output,
        ICollection<string> applied)
        : ITextRepair
    {
        public string Name => name;

        public ValueTask<TextRepairAttempt> RepairAsync(
            string input,
            CancellationToken cancellationToken = default)
        {
            if (input != expectedInput)
            {
                return new(new TextRepairAttempt(
                    RepairOutcome.NotApplicable,
                    null));
            }

            applied.Add(name);

            return new(new TextRepairAttempt(
                RepairOutcome.Repaired,
                output));
        }
    }

    private sealed class ThrowingStrategy
        : ITextRepair
    {
        public string Name => "throws";

        public ValueTask<TextRepairAttempt> RepairAsync(
            string input,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class NotingStrategy
        : ITextRepair
    {
        public string Name => "noting";

        public ValueTask<TextRepairAttempt> RepairAsync(
            string input,
            CancellationToken cancellationToken = default) =>
            new(new TextRepairAttempt(
                RepairOutcome.NotApplicable,
                null,
                "declined"));
    }

    private sealed class CancelingStrategy(CancellationTokenSource source)
        : ITextRepair
    {
        public string Name => "cancels";

        public ValueTask<TextRepairAttempt> RepairAsync(
            string input,
            CancellationToken cancellationToken = default)
        {
            source.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }
    }

    private sealed class CapturingLogger
        : ILogger<JsonRepairPipeline>
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(
            TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class RecordingTolerantParser
        : ITolerantJsonSyntaxTreeParser
    {
        public int CallCount { get; private set; }

        public TolerantJsonSyntaxTreeParseResult Parse(
            string input,
            JsonSchemaExpectation? expectation,
            JsonRepairLimits limits,
            CancellationToken cancellationToken,
            bool allowTruncationSalvage = false)
        {
            CallCount++;
            return new TolerantJsonSyntaxTreeParseResult(
                JsonNode.Parse("""{"fromParser":true}"""),
                "injected");
        }
    }

    private sealed class UnchangedNodeStrategy : INodeRepair
    {
        public string Name => "unchanged";

        public ValueTask<NodeRepairAttempt> RepairAsync(
            JsonNode node,
            JsonSchemaExpectation expectation,
            CancellationToken cancellationToken = default) =>
            new(new NodeRepairAttempt(
                RepairOutcome.Repaired,
                node.DeepClone()));
    }
}
