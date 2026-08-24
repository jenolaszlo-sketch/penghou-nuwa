using FluentAssertions;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Tests;

public sealed class JsonSchemaExpectationNormalizerTests
{
    [Fact]
    public void FromSchemaNode_InlinesLocalRefPointers()
    {
        var schema = JsonNode.Parse(
            """
            {
              "$defs": {
                "user": {
                  "type": "object",
                  "properties": {
                    "name": { "type": "string" },
                    "age": { "type": "integer" }
                  }
                }
              },
              "type": "object",
              "properties": {
                "owner": { "$ref": "#/$defs/user" }
              }
            }
            """);

        var expectation = JsonSchemaExpectation.FromSchemaNode(schema!);

        expectation.PropertyKinds.Should().Contain("owner", JsonSchemaFieldKind.Object);
        expectation.GetProperty("owner")!.PropertyKinds.Should().Contain("name", JsonSchemaFieldKind.String);
        expectation.GetProperty("owner")!.PropertyKinds.Should().Contain("age", JsonSchemaFieldKind.Number);
    }

    [Fact]
    public void FromSchemaNode_InlinesDefsUsingArrayPointers()
    {
        var schema = JsonNode.Parse(
            """
            {
              "definitions": {
                "items": {
                  "type": "object",
                  "properties": {
                    "id": { "type": "integer" }
                  }
                }
              },
              "type": "object",
              "properties": {
                "rows": {
                  "type": "array",
                  "items": { "$ref": "#/definitions/items" }
                }
              }
            }
            """);

        var expectation = JsonSchemaExpectation.FromSchemaNode(schema!);

        expectation.PropertyKinds.Should().Contain("rows", JsonSchemaFieldKind.Array);
        expectation.GetProperty("rows")!.GetItem()!.PropertyKinds.Should().Contain("id", JsonSchemaFieldKind.Number);
    }

    [Fact]
    public void FromSchemaNode_CutsRecursiveReferences()
    {
        var schema = JsonNode.Parse(
            """
            {
              "$defs": {
                "node": {
                  "type": "object",
                  "properties": {
                    "value": { "type": "string" },
                    "children": {
                      "type": "array",
                      "items": { "$ref": "#/$defs/node" }
                    }
                  }
                }
              },
              "type": "object",
              "properties": {
                "root": { "$ref": "#/$defs/node" }
              }
            }
            """);

        var expectation = JsonSchemaExpectation.FromSchemaNode(schema!);

        expectation.PropertyKinds.Should().Contain("root", JsonSchemaFieldKind.Object);
        var root = expectation.GetProperty("root")!;
        root.PropertyKinds.Should().Contain("value", JsonSchemaFieldKind.String);
        root.PropertyKinds.Should().Contain("children", JsonSchemaFieldKind.Array);
        root.GetProperty("children")!.GetItem()!.ExpectedKind.Should().Be(JsonSchemaFieldKind.Object);
    }

    [Fact]
    public void FromSchemaNode_LeavesUnresolvableRefsAsOpaqueStubs()
    {
        var schema = JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "remote": { "$ref": "https://example.com/schemas/shared.json#/Thing" }
              }
            }
            """);

        var expectation = JsonSchemaExpectation.FromSchemaNode(schema!);

        expectation.PropertyKinds.Should().NotContainKey("remote");
    }

    [Fact]
    public void FromSchemaNode_MergesAllOfAndRespectsNullability()
    {
        var schema = JsonNode.Parse(
            """
            {
              "allOf": [
                {
                  "type": "object",
                  "properties": {
                    "name": { "type": "string" }
                  },
                  "required": ["name"]
                },
                {
                  "type": "object",
                  "properties": {
                    "age": { "type": "integer", "nullable": true }
                  }
                }
              ]
            }
            """);

        var expectation = JsonSchemaExpectation.FromSchemaNode(schema!);

        expectation.ExpectedKind.Should().Be(JsonSchemaFieldKind.Object);
        expectation.PropertyKinds.Should().Contain("name", JsonSchemaFieldKind.String);
        expectation.PropertyKinds.Should().Contain("age", JsonSchemaFieldKind.Number);
        expectation.GetProperty("age")!.Nullable.Should().BeTrue();
        expectation.Schema!.AsObject()["required"]!.AsArray()
            .Should().Contain(node => node!.GetValue<string>() == "name");
    }

    [Fact]
    public void FromSchemaNode_InfersStringTypeFromEnum()
    {
        var schema = JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "status": { "enum": ["active", "inactive"] }
              }
            }
            """);

        var expectation = JsonSchemaExpectation.FromSchemaNode(schema!);

        expectation.GetProperty("status")!.PropertyKinds.Should().BeEmpty();
        expectation.GetProperty("status")!.ExpectedKind.Should().Be(JsonSchemaFieldKind.String);
    }

    [Fact]
    public void FromSchemaNode_UnionsOneOfTypes()
    {
        var schema = JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "id": {
                  "oneOf": [
                    { "type": "string" },
                    { "type": "integer" }
                  ]
                }
              }
            }
            """);

        var expectation = JsonSchemaExpectation.FromSchemaNode(schema!);

        expectation.GetProperty("id")!.PropertyKinds.Should().BeEmpty();
        expectation.GetProperty("id")!.ExpectedKind.Should().Be(JsonSchemaFieldKind.String);
    }

    [Fact]
    public void FromSchemaNode_TreatsAdditionalPropertiesSchemaAsObject()
    {
        var schema = JsonNode.Parse(
            """
            {
              "type": "object",
              "additionalProperties": { "type": "string" }
            }
            """);

        var expectation = JsonSchemaExpectation.FromSchemaNode(schema!);

        expectation.ExpectedKind.Should().Be(JsonSchemaFieldKind.Object);
    }

    [Fact]
    public void FromSchemaNode_MarksUntypedSchemasAsNullable()
    {
        var schema = JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "loose": {}
              }
            }
            """);

        var expectation = JsonSchemaExpectation.FromSchemaNode(schema!);

        expectation.GetProperty("loose")!.Nullable.Should().BeTrue();
    }

    [Fact]
    public void FromSchemaNode_TypeUnionWithNullMarksNullable()
    {
        var schema = JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "optional": { "type": ["string", "null"] }
              }
            }
            """);

        var expectation = JsonSchemaExpectation.FromSchemaNode(schema!);

        expectation.GetProperty("optional")!.Nullable.Should().BeTrue();
        expectation.GetProperty("optional")!.ExpectedKind.Should().Be(JsonSchemaFieldKind.String);
    }

    [Fact]
    public void FromSchemaNode_OpenAiStyleDefsSchemaResolves()
    {
        var schema = JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "arguments": {
                  "$ref": "#/$defs/search_args"
                }
              },
              "$defs": {
                "search_args": {
                  "type": "object",
                  "properties": {
                    "query": { "type": "string" },
                    "limit": { "type": "integer" }
                  },
                  "required": ["query"]
                }
              }
            }
            """);

        var expectation = JsonSchemaExpectation.FromSchemaNode(schema!);

        expectation.GetProperty("arguments")!.PropertyKinds.Should().Contain("query", JsonSchemaFieldKind.String);
        expectation.GetProperty("arguments")!.PropertyKinds.Should().Contain("limit", JsonSchemaFieldKind.Number);
        expectation.GetProperty("arguments")!.Schema!.AsObject()["required"]!.AsArray()
            .Should().Contain(node => node!.GetValue<string>() == "query");
    }

    [Fact]
    public void FromSchemaNode_BuildsBranchesWithDiscriminators()
    {
        var expectation = JsonSchemaExpectation.FromSchemaNode(
            CreateToolUnionSchema());

        expectation.Branches.Should().HaveCount(2);
        expectation.Branches[0].DiscriminatorProperty.Should().Be("name");
        expectation.Branches[0].DiscriminatorValues.Should()
            .Contain("emit_files");
        expectation.Branches[0].Expectation.GetProperty("arguments")!
            .PropertyKinds.Should().Contain("files", JsonSchemaFieldKind.Array);
        expectation.Branches[1].Expectation.GetProperty("arguments")!
            .PropertyKinds.Should().Contain("count", JsonSchemaFieldKind.Number);
    }

    [Fact]
    public void FactoryExpectation_MemoizesChildAndItemExpectations()
    {
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","properties":{"id":{"type":"string"}}}}}}""")!;

        var firstProperty = expectation.GetProperty("items");
        var secondProperty = expectation.GetProperty("items");
        var firstItem = firstProperty!.GetItem();
        var secondItem = firstProperty.GetItem();

        firstProperty.Should().BeSameAs(secondProperty);
        firstItem.Should().BeSameAs(secondItem);
    }

    [Fact]
    public void FromSchemaNode_UnionRootKeepsCanonicalShape()
    {
        var expectation = JsonSchemaExpectation.FromSchemaNode(
            CreateToolUnionSchema());

        expectation.ExpectedKind.Should().Be(JsonSchemaFieldKind.Object);
        expectation.PropertyKinds.Should().Contain("name", JsonSchemaFieldKind.String);
        expectation.PropertyKinds.Should().Contain("arguments", JsonSchemaFieldKind.Object);
    }

    [Fact]
    public void TryResolveBranch_SelectsByDiscriminator()
    {
        var expectation = JsonSchemaExpectation.FromSchemaNode(
            CreateToolUnionSchema());

        var resolved = expectation.TryResolveBranch(
            JsonNode.Parse("""{"name":"emit_files","arguments":{}}""")!);

        resolved.Should().NotBeNull();
        resolved!.GetProperty("arguments")!.PropertyKinds
            .Should().Contain("files", JsonSchemaFieldKind.Array);
    }

    [Fact]
    public void TryResolveBranch_RefusesMultipleStructurallyFittingBranches()
    {
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """{"oneOf":[{"type":"object","properties":{"id":{"type":"string"}}},{"type":"object","properties":{"id":{"type":"string"},"note":{"type":"string"}}}]}""")!;

        expectation.TryResolveBranch(
                JsonNode.Parse("""{"id":"A1"}""")!)
            .Should().BeNull();
    }

    [Fact]
    public void TryResolveBranch_RefusesDuplicateDiscriminatorMatches()
    {
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """{"oneOf":[{"type":"object","properties":{"name":{"const":"run","type":"string"}}},{"type":"object","properties":{"name":{"const":"run","type":"string"},"note":{"type":"string"}}}]}""")!;

        expectation.TryResolveBranch(
                JsonNode.Parse("""{"name":"run"}""")!)
            .Should().BeNull();
    }

    [Fact]
    public void ValidateShape_RejectsValueOutsideEnum()
    {
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """{"type":"string","enum":["active","inactive"]}""")!;

        expectation.ValidateShape(JsonValue.Create("unknown")!)
            .Should().ContainSingle("*allowed enum value*");
    }

    [Fact]
    public void ValidateShape_RejectsValueDifferentFromConst()
    {
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """{"type":"string","const":"run"}""")!;

        expectation.ValidateShape(JsonValue.Create("stop")!)
            .Should().ContainSingle("*required const value*");
    }

    [Fact]
    public void ValidateShape_RejectsPropertiesWhenStrictSchemaDeclaresNone()
    {
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """{"type":"object","additionalProperties":false}""")!;

        expectation.ValidateShape(
                JsonNode.Parse("""{"unexpected":true}""")!)
            .Should().ContainSingle("*not declared by the schema*");
    }

    [Fact]
    public void Accepts_RejectsUndeclaredProperties()
    {
        var expectation = JsonSchemaExpectation.FromSchemaNode(
            CreateToolUnionSchema());
        var branch = expectation.Branches[0].Expectation.GetProperty("arguments")!;

        branch.Accepts(
            JsonNode.Parse("""{"files":[],"repo":"x"}""")!)
            .Should().BeFalse();
    }

    [Fact]
    public void Accepts_AcceptsDeclaredContent()
    {
        var expectation = JsonSchemaExpectation.FromSchemaNode(
            CreateToolUnionSchema());
        var branch = expectation.Branches[0].Expectation.GetProperty("arguments")!;

        branch.Accepts(
            JsonNode.Parse(
                """{"files":[{"path":"A.cs","content":"go"}],"notes":"done"}""")!)
            .Should().BeTrue();
    }

    private static JsonNode CreateToolUnionSchema() =>
        JsonNode.Parse(
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
                        "files": { "type": "array", "items": { "type": "object" } },
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
