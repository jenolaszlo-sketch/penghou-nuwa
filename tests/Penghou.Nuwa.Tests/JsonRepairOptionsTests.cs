using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Nuwa.Extensions;
using Penghou.Nuwa.Strategies;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Tests;

public sealed class JsonRepairOptionsTests
{
    [Fact]
    public void Default_RegistersKnownStrategiesInOrder()
    {
        var options = new JsonRepairOptions();

        options.TextRepairs.Should().Equal(
            typeof(MarkdownJsonFenceRepairStrategy),
            typeof(PseudoCSharpVerbatimStringRepairStrategy),
            typeof(PseudoJavaScriptTemplateStringRepairStrategy));
        options.SalvageRepairs.Should().Equal(
            typeof(SalvageRepairStrategy));
        options.NodeRepairs.Should().Equal(
            typeof(SchemaGuidedOptionalNullRemovalStrategy),
            typeof(SchemaGuidedJsonStringExpansionStrategy));
    }

    [Fact]
    public void AddTextRepair_AppendsAfterDefaults()
    {
        var options = new JsonRepairOptions();

        options.AddTextRepair<NoopTextRepair>();

        options.TextRepairs.Should().EndWith(typeof(NoopTextRepair));
    }

    [Fact]
    public void InsertTextRepairAfter_PlacesNewStrategyAfterAnchor()
    {
        var options = new JsonRepairOptions();

        options.InsertTextRepairAfter<
            MarkdownJsonFenceRepairStrategy,
            NoopTextRepair>();

        options.TextRepairs.Should().Equal(
            typeof(MarkdownJsonFenceRepairStrategy),
            typeof(NoopTextRepair),
            typeof(PseudoCSharpVerbatimStringRepairStrategy),
            typeof(PseudoJavaScriptTemplateStringRepairStrategy));
    }

    [Fact]
    public void InsertTextRepairAfter_ThrowsWhenAnchorMissing()
    {
        var options = new JsonRepairOptions();

        var act = () => options
            .InsertTextRepairAfter<NoopTextRepair, NoopTextRepair>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void InsertTextRepairAfter_ThrowsWhenTypeAlreadyRegistered()
    {
        var options = new JsonRepairOptions();

        var act = () => options
            .InsertTextRepairAfter<
                MarkdownJsonFenceRepairStrategy,
                PseudoCSharpVerbatimStringRepairStrategy>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RemoveTextRepair_RemovesExisting()
    {
        var options = new JsonRepairOptions();

        options.RemoveTextRepair<MarkdownJsonFenceRepairStrategy>();

        options.TextRepairs.Should().NotContain(
            typeof(MarkdownJsonFenceRepairStrategy));
    }

    [Fact]
    public void RemoveTextRepair_ThrowsWhenNotRegistered()
    {
        var options = new JsonRepairOptions();

        var act = () => options.RemoveTextRepair<NoopTextRepair>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ClearTextRepairs_RemovesAll()
    {
        var options = new JsonRepairOptions();

        options.ClearTextRepairs();

        options.TextRepairs.Should().BeEmpty();
    }

    [Fact]
    public void AddSalvageRepair_AppendsAfterDefaults()
    {
        var options = new JsonRepairOptions();

        options.AddSalvageRepair<NoopTextRepair>();

        options.SalvageRepairs.Should().Equal(
            typeof(SalvageRepairStrategy),
            typeof(NoopTextRepair));
    }

    [Fact]
    public void RemoveSalvageRepair_RemovesExisting()
    {
        var options = new JsonRepairOptions();

        options.RemoveSalvageRepair<SalvageRepairStrategy>();

        options.SalvageRepairs.Should().BeEmpty();
    }

    [Fact]
    public void DisableSalvageFallback_RemovesAllSalvageRepairs()
    {
        var options = new JsonRepairOptions();
        options.AddSalvageRepair<NoopTextRepair>();

        options.DisableSalvageFallback();

        options.SalvageRepairs.Should().BeEmpty();
    }

    [Fact]
    public void NodeRepairs_FluentMutationsFollowOrder()
    {
        var options = new JsonRepairOptions();

        options
            .InsertNodeRepairAfter<
                SchemaGuidedOptionalNullRemovalStrategy,
                NoopNodeRepair>()
            .AddNodeRepair<NoopNodeRepair>();

        options.NodeRepairs.Should().Equal(
            typeof(SchemaGuidedOptionalNullRemovalStrategy),
            typeof(NoopNodeRepair),
            typeof(SchemaGuidedJsonStringExpansionStrategy),
            typeof(NoopNodeRepair));
    }

    [Fact]
    public void RemoveNodeRepair_ThrowsWhenNotRegistered()
    {
        var options = new JsonRepairOptions();

        var act = () => options.RemoveNodeRepair<NoopNodeRepair>();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddJsonRepair_WithDuplicateRegistration_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddJsonRepair(
            options => options.AddTextRepair<MarkdownJsonFenceRepairStrategy>());

        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class NoopTextRepair
        : ITextRepair
    {
        public string Name => "noop-text";

        public ValueTask<TextRepairAttempt> RepairAsync(
            string input,
            CancellationToken cancellationToken = default) =>
            new(new TextRepairAttempt(
                RepairOutcome.NotApplicable,
                null));
    }

    private sealed class NoopNodeRepair
        : INodeRepair
    {
        public string Name => "noop-node";

        public ValueTask<NodeRepairAttempt> RepairAsync(
            JsonNode node,
            JsonSchemaExpectation expectation,
            CancellationToken cancellationToken = default) =>
            new(new NodeRepairAttempt(
                RepairOutcome.NotApplicable,
                null));
    }
}
