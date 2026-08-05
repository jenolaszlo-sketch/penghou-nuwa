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
    public void TryRepair_ConvertsMultilineTemplateValue()
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

        var changed = _strategy.TryRepair(
            input,
            out var repaired);

        changed.Should().BeTrue();
        using var document =
            JsonDocument.Parse(repaired);
        document.RootElement
            .GetProperty("content")
            .GetString()
            .Should()
            .Contain("var message = \"hello\";");
    }

    [Fact]
    public void TryRepair_ConvertsMultipleTemplateValues()
    {
        const string input =
            """
            {
              "path": `Program.cs`,
              "content": `app.Run();
            `
            }
            """;

        var changed = _strategy.TryRepair(
            input,
            out var repaired);

        changed.Should().BeTrue();
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
    public void TryRepair_DecodesEscapedBacktick()
    {
        const string input =
            """{"content": `value \`inside\` content`}""";

        var changed = _strategy.TryRepair(
            input,
            out var repaired);

        changed.Should().BeTrue();
        using var document =
            JsonDocument.Parse(repaired);
        document.RootElement
            .GetProperty("content")
            .GetString()
            .Should()
            .Be("value `inside` content");
    }

    [Fact]
    public void TryRepair_DoesNotChangeBackticksInsideJsonString()
    {
        const string input =
            """{"content":"use `code` here"}""";

        var changed = _strategy.TryRepair(
            input,
            out var repaired);

        changed.Should().BeFalse();
        repaired.Should().Be(input);
    }

    [Fact]
    public void TryRepair_LeavesStructuralDamageForParser()
    {
        const string input =
            """{"files":[{"content":`app.Run();`}]""";

        var changed = _strategy.TryRepair(
            input,
            out var repaired);

        changed.Should().BeTrue();
        repaired.Should().Contain(
            "\"content\":\"app.Run();\"");
        repaired.Should().EndWith("}]");
    }
}
