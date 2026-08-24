using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Repairs string values that should have been one of a schema's declared
/// enum members. Matches case-insensitively first, then by trimmed
/// comparison, and finally by bounded edit distance (≤ 2) for typos such as
/// <c>"Actve"</c> → <c>"Active"</c>. Values with no plausible match are left
/// untouched so shape validation still reports them.
/// </summary>
public sealed class SchemaGuidedEnumFuzzyMatchStrategy
    : INodeRepair
{
    private const int MaxEditDistance = 2;

    public string Name => "schema-guided-enum-fuzzy-match";

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
            foreach (var property in jsonObject)
            {
                if (property.Value is null)
                    continue;

                var propertyExpectation =
                    effective.GetProperty(property.Key);
                if (propertyExpectation is null)
                    continue;

                if (property.Value is JsonValue value &&
                    value.TryGetValue<string>(out var text) &&
                    TryMatchEnum(
                        propertyExpectation.Schema,
                        text,
                        out var matched) &&
                    !string.Equals(
                        text,
                        matched,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                if (CanRepair(
                        property.Value,
                        propertyExpectation))
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
                if (item is null)
                    continue;

                if (item is JsonValue value &&
                    value.TryGetValue<string>(out var text) &&
                    TryMatchEnum(
                        itemExpectation.Schema,
                        text,
                        out var matched) &&
                    !string.Equals(
                        text,
                        matched,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                if (CanRepair(item, itemExpectation))
                    return true;
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

                if (property.Value is JsonValue value &&
                    value.TryGetValue<string>(out var text) &&
                    TryMatchEnum(
                        propertyExpectation.Schema,
                        text,
                        out var matched))
                {
                    jsonObject[property.Key] = matched;
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

                if (item is JsonValue value &&
                    value.TryGetValue<string>(out var text) &&
                    TryMatchEnum(
                        itemExpectation.Schema,
                        text,
                        out var matched))
                {
                    jsonArray[index] = matched;
                    changed = true;
                    continue;
                }

                changed |= RepairNode(item, itemExpectation);
            }
        }

        return changed;
    }

    internal static bool TryMatchEnum(
        JsonNode? propertySchema,
        string candidate,
        out string matched)
    {
        matched = string.Empty;

        var allowed = ReadEnumValues(propertySchema);
        if (allowed.Count == 0)
        {
            return false;
        }

        // Exact, then case-insensitive, then trimmed.
        var trimmed = candidate.Trim();
        foreach (var member in allowed)
        {
            if (member == candidate ||
                string.Equals(
                    member,
                    candidate,
                    StringComparison.OrdinalIgnoreCase) ||
                member == trimmed)
            {
                matched = member;
                return true;
            }
        }

        // Bounded edit distance for typos.
        string? best = null;
        var bestDistance = MaxEditDistance + 1;
        var hasTie = false;
        foreach (var member in allowed)
        {
            var distance = Levenshtein(
                trimmed,
                member,
                bestDistance);
            if (distance <= MaxEditDistance &&
                distance < bestDistance)
            {
                best = member;
                bestDistance = distance;
                hasTie = false;
            }
            else if (distance <= MaxEditDistance &&
                     distance == bestDistance)
            {
                hasTie = true;
            }
        }

        if (best is not null && !hasTie)
        {
            matched = best;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> ReadEnumValues(
        JsonNode? propertySchema)
    {
        if (propertySchema is not JsonObject schemaObject ||
            schemaObject["enum"] is not JsonArray enumArray)
        {
            return [];
        }

        return enumArray
            .OfType<JsonValue>()
            .Select(value =>
                value.TryGetValue<string>(out var text) ? text : null)
            .Where(text => text is not null)
            .Cast<string>()
            .ToArray();
    }

    /// <summary>Bounded Levenshtein: bails out once the distance exceeds the cap.</summary>
    internal static int Levenshtein(
        string first,
        string second,
        int cutoff)
    {
        if (Math.Abs(first.Length - second.Length) > cutoff)
        {
            return cutoff + 1;
        }

        var previous = new int[first.Length + 1];
        var current = new int[first.Length + 1];

        for (var index = 0; index <= first.Length; index++)
        {
            previous[index] = index;
        }

        for (var row = 1; row <= second.Length; row++)
        {
            current[0] = row;
            var rowMinimum = row;

            for (var column = 1; column <= first.Length; column++)
            {
                var substitution = previous[column - 1] +
                    (first[column - 1] == second[row - 1] ? 0 : 1);
                current[column] = Math.Min(
                    substitution,
                    Math.Min(
                        previous[column] + 1,
                        current[column - 1] + 1));
                rowMinimum = Math.Min(rowMinimum, current[column]);
            }

            if (rowMinimum > cutoff)
            {
                return cutoff + 1;
            }

            (previous, current) = (current, previous);
        }

        return previous[first.Length];
    }
}
