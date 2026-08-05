using FluentAssertions;
using Penghou.Nuwa.Strategies;

namespace Penghou.Nuwa.Tests;

public sealed class JsonRepairPipelineFactoryTests
{
    [Fact]
    public void Create_DefaultPipeline_RepairsSalvageOnlyInput()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = Repair(pipeline, "{name: None}");

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.TextRepairs.Should().Contain(
            report =>
                report.Name == "salvage" &&
                report.Status == StrategyStatus.Succeeded);
        result.SucceededBy!.Name.Should().Be("salvage");
    }

    [Fact]
    public void Create_DefaultPipeline_RemovesMarkdownFence()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = Repair(
            pipeline,
            """
            ```json
            {"ok": true}
            ```
            """);

        result.Succeeded.Should().BeTrue();
        result.SucceededBy!.Name.Should().Be("markdown-json-fence");
    }

    [Fact]
    public void Create_DefaultPipeline_RunsSchemaGuidedNodeRepair()
    {
        var pipeline = JsonRepairPipeline.Create();
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": {
                "files": { "type": "array", "items": { "type": "string" } }
              },
              "required": ["files"]
            }
            """);

        using var result = Repair(
            pipeline,
            """{"files": "[1, 2]"}""",
            expectation);

        result.Succeeded.Should().BeTrue();
        result.NodeRepairs.Should().Contain(
            report =>
                report.Name == "schema-guided-json-string-expansion" &&
                report.Status == StrategyStatus.Succeeded);
        result.SucceededBy!.Name.Should()
            .Be("schema-guided-json-string-expansion");
    }

    [Fact]
    public void Create_DisableSalvageFallback_FailsOnSalvageOnlyInput()
    {
        var pipeline = JsonRepairPipeline.Create(
            options => options.DisableSalvageFallback());

        using var result = Repair(pipeline, "{name: None}");

        result.Succeeded.Should().BeFalse();
        result.Document.Should().BeNull();

        var act = () => result.GetDocumentOrThrow();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void JsonRepair_OneShotHelper_RepairsInput()
    {
        using var result = RepairOneShot(
            """
            ```json
            {"ok": true}
            ```
            """);

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
    }

    [Fact]
    public void Result_ExposesRootAndRepairedText()
    {
        var pipeline = JsonRepairPipeline.Create();

        using var result = Repair(pipeline, "{name: None}");

        result.Root.Should().NotBeNull();
        result.RepairedText.Should().NotBeNullOrWhiteSpace();
        result.GetRootOrThrow().Should().NotBeNull();
        result.GetRepairedTextOrThrow().Should()
            .NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Create_StrategyWithUnresolvableConstructor_Throws()
    {
        var act = () => JsonRepairPipeline.Create(
            options => options.AddTextRepair<UnresolvableTextRepair>());

        act.Should().Throw<InvalidOperationException>();
    }

    private static JsonRepairResult Repair(
        IJsonRepairPipeline pipeline,
        string input,
        JsonSchemaExpectation? expectation = null) =>
        pipeline.RepairAsync(input, expectation)
            .GetAwaiter()
            .GetResult();

    private static JsonRepairResult RepairOneShot(
        string input) =>
        JsonRepair.RepairAsync(input)
            .GetAwaiter()
            .GetResult();

    private sealed class UnresolvableTextRepair(string name)
        : ITextRepair
    {
        public string Name => name;

        public ValueTask<TextRepairAttempt> RepairAsync(
            string input,
            CancellationToken cancellationToken = default) =>
            new(new TextRepairAttempt(
                RepairOutcome.NotApplicable,
                null));
    }
}
