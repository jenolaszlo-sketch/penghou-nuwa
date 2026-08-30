using System.Text.Json;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Converts JSON number and boolean values to their deterministic JSON token
/// spelling when the schema requires a string. Nulls, objects, arrays, and
/// existing strings are never converted. Every converted value must satisfy
/// the expected structural shape after conversion.
/// </summary>
public sealed class SchemaGuidedScalarToStringCoercionStrategy
    : INodeRepair
{
    public string Name => "schema-guided-scalar-to-string";

    public ValueTask<NodeRepairAttempt> RepairAsync(
        JsonNode node,
        JsonSchemaExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(expectation);

        var effective = expectation.TryResolveBranch(node) ?? expectation;
        if (effective.ExpectedKind == JsonSchemaFieldKind.String &&
            node is JsonValue rootValue &&
            TryCoerce(rootValue, effective, out var coercedRoot))
        {
            return new(new NodeRepairAttempt(
                RepairOutcome.Repaired,
                coercedRoot,
                "Converted one numeric or boolean scalar to a schema-required string."));
        }

        var repaired = node.DeepClone();
        var correctionCount = RepairNode(repaired, expectation);

        return new(new NodeRepairAttempt(
            correctionCount > 0
                ? RepairOutcome.Repaired
                : RepairOutcome.NotApplicable,
            correctionCount > 0 ? repaired : null,
            correctionCount > 0
                ? $"Converted {correctionCount} numeric or boolean scalar(s) to schema-required strings."
                : null));
    }

    private static int RepairNode(
        JsonNode node,
        JsonSchemaExpectation expectation)
    {
        var corrections = 0;
        var effective = expectation.TryResolveBranch(node) ?? expectation;

        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (property.Value is null)
                    continue;

                var propertyExpectation = effective.GetProperty(property.Key);
                if (propertyExpectation is null)
                    continue;

                if (property.Value is JsonValue propertyValue &&
                    propertyExpectation.ExpectedKind == JsonSchemaFieldKind.String &&
                    TryCoerce(
                        propertyValue,
                        propertyExpectation,
                        out var coercedProperty))
                {
                    jsonObject[property.Key] = coercedProperty;
                    corrections++;
                    continue;
                }

                corrections += RepairNode(
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
                    itemExpectation.ExpectedKind == JsonSchemaFieldKind.String &&
                    TryCoerce(itemValue, itemExpectation, out var coercedItem))
                {
                    jsonArray[index] = coercedItem;
                    corrections++;
                    continue;
                }

                corrections += RepairNode(item, itemExpectation);
            }
        }

        return corrections;
    }

    private static bool TryCoerce(
        JsonValue value,
        JsonSchemaExpectation expectation,
        out JsonNode coerced)
    {
        coerced = null!;
        var token = value.ToJsonString();
        using var document = JsonDocument.Parse(token);
        if (document.RootElement.ValueKind is not (
                JsonValueKind.Number or
                JsonValueKind.True or
                JsonValueKind.False))
        {
            return false;
        }

        coerced = JsonValue.Create(document.RootElement.GetRawText())!;
        return expectation.ValidateShape(coerced).Count == 0;
    }
}
