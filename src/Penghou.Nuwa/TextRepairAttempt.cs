namespace Penghou.Nuwa;

/// <summary>
/// The outcome of a text-repair strategy. <see cref="Repaired"/> is only
/// meaningful when <see cref="Outcome"/> is <see cref="RepairOutcome.Repaired"/>.
/// <see cref="Note"/> carries optional diagnostic detail from the strategy
/// (for example why it declined to repair).
/// </summary>
public readonly record struct TextRepairAttempt(
    RepairOutcome Outcome,
    string? Repaired,
    string? Note = null);
