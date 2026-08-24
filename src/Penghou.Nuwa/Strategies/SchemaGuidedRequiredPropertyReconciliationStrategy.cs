using System.Text;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Renames unknown properties when they uniquely explain a missing required
/// schema property through a strong deterministic name match and a directly
/// compatible value. Ambiguous mappings are left unchanged.
/// </summary>
public sealed class SchemaGuidedRequiredPropertyReconciliationStrategy
    : INodeRepair
{
    public string Name => "schema-guided-required-property-reconciliation";

    public ValueTask<NodeRepairAttempt> RepairAsync(
        JsonNode node,
        JsonSchemaExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(expectation);

        var repaired = node.DeepClone();
        var actions = new List<string>();
        var changed = RepairNode(
            repaired,
            expectation,
            "$",
            actions,
            cancellationToken);

        return new(new NodeRepairAttempt(
            changed ? RepairOutcome.Repaired : RepairOutcome.NotApplicable,
            changed ? repaired : null,
            changed ? string.Join("; ", actions) : null));
    }

    private static bool RepairNode(
        JsonNode node,
        JsonSchemaExpectation expectation,
        string path,
        ICollection<string> actions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var effective = expectation.TryResolveBranch(node);
        if (effective is null)
            return false;

        var changed = false;
        if (node is JsonObject jsonObject)
        {
            changed |= ReconcileObject(
                jsonObject,
                effective,
                path,
                actions);

            foreach (var property in jsonObject.ToArray())
            {
                if (property.Value is null ||
                    effective.GetProperty(property.Key) is not { } childExpectation)
                    continue;

                changed |= RepairNode(
                    property.Value,
                    childExpectation,
                    AppendProperty(path, property.Key),
                    actions,
                    cancellationToken);
            }
        }
        else if (node is JsonArray array &&
                 effective.GetItem() is { } itemExpectation)
        {
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is { } item)
                {
                    changed |= RepairNode(
                        item,
                        itemExpectation,
                        $"{path}[{index}]",
                        actions,
                        cancellationToken);
                }
            }
        }

        return changed;
    }

    private static bool ReconcileObject(
        JsonObject value,
        JsonSchemaExpectation expectation,
        string path,
        ICollection<string> actions)
    {
        var beforeErrors = expectation.ValidateShape(value).Count;
        var missingTargets = expectation.GetRequiredPropertyNames()
            .Where(name => !value.ContainsKey(name))
            .ToArray();
        if (missingTargets.Length == 0)
            return false;

        var proposals = new List<(string Source, string Target, JsonNode? Value, int Distance)>();
        foreach (var property in value.ToArray())
        {
            if (expectation.DefinesProperty(property.Key))
                continue;

            var matches = missingTargets
                .Select(target => new
                {
                    Target = target,
                    Distance = NameDistance(property.Key, target),
                    Prepared = expectation.GetProperty(target) is { } targetExpectation &&
                        TryPrepareValue(
                            property.Value,
                            targetExpectation,
                            out var prepared)
                            ? prepared
                            : null,
                    CompatibleNull = property.Value is null &&
                        expectation.GetProperty(target)?.Nullable == true
                })
                .Where(candidate =>
                    (candidate.Prepared is not null || candidate.CompatibleNull) &&
                    candidate.Distance <= 1)
                .OrderBy(candidate => candidate.Distance)
                .ThenBy(candidate => candidate.Target, StringComparer.Ordinal)
                .ToArray();

            if (matches.Length == 0 ||
                matches.Length > 1 && matches[0].Distance == matches[1].Distance)
                continue;

            proposals.Add((
                property.Key,
                matches[0].Target,
                matches[0].Prepared,
                matches[0].Distance));
        }

        var accepted = proposals
            .GroupBy(proposal => proposal.Target, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToArray();
        foreach (var (source, target, prepared, _) in accepted)
        {
            value.Remove(source);
            value[target] = prepared;
        }

        var afterErrors = expectation.ValidateShape(value).Count;
        foreach (var (source, target, _, distance) in accepted)
        {
            actions.Add(
                $"renamed {AppendProperty(path, source)} to '{target}' " +
                $"(name distance {distance}; compatible value; unique target; " +
                $"shape errors {beforeErrors}->{afterErrors})");
        }

        return accepted.Length > 0;
    }

    private static bool TryPrepareValue(
        JsonNode? value,
        JsonSchemaExpectation expectation,
        out JsonNode? prepared)
    {
        if (value is null)
        {
            prepared = null;
            return expectation.Nullable;
        }

        return SchemaGuidedValueConversion.TryPrepareValue(
            value,
            expectation,
            out prepared);
    }

    internal static int NameDistance(string source, string target)
    {
        var left = NormalizeName(source);
        var right = NormalizeName(target);
        if (string.Equals(left, right, StringComparison.Ordinal))
            return 0;
        if (left.Length < 4 || right.Length < 4)
            return int.MaxValue;
        return DamerauLevenshtein(left, right);
    }

    private static string NormalizeName(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
                result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }

    private static int DamerauLevenshtein(string left, string right)
    {
        var matrix = new int[left.Length + 1, right.Length + 1];
        for (var row = 0; row <= left.Length; row++) matrix[row, 0] = row;
        for (var column = 0; column <= right.Length; column++) matrix[0, column] = column;

        for (var row = 1; row <= left.Length; row++)
        {
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                matrix[row, column] = Math.Min(
                    Math.Min(matrix[row - 1, column] + 1, matrix[row, column - 1] + 1),
                    matrix[row - 1, column - 1] + cost);
                if (row > 1 && column > 1 &&
                    left[row - 1] == right[column - 2] &&
                    left[row - 2] == right[column - 1])
                {
                    matrix[row, column] = Math.Min(
                        matrix[row, column],
                        matrix[row - 2, column - 2] + 1);
                }
            }
        }

        return matrix[left.Length, right.Length];
    }

    private static string AppendProperty(string path, string property) =>
        $"{path}.{property}";
}
