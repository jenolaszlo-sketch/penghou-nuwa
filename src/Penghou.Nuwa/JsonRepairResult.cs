using System.Text.Json;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa;

public sealed class JsonRepairResult
    : IDisposable
{
    /// <summary>
    /// Creates a repair result. Prefer the <see cref="Success"/> and
    /// <see cref="Failure"/> factories, which produce a consistently
    /// populated document, root and repair flags.
    /// </summary>
    public JsonRepairResult(
        JsonDocument? document,
        JsonNode? root,
        string originalText,
        string? repairedText,
        bool wasRepaired,
        IReadOnlyList<StrategyReport> textRepairs,
        IReadOnlyList<StrategyReport> nodeRepairs)
        : this(
            document,
            root,
            originalText,
            repairedText,
            wasRepaired,
            textRepairs,
            nodeRepairs,
            tolerantParse: null)
    {
    }

    internal JsonRepairResult(
        JsonDocument? document,
        JsonNode? root,
        string originalText,
        string? repairedText,
        bool wasRepaired,
        IReadOnlyList<StrategyReport> textRepairs,
        IReadOnlyList<StrategyReport> nodeRepairs,
        TolerantJsonSyntaxTreeParseResult? tolerantParse)
    {
        Document = document;
        Root = root;
        OriginalText = originalText;
        RepairedText = repairedText;
        WasRepaired = wasRepaired;
        TextRepairs = textRepairs;
        NodeRepairs = nodeRepairs;
        TolerantParse = tolerantParse;
        SucceededBy =
            nodeRepairs.LastOrDefault(
                report =>
                    report.Status == StrategyStatus.Succeeded) ??
            textRepairs.LastOrDefault(
                report =>
                    report.Status == StrategyStatus.Succeeded);
    }

    /// <summary>
    /// Creates a successful result from the repaired root and its text.
    /// </summary>
    public static JsonRepairResult Success(
        JsonNode root,
        string originalText,
        string repairedText,
        bool wasRepaired,
        IReadOnlyList<StrategyReport> textRepairs,
        IReadOnlyList<StrategyReport> nodeRepairs)
    {
        ArgumentNullException.ThrowIfNull(root);
        var document = JsonDocument.Parse(repairedText);

        return new JsonRepairResult(
            document,
            root,
            originalText,
            repairedText,
            wasRepaired,
            textRepairs,
            nodeRepairs);
    }

    /// <summary>
    /// Creates a failed result with no document.
    /// </summary>
    public static JsonRepairResult Failure(
        string originalText,
        string? repairedText,
        IReadOnlyList<StrategyReport> textRepairs,
        IReadOnlyList<StrategyReport> nodeRepairs) =>
        new(
            document: null,
            root: null,
            originalText,
            repairedText,
            wasRepaired: false,
            textRepairs,
            nodeRepairs);

    public JsonDocument? Document { get; }

    /// <summary>
    /// The repaired document as a mutable node tree. It is an independent
    /// object from <see cref="Document"/> (which is a snapshot parsed from
    /// <see cref="RepairedText"/>) and remains valid after disposal.
    /// </summary>
    public JsonNode? Root { get; }

    /// <summary>The exact input that was passed to the pipeline.</summary>
    public string OriginalText { get; }

    /// <summary>
    /// The best-effort JSON text produced by the pipeline. When
    /// <see cref="Succeeded"/> is true this is valid JSON; otherwise it is the
    /// partially repaired text the pipeline ended on.
    /// </summary>
    public string? RepairedText { get; }

    public bool Succeeded => Document is not null;

    public bool WasRepaired { get; }

    /// <summary>
    /// The strategy whose output became the final document, or null when no
    /// strategy repaired anything (already-valid input or plain tolerant
    /// recovery).
    /// </summary>
    public StrategyReport? SucceededBy { get; }

    /// <summary>
    /// Ordered per-strategy diagnostics for the text phase, including salvage
    /// strategies. Every configured strategy is reported exactly once, in
    /// configuration order, with <see cref="StrategyStatus.Skipped"/> for those
    /// never reached.
    /// </summary>
    public IReadOnlyList<StrategyReport> TextRepairs { get; }

    /// <summary>
    /// Ordered per-strategy diagnostics for the node phase. Empty when no
    /// schema expectation was supplied.
    /// </summary>
    public IReadOnlyList<StrategyReport> NodeRepairs { get; }

    /// <summary>
    /// Outcome of the tolerant syntax-tree recovery, when it was attempted.
    /// </summary>
    internal TolerantJsonSyntaxTreeParseResult? TolerantParse { get; }

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
        var nodes = string.Join(
            ", ",
            NodeRepairs.Select(
                report => $"{report.Name}={report.Status}"));
        var nodePhase =
            NodeRepairs.Count > 0
                ? $" Node repairs: {nodes}."
                : string.Empty;
        var recovery =
            TolerantParse is null
                ? "not attempted"
                : TolerantParse.Outcome;

        return $"JSON repair failed; the input could not be recovered into valid JSON. Text repairs: {text}.{nodePhase} Tolerant recovery: {recovery}.";
    }
}
