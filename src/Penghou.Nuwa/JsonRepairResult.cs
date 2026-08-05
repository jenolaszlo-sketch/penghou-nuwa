using System.Text.Json;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa;

public sealed class JsonRepairResult(
    JsonDocument? document,
    JsonNode? root,
    string? repairedText,
    bool wasRepaired,
    IReadOnlyList<StrategyReport> textRepairs,
    IReadOnlyList<StrategyReport> nodeRepairs,
    TolerantJsonSyntaxTreeParseResult? tolerantParse)
    : IDisposable
{
    public JsonDocument? Document { get; } = document;

    /// <summary>
    /// The repaired document as a mutable node tree. Shares state with
    /// <see cref="Document"/> and remains valid after disposal.
    /// </summary>
    public JsonNode? Root { get; } = root;

    /// <summary>
    /// The best-effort JSON text produced by the pipeline. When
    /// <see cref="Succeeded"/> is true this is valid JSON; otherwise it is the
    /// partially repaired text the pipeline ended on.
    /// </summary>
    public string? RepairedText { get; } = repairedText;

    public bool Succeeded => Document is not null;

    public bool WasRepaired { get; } = wasRepaired;

    /// <summary>
    /// The strategy whose output became the final document, or null when no
    /// strategy repaired anything (already-valid input or plain tolerant
    /// recovery).
    /// </summary>
    public StrategyReport? SucceededBy { get; } =
        nodeRepairs.LastOrDefault(
            report =>
                report.Status == StrategyStatus.Succeeded) ??
        textRepairs.LastOrDefault(
            report =>
                report.Status == StrategyStatus.Succeeded);

    /// <summary>
    /// Ordered per-strategy diagnostics for the text phase, including salvage
    /// strategies. Every configured strategy is reported exactly once, in
    /// configuration order, with <see cref="StrategyStatus.Skipped"/> for those
    /// never reached.
    /// </summary>
    public IReadOnlyList<StrategyReport> TextRepairs { get; } = textRepairs;

    /// <summary>
    /// Ordered per-strategy diagnostics for the node phase. Empty when no
    /// schema expectation was supplied.
    /// </summary>
    public IReadOnlyList<StrategyReport> NodeRepairs { get; } = nodeRepairs;

    /// <summary>
    /// Outcome of the tolerant syntax-tree recovery, when it was attempted.
    /// </summary>
    public TolerantJsonSyntaxTreeParseResult? TolerantParse { get; } = tolerantParse;

    /// <summary>
    /// Returns <see cref="Document"/>, or throws when the repair failed.
    /// </summary>
    public JsonDocument GetDocumentOrThrow()
    {
        if (Document is null)
        {
            throw new InvalidOperationException(DescribeFailure());
        }

        return Document;
    }

    /// <summary>
    /// Returns <see cref="Root"/>, or throws when the repair failed.
    /// </summary>
    public JsonNode GetRootOrThrow()
    {
        if (Root is null)
        {
            throw new InvalidOperationException(DescribeFailure());
        }

        return Root;
    }

    /// <summary>
    /// Returns <see cref="RepairedText"/>, or throws when the repair failed.
    /// </summary>
    public string GetRepairedTextOrThrow()
    {
        if (!Succeeded || RepairedText is null)
        {
            throw new InvalidOperationException(DescribeFailure());
        }

        return RepairedText;
    }

    public void Dispose() => Document?.Dispose();

    private string DescribeFailure()
    {
        var text = string.Join(
            ", ",
            TextRepairs.Select(
                report => $"{report.Name}={report.Status}"));

        return $"JSON repair failed; the input could not be recovered into valid JSON. Text repairs: {text}.";
    }
}
