using FluentAssertions;
using Penghou.Nuwa.Strategies;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Tests.Strategies;

public sealed class SchemaGuidedJsonStringExpansionStrategyBranchTests
{
    [Fact]
    public void RepairAsync_ExpandsDoubleSerializedArgumentsForMatchingBranch()
    {
        var node = JsonNode.Parse(
            """
            {
              "name": "run_shell",
              "arguments": "{\"repo\":\"acme\",\"count\":3}"
            }
            """)!;
        var expectation = CreateUnionExpectation();

        var attempt = Repair(node, expectation);

        attempt.Outcome.Should().Be(RepairOutcome.Repaired);
        var arguments = attempt.Repaired!["arguments"]!;
        arguments.AsObject()["repo"]!.GetValue<string>().Should().Be("acme");
        arguments.AsObject()["count"]!.GetValue<int>().Should().Be(3);
    }

    [Fact]
    public void RepairAsync_DoesNotExpandHybridArgumentsFromAnotherTool()
    {
        var node = JsonNode.Parse(
            """
            {
              "name": "emit_files",
              "arguments": "{\"repo\":\"acme\",\"count\":3}"
            }
            """)!;
        var expectation = CreateUnionExpectation();

        var attempt = Repair(node, expectation);

        attempt.Outcome.Should().Be(RepairOutcome.NotApplicable);
        attempt.Repaired.Should().BeNull();
        node["arguments"]!.GetValue<string>()
            .Should().Be("{\"repo\":\"acme\",\"count\":3}");
    }

    [Fact]
    public void RepairAsync_ExpandsArgumentsThatMatchTheNamedTool()
    {
        var node = JsonNode.Parse(
            """
            {
              "name": "emit_files",
              "arguments": "{\"files\":[{\"path\":\"A.cs\",\"content\":\"go\"}]}"
            }
            """)!;
        var expectation = CreateUnionExpectation();

        var attempt = Repair(node, expectation);

        attempt.Outcome.Should().Be(RepairOutcome.Repaired);
        attempt.Repaired!["arguments"]!
            .AsObject()["files"]!.AsArray().Should().HaveCount(1);
    }

    [Fact]
    public void RepairAsync_ExpandsDoubleSerializedRootAgainstMatchingBranch()
    {
        var node = JsonNode.Parse(
            """
            "{\"name\":\"run_shell\",\"arguments\":{\"repo\":\"acme\",\"count\":3}}"
            """)!;
        var expectation = CreateUnionExpectation();

        var attempt = Repair(node, expectation);

        attempt.Outcome.Should().Be(RepairOutcome.Repaired);
        var root = attempt.Repaired!.AsObject();
        root["name"]!.GetValue<string>().Should().Be("run_shell");
        root["arguments"]!.AsObject()["count"]!.GetValue<int>().Should().Be(3);
    }

    [Fact]
    public void RepairAsync_ExpandsFreeFormObjectSchema()
    {
        var node = JsonNode.Parse("\"{\\\"a\\\":1}\"")!;
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """{ "type": "object" }""")!;

        var attempt = Repair(node, expectation);

        attempt.Outcome.Should().Be(RepairOutcome.Repaired);
        attempt.Repaired!.AsObject()["a"]!.GetValue<int>().Should().Be(1);
    }

    private static NodeRepairAttempt Repair(
        JsonNode node,
        JsonSchemaExpectation expectation) =>
        new SchemaGuidedJsonStringExpansionStrategy()
            .RepairAsync(node, expectation)
            .GetAwaiter()
            .GetResult();

    private static JsonSchemaExpectation CreateUnionExpectation() =>
        JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "oneOf": [
                {
                  "type": "object",
                  "properties": {
                    "name": { "type": "string", "const": "emit_files" },
                    "arguments": {
                      "type": "object",
                      "properties": {
                        "files": {
                          "type": "array",
                          "items": {
                            "type": "object",
                            "properties": {
                              "path": { "type": "string" },
                              "content": { "type": "string" }
                            }
                          }
                        },
                        "notes": { "type": "string" }
                      }
                    }
                  },
                  "required": ["name", "arguments"]
                },
                {
                  "type": "object",
                  "properties": {
                    "name": { "type": "string", "const": "run_shell" },
                    "arguments": {
                      "type": "object",
                      "properties": {
                        "repo": { "type": "string" },
                        "count": { "type": "integer" }
                      }
                    }
                  },
                  "required": ["name", "arguments"]
                }
              ]
            }
            """)!;
}
