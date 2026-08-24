using System.Globalization;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Converts string values to numbers where the schema requires a number and
/// the model emitted a quoted literal, e.g. <c>"42"</c> → <c>42</c>. Integral
/// strings become <see cref="long"/> values when they fit; anything else
/// becomes a <see cref="double"/> parsed with the invariant culture.
/// </summary>
public sealed class SchemaGuidedStringToNumberCoercionStrategy
    : INodeRepair
{
    public string Name => "schema-guided-string-to-number";

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

        // Root-level quoted number.
        if (node is JsonValue value &&
            value.TryGetValue<string>(out var text) &&
            effective.ExpectedKind ==
            JsonSchemaFieldKind.Number &&
            TryCoerceNumber(
                text,
                out var coerced))
        {
            return new(new NodeRepairAttempt(
                RepairOutcome.Repaired,
                coerced));
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
                    JsonSchemaFieldKind.Number &&
                    TryCoerceNumber(
                        propertyText,
                        out var coercedProperty))
                {
                    jsonObject[property.Key] = coercedProperty;
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
                    JsonSchemaFieldKind.Number &&
                    TryCoerceNumber(
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

    internal static bool TryCoerceNumber(
        string text,
        out JsonNode coerced)
    {
        coerced = null!;

        var candidate = text.Trim();
        if (candidate.Length == 0)
        {
            return false;
        }

        if (long.TryParse(
                candidate,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var integral))
        {
            coerced = JsonValue.Create(integral);
            return true;
        }

        if (double.TryParse(
                candidate,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var floating) &&
            double.IsFinite(floating))
        {
            coerced = JsonValue.Create(floating);
            return true;
        }

        return false;
    }
}
