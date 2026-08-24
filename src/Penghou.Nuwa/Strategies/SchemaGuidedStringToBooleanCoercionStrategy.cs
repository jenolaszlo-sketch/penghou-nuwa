using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Converts string values to booleans where the schema requires a boolean and
/// the model emitted a quoted literal, e.g. <c>"true"</c> → <c>true</c>.
/// Matching is case-insensitive; anything other than true/false is left for
/// shape validation to report.
/// </summary>
public sealed class SchemaGuidedStringToBooleanCoercionStrategy
    : INodeRepair
{
    public string Name => "schema-guided-string-to-boolean";

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

        if (node is JsonValue value &&
            value.TryGetValue<string>(out var text) &&
            effective.ExpectedKind ==
            JsonSchemaFieldKind.Boolean)
        {
            var candidate = text.Trim();
            if (candidate.Equals(
                    "true",
                    StringComparison.OrdinalIgnoreCase) ||
                candidate.Equals(
                    "false",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new(new NodeRepairAttempt(
                    RepairOutcome.Repaired,
                    JsonValue.Create(
                        bool.Parse(candidate))));
            }
        }

        var repaired = node.DeepClone();
        var changed = RepairNode(repaired, expectation);

        return new(new NodeRepairAttempt(
            changed ? RepairOutcome.Repaired : RepairOutcome.NotApplicable,
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
                if (property.Value is null)
                    continue;

                var propertyExpectation =
                    effective.GetProperty(property.Key);
                if (propertyExpectation is null)
                    continue;

                if (property.Value is JsonValue propertyValue &&
                    propertyValue.TryGetValue<string>(out var propertyText) &&
                    propertyExpectation.ExpectedKind ==
                    JsonSchemaFieldKind.Boolean &&
                    TryCoerceBoolean(
                        propertyText,
                        out var coerced))
                {
                    jsonObject[property.Key] = coerced;
                    changed = true;
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
            for (var index = 0; index < jsonArray.Count; index++)
            {
                var item = jsonArray[index];
                if (item is null)
                    continue;

                if (item is JsonValue itemValue &&
                    itemValue.TryGetValue<string>(out var itemText) &&
                    itemExpectation.ExpectedKind ==
                    JsonSchemaFieldKind.Boolean &&
                    TryCoerceBoolean(
                        itemText,
                        out var coercedItem))
                {
                    jsonArray[index] = coercedItem;
                    changed = true;
                    continue;
                }

                changed |= RepairNode(item, itemExpectation);
            }
        }

        return changed;
    }

    private static bool TryCoerceBoolean(
        string text,
        out JsonNode coerced)
    {
        coerced = null!;

        var candidate = text.Trim();
        if (candidate.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            coerced = JsonValue.Create(true);
            return true;
        }

        if (candidate.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            coerced = JsonValue.Create(false);
            return true;
        }

        return false;
    }
}
