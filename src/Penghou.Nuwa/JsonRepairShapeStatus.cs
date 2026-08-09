namespace Penghou.Nuwa;

/// <summary>Whether repaired JSON matches the structural subset of a supplied schema.</summary>
public enum JsonRepairShapeStatus
{
    /// <summary>No schema expectation was supplied.</summary>
    NotEvaluated,

    /// <summary>The JSON matches the types and required shape used by recovery.</summary>
    Matched,

    /// <summary>The JSON is syntactically valid but does not match the expected shape.</summary>
    Mismatched
}
