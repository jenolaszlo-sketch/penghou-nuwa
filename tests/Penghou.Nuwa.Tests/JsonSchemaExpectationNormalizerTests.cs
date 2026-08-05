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
}
