namespace Penghou.Nuwa;

/// <summary>
/// Per-strategy diagnostic for a repair run. Reported in configuration order,
/// including strategies the pipeline never reached.
/// </summary>
public sealed record StrategyReport(
    string Name,
    StrategyStatus Status,
    string? Repaired = null,
    string? Note = null);
