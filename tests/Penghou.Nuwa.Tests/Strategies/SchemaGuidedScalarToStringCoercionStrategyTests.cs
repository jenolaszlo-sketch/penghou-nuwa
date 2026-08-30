using FluentAssertions;
using Penghou.Nuwa.Strategies;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Tests.Strategies;

public sealed class SchemaGuidedScalarToStringCoercionStrategyTests
{
    private readonly SchemaGuidedScalarToStringCoercionStrategy strategy = new();

    [Theory]
    [InlineData("42", "42")]
    [InlineData("-0.25", "-0.25")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    public async Task RequiredString_UsesDeterministicScalarTokenSpelling(
        string input,
        string expected)
    {
        var node = JsonNode.Parse(input)!;
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """{"type":"string"}""")!;

        var result = await strategy.RepairAsync(
            node,
            expectation,
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RepairOutcome.Repaired);
        result.Repaired!.GetValue<string>().Should().Be(expected);
    }

    [Fact]
    public async Task NullAndCompositeValues_AreNotStringified()
    {
        var node = JsonNode.Parse(
            """{"nullValue":null,"objectValue":{"x":1},"arrayValue":[1]}""")!;
        var expectation = JsonSchemaExpectation.FromSchemaJson(
            """
            {
              "type":"object",
              "properties":{
                "nullValue":{"type":"string"},
                "objectValue":{"type":"string"},
                "arrayValue":{"type":"string"}
              }
            }
            """)!;

        var result = await strategy.RepairAsync(
            node,
            expectation,
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RepairOutcome.NotApplicable);
        result.Repaired.Should().BeNull();
    }
}
