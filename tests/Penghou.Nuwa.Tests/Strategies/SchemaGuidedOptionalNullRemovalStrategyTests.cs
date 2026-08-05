using FluentAssertions;
using Penghou.Nuwa.Strategies;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Tests.Strategies;

public sealed class SchemaGuidedOptionalNullRemovalStrategyTests
{
    [Fact]
    public void RepairAsync_RemovesNestedOptionalNullRejectedBySchema()
    {
        var node = JsonNode.Parse(
            """
            {
              "taskReplacements": [
                { "id": "scaffold", "moduleId": null }
              ]
            }
            """)!;
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": {
                "taskReplacements": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "id": { "type": "string" },
                      "moduleId": { "type": "string" }
                    },
                    "required": ["id"]
                  }
                }
              },
              "required": ["taskReplacements"]
            }
            """)!;

        var attempt = Repair(
            new SchemaGuidedOptionalNullRemovalStrategy(),
            node,
            expectation);
        var repaired = attempt.Repaired!;

        attempt.Outcome.Should()
            .Be(RepairOutcome.Repaired);
        repaired["taskReplacements"]![0]!.AsObject()
            .ContainsKey("moduleId").Should().BeFalse();
        expectation.Validate(repaired).Should().BeEmpty();
    }

    [Fact]
    public void RepairAsync_PreservesRequiredNull()
    {
        var node = JsonNode.Parse("""{"value":null}""")!;
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": { "value": { "type": "string" } },
              "required": ["value"]
            }
            """)!;

        var attempt = Repair(
            new SchemaGuidedOptionalNullRemovalStrategy(),
            node,
            expectation);

        attempt.Outcome.Should()
            .Be(RepairOutcome.NotApplicable);
        node.AsObject().ContainsKey("value").Should().BeTrue();
        expectation.Validate(node).Should().ContainSingle();
    }

    [Fact]
    public void RepairAsync_PreservesOptionalNullAllowedBySchema()
    {
        var node = JsonNode.Parse("""{"value":null}""")!;
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": {
                "value": { "type": ["string", "null"] }
              }
            }
            """)!;

        var attempt = Repair(
            new SchemaGuidedOptionalNullRemovalStrategy(),
            node,
            expectation);

        attempt.Outcome.Should()
            .Be(RepairOutcome.NotApplicable);
        node.AsObject().ContainsKey("value").Should().BeTrue();
        expectation.Validate(node).Should().BeEmpty();
    }

    private static NodeRepairAttempt Repair(
        INodeRepair strategy,
        JsonNode node,
        JsonSchemaExpectation expectation) =>
        strategy.RepairAsync(node, expectation).GetAwaiter().GetResult();
}
