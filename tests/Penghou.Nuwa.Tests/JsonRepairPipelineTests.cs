using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Penghou.Nuwa.Strategies;

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
}
