using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Removes properties the schema does not declare when the schema sets
/// <c>additionalProperties: false</c>. Models frequently echo prompt
/// fragments or invent metadata keys; strict wire contracts reject such
/// objects outright, so pruning is often the only path to a usable payload.
/// Declared properties — including required ones — are never touched.
/// </summary>
public sealed class SchemaGuidedUnknownPropertyPruneStrategy
    : INodeRepair
{
    public string Name => "schema-guided-unknown-property-prune";

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
            changed ? RepairOutcome.Repaired : RepairOutcome.NotApplicable,
            changed ? repaired : null));
    }

    private static bool CanRepair(
        JsonNode node,
        JsonSchemaExpectation expectation)
    {
        var effective =
            expectation.TryResolveBranch(node) ??
            expectation;

        if (node is JsonObject jsonObject)
        {
            if (ForbidsAdditionalProperties(effective.Schema) &&
                jsonObject.Any(property =>
                    !effective.DefinesProperty(property.Key)))
            {
                return true;
            }

            foreach (var property in jsonObject)
            {
                var propertyExpectation =
                    effective.GetProperty(property.Key);
                if (property.Value is not null &&
                    propertyExpectation is not null &&
                    CanRepair(property.Value, propertyExpectation))
                {
                    return true;
                }
            }
        }
        else if (node is JsonArray jsonArray &&
                 effective.GetItem() is { } itemExpectation)
        {
            foreach (var item in jsonArray)
            {
                if (item is not null &&
                    CanRepair(item, itemExpectation))
                {
                    return true;
                }
            }
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

        if (node is JsonObject jsonObject &&
            ForbidsAdditionalProperties(effective.Schema))
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (!effective.DefinesProperty(property.Key))
                {
                    jsonObject.Remove(property.Key);
                    changed = true;
                }
            }
        }

        if (node is JsonObject objectNode)
        {
            foreach (var property in objectNode.ToArray())
            {
                var propertyExpectation =
                    effective.GetProperty(property.Key);
                if (property.Value is null ||
                    propertyExpectation is null)
                    continue;

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
                {
                    changed |= RepairNode(item, itemExpectation);
                }
            }
        }

        return changed;
    }

    internal static bool ForbidsAdditionalProperties(JsonNode? schema) =>
        schema is JsonObject schemaObject &&
        schemaObject["additionalProperties"] is JsonValue value &&
        value.TryGetValue<bool>(out var allowed) &&
        !allowed;
}
