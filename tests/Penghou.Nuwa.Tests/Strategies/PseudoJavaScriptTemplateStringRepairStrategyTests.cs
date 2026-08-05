using FluentAssertions;
using Penghou.Nuwa.Strategies;
using System.Text.Json;

namespace Penghou.Nuwa.Tests.Strategies;

public sealed class
    PseudoJavaScriptTemplateStringRepairStrategyTests
{
    private readonly
        PseudoJavaScriptTemplateStringRepairStrategy
        _strategy = new();

    [Fact]
    public void RepairAsync_ConvertsMultilineTemplateValue()
    {
        const string input =
            """
            {
              "content": `
            using System;
            var message = "hello";
            `
            }
            """;

        var attempt = Repair(_strategy, input);
        var repaired = attempt.Repaired!;

        attempt.Outcome.Should()
            .Be(RepairOutcome.Repaired);
        using var document =
            JsonDocument.Parse(repaired);
        document.RootElement
            .GetProperty("content")
            .GetString()
            .Should()
            .Contain("var message = \"hello\";");
    }

    [Fact]
    public void RepairAsync_ConvertsMultipleTemplateValues()
    {
        const string input =
            """
            {
              "path": `Program.cs`,
              "content": `app.Run();
            `
            }
            """;

        var attempt = Repair(_strategy, input);
        var repaired = attempt.Repaired!;

        attempt.Outcome.Should()
            .Be(RepairOutcome.Repaired);
        using var document =
            JsonDocument.Parse(repaired);
        document.RootElement
            .GetProperty("path")
            .GetString()
            .Should()
            .Be("Program.cs");
        document.RootElement
            .GetProperty("content")
            .GetString()
            .Should()
            .Contain("app.Run();");
    }

    [Fact]
    public void RepairAsync_DecodesEscapedBacktick()
    {
        const string input =
            """{"content": `value \`inside\` content`}""";

        var attempt = Repair(_strategy, input);
        var repaired = attempt.Repaired!;

        attempt.Outcome.Should()
            .Be(RepairOutcome.Repaired);
        using var document =
            JsonDocument.Parse(repaired);
        document.RootElement
            .GetProperty("content")
            .GetString()
            .Should()
            .Be("value `inside` content");
    }

    [Fact]
    public void RepairAsync_DoesNotChangeBackticksInsideJsonString()
    {
        const string input =
            """{"content":"use `code` here"}""";

        var attempt = Repair(_strategy, input);

        attempt.Outcome.Should()
            .Be(RepairOutcome.NotApplicable);
        attempt.Repaired.Should().BeNull();
    }

    [Fact]
    public void RepairAsync_LeavesStructuralDamageForParser()
    {
        const string input =
            """{"files":[{"content":`app.Run();`}]""";

        var attempt = Repair(_strategy, input);
        var repaired = attempt.Repaired!;

        attempt.Outcome.Should()
            .Be(RepairOutcome.Repaired);
        repaired.Should().Contain(
            "\"content\":\"app.Run();\"");
        repaired.Should().EndWith("}]");
    }

    private static TextRepairAttempt Repair(
        ITextRepair strategy,
        string input) =>
        strategy.RepairAsync(input).GetAwaiter().GetResult();
}
