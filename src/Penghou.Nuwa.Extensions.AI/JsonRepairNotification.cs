namespace Penghou.Nuwa.Extensions.AI;

/// <summary>Audit information emitted after the middleware attempts a repair.</summary>
public sealed record JsonRepairNotification(
    string Target,
    bool Succeeded,
    bool WasRepaired,
    JsonRepairShapeStatus ShapeStatus,
    IReadOnlyList<string> ShapeErrors,
    IReadOnlyList<StrategyReport> TextRepairs,
    IReadOnlyList<StrategyReport> NodeRepairs,
    TolerantRecoveryReport? TolerantRecovery,
    double Confidence = 1);
