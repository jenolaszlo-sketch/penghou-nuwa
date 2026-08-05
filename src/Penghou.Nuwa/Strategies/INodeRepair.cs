using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Fixes malformations that survive as valid-but-wrong-shaped JSON
/// (e.g. an array serialized as a string). Operates on a parsed JsonNode tree
/// because detecting "wrong shape" requires knowing each node's actual JsonValueKind.
/// </summary>
public interface INodeRepair
{
    string Name { get; }

    ValueTask<NodeRepairAttempt> RepairAsync(
        JsonNode node,
        JsonSchemaExpectation expectation,
        CancellationToken cancellationToken = default);
}
