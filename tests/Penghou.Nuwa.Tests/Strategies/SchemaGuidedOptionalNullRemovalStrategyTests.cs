using FluentAssertions;
using Penghou.Nuwa.Strategies;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Tests.Strategies;

public sealed class SchemaGuidedOptionalNullRemovalStrategyTests
{
    [Fact]
    public void TryRepair_RemovesNestedOptionalNullRejectedBySchema()
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

        var changed = new SchemaGuidedOptionalNullRemovalStrategy()
            .TryRepair(node, expectation, out var repaired);

        changed.Should().BeTrue();
        repaired["taskReplacements"]![0]!.AsObject()
            .ContainsKey("moduleId").Should().BeFalse();
        expectation.Validate(repaired).Should().BeEmpty();
    }

    [Fact]
    public void TryRepair_PreservesRequiredNull()
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

        var changed = new SchemaGuidedOptionalNullRemovalStrategy()
            .TryRepair(node, expectation, out var repaired);

        changed.Should().BeFalse();
        repaired.AsObject().ContainsKey("value").Should().BeTrue();
        expectation.Validate(repaired).Should().ContainSingle();
    }

    [Fact]
    public void TryRepair_PreservesOptionalNullAllowedBySchema()
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

        var changed = new SchemaGuidedOptionalNullRemovalStrategy()
            .TryRepair(node, expectation, out var repaired);

        changed.Should().BeFalse();
        repaired.AsObject().ContainsKey("value").Should().BeTrue();
        expectation.Validate(repaired).Should().BeEmpty();
    }
}
