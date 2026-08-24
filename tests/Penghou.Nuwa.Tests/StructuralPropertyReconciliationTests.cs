using FluentAssertions;

namespace Penghou.Nuwa.Tests;

public sealed class StructuralPropertyReconciliationTests
{
    [Fact]
    public async Task DistinctiveObjectShape_MapsUnrelatedPropertyName()
    {
        using var result = await RepairAsync(
            """{"payload":{"city":"Manila","days":3}}""",
            """{"type":"object","required":["forecast"],"properties":{"forecast":{"type":"object","required":["city","days"],"properties":{"city":{"type":"string"},"days":{"type":"integer"}}}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Matched);
        result.Root!.AsObject().Should().ContainKey("forecast");
        result.Root.AsObject().Should().NotContainKey("payload");
        result.NodeRepairs.Should().Contain(report =>
            report.Name == "schema-guided-structural-property-reconciliation" &&
            report.Note!.Contains("distinctive object shape") &&
            report.Note.Contains("unique target") &&
            report.Note.Contains("shape errors"));
    }

    [Fact]
    public async Task DistinctiveArrayItemShape_MapsUnrelatedPropertyName()
    {
        using var result = await RepairAsync(
            """{"payload":[{"sku":"A1"}]}""",
            """{"type":"object","required":["products"],"properties":{"products":{"type":"array","items":{"type":"object","required":["sku"],"properties":{"sku":{"type":"string"}}}}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Matched);
        result.Root!.AsObject().Should().ContainKey("products");
    }

    [Fact]
    public async Task ExactEnumMembership_MapsUnrelatedPropertyName()
    {
        using var result = await RepairAsync(
            """{"choice":"urgent"}""",
            """{"type":"object","required":["priority"],"properties":{"priority":{"type":"string","enum":["normal","urgent"]}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Matched);
        result.Root!["priority"]!.GetValue<string>().Should().Be("urgent");
    }

    [Fact]
    public async Task PrimitiveTypeCompatibilityAlone_IsRefused()
    {
        using var result = await RepairAsync(
            """{"payload":"hello"}""",
            """{"type":"object","required":["message"],"properties":{"message":{"type":"string"}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!.AsObject().Should().ContainKey("payload");
    }

    [Fact]
    public async Task ShapeMatchingMultipleTargets_IsRefused()
    {
        using var result = await RepairAsync(
            """{"payload":{"id":"A1"}}""",
            """{"type":"object","required":["source","destination"],"properties":{"source":{"type":"object","required":["id"],"properties":{"id":{"type":"string"}}},"destination":{"type":"object","required":["id"],"properties":{"id":{"type":"string"}}}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!.AsObject().Should().ContainKey("payload");
    }

    [Fact]
    public async Task EmptyArray_IsNotDistinctiveItemShapeEvidence()
    {
        using var result = await RepairAsync(
            """{"payload":[]}""",
            """{"type":"object","required":["products"],"properties":{"products":{"type":"array","items":{"type":"object","required":["sku"],"properties":{"sku":{"type":"string"}}}}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!.AsObject().Should().ContainKey("payload");
    }

    [Fact]
    public async Task UnconstrainedObject_IsNotDistinctiveShapeEvidence()
    {
        using var result = await RepairAsync(
            """{"payload":{"anything":true}}""",
            """{"type":"object","required":["metadata"],"properties":{"metadata":{"type":"object"}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!.AsObject().Should().ContainKey("payload");
    }

    [Fact]
    public async Task ArrayWithUnconstrainedItems_IsNotDistinctiveShapeEvidence()
    {
        using var result = await RepairAsync(
            """{"payload":[{"anything":true}]}""",
            """{"type":"object","required":["items"],"properties":{"items":{"type":"array","items":{}}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!.AsObject().Should().ContainKey("payload");
    }

    [Fact]
    public async Task MultipleSourcesForOneTarget_AreRefused()
    {
        using var result = await RepairAsync(
            """{"first":{"id":"A1"},"second":{"id":"B2"}}""",
            """{"type":"object","required":["source"],"properties":{"source":{"type":"object","required":["id"],"properties":{"id":{"type":"string"}}}}}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!.AsObject().Should().ContainKeys("first", "second");
    }

    [Fact]
    public async Task UnresolvedUnion_IsRefused()
    {
        using var result = await RepairAsync(
            """{"payload":{"id":"A1"}}""",
            """{"oneOf":[{"type":"object","required":["source"],"properties":{"source":{"type":"object","required":["id"],"properties":{"id":{"type":"string"}}}}},{"type":"object","required":["destination"],"properties":{"destination":{"type":"object","required":["id"],"properties":{"id":{"type":"string"}}}}}]}""");

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!.AsObject().Should().ContainKey("payload");
    }

    [Fact]
    public async Task Policy_IsOffByDefault()
    {
        var pipeline = JsonRepairPipeline.Create();
        using var result = await pipeline.RepairAsync(
            """{"payload":{"city":"Manila"}}""",
            JsonSchemaExpectation.FromSchemaJson(
                """{"type":"object","required":["forecast"],"properties":{"forecast":{"type":"object","required":["city"],"properties":{"city":{"type":"string"}}}}}"""),
            TestContext.Current.CancellationToken);

        result.ShapeStatus.Should().Be(JsonRepairShapeStatus.Mismatched);
        result.Root!.AsObject().Should().ContainKey("payload");
    }

    [Fact]
    public async Task StructuralInference_HasLowerConfidenceThanStrongNameMatch()
    {
        const string schema = """
            {"type":"object","required":["forecast"],"properties":{"forecast":{"type":"object","required":["city"],"properties":{"city":{"type":"string"}}}}}
            """;
        var structural = await RepairAsync(
            """{"payload":{"city":"Manila"}}""",
            schema);
        var namedPipeline = JsonRepairPipeline.Create(options =>
            options.EnableRequiredPropertyReconciliation());
        using var named = await namedPipeline.RepairAsync(
            """{"forecat":{"city":"Manila"}}""",
            JsonSchemaExpectation.FromSchemaJson(schema),
            TestContext.Current.CancellationToken);
        using (structural)
        {
            structural.Confidence.Should().BeLessThan(named.Confidence);
        }
    }

    private static async Task<JsonRepairResult> RepairAsync(
        string input,
        string schema)
    {
        var pipeline = JsonRepairPipeline.Create(options =>
            options.EnableStructuralPropertyReconciliation());
        return await pipeline.RepairAsync(
            input,
            JsonSchemaExpectation.FromSchemaJson(schema),
            TestContext.Current.CancellationToken);
    }
}
