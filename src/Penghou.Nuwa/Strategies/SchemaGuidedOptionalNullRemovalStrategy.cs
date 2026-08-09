using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Removes explicit null values from optional object properties when the wire
/// schema rejects null. For such properties omission and null deserialize to
/// the same CLR value, while omission remains provider-schema compatible.
/// Required properties and schemas that explicitly allow null are preserved.
/// </summary>
public sealed class SchemaGuidedOptionalNullRemovalStrategy
    : INodeRepair
{
    public string Name => "schema-guided-optional-null-removal";

    public ValueTask<NodeRepairAttempt> RepairAsync(
        JsonNode node,
        JsonSchemaExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(expectation);

        var repaired = node.DeepClone();
        var changed = RepairNode(repaired, expectation);

        return new(new NodeRepairAttempt(
            changed
                ? RepairOutcome.Repaired
                : RepairOutcome.NotApplicable,
            changed ? repaired : null));
    }

    private static bool RepairNode(
        JsonNode node,
        JsonSchemaExpectation expectation)
    {
        var changed = false;

        var effective =
            expectation.TryResolveBranch(node) ??
            expectation;

        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                var propertyExpectation =
                    effective.GetProperty(property.Key);
                if (propertyExpectation is null)
                    continue;

                if (property.Value is null)
                {
                    if (!effective.RequiresProperty(property.Key) &&
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
                 effective.GetItem() is { } itemExpectation)
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
