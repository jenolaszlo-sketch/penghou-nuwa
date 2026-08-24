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
            tolerantParse: null,
            JsonRepairShapeStatus.NotEvaluated,
            shapeErrors: [])
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
        TolerantJsonSyntaxTreeParseResult? tolerantParse,
        JsonRepairShapeStatus shapeStatus,
        IReadOnlyList<string> shapeErrors)
    {
        ArgumentNullException.ThrowIfNull(originalText);
        ArgumentNullException.ThrowIfNull(textRepairs);
        ArgumentNullException.ThrowIfNull(nodeRepairs);
        ArgumentNullException.ThrowIfNull(shapeErrors);

        if ((document is null) != (root is null))
        {
            throw new ArgumentException(
                "Document and root must either both be present or both be null.");
        }

        if (document is not null && repairedText is null)
        {
            throw new ArgumentException(
                "A successful result must include repaired text.",
                nameof(repairedText));
        }

        if (document is not null && root is not null && repairedText is not null)
        {
            JsonNode? repairedNode;
            try
            {
                repairedNode = JsonNode.Parse(repairedText);
            }
            catch (JsonException exception)
            {
                throw new ArgumentException(
                    "Repaired text must contain valid JSON.",
                    nameof(repairedText),
                    exception);
            }

            var documentNode = JsonNode.Parse(document.RootElement.GetRawText());
            if (!JsonNode.DeepEquals(root, repairedNode) ||
                !JsonNode.DeepEquals(root, documentNode))
            {
                throw new ArgumentException(
                    "Document, root, and repaired text must represent the same JSON value.");
            }
        }

        if (document is null && wasRepaired)
        {
            throw new ArgumentException(
                "A failed result cannot be marked as repaired.",
                nameof(wasRepaired));
        }

        Document = document;
        Root = root;
        OriginalText = originalText;
        RepairedText = repairedText;
        WasRepaired = wasRepaired;
        TextRepairs = textRepairs.ToArray();
        NodeRepairs = nodeRepairs.ToArray();
        TolerantParse = tolerantParse;
        ShapeStatus = shapeStatus;
        ShapeErrors = shapeErrors.ToArray();
        TolerantRecovery = tolerantParse?.ToPublicReport();
        SucceededBy =
            nodeRepairs.LastOrDefault(
                report =>
                    report.Status == StrategyStatus.Succeeded) ??
            textRepairs.LastOrDefault(
                report =>
                    report.Status == StrategyStatus.Succeeded);
        Confidence = ComputeConfidence(
            succeeded: Document is not null,
            wasRepaired,
            textRepairs,
            nodeRepairs,
            tolerantParse,
            shapeStatus);
    }

    /// <summary>
    /// A heuristic 0–1 confidence score for the repaired payload. Unmodified
    /// valid JSON scores 1.0; every recorded mutation reduces the score, and
    /// lossy salvage or a shape mismatch reduce it sharply. Deterministic for
    /// a given input: the value is derived only from repair diagnostics.
    /// </summary>
    public double Confidence { get; }

    private static double ComputeConfidence(
        bool succeeded,
        bool wasRepaired,
        IReadOnlyList<StrategyReport> textRepairs,
        IReadOnlyList<StrategyReport> nodeRepairs,
        TolerantJsonSyntaxTreeParseResult? tolerantParse,
        JsonRepairShapeStatus shapeStatus)
    {
        if (!succeeded)
        {
            return 0;
        }

        if (!wasRepaired)
        {
            return 1;
        }

        var score = 1.0;

        // Transport-level rewrites (fences, wrappers) are cheap signals.
        score -= 0.05 *
            textRepairs.Count(
                report =>
                    report.Status ==
                    StrategyStatus.Succeeded);

        // Tolerant punctuation corrections are routine.
        score -= Math.Min(
            0.30,
            0.02 * (tolerantParse?.CorrectionCount ?? 0));
        score -= Math.Min(
            0.15,
            0.03 * (tolerantParse?.SchemaGuidedStringCorrectionCount ?? 0));

        // Node-level mutations alter values or structure more substantively.
        score -= 0.08 *
            nodeRepairs.Count(
                report =>
                    report.Status ==
                    StrategyStatus.Succeeded);

        // Lossy salvage discards information by design.
        if (textRepairs.Any(
                report =>
                    report.Status ==
                    StrategyStatus.Succeeded &&
                    report.Name ==
                    "salvage"))
        {
            score *= 0.6;
        }

        // Extraction strategies deliberately discard surrounding or trailing
        // content. Treat that as materially less certain than transport-only
        // normalization such as removing a Markdown fence.
        if (textRepairs.Any(
                report =>
                    report.Status == StrategyStatus.Succeeded &&
                    report.Name is "concatenated-json" or
                        "prose-wrapper-extraction" or
                        "xml-wrapped-extraction"))
        {
            score *= 0.7;
        }

        if (shapeStatus ==
            JsonRepairShapeStatus.Mismatched)
        {
            score *= 0.5;
        }

        return Math.Clamp(score, 0, 1);
    }

    /// <summary>
    /// Whether <see cref="Confidence"/> meets the supplied threshold
    /// (mirrors the confidence-gating pattern used by constrained local
    /// extractors).
    /// </summary>
    public bool IsConfident(double minimumConfidence) =>
        Confidence >= minimumConfidence;

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
    /// Whether the final JSON matches the structural subset of the supplied
    /// schema expectation. This is shape validation, not full JSON Schema
    /// dialect validation.
    /// </summary>
    public JsonRepairShapeStatus ShapeStatus { get; }

    /// <summary>Structural schema mismatches found in the final JSON.</summary>
    public IReadOnlyList<string> ShapeErrors { get; }

    /// <summary>Diagnostics from tolerant syntax-tree recovery, when attempted.</summary>
    public TolerantRecoveryReport? TolerantRecovery { get; }

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
