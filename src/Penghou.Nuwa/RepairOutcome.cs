namespace Penghou.Nuwa;

/// <summary>
/// How a repair strategy evaluated the input it was given.
/// </summary>
public enum RepairOutcome
{
    /// <summary>The input is not a shape this strategy handles.</summary>
    NotApplicable,

    /// <summary>The strategy attempted to repair but produced nothing usable.</summary>
    Failed,

    /// <summary>The strategy produced a repaired candidate.</summary>
    Repaired
}
