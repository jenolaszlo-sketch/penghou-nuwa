using FluentAssertions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Tests;

public sealed class JsonRepairResultTests
{
    [Fact]
    public void SuccessFactory_ProducesDocumentRootAndRepairedText()
    {
        using var result = JsonRepairResult.Success(
            JsonNode.Parse("""{"ok":true}""")!,
            originalText: "{ok: true}",
            repairedText: """{"ok":true}""",
            wasRepaired: true,
            textRepairs: [],
            nodeRepairs: []);

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.OriginalText.Should().Be("{ok: true}");
        result.RepairedText.Should().Be("""{"ok":true}""");
        result.Document.Should().NotBeNull();
        result.Root.Should().NotBeNull();
        result.GetDocumentOrThrow().Should().NotBeNull();
        result.GetRootOrThrow().Should().NotBeNull();
        result.GetRepairedTextOrThrow().Should()
            .Be("""{"ok":true}""");
    }

    [Fact]
    public void FailureFactory_HasNoDocumentAndReportsNoRepair()
    {
        using var result = JsonRepairResult.Failure(
            originalText: "{nope",
            repairedText: "{nope",
            textRepairs: [],
            nodeRepairs: []);

        result.Succeeded.Should().BeFalse();
        result.WasRepaired.Should().BeFalse();
        result.Document.Should().BeNull();
        result.Root.Should().BeNull();
        result.RepairedText.Should().Be("{nope");
        result.OriginalText.Should().Be("{nope");

        var act = () => result.GetDocumentOrThrow();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SevenArgConstructor_HasNoTolerantParse()
    {
        var json = """{"ok":true}""";

        using var result = new JsonRepairResult(
            JsonDocument.Parse(json),
            JsonNode.Parse(json),
            originalText: json,
            repairedText: json,
            wasRepaired: false,
            textRepairs: [],
            nodeRepairs: []);

        result.Succeeded.Should().BeTrue();
        result.TolerantParse.Should().BeNull();
    }

    [Fact]
    public void FailureMessage_IncludesNodeRepairsAndRecoveryOutcome()
    {
        using var result = JsonRepairResult.Failure(
            originalText: "{nope",
            repairedText: "{nope",
            textRepairs:
            [
                new StrategyReport(
                    "salvage",
                    StrategyStatus.Failed)
            ],
            nodeRepairs:
            [
                new StrategyReport(
                    "expand",
                    StrategyStatus.NotApplicable)
            ]);

        var act = () => result.GetDocumentOrThrow();

        var exception =
            act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("Text repairs:");
        exception.Message.Should().Contain("salvage=Failed");
        exception.Message.Should().Contain("Node repairs:");
        exception.Message.Should().Contain("expand=NotApplicable");
        exception.Message.Should().Contain("Tolerant recovery:");
    }
}
