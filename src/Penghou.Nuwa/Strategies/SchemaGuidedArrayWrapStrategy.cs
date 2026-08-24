using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Wraps a single value in a one-element array where the schema expects an
/// array. Emitting a scalar instead of an array is one of the most common
/// structured-output mistakes, and the fix is unambiguous: the wire contract
/// requires a sequence.
/// </summary>
public sealed class SchemaGuidedArrayWrapStrategy
    : INodeRepair
{
    public string Name => "schema-guided-array-wrap";

    public ValueTask<NodeRepairAttempt> RepairAsync(
        JsonNode node,
        JsonSchemaExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(expectation);

        var effective =
            expectation.TryResolveBranch(node) ??
            expectation;

        // Root-level scalar where an array is required.
        if (node is not (JsonObject or JsonArray) &&
            effective.ExpectedKind ==
            JsonSchemaFieldKind.Array)
        {
            return new(new NodeRepairAttempt(
                RepairOutcome.Repaired,
                new JsonArray(node.DeepClone())));
        }

        var repaired = node.DeepClone();
        var changed = RepairChildren(repaired, effective);

        return new(new NodeRepairAttempt(
            changed ? RepairOutcome.Repaired : RepairOutcome.NotApplicable,
            changed ? repaired : null));
    }

    private static bool RepairChildren(
        JsonNode node,
        JsonSchemaExpectation expectation)
    {
        var changed = false;

        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (property.Value is null)
                    continue;

                var propertyExpectation =
                    expectation.GetProperty(property.Key);

                if (propertyExpectation?.ExpectedKind ==
                        JsonSchemaFieldKind.Array &&
                    property.Value is not JsonArray)
                {
                    jsonObject[property.Key] =
                        new JsonArray(property.Value.DeepClone());
                    changed = true;
                    continue;
                }

                if (propertyExpectation is not null)
                {
                    changed |= RepairChildren(
                        property.Value,
                        propertyExpectation);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            var itemExpectation = expectation.GetItem();

            for (var index = 0; index < jsonArray.Count; index++)
            {
                var item = jsonArray[index];
                if (item is null)
                    continue;

                if (itemExpectation?.ExpectedKind ==
                        JsonSchemaFieldKind.Array &&
                    item is not JsonArray)
                {
                    jsonArray[index] = new JsonArray(item.DeepClone());
                    changed = true;
                    continue;
                }

                if (itemExpectation is not null)
                {
                    changed |= RepairChildren(item, itemExpectation);
                }
            }
        }

        return changed;
    }
}
