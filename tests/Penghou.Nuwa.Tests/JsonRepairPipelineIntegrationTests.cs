using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Penghou.Nuwa.Extensions;
using Penghou.Nuwa.Strategies;
using System.Text.Json;

namespace Penghou.Nuwa.Tests;

public sealed class JsonRepairPipelineIntegrationTests
{
    [Fact]
    public void AddJsonRepair_ResolvesConfiguredPipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJsonRepair();

        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IJsonRepairPipeline>();

        using var result = Repair(pipeline, 
            """
            ```json
            {
              "name": "emit_files",
              "arguments": {
                "files": []
              }
            }
            ```
            """);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Repair_RecoversBacktickPseudoToolCall()
    {
        var pipeline = CreateDefaultPipeline();
        const string input =
            """
            ```json
            {
              "files": [
                {
                  "path": "Program.cs",
                  "content": `
            using System;
            var message = "hello";
            `
                }
              ]
            }
            ```
            """;

        using var result = Repair(pipeline, 
            input,
            CreateEmitFilesExpectation());

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.TextRepairs.Should().Contain(
            report =>
                report.Name == "pseudo-javascript-template-string" &&
                report.Status == StrategyStatus.Succeeded);
        var files = result.Document!.RootElement
            .GetProperty("files");
        files.GetArrayLength().Should().Be(1);
        files[0].GetProperty("content")
            .GetString()
            .Should()
            .Contain("\"hello\"");
    }

    [Fact]
    public void Repair_RecoversMalformedNativeToolArguments()
    {
        var pipeline = CreateDefaultPipeline();
        const string input =
            """
            {"files":[{"path":"Test.cs","content": using System;
            var message = "hello";
            "}]}
            """;

        using var result = Repair(pipeline, 
            input,
            CreateEmitFilesExpectation());

        result.Succeeded.Should().BeTrue();
        result.WasRepaired.Should().BeTrue();
        result.TolerantParse.Should().NotBeNull();
        var content = result.Document!.RootElement
            .GetProperty("files")[0]
            .GetProperty("content")
            .GetString();
        content.Should().Contain("using System;");
        content.Should().Contain("\"hello\"");
    }

    [Fact]
    public void Repair_ExpandsDoubleSerializedArrayUsingSchema()
    {
        var pipeline = CreateDefaultPipeline();
        const string input =
            """
            {
              "files": "[{\"path\":\"Program.cs\",\"content\":\"app.Run();\"}]",
              "notes": "done"
            }
            """;

        using var result = Repair(pipeline, 
            input,
            CreateEmitFilesExpectation());

        result.Succeeded.Should().BeTrue();
        result.NodeRepairs.Should().Contain(
            report =>
                report.Name == "schema-guided-json-string-expansion" &&
                report.Status == StrategyStatus.Succeeded);
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

    private static JsonRepairPipeline CreateDefaultPipeline()
    {
        var tolerantParser = new TolerantJsonSyntaxTreeParser();

        return new JsonRepairPipeline(
            [
                new MarkdownJsonFenceRepairStrategy(),
                new PseudoCSharpVerbatimStringRepairStrategy(),
                new PseudoJavaScriptTemplateStringRepairStrategy()
            ],
            [new SalvageRepairStrategy()],
            tolerantParser,
            [
                new SchemaGuidedOptionalNullRemovalStrategy(),
                new SchemaGuidedJsonStringExpansionStrategy(
                    tolerantParser)
            ],
            Microsoft.Extensions.Logging.Abstractions
                .NullLogger<JsonRepairPipeline>.Instance);
    }

    private static JsonSchemaExpectation
        CreateEmitFilesExpectation() =>
        JsonSchemaExpectation.FromSchemaJson(
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
            """)!;
}
