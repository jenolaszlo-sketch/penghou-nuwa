using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Repairs JSON values that are syntactically valid but encoded at the wrong
/// structural level, such as an array supplied as a JSON string. Only object
/// and array expectations are expanded; scalar coercion is deliberately
/// avoided because it can silently change model intent.
/// </summary>
public sealed class SchemaGuidedJsonStringExpansionStrategy(
    ITolerantJsonSyntaxTreeParser tolerantParser)
    : INodeRepairStrategy
{
    public string Name => "schema-guided-json-string-expansion";

    public bool TryRepair(
        JsonNode node,
        JsonSchemaExpectation expectation,
        out JsonNode repaired)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(expectation);

        repaired = RepairNode(
            node.DeepClone(),
            expectation,
            out var changed);

        return changed;
    }

    private JsonNode RepairNode(
        JsonNode node,
        JsonSchemaExpectation expectation,
        out bool changed)
    {
        changed = false;

        if (ShouldExpand(expectation.ExpectedKind) &&
            TryGetString(node, out var encoded))
        {
            var parsed = tolerantParser.Parse(
                encoded,
                expectation);

            if (parsed.Root is not null &&
                MatchesExpectedKind(
                    parsed.Root,
                    expectation.ExpectedKind))
            {
                node = parsed.Root;
                changed = true;
            }
        }

        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (property.Value is null)
                    continue;

                var propertyExpectation =
                    expectation.GetProperty(property.Key);

                if (propertyExpectation is null)
                    continue;

                var repairedProperty = RepairNode(
                    property.Value,
                    propertyExpectation,
                    out var propertyChanged);

                if (propertyChanged)
                {
                    jsonObject[property.Key] =
                        repairedProperty;
                    changed = true;
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            var itemExpectation =
                expectation.GetItem();

            if (itemExpectation is not null)
            {
                for (var index = 0;
                     index < jsonArray.Count;
                     index++)
                {
                    if (jsonArray[index] is not { } item)
                        continue;

                    var repairedItem = RepairNode(
                        item,
                        itemExpectation,
                        out var itemChanged);

                    if (itemChanged)
                    {
                        jsonArray[index] =
                            repairedItem;
                        changed = true;
                    }
                }
            }
        }

        return node;
    }

    private static bool ShouldExpand(
        JsonSchemaFieldKind? kind) =>
        kind is
            JsonSchemaFieldKind.Array or
            JsonSchemaFieldKind.Object;

    private static bool MatchesExpectedKind(
        JsonNode node,
        JsonSchemaFieldKind? kind) =>
        kind switch
        {
            JsonSchemaFieldKind.Array =>
                node is JsonArray,
            JsonSchemaFieldKind.Object =>
                node is JsonObject,
            _ => false
        };

    private static bool TryGetString(
        JsonNode node,
        out string value)
    {
        value = string.Empty;

        if (node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(
                out var parsed) ||
            parsed is null)
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
