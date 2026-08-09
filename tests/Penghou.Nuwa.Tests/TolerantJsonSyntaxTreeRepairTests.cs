using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Penghou.Nuwa;
using Penghou.Nuwa.Strategies;
using System.Text.Json;

namespace Penghou.Nuwa.Tests;

public sealed class TolerantJsonSyntaxTreeRepairTests
{
    [Theory]
    [InlineData("""{"value":[1,2""")]
    [InlineData("""{"value":[1,2,]}""")]
    [InlineData("""{value:'text'}""")]
    [InlineData("""{"first":1 "second":2}""")]
    public void Repair_RecoversCommonStructuralDamage(
        string input)
    {
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline, input);

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.TolerantParse.Should().NotBeNull();
    }

    [Fact]
    public void Repair_CorrectsMismatchedObjectDelimiter()
    {
        const string input =
            """
            {
              "files": [
                {
                  "path": "Usings.cs",
                  "content": "global using Xunit;\n"
                ],
              "notes": "done"
            }
            """;
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateEmitFilesExpectation());

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.TolerantParse!.Outcome.Should()
            .StartWith("succeeded:");
        result.Document!.RootElement
            .GetProperty("files")
            .GetArrayLength()
            .Should()
            .Be(1);
        result.Document.RootElement
            .GetProperty("files")[0]
            .GetProperty("content")
            .GetString()
            .Should()
            .Be("global using Xunit;\n");
    }

    [Fact]
    public void Repair_UsesSchemaToRecoverRawSourceString()
    {
        const string input =
            """
            {
              "files": [
                {
                  "path": "Program.cs",
                  "content": using System;
            var message = "hello";
            "
                }
              ],
              "notes": "done"
            }
            """;
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateEmitFilesExpectation());

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.TolerantParse!.Outcome
            .Should()
            .Contain("schema-guided recovery");
        result.Document!.RootElement
            .GetProperty("files")[0]
            .GetProperty("content")
            .GetString()
            .Should()
            .Contain("var message = \"hello\";");
    }

    [Fact]
    public void Repair_RanksClosingQuoteCandidatesByTreeValidity()
    {
        const string input =
            """
            {
              "files": [
                {
                  "path": "GreetingController.cs",
                  "content": "var response = new { message = $"Hello, {name.Trim()}!" };
            return response;
            "
                }
              ],
              "notes": "done"
            }
            """;
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateEmitFilesExpectation());

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        var content = result.Document!.RootElement
            .GetProperty("files")[0]
            .GetProperty("content")
            .GetString();
        content.Should().Contain(
            "$\"Hello, {name.Trim()}!\"");
        content.Should().Contain(
            "return response;");
    }

    [Fact]
    public void Repair_DoesNotCrossIntoLaterFileObjects()
    {
        const string input =
            """
            {
              "files": [
                {
                  "path": "One.cs",
                  "content": "first"
                },
                {
                  "path": "Two.cs",
                  "content": "var response = new { message = $"Hello!" };
            return response;
            "
                },
                {
                  "path": "Three.cs",
                  "content": "third"
                }
              ],
              "notes": "done"
            }
            """;
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateEmitFilesExpectation());

        result.Succeeded.Should().BeTrue();
        var files = result.Document!.RootElement
            .GetProperty("files");
        files.GetArrayLength().Should().Be(3);
        files[0].GetProperty("content")
            .GetString().Should().Be("first");
        files[1].GetProperty("content")
            .GetString().Should()
            .Contain("$\"Hello!\"");
        files[2].GetProperty("content")
            .GetString().Should().Be("third");

        foreach (var file in files.EnumerateArray())
        {
            file.EnumerateObject()
                .Count(property =>
                    property.NameEquals("content"))
                .Should()
                .Be(1);
        }
    }

    [Fact]
    public void Repair_RecoversSchemaStringWithoutOpeningQuoteAtEndOfInput()
    {
        const string input =
            """
            {
              "files": [
                {
                  "path": "GreetingApiTests.cs",
                  "content": using System.Net;
            using System.Net.Http.Json;
            using Xunit;

            public sealed class GreetingApiTests
            {
                [Fact]
                public async Task ReturnsGreeting()
                {
                    var response = await client.GetFromJsonAsync<GreetingResponse>("/greeting?name=Solo");
                    Assert.Equal("Hello, Solo!", response!.Message);
                }
            }

            public record GreetingResponse(string Message);
            }
            """;
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateEmitFilesExpectation());

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.TolerantParse!.Outcome
            .Should()
            .Contain("schema-guided recovery");
        var files = result.Document!.RootElement
            .GetProperty("files");
        files.GetArrayLength().Should().Be(1);
        var content = files[0]
            .GetProperty("content")
            .GetString()!
            .Replace("\r\n", "\n");
        content.Should().Contain(
            "GetFromJsonAsync<GreetingResponse>(\"/greeting?name=Solo\")");
        content.Should().Contain(
            "Assert.Equal(\"Hello, Solo!\", response!.Message);");
        content.Should().EndWith(
            "public record GreetingResponse(string Message);\n}");
    }

    [Fact]
    public void Repair_InsertsMissingClosingQuoteAndContainerClosers()
    {
        const string input =
            """{"files":[{"path":"Program.cs","content":"app.Run();""";
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateEmitFilesExpectation());

        result.Succeeded.Should().BeTrue();
        result.Document!.RootElement
            .GetProperty("files")[0]
            .GetProperty("content")
            .GetString()
            .Should()
            .Be("app.Run();");
        result.TolerantParse!.Outcome
            .Should()
            .Contain("inserted closing string quote");
    }

    [Fact]
    public void Repair_TreatsQuoteFollowedBySourceTextAsEmbeddedContent()
    {
        const string input =
            """{"value":"something something" something"}""";
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateStringValueExpectation());

        result.Succeeded.Should().BeTrue();
        result.Document!.RootElement
            .GetProperty("value")
            .GetString()
            .Should()
            .Be("something something\" something");
    }

    [Fact]
    public void Repair_PreservesBackslashesInSchemaGuidedRawSource()
    {
        const string input =
            """{"value": var path = @"C:\temp\new";""";
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateStringValueExpectation());

        result.Succeeded.Should().BeTrue();
        result.Document!.RootElement
            .GetProperty("value")
            .GetString()
            .Should()
            .Be("""var path = @"C:\temp\new";""");
    }

    [Fact]
    public void Repair_DecodesJsonEscapesInSchemaGuidedRawString()
    {
        const string input =
            """{"value": using Solo.Generated;\nvar name = \"Jeno\";\n"}""";
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateStringValueExpectation());

        result.Succeeded.Should().BeTrue();
        result.Document!.RootElement
            .GetProperty("value")
            .GetString()!
            .Replace("\r\n", "\n")
            .Should()
            .Be("using Solo.Generated;\nvar name = \"Jeno\";\n");
    }

    [Fact]
    public void Repair_DecodesJsonUnicodeEscapeWhileCompletingStructure()
    {
        const string input =
            """{"value":"\u0041""";
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateStringValueExpectation());

        result.Succeeded.Should().BeTrue();
        result.Document!.RootElement
            .GetProperty("value")
            .GetString()
            .Should()
            .Be("A");
    }

    [Fact]
    public void Repair_InsertsCurrentCloserBeforeAncestorCloser()
    {
        const string input =
            """{"files":[{"path":"a","content":"x"]}""";
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateEmitFilesExpectation());

        result.Succeeded.Should().BeTrue();
        result.Document!.RootElement
            .GetProperty("files")
            .GetArrayLength()
            .Should()
            .Be(1);
        result.Document.RootElement
            .GetProperty("files")[0]
            .GetProperty("content")
            .GetString()
            .Should()
            .Be("x");
    }

    [Theory]
    [InlineData("""{"files":[]]}""")]
    [InlineData("""{"files":[]}}""")]
    public void Repair_IgnoresUnmatchedDuplicateClosers(
        string input)
    {
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateEmitFilesExpectation());

        result.Succeeded.Should().BeTrue();
        result.Document!.RootElement
            .GetProperty("files")
            .GetArrayLength()
            .Should()
            .Be(0);
    }

    [Fact]
    public void Repair_PreservesLegitimateAdjacentClosers()
    {
        const string input =
            """{"outer":{"inner":[[]]}}""";
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline, input);

        result.Succeeded.Should().BeTrue();
        result.Document!.RootElement
            .GetProperty("outer")
            .GetProperty("inner")[0]
            .GetArrayLength()
            .Should()
            .Be(0);
    }

    [Fact]
    public void Repair_ReconsidersPrematureRootCloserBeforeLaterSchemaProperty()
    {
        const string input =
            """
            {
              "tasks": [
                {
                  "id": "T01",
                  "acceptanceCriteria": [{ "id": "AC01" }]
                }
              ]
            },
              "modules": [{ "name": "Core" }]
            }
            """;
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreatePlanningExpectation());

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.Document!.RootElement
            .GetProperty("tasks")
            .GetArrayLength()
            .Should()
            .Be(1);
        result.Document.RootElement
            .TryGetProperty(
                "modules",
                out var modules)
            .Should()
            .BeTrue(
                result.Document.RootElement.GetRawText());
        modules[0]
            .GetProperty("name")
            .GetString()
            .Should()
            .Be("Core");
        result.TolerantParse!.Outcome
            .Should()
            .Contain("ignored premature '}'");
    }

    [Fact]
    public void Repair_ReconsidersPrematureNestedCloserWhenSchemaOwnsProperty()
    {
        const string input =
            """{"outer":{"a":1},"b":2}}""";
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateNestedObjectExpectation());

        result.Succeeded.Should().BeTrue();
        result.Document!.RootElement
            .GetProperty("outer")
            .GetProperty("b")
            .GetInt32()
            .Should()
            .Be(2);
    }

    [Fact]
    public void Repair_DoesNotMoveParentPropertyIntoCompletedNestedObject()
    {
        const string input =
            """{"outer":{"a":1},"b":2}""";
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateParentPropertyExpectation());

        result.Succeeded.Should().BeTrue();
        result.Document!.RootElement
            .GetProperty("outer")
            .TryGetProperty(
                "b",
                out _)
            .Should()
            .BeFalse();
        result.Document.RootElement
            .GetProperty("b")
            .GetInt32()
            .Should()
            .Be(2);
    }

    [Theory]
    [InlineData("[1],2]", 2)]
    [InlineData("[[1],2]", 2)]
    public void Repair_UsesContainerLookbackForArrayCloser(
        string input,
        int expectedRootItems)
    {
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline, input);

        result.Succeeded.Should().BeTrue();
        result.Document!.RootElement
            .GetArrayLength()
            .Should()
            .Be(expectedRootItems);
    }

    [Fact]
    public void Repair_ReparentsPropertyAfterMissingNestedClosers()
    {
        const string input =
            """
            {
              "leafTasks": [{
                "artifacts": [{
                  "requirements": [],
                  "verificationKinds": ["Compilation"]
                }],
                "architectureGaps": []
              }
            """;
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": {
                "leafTasks": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "artifacts": {
                        "type": "array",
                        "items": {
                          "type": "object",
                          "properties": {
                            "requirements": { "type": "array" }
                          },
                          "required": ["requirements"]
                        }
                      },
                      "verificationKinds": { "type": "array" }
                    },
                    "required": ["artifacts", "verificationKinds"]
                  }
                },
                "architectureGaps": { "type": "array" }
              },
              "required": ["leafTasks", "architectureGaps"]
            }
            """)!;
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline, input, expectation);

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.TolerantParse!.Outcome
            .Should().Contain("ancestor property 'verificationKinds'");
        var root = result.Document!.RootElement;
        expectation.ValidateShape(
                System.Text.Json.Nodes.JsonNode.Parse(
                    root.GetRawText())!)
            .Should().BeEmpty();
        var leaf = root.GetProperty("leafTasks")[0];
        leaf.GetProperty("verificationKinds")[0]
            .GetString().Should().Be("Compilation");
        leaf.GetProperty("artifacts")[0]
            .TryGetProperty("verificationKinds", out _)
            .Should().BeFalse();
        root.GetProperty("architectureGaps")
            .GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Repair_UnwindsRepeatedlyForPropertiesOwnedByDifferentAncestors()
    {
        const string input =
            """
            {"leafTasks":[{"artifacts":[{"requirements":[],"verificationKinds":[],"architectureGaps":[]
            """;
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": {
                "leafTasks": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "artifacts": {
                        "type": "array",
                        "items": {
                          "type": "object",
                          "properties": {
                            "requirements": { "type": "array" }
                          }
                        }
                      },
                      "verificationKinds": { "type": "array" }
                    }
                  }
                },
                "architectureGaps": { "type": "array" }
              }
            }
            """)!;
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline, input, expectation);

        result.Succeeded.Should().BeTrue();
        var root = result.Document!.RootElement;
        root.GetProperty("architectureGaps")
            .GetArrayLength().Should().Be(0);
        root.GetProperty("leafTasks")[0]
            .GetProperty("verificationKinds")
            .GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void Repair_KeepsPropertyAtCurrentLevelWhenCurrentSchemaOwnsIt()
    {
        const string input =
            """
            {"outer":{"inner":{"status":"inner"
            """;
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": {
                "status": { "type": "string" },
                "outer": {
                  "type": "object",
                  "properties": {
                    "inner": {
                      "type": "object",
                      "properties": {
                        "status": { "type": "string" }
                      }
                    }
                  }
                }
              }
            }
            """)!;
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline, input, expectation);

        result.Succeeded.Should().BeTrue();
        var root = result.Document!.RootElement;
        root.TryGetProperty("status", out _).Should().BeFalse();
        root.GetProperty("outer").GetProperty("inner")
            .GetProperty("status").GetString().Should().Be("inner");
    }

    [Fact]
    public void Repair_UsesNearestAncestorWhenSeveralAncestorsOwnProperty()
    {
        const string input =
            """
            {"status":"root","outer":{"inner":{"value":1,"status":"nearest"
            """;
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": {
                "status": { "type": "string" },
                "outer": {
                  "type": "object",
                  "properties": {
                    "status": { "type": "string" },
                    "inner": {
                      "type": "object",
                      "properties": {
                        "value": { "type": "integer" }
                      }
                    }
                  }
                }
              }
            }
            """)!;
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline, input, expectation);

        result.Succeeded.Should().BeTrue();
        var root = result.Document!.RootElement;
        root.GetProperty("status").GetString().Should().Be("root");
        root.GetProperty("outer").GetProperty("status")
            .GetString().Should().Be("nearest");
        root.GetProperty("outer").GetProperty("inner")
            .TryGetProperty("status", out _).Should().BeFalse();
    }

    [Fact]
    public void Repair_DoesNotReparentUnknownExtensionProperty()
    {
        const string input =
            """
            {"outer":{"inner":{"extension":1
            """;
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": {
                "outer": {
                  "type": "object",
                  "properties": {
                    "inner": {
                      "type": "object",
                      "properties": {}
                    }
                  }
                }
              }
            }
            """)!;
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline, input, expectation);

        result.Succeeded.Should().BeTrue();
        var root = result.Document!.RootElement;
        root.GetProperty("outer").GetProperty("inner")
            .GetProperty("extension").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Repair_DoesNotReparentFromOpenObjectSchema()
    {
        const string input =
            """
            {"status":"root","payload":{"status":"local"
            """;
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": {
                "status": { "type": "string" },
                "payload": {}
              }
            }
            """)!;
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline, input, expectation);

        result.Succeeded.Should().BeTrue();
        var root = result.Document!.RootElement;
        root.GetProperty("status").GetString().Should().Be("root");
        root.GetProperty("payload").GetProperty("status")
            .GetString().Should().Be("local");
    }

    [Fact]
    public void Repair_UsesSchemaToExpandDoubleSerializedArray()
    {
        const string input =
            """
            {
              "files": "[{\"path\":\"Program.cs\",\"content\":\"app.Run();\"}]",
              "notes": "done"
            }
            """;
        var pipeline = CreatePipeline();

        using var result = Repair(pipeline,
            input,
            CreateEmitFilesExpectation());

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.NodeRepairs.Should().Contain(
            report =>
                report.Name == "schema-guided-json-string-expansion" &&
                report.Status == StrategyStatus.Succeeded);
        result.Document!.RootElement
            .GetProperty("files")
            .ValueKind.Should()
            .Be(JsonValueKind.Array);
    }

    [Fact]
    public void Repair_ExpandsDoubleSerializedArrayDuringTolerantRecovery()
    {
        const string input =
            """
            {
              "files": "[{\"path\":\"Program.cs\",\"content\":\"app.Run();\"}]",
              "notes": done
            }
            """;
        var pipeline = CreatePipeline();

        using var result = Repair(
            pipeline,
            input,
            CreateEmitFilesExpectation());

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.TolerantParse.Should().NotBeNull();
        result.TolerantParse!.Outcome.Should().Contain(
            "expanded double-encoded");
        result.Document!.RootElement
            .GetProperty("files")
            .ValueKind.Should()
            .Be(JsonValueKind.Array);
    }

    private static JsonRepairResult Repair(
        IJsonRepairPipeline pipeline,
        string input,
        JsonSchemaExpectation? expectation = null) =>
        pipeline.RepairAsync(input, expectation)
            .GetAwaiter()
            .GetResult();

    private static JsonRepairPipeline CreatePipeline() =>
        new(
            [],
            [new SalvageRepairStrategy()],
            [
                new SchemaGuidedJsonStringExpansionStrategy()
            ],
            NullLogger<JsonRepairPipeline>.Instance);

    private static JsonSchemaExpectation
        CreateEmitFilesExpectation() =>
        JsonSchemaExpectation.FromSchemaJson(
            CreateEmitFilesSchema())!;

    private static JsonSchemaExpectation
        CreateStringValueExpectation() =>
        JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": {
                "value": { "type": "string" }
              },
              "required": ["value"]
            }
            """)!;

    private static JsonSchemaExpectation
        CreatePlanningExpectation() =>
        JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": {
                "tasks": { "type": "array" },
                "modules": { "type": "array" }
              },
              "required": ["tasks", "modules"]
            }
            """)!;

    private static JsonSchemaExpectation
        CreateNestedObjectExpectation() =>
        JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": {
                "outer": {
                  "type": "object",
                  "properties": {
                    "a": { "type": "integer" },
                    "b": { "type": "integer" }
                  },
                  "required": ["a", "b"]
                }
              },
              "required": ["outer"]
            }
            """)!;

    private static JsonSchemaExpectation
        CreateParentPropertyExpectation() =>
        JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type": "object",
              "properties": {
                "outer": {
                  "type": "object",
                  "properties": {
                    "a": { "type": "integer" }
                  },
                  "required": ["a"]
                },
                "b": { "type": "integer" }
              },
              "required": ["outer", "b"]
            }
            """)!;

    private static string CreateEmitFilesSchema() =>
        """
        {
          "type": "object",
          "properties": {
            "files": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "path": { "type": "string" },
                  "content": { "type": "string" }
                },
                "required": ["path", "content"]
              }
            },
            "notes": { "type": "string" }
          },
          "required": ["files"]
        }
        """;
}
