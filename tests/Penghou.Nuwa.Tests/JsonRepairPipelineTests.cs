using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Penghou.Nuwa.Strategies;

namespace Penghou.Nuwa.Tests;

public sealed class JsonRepairPipelineTests
{
    [Fact]
    public void Repair_AppliesStrategiesInInjectedOrderAndReportsAttempts()
    {
        var applied = new List<string>();
        ITextRepairStrategy[] strategies =
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
            new TolerantJsonSyntaxTreeParser(),
            [],
            NullLogger<JsonRepairPipeline>.Instance);

        using var result = pipeline.Repair("broken");

        result.Document.Should().NotBeNull();
        result.WasRepaired.Should().BeTrue();
        result.Attempts.Should().Contain(
            "first",
            "applied; JSON remained malformed");
        result.Attempts.Should().Contain(
            "second",
            "succeeded");
        applied.Should().Equal("first", "second");
    }

    private sealed class RecordingStrategy(
        string name,
        string expectedInput,
        string output,
        ICollection<string> applied)
        : ITextRepairStrategy
    {
        public string Name => name;

        public bool MightApply(string input) =>
            input == expectedInput;

        public bool TryRepair(
            string input,
            out string repaired)
        {
            applied.Add(name);
            repaired = output;
            return true;
        }
    }
}
