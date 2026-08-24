namespace Penghou.Nuwa;

/// <summary>Resource limits applied to a repair operation.</summary>
public sealed record JsonRepairLimits
{
    /// <summary>Default conservative limits for model-produced JSON.</summary>
    public static JsonRepairLimits Default { get; } = new();

    /// <summary>Maximum number of UTF-16 characters accepted as input.</summary>
    public int MaxInputLength { get; init; } = 4 * 1024 * 1024;

    /// <summary>Maximum number of UTF-16 characters returned as repaired JSON.</summary>
    public int MaxOutputLength { get; init; } = 8 * 1024 * 1024;

    /// <summary>Maximum nesting depth accepted by tolerant recovery.</summary>
    public int MaxDepth { get; init; } = 128;

    /// <summary>
    /// Maximum number of tolerant-parser and node-tree corrections a repair
    /// operation may apply.
    /// </summary>
    public int MaxCorrections { get; init; } = 10_000;

    internal void Validate()
    {
        if (MaxInputLength <= 0)
            throw new InvalidOperationException("MaxInputLength must be greater than zero.");
        if (MaxOutputLength <= 0)
            throw new InvalidOperationException("MaxOutputLength must be greater than zero.");
        if (MaxDepth <= 0)
            throw new InvalidOperationException("MaxDepth must be greater than zero.");
        if (MaxCorrections <= 0)
            throw new InvalidOperationException("MaxCorrections must be greater than zero.");
    }
}
