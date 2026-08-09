namespace Penghou.Nuwa;

/// <summary>Diagnostics from handwritten tolerant syntax-tree recovery.</summary>
public sealed record TolerantRecoveryReport(
    bool Succeeded,
    string Outcome,
    int CorrectionCount,
    int SchemaGuidedStringCorrectionCount,
    IReadOnlyList<string> Corrections);
