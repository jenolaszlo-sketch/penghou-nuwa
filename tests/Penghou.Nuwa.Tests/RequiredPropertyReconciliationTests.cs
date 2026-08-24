using FluentAssertions;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Tests;

public sealed class RequiredPropertyReconciliationTests
{
    [Theory]
    [InlineData("qurey", "query")]
    [InlineData("Query", "query")]
    [InlineData("user_nam", "user_name")]
    [InlineData("maxResult", "maxResults")]
    public async Task StrongUniqueNameMatch_RenamesMissingRequiredProperty(
        string source,
        string target)
    {
        var schema =
            $"{{\"type\":\"object\",\"required\":[\"{target}\"]," +
            $"\"properties\":{{\"{target}\":{{\"type\":\"string\"}}}}," +
            "\"additionalProperties\":false}";
        var result = await RepairAsync(
            $"{{\"{source}\":\"value\"}}",
            schema);

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Matched);
        result.Root!.AsObject().Should().ContainKey(target)
            .WhoseValue!.GetValue<string>().Should().Be("value");
        result.Root.AsObject().Should().NotContainKey(source);
        result.NodeRepairs.Should().Contain(report =>
            report.Name == "schema-guided-required-property-reconciliation" &&
            report.Status == StrategyStatus.Succeeded);
    }

    [Fact]
    public async Task NestedObjectAndArrayItems_AreReconciled()
    {
        const string schema = """
            {
              "type":"object",
              "required":["filters","entries"],
              "properties":{
                "filters":{
                  "type":"object",
                  "required":["query"],
                  "properties":{"query":{"type":"string"}}
                },
                "entries":{
                  "type":"array",
                  "items":{
                    "type":"object",
                    "required":["query"],
                    "properties":{"query":{"type":"string"}}
                  }
                }
              }
            }
            """;
        var result = await RepairAsync(
            """{"filters":{"qurey":"a"},"entries":[{"qurey":"b"}]}""",
            schema);

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Matched);
        result.Root!["filters"]!["query"]!.GetValue<string>().Should().Be("a");
        result.Root["entries"]![0]!["query"]!.GetValue<string>().Should().Be("b");
    }

    [Fact]
    public async Task AmbiguousNameMatch_IsNotGuessed()
    {
        const string schema = """
            {"type":"object","required":["query","quary"],"properties":{"query":{"type":"string"},"quary":{"type":"string"}}}
            """;

        var result = await RepairAsync("""{"qury":"value"}""", schema);

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!.AsObject().Should().ContainKey("qury");
    }

    [Fact]
    public async Task ExistingTarget_IsNeverOverwritten()
    {
        const string schema = """
            {"type":"object","required":["query"],"properties":{"query":{"type":"string"}},"additionalProperties":false}
            """;

        var result = await RepairAsync(
            """{"query":"a","qurey":"b"}""",
            schema);

        result.Root!["query"]!.GetValue<string>().Should().Be("a");
        result.Root["qurey"]!.GetValue<string>().Should().Be("b");
    }

    [Fact]
    public async Task IncompatibleValue_IsNotRenamed()
    {
        const string schema = """
            {"type":"object","required":["items"],"properties":{"items":{"type":"array","items":{"type":"string"}}}}
            """;

        var result = await RepairAsync("""{"itmes":{"value":1}}""", schema);

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!.AsObject().Should().ContainKey("itmes");
    }

    [Fact]
    public async Task StrongNameMatch_WithSafeCoercion_IsAppliedAtomically()
    {
        const string schema = """
            {"type":"object","required":["count"],"properties":{"count":{"type":"integer"}}}
            """;

        var result = await RepairAsync("""{"cont":"42"}""", schema);

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Matched);
        result.Root!["count"]!.GetValue<long>().Should().Be(42);
        result.Root.AsObject().Should().NotContainKey("cont");
    }

    [Fact]
    public async Task StrongNameMatch_WithUnsafeCoercion_IsRefused()
    {
        const string schema = """
            {"type":"object","required":["count"],"properties":{"count":{"type":"integer"}}}
            """;

        var result = await RepairAsync("""{"cont":"4.2"}""", schema);

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!.AsObject().Should().ContainKey("cont");
    }

    [Fact]
    public async Task ReconciliationRunsBeforeUnknownPropertyPruning()
    {
        const string schema = """
            {"type":"object","required":["query"],"properties":{"query":{"type":"string"}},"additionalProperties":false}
            """;
        var expectation = JsonSchemaExpectation.FromSchemaJson(schema)!;
        var pipeline = JsonRepairPipeline.Create(options =>
        {
            options.EnableSchemaCoercions();
            options.EnableRequiredPropertyReconciliation();
        });

        using var result = await pipeline.RepairAsync(
            """{"qurey":"value"}""",
            expectation,
            TestContext.Current.CancellationToken);

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Matched);
        result.Root!["query"]!.GetValue<string>().Should().Be("value");
    }

    private static async Task<JsonRepairResult> RepairAsync(
        string input,
        string schema)
    {
        var pipeline = JsonRepairPipeline.Create(options =>
            options.EnableRequiredPropertyReconciliation());
        return await pipeline.RepairAsync(
            input,
            JsonSchemaExpectation.FromSchemaJson(schema),
            TestContext.Current.CancellationToken);
    }
}
