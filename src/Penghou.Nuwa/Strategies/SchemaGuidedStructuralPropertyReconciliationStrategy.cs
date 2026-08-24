using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Reconciles an unknown property to a missing required property only when a
/// distinctive object shape, array-item shape, or exact enum membership
/// identifies one target. Primitive type compatibility alone is insufficient.
/// </summary>
public sealed class SchemaGuidedStructuralPropertyReconciliationStrategy
    : INodeRepair
{
    public string Name => "schema-guided-structural-property-reconciliation";

    public ValueTask<NodeRepairAttempt> RepairAsync(
        JsonNode node,
        JsonSchemaExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(expectation);

        var repaired = node.DeepClone();
        var evidence = new List<string>();
        var changed = SchemaReconciliationTraversal.Repair(
            repaired,
            expectation,
            "$",
            (value, effective, path) =>
                ReconcileObject(value, effective, path, evidence),
            cancellationToken);

        return new(new NodeRepairAttempt(
            changed ? RepairOutcome.Repaired : RepairOutcome.NotApplicable,
            changed ? repaired : null,
            changed ? SchemaRepairDiagnostics.Join(evidence) : null));
    }

    private static bool ReconcileObject(
        JsonObject value,
        JsonSchemaExpectation expectation,
        string path,
        ICollection<string> evidence)
    {
        var missing = expectation.GetRequiredPropertyNames()
            .Where(name => !value.ContainsKey(name))
            .Select(name => (Name: name, Expectation: expectation.GetProperty(name)))
            .Where(target => target.Expectation is not null)
            .ToArray();
        if (missing.Length == 0)
            return false;

        var beforeErrors = expectation.ValidateShape(value).Count;
        var proposals = new List<(string Source, string Target, string Reason)>();
        foreach (var property in value.ToArray())
        {
            if (property.Value is null || expectation.DefinesProperty(property.Key))
                continue;

            var matches = missing
                .Select(target => (
                    target.Name,
                    Reason: MatchReason(property.Value, target.Expectation!)))
                .Where(match => match.Reason is not null)
                .ToArray();
            if (matches.Length == 1)
            {
                proposals.Add((
                    property.Key,
                    matches[0].Name,
                    matches[0].Reason!));
            }
        }

        var accepted = proposals
            .GroupBy(proposal => proposal.Target, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToArray();
        if (accepted.Length == 0)
            return false;

        var candidate = value.DeepClone().AsObject();
        foreach (var proposal in accepted)
        {
            var sourceValue = candidate[proposal.Source];
            candidate.Remove(proposal.Source);
            candidate[proposal.Target] = sourceValue;
        }

        var afterErrors = expectation.ValidateShape(candidate).Count;
        if (afterErrors >= beforeErrors)
            return false;

        foreach (var proposal in accepted)
        {
            var sourceValue = value[proposal.Source];
            value.Remove(proposal.Source);
            value[proposal.Target] = sourceValue;
        }

        foreach (var proposal in accepted)
        {
            evidence.Add(
                $"mapped {path}.{proposal.Source} to '{proposal.Target}' " +
                $"({proposal.Reason}; unique target; shape errors {beforeErrors}->{afterErrors})");
        }

        return true;
    }

    private static string? MatchReason(
        JsonNode value,
        JsonSchemaExpectation target)
    {
        if (target.ExpectedKind == JsonSchemaFieldKind.Object &&
            value is JsonObject objectValue &&
            HasDistinctiveObjectEvidence(objectValue, target) &&
            target.ValidateShape(value).Count == 0)
        {
            return "distinctive object shape";
        }

        if (target.ExpectedKind == JsonSchemaFieldKind.Array &&
            value is JsonArray { Count: > 0 } arrayValue &&
            HasDistinctiveArrayEvidence(arrayValue, target) &&
            target.ValidateShape(value).Count == 0)
        {
            return "distinctive array-item shape";
        }

        return IsExactEnumMember(value, target.Schema)
            ? "exact enum membership"
            : null;
    }

    private static bool HasDistinctiveObjectEvidence(
        JsonObject value,
        JsonSchemaExpectation target)
    {
        var required = target.GetRequiredPropertyNames();
        if (required.Any(value.ContainsKey))
            return true;

        if (target.Schema is not JsonObject schemaObject ||
            schemaObject["properties"] is not JsonObject properties)
            return false;

        return value.Any(property =>
            properties[property.Key] is JsonObject propertySchema &&
            HasValueConstraint(propertySchema));
    }

    private static bool HasDistinctiveArrayEvidence(
        JsonArray value,
        JsonSchemaExpectation target)
    {
        var item = target.GetItem();
        if (item is null)
            return false;

        return value.All(element =>
            element is not null &&
            (element is JsonObject objectElement
                ? HasDistinctiveObjectEvidence(objectElement, item)
                : item.Schema is JsonObject itemSchema &&
                    HasValueConstraint(itemSchema)));
    }

    private static bool HasValueConstraint(JsonObject schema) =>
        schema.ContainsKey("const") ||
        schema.ContainsKey("enum") ||
        schema["type"] is not null;

    private static bool IsExactEnumMember(JsonNode value, JsonNode? schema) =>
        schema is JsonObject schemaObject &&
        schemaObject["enum"] is JsonArray members &&
        members.Any(member =>
            member is not null &&
            JsonNode.DeepEquals(member, value));
}
