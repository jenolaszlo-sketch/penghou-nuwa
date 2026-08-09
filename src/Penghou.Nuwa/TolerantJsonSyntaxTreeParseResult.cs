using System.Text.Json.Nodes;

namespace Penghou.Nuwa;

internal sealed record TolerantJsonSyntaxTreeParseResult(
    JsonNode? Root,
    string Outcome,
    int CorrectionCount = 0,
    int SchemaGuidedStringCorrectionCount = 0,
    IReadOnlyList<string>? Corrections = null)
{
    public bool Succeeded => Root is not null;

    public TolerantRecoveryReport ToPublicReport() =>
        new(
            Succeeded,
            Outcome,
            CorrectionCount,
            SchemaGuidedStringCorrectionCount,
            Corrections ?? []);
}
