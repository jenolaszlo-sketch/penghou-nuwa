using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Fixes malformations that survive as valid-but-wrong-shaped JSON
/// (e.g. an array serialized as a string). Operates on a parsed JsonNode tree
/// because detecting "wrong shape" requires knowing each node's actual JsonValueKind.
/// </summary>
public interface INodeRepairStrategy
{
    string Name { get; }

    bool TryRepair(JsonNode node, JsonSchemaExpectation expectation, out JsonNode repaired);
}
