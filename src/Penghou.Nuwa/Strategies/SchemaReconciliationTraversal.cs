using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

internal static class SchemaReconciliationTraversal
{
    public static bool Repair(
        JsonNode node,
        JsonSchemaExpectation expectation,
        string path,
        Func<JsonObject, JsonSchemaExpectation, string, bool> reconcileObject,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var effective = expectation.TryResolveBranch(node);
        if (effective is null)
            return false;

        var changed = false;
        if (node is JsonObject value)
        {
            changed |= reconcileObject(value, effective, path);
            foreach (var property in value.ToArray())
            {
                if (property.Value is null ||
                    effective.GetProperty(property.Key) is not { } child)
                    continue;

                changed |= Repair(
                    property.Value,
                    child,
                    $"{path}.{property.Key}",
                    reconcileObject,
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
                    changed |= Repair(
                        item,
                        itemExpectation,
                        $"{path}[{index}]",
                        reconcileObject,
                        cancellationToken);
                }
            }
        }

        return changed;
    }
}
