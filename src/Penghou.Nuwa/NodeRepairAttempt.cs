using System.Text.Json.Nodes;

namespace Penghou.Nuwa;

/// <summary>
/// The outcome of a node-repair strategy. <see cref="Repaired"/> is only
/// meaningful when <see cref="Outcome"/> is <see cref="RepairOutcome.Repaired"/>.
/// <see cref="Note"/> carries optional diagnostic detail from the strategy.
/// </summary>
public readonly record struct NodeRepairAttempt(
    RepairOutcome Outcome,
    JsonNode? Repaired,
    string? Note = null);
