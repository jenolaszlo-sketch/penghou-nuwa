using FluentAssertions;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Tests.Strategies;

public sealed class SchemaGuidedArrayWrapStrategyTests
{
    [Fact]
    public async Task CompatibleObject_IsWrapped()
    {
        using var result = await RepairAsync(
            """{"items":{"name":"one"}}""",
            """{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","required":["name"],"properties":{"name":{"type":"string"}}}}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Matched);
        result.Root!["items"]!.AsArray().Should().ContainSingle();
    }

    [Fact]
    public async Task ObjectMissingRequiredItemProperty_IsNotWrapped()
    {
        using var result = await RepairAsync(
            """{"items":{"title":"one"}}""",
            """{"type":"object","properties":{"items":{"type":"array","items":{"type":"object","required":["name"],"properties":{"name":{"type":"string"}}}}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!["items"].Should().BeOfType<JsonObject>();
    }

    [Fact]
    public async Task IncompatiblePrimitive_IsNotWrapped()
    {
        using var result = await RepairAsync(
            """{"items":true}""",
            """{"type":"object","properties":{"items":{"type":"array","items":{"type":"string"}}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!["items"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task SafelyCoercibleItem_IsCoercedAndWrappedAtomically()
    {
        using var result = await RepairAsync(
            """{"items":"42"}""",
            """{"type":"object","properties":{"items":{"type":"array","items":{"type":"integer"}}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Matched);
        result.Root!["items"]![0]!.GetValue<long>().Should().Be(42);
    }

    [Fact]
    public async Task FractionalValueForIntegerItem_IsNotCoercedOrWrapped()
    {
        using var result = await RepairAsync(
            """{"items":"4.2"}""",
            """{"type":"object","properties":{"items":{"type":"array","items":{"type":"integer"}}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!["items"]!.GetValue<string>().Should().Be("4.2");
    }

    [Fact]
    public async Task FractionalStringForIntegerProperty_IsNotMutated()
    {
        using var result = await RepairAsync(
            """{"count":"4.2"}""",
            """{"type":"object","properties":{"count":{"type":"integer"}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!["count"]!.GetValue<string>().Should().Be("4.2");
    }

    [Fact]
    public async Task FractionalNumber_IsRepresentedLosslesslyAsDecimal()
    {
        using var result = await RepairAsync(
            """{"value":"0.1234567890123456789012345678"}""",
            """{"type":"object","properties":{"value":{"type":"number"}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Matched);
        result.Root!["value"]!.GetValue<decimal>().Should().Be(
            0.1234567890123456789012345678m);
    }

    [Fact]
    public async Task NumberOutsideLosslessRange_IsNotMutated()
    {
        using var result = await RepairAsync(
            """{"value":"1e400"}""",
            """{"type":"object","properties":{"value":{"type":"number"}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!["value"]!.GetValue<string>().Should().Be("1e400");
    }

    [Fact]
    public async Task RootScalar_UsesTheSameItemCompatibilityRule()
    {
        using var result = await RepairAsync(
            """"true"""",
            """{"type":"array","items":{"type":"boolean"}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Matched);
        result.Root!.AsArray()[0]!.GetValue<bool>().Should().BeTrue();
    }

    private static async Task<JsonRepairResult> RepairAsync(
        string input,
        string schema)
    {
        var pipeline = JsonRepairPipeline.Create(options =>
            options.EnableSchemaCoercions());
        return await pipeline.RepairAsync(
            input,
            JsonSchemaExpectation.FromSchemaJson(schema),
            TestContext.Current.CancellationToken);
    }
}
