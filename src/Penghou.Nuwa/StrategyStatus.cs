namespace Penghou.Nuwa;

/// <summary>
/// The pipeline-level disposition of a configured repair strategy for a
/// single repair run.
/// </summary>
public enum StrategyStatus
{
    /// <summary>The strategy was never invoked because an earlier strategy already produced valid JSON.</summary>
    Skipped,

    /// <summary>The strategy ran and declined to modify the input.</summary>
    NotApplicable,

    /// <summary>The strategy ran but produced no usable result.</summary>
    Failed,

    /// <summary>The strategy produced a repaired candidate.</summary>
    Succeeded
}
