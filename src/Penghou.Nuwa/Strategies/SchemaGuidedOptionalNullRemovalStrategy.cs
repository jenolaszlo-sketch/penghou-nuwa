using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Removes explicit null values from optional object properties when the wire
/// schema rejects null. For such properties omission and null deserialize to
/// the same CLR value, while omission remains provider-schema compatible.
/// Required properties and schemas that explicitly allow null are preserved.
/// </summary>
public sealed class SchemaGuidedOptionalNullRemovalStrategy
    : INodeRepairStrategy
{
    public string Name => "schema-guided-optional-null-removal";

    public bool TryRepair(
        JsonNode node,
        JsonSchemaExpectation expectation,
        out JsonNode repaired)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(expectation);

        repaired = node.DeepClone();
        var changed = RepairNode(repaired, expectation);
        return changed;
    }

    private static bool RepairNode(
        JsonNode node,
        JsonSchemaExpectation expectation)
    {
        var changed = false;

        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                var propertyExpectation =
                    expectation.GetProperty(property.Key);
                if (propertyExpectation is null)
                    continue;

                if (property.Value is null)
                {
                    if (!expectation.RequiresProperty(property.Key) &&
                        !propertyExpectation.AllowsNull)
                    {
                        jsonObject.Remove(property.Key);
                        changed = true;
                    }

                    continue;
                }

                changed |= RepairNode(
                    property.Value,
                    propertyExpectation);
            }
        }
        else if (node is JsonArray jsonArray &&
                 expectation.GetItem() is { } itemExpectation)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null)
                    changed |= RepairNode(item, itemExpectation);
            }
        }

        return changed;
    }
}
