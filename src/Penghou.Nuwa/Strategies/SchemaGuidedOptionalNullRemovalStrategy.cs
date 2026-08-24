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

        if (!CanRepair(node, expectation))
        {
            return new(new NodeRepairAttempt(
                RepairOutcome.NotApplicable,
                null));
        }

        var repaired = node.DeepClone();
        var changed = RepairNode(repaired, expectation);

        return new(new NodeRepairAttempt(
            changed
                ? RepairOutcome.Repaired
                : RepairOutcome.NotApplicable,
            changed ? repaired : null));
    }

    private static bool CanRepair(
        JsonNode node,
        JsonSchemaExpectation expectation)
    {
        var effective = expectation.TryResolveBranch(node) ?? expectation;
        if (node is JsonObject value)
        {
            foreach (var property in value)
            {
                var child = effective.GetProperty(property.Key);
                if (child is null)
                    continue;
                if (property.Value is null)
                {
                    if (!effective.RequiresProperty(property.Key) &&
                        !child.AllowsNull)
                        return true;
                }
                else if (CanRepair(property.Value, child))
                {
                    return true;
                }
            }
        }
        else if (node is JsonArray array &&
                 effective.GetItem() is { } item)
        {
            return array.Any(value => value is not null && CanRepair(value, item));
        }

        return false;
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
