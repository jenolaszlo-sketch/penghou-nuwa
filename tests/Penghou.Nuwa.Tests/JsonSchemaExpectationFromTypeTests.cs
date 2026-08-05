using FluentAssertions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Penghou.Nuwa.Tests;

public sealed class JsonSchemaExpectationFromTypeTests
{
    private sealed class SimpleArguments
    {
        public string? Query { get; set; }
        public int Limit { get; set; }
        public bool IncludeDetails { get; set; }
        public double? Score { get; set; }
    }

    private sealed class EnumArguments
    {
        public WeatherKind Kind { get; set; }
    }

    private enum WeatherKind
    {
        Sunny,
        Rainy
    }

    private sealed class NestedArguments
    {
        public Address? Address { get; set; }
        public List<int>? Scores { get; set; }
        public Dictionary<string, string>? Labels { get; set; }
    }

    private sealed class Address
    {
        public string? City { get; set; }
    }

    private sealed class PascalArguments
    {
        [JsonPropertyName("user_query")]
        public string? Query { get; set; }

        [JsonIgnore]
        public string? Secret { get; set; }
    }

    private sealed class RequiredArguments
    {
        [JsonRequired]
        public string? RequiredValue { get; set; }

        public string? OptionalValue { get; set; }
    }

    private sealed class SelfReferencingArguments
    {
        public string? Name { get; set; }
        public SelfReferencingArguments? Child { get; set; }
    }

    [Fact]
    public void FromType_MapsPrimitivePropertiesToKinds()
    {
        var expectation = JsonSchemaExpectation.FromType<SimpleArguments>();

        expectation.PropertyKinds.Should().Contain("Query", JsonSchemaFieldKind.String);
        expectation.PropertyKinds.Should().Contain("Limit", JsonSchemaFieldKind.Number);
        expectation.PropertyKinds.Should().Contain("IncludeDetails", JsonSchemaFieldKind.Boolean);
        expectation.PropertyKinds.Should().Contain("Score", JsonSchemaFieldKind.Number);
    }

    [Fact]
    public void FromType_HonorsCamelCaseNamingPolicy()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var expectation = JsonSchemaExpectation.FromType<SimpleArguments>(options);

        expectation.PropertyKinds.Should().ContainKeys(
            "query", "limit", "includeDetails", "score");
    }

    [Fact]
    public void FromType_MapsEnumToString()
    {
        var expectation = JsonSchemaExpectation.FromType<EnumArguments>();

        expectation.PropertyKinds.Should().Contain("Kind", JsonSchemaFieldKind.String);
    }

    [Fact]
    public void FromType_MapsNestedObjectArrayAndDictionary()
    {
        var expectation = JsonSchemaExpectation.FromType<NestedArguments>();

        expectation.PropertyKinds.Should().Contain("Address", JsonSchemaFieldKind.Object);
        expectation.PropertyKinds.Should().Contain("Scores", JsonSchemaFieldKind.Array);
        expectation.PropertyKinds.Should().Contain("Labels", JsonSchemaFieldKind.Object);
    }

    [Fact]
    public void FromType_GeneratedSchemaValidatesMatchingPayload()
    {
        var expectation = JsonSchemaExpectation.FromType<NestedArguments>();

        var payload = JsonNode.Parse(
            """{"address":{"city":"London"},"scores":[1,2,3],"labels":{"a":"b"}}""");

        expectation.Validate(payload!).Should().BeEmpty();
    }

    [Fact]
    public void FromType_GeneratedSchemaReportsMismatchedPayload()
    {
        var expectation = JsonSchemaExpectation.FromType<NestedArguments>();

        var payload = JsonNode.Parse(
            """{"Address":"not-an-object","Scores":"not-an-array"}""");

        expectation.Validate(payload!).Should().NotBeEmpty();
    }

    [Fact]
    public void FromType_HonorsJsonPropertyNameAndJsonIgnore()
    {
        var expectation = JsonSchemaExpectation.FromType<PascalArguments>();

        expectation.PropertyKinds.Should().ContainKey("user_query");
        expectation.PropertyKinds.Should().NotContainKey("Secret");
    }

    [Fact]
    public void FromType_MarksJsonRequiredPropertiesAsRequired()
    {
        var expectation = JsonSchemaExpectation.FromType<RequiredArguments>();

        var schema = expectation.Schema!.AsObject();
        var required = schema["required"]!.AsArray();

        required.Should().Contain(node => node!.GetValue<string>() == "RequiredValue");
        required.Should().NotContain(node => node!.GetValue<string>() == "OptionalValue");
    }

    [Fact]
    public void FromType_HandlesSelfReferencingTypes()
    {
        var expectation = JsonSchemaExpectation.FromType<SelfReferencingArguments>();

        expectation.PropertyKinds.Should().Contain("Name", JsonSchemaFieldKind.String);
        expectation.PropertyKinds.Should().Contain("Child", JsonSchemaFieldKind.Object);
    }

    [Fact]
    public void FromType_FromTypeNonGeneric_MatchesGeneric()
    {
        var generic = JsonSchemaExpectation.FromType<SimpleArguments>();
        var nonGeneric = JsonSchemaExpectation.FromType(typeof(SimpleArguments));

        generic.Schema!.ToJsonString()
            .Should()
            .Be(nonGeneric.Schema!.ToJsonString());
    }

    [Fact]
    public void FromType_NullType_Throws()
    {
        var act = () => JsonSchemaExpectation.FromType((Type)null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
