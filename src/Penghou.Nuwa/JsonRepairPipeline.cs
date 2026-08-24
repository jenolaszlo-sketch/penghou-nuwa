using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Penghou.Nuwa.Strategies;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa;

public sealed class JsonRepairPipeline
    : IJsonRepairPipeline
{
    private readonly IReadOnlyList<ITextRepair> _textRepairs;
    private readonly IReadOnlyList<ITextRepair> _salvageRepairs;
    private readonly IReadOnlyList<INodeRepair> _nodeRepairs;
    private readonly ILogger<JsonRepairPipeline> _logger;
    private readonly JsonRepairLimits _limits;
    private readonly bool _allowTruncationSalvage;
    private readonly TolerantJsonSyntaxTreeParser _tolerantParser =
        new();

    public JsonRepairPipeline(
        IReadOnlyList<ITextRepair> textRepairs,
        IReadOnlyList<ITextRepair> salvageRepairs,
        IReadOnlyList<INodeRepair> nodeRepairs,
        ILogger<JsonRepairPipeline> logger)
        : this(textRepairs, salvageRepairs, nodeRepairs, logger, JsonRepairLimits.Default)
    {
    }

    /// <summary>Initializes a reusable repair pipeline with explicit resource limits.</summary>
    public JsonRepairPipeline(
        IReadOnlyList<ITextRepair> textRepairs,
        IReadOnlyList<ITextRepair> salvageRepairs,
        IReadOnlyList<INodeRepair> nodeRepairs,
        ILogger<JsonRepairPipeline> logger,
        JsonRepairLimits limits)
        : this(
            textRepairs,
            salvageRepairs,
            nodeRepairs,
            logger,
            limits,
            allowTruncationSalvage: true)
    {
    }

    /// <summary>
    /// Initializes a reusable repair pipeline with explicit resource limits
    /// and truncation behaviour.
    /// </summary>
    public JsonRepairPipeline(
        IReadOnlyList<ITextRepair> textRepairs,
        IReadOnlyList<ITextRepair> salvageRepairs,
        IReadOnlyList<INodeRepair> nodeRepairs,
        ILogger<JsonRepairPipeline> logger,
        JsonRepairLimits limits,
        bool allowTruncationSalvage)
    {
        ArgumentNullException.ThrowIfNull(textRepairs);
        ArgumentNullException.ThrowIfNull(salvageRepairs);
        ArgumentNullException.ThrowIfNull(nodeRepairs);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        _textRepairs = textRepairs.ToArray();
        _salvageRepairs = salvageRepairs.ToArray();
        _nodeRepairs = nodeRepairs.ToArray();
        _logger = logger;
        _limits = limits;
        _allowTruncationSalvage = allowTruncationSalvage;
    }

    /// <summary>
    /// Builds a ready-to-use pipeline without a service collection. Strategies
    /// are resolved through public constructors whose parameters are
    /// satisfiable with a null logger; strategies with other dependencies
    /// should be registered with <c>AddJsonRepair</c> instead.
    /// </summary>
    public static JsonRepairPipeline Create(
        Action<JsonRepairOptions>? configure = null)
    {
        var options = new JsonRepairOptions();
        configure?.Invoke(options);
        options.Validate();

        return new JsonRepairPipeline(
            Instantiate<ITextRepair>(
                options.TextRepairs),
            Instantiate<ITextRepair>(
                options.SalvageRepairs),
            Instantiate<INodeRepair>(
                options.NodeRepairs),
            NullLogger<JsonRepairPipeline>.Instance,
            options.Limits,
            options.AllowTruncationSalvage);
    }

    public async ValueTask<JsonRepairResult> RepairAsync(
        string input,
        JsonSchemaExpectation? expectation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        cancellationToken.ThrowIfCancellationRequested();

        if (input.Length > _limits.MaxInputLength)
        {
            throw new JsonRepairLimitException(
                $"Input length {input.Length} exceeds the configured maximum of {_limits.MaxInputLength} characters.");
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await RepairCoreAsync(
            input,
            expectation,
            cancellationToken);
        stopwatch.Stop();

        LogOutcome(
            result,
            stopwatch.ElapsedMilliseconds);

        return result;
    }

    /// <summary>
    /// Repairs a payload as it streams in. After every chunk the pipeline
    /// emits a <see cref="JsonRepairStreamDelta"/> covering the newly stable
    /// prefix of the accumulated input — text that lies outside any open
    /// string, ends on a complete token boundary, and keeps a holdback margin
    /// from the tail so pending punctuation repairs cannot invalidate it.
    /// When the chunk stream finishes, one
    /// <see cref="JsonRepairStreamCompleted"/> event carries the authoritative
    /// <see cref="JsonRepairResult"/> for the full payload. Deltas are a
    /// best-effort live preview; only the completed event is contractual.
    /// </summary>
    public async IAsyncEnumerable<JsonRepairStreamEvent> RepairStreamAsync(
        IAsyncEnumerable<string> chunks,
        JsonSchemaExpectation? expectation = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        cancellationToken.ThrowIfCancellationRequested();

        var buffer = new StringBuilder();
        var emittedOffset = 0;

        await foreach (var chunk in chunks.WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            if (chunk.Length == 0)
            {
                continue;
            }

            buffer.Append(chunk);

            if (buffer.Length > _limits.MaxInputLength)
            {
                throw new JsonRepairLimitException(
                    $"Streamed input length {buffer.Length} exceeds the configured maximum of {_limits.MaxInputLength} characters.");
            }

            var stableLength =
                StablePrefixScanner.FindStableLength(
                    buffer.ToString(),
                    emittedOffset);

            if (stableLength > emittedOffset)
            {
                yield return new JsonRepairStreamDelta(
                    emittedOffset,
                    buffer.ToString(
                        emittedOffset,
                        stableLength - emittedOffset));
                emittedOffset = stableLength;
            }
        }

        var accumulated = buffer.ToString();
        if (accumulated.Trim().Length == 0)
        {
            throw new ArgumentException(
                "The chunk stream contained no content.",
                nameof(chunks));
        }

        yield return new JsonRepairStreamCompleted(
            await RepairAsync(
                    accumulated,
                    expectation,
                    cancellationToken)
                .ConfigureAwait(false));
    }

    /// <summary>
    /// Conservative stability rules for streamed previews:
    /// <list type="bullet">
    /// <item>No emission inside strings or before the first structural token.</item>
    /// <item>Emission points are token boundaries outside string literals.</item>
    /// <item>A holdback margin is kept from the tail so repairs that edit
    /// trailing punctuation (e.g. removing a trailing comma) cannot invalidate
    /// already-emitted deltas.</item>
    /// </list>
    /// </summary>
    internal static class StablePrefixScanner
    {
        /// <summary>Characters withheld from emission until more text arrives.</summary>
        internal const int TailHoldback = 16;

        public static int FindStableLength(
            string buffer,
            int emittedOffset)
        {
            var limit = buffer.Length - TailHoldback;
            if (limit <= emittedOffset)
            {
                return emittedOffset;
            }

            var depth = 0;
            var sawStructure = false;
            var boundary = -1;

            var index = 0;
            while (index < limit)
            {
                var current = buffer[index];

                switch (current)
                {
                    case '"':
                        {
                            // Skip a complete string literal.
                            index++;
                            while (index < limit)
                            {
                                if (buffer[index] == '\\')
                                {
                                    index += 2;
                                    continue;
                                }

                                if (buffer[index] == '"')
                                {
                                    break;
                                }

                                index++;
                            }

                            if (index >= limit)
                            {
                                // Unterminated within the safe region.
                                return boundary < emittedOffset
                                    ? emittedOffset
                                    : Math.Max(boundary + 1, emittedOffset);
                            }

                            if (depth > 0)
                            {
                                boundary = index;
                                sawStructure = true;
                            }

                            index++;
                            break;
                        }
                    case '{' or '[':
                        depth++;
                        sawStructure = true;
                        boundary = index;
                        index++;
                        break;
                    case '}' or ']':
                        depth--;
                        boundary = index;
                        index++;
                        break;
                    case ':' or ',':
                        boundary = index;
                        index++;
                        break;
                    default:
                        if (char.IsWhiteSpace(current))
                        {
                            if (boundary >= 0)
                            {
                                boundary = index - 1 < boundary
                                    ? boundary
                                    : index - 1;
                            }

                            index++;
                            break;
                        }

                        // Numbers and literals: emit through their end.
                        if (boundary >= 0 && depth > 0)
                        {
                            var tokenEnd = index;
                            while (tokenEnd < limit &&
                                   !char.IsWhiteSpace(buffer[tokenEnd]) &&
                                   buffer[tokenEnd] is not (',' or '}' or ']' or '"' or ':'))
                            {
                                tokenEnd++;
                            }

                            boundary = tokenEnd - 1;
                            index = tokenEnd;
                            break;
                        }

                        index++;
                        break;
                }
            }

            if (!sawStructure ||
                boundary < emittedOffset)
            {
                return emittedOffset;
            }

            // Never emit past an unbalanced close.
            return depth < 0
                ? emittedOffset
                : Math.Min(boundary + 1, limit);
        }
    }

    private async ValueTask<JsonRepairResult> RepairCoreAsync(
        string input,
        JsonSchemaExpectation? expectation,
        CancellationToken cancellationToken)
    {
        var textReports = new List<StrategyReport>();
        var textWasRepaired = false;
        var current = input;

        // Already valid JSON: no text repair needed.
        if (TryParseNode(current, out var root))
        {
            ReportSkipped(textReports, _textRepairs);
            ReportSkipped(textReports, _salvageRepairs);

            return await CreateResultAsync(
                root!,
                expectation,
                textReports,
                textWasRepaired: false,
                originalText: input,
                tolerantParse: null,
                cancellationToken);
        }

        // Ordered text-repair phase.
        var successIndex = -1;

        for (var index = 0; index < _textRepairs.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var strategy = _textRepairs[index];
            var repair = await TryRepairAsync(
                strategy,
                current,
                cancellationToken);

            if (repair.Error is not null)
            {
                textReports.Add(new StrategyReport(
                    strategy.Name,
                    StrategyStatus.Failed,
                    Note: repair.Error));
                continue;
            }

            var attempt = repair.Attempt;
            var report = ReportTextAttempt(
                strategy.Name,
                attempt,
                current);
            textReports.Add(report);

            if (report.Status != StrategyStatus.Succeeded)
            {
                continue;
            }

            current = attempt.Repaired!;
            textWasRepaired = true;

            if (TryParseNode(current, out root))
            {
                successIndex = index;
                break;
            }
        }

        if (successIndex >= 0)
        {
            for (var index = successIndex + 1;
                 index < _textRepairs.Count;
                 index++)
            {
                textReports.Add(new StrategyReport(
                    _textRepairs[index].Name,
                    StrategyStatus.Skipped));
            }

            ReportSkipped(textReports, _salvageRepairs);

            return await CreateResultAsync(
                root!,
                expectation,
                textReports,
                textWasRepaired,
                originalText: input,
                tolerantParse: null,
                cancellationToken);
        }

        // Tolerant recovery, then the ordered salvage fallback phase.
        var tolerantParse =
            _tolerantParser.Parse(
                current,
                expectation,
                _limits,
                cancellationToken,
                _allowTruncationSalvage);

        if (tolerantParse.Root is null)
        {
            var salvageSuccessIndex = -1;

            for (var index = 0; index < _salvageRepairs.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var strategy = _salvageRepairs[index];
                var repair = await TryRepairAsync(
                    strategy,
                    current,
                    cancellationToken);

                if (repair.Error is not null)
                {
                    textReports.Add(new StrategyReport(
                        strategy.Name,
                        StrategyStatus.Failed,
                        Note: repair.Error));
                    continue;
                }

                var attempt = repair.Attempt;
                var report = ReportTextAttempt(
                    strategy.Name,
                    attempt,
                    current);
                textReports.Add(report);

                if (report.Status != StrategyStatus.Succeeded)
                {
                    continue;
                }

                current = attempt.Repaired!;
                textWasRepaired = true;

                tolerantParse =
                    _tolerantParser.Parse(
                        current,
                        expectation,
                        _limits,
                        cancellationToken,
                        _allowTruncationSalvage);

                if (tolerantParse.Root is not null)
                {
                    salvageSuccessIndex = index;
                    break;
                }
            }

            if (salvageSuccessIndex >= 0)
            {
                for (var index = salvageSuccessIndex + 1;
                     index < _salvageRepairs.Count;
                     index++)
                {
                    textReports.Add(new StrategyReport(
                        _salvageRepairs[index].Name,
                        StrategyStatus.Skipped));
                }
            }
        }
        else
        {
            // Recovery succeeded directly; salvage never ran.
            ReportSkipped(textReports, _salvageRepairs);
        }

        if (tolerantParse.Root is null)
        {
            return new JsonRepairResult(
                document: null,
                root: null,
                originalText: input,
                repairedText: current,
                wasRepaired: false,
                textReports,
                [],
                tolerantParse,
                JsonRepairShapeStatus.NotEvaluated,
                shapeErrors: []);
        }

        return await CreateResultAsync(
            tolerantParse.Root,
            expectation,
            textReports,
            textWasRepaired: true,
            originalText: input,
            tolerantParse,
            cancellationToken);
    }

    private async ValueTask<JsonRepairResult> CreateResultAsync(
        JsonNode root,
        JsonSchemaExpectation? expectation,
        IReadOnlyList<StrategyReport> textReports,
        bool textWasRepaired,
        string originalText,
        TolerantJsonSyntaxTreeParseResult? tolerantParse,
        CancellationToken cancellationToken)
    {
        var nodeReports = new List<StrategyReport>();
        var current = root;
        var nodeWasRepaired = false;
        if (expectation is not null)
        {
            foreach (var strategy in _nodeRepairs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NodeRepairAttempt attempt;

                try
                {
                    attempt = await strategy.RepairAsync(
                        current,
                        expectation,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (JsonRepairLimitException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    nodeReports.Add(new StrategyReport(
                        strategy.Name,
                        StrategyStatus.Failed,
                        Note: exception.Message));
                    continue;
                }

                var report = ReportNodeAttempt(
                    strategy.Name,
                    attempt);
                nodeReports.Add(report);

                if (report.Status != StrategyStatus.Succeeded)
                {
                    continue;
                }

                current = attempt.Repaired!;
                nodeWasRepaired = true;
            }
        }

        var wasRepaired =
            textWasRepaired ||
            nodeWasRepaired;
        var repairedText = wasRepaired
            ? current.ToJsonString()
            : originalText;
        if (repairedText.Length > _limits.MaxOutputLength)
        {
            throw new JsonRepairLimitException(
                $"Repaired output length {repairedText.Length} exceeds the configured maximum of {_limits.MaxOutputLength} characters.");
        }
        var document = JsonDocument.Parse(repairedText);
        var shapeErrors = expectation?.ValidateShape(current) ?? [];
        var shapeStatus = expectation is null
            ? JsonRepairShapeStatus.NotEvaluated
            : shapeErrors.Count == 0
                ? JsonRepairShapeStatus.Matched
                : JsonRepairShapeStatus.Mismatched;

        return new JsonRepairResult(
            document,
            current,
            originalText,
            repairedText,
            wasRepaired,
            textReports,
            nodeReports,
            tolerantParse,
            shapeStatus,
            shapeErrors);
    }

    private void LogOutcome(
        JsonRepairResult result,
        long elapsedMilliseconds)
    {
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Malformed JSON could not be repaired in {ElapsedMilliseconds} ms. Text repairs: {TextRepairs}.",
                elapsedMilliseconds,
                Summarize(result.TextRepairs));
            return;
        }

        if (result.WasRepaired)
        {
            _logger.LogWarning(
                "Malformed JSON was repaired in {ElapsedMilliseconds} ms. Winner: {Winner}. Shape status: {ShapeStatus}. Text repairs: {TextRepairs}.",
                elapsedMilliseconds,
                result.SucceededBy?.Name ??
                    "tolerant-recovery",
                result.ShapeStatus,
                Summarize(result.TextRepairs));
        }
        else
        {
            _logger.LogDebug(
                "JSON parsed without repair in {ElapsedMilliseconds} ms.",
                elapsedMilliseconds);
        }
    }

    private static void ReportSkipped(
        List<StrategyReport> reports,
        IReadOnlyList<ITextRepair> strategies)
    {
        foreach (var strategy in strategies)
        {
            reports.Add(new StrategyReport(
                strategy.Name,
                StrategyStatus.Skipped));
        }
    }

    private static StrategyReport ReportTextAttempt(
        string name,
        TextRepairAttempt attempt,
        string current)
    {
        if (attempt.Outcome == RepairOutcome.NotApplicable)
        {
            return new StrategyReport(
                name,
                StrategyStatus.NotApplicable,
                Note: attempt.Note);
        }

        if (attempt.Outcome == RepairOutcome.Failed ||
            string.IsNullOrWhiteSpace(attempt.Repaired) ||
            string.Equals(
                attempt.Repaired,
                current,
                StringComparison.Ordinal))
        {
            return new StrategyReport(
                name,
                StrategyStatus.Failed,
                Note: attempt.Note);
        }

        return new StrategyReport(
            name,
            StrategyStatus.Succeeded,
            Repaired: null,
            attempt.Note);
    }

    private static StrategyReport ReportNodeAttempt(
        string name,
        NodeRepairAttempt attempt)
    {
        if (attempt.Outcome == RepairOutcome.NotApplicable)
        {
            return new StrategyReport(
                name,
                StrategyStatus.NotApplicable,
                Note: attempt.Note);
        }

        if (attempt.Outcome == RepairOutcome.Failed ||
            attempt.Repaired is null)
        {
            return new StrategyReport(
                name,
                StrategyStatus.Failed,
                Note: attempt.Note);
        }

        return new StrategyReport(
            name,
            StrategyStatus.Succeeded,
            Note: attempt.Note);
    }

    private static async ValueTask<RepairResult> TryRepairAsync(
        ITextRepair strategy,
        string input,
        CancellationToken cancellationToken)
    {
        try
        {
            return new RepairResult(
                await strategy.RepairAsync(
                    input,
                    cancellationToken),
                Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonRepairLimitException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new RepairResult(
                default,
                exception.Message);
        }
    }

    private static string Summarize(IEnumerable<StrategyReport> reports) =>
        string.Join(
            ", ",
            reports.Select(report => $"{report.Name}={report.Status}"));

    private static IReadOnlyList<T> Instantiate<T>(
        IReadOnlyList<Type> types)
        where T : class
    {
        var repairs = new T[types.Count];

        for (var index = 0; index < types.Count; index++)
        {
            repairs[index] = Instantiate<T>(
                types[index]);
        }

        return repairs;
    }

    private static T Instantiate<T>(
        Type type)
        where T : class
    {
        var constructor = type
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault();

        if (constructor is null)
        {
            throw new InvalidOperationException(
                $"The strategy '{type.Name}' has no public constructor and cannot be created by the non-DI factory. Register it with AddJsonRepair(Action<JsonRepairOptions>) instead.");
        }

        var parameters = constructor.GetParameters();
        var arguments = new object?[parameters.Length];

        for (var index = 0; index < parameters.Length; index++)
        {
            arguments[index] = ResolveConstructorArgument(
                type,
                parameters[index]);
        }

        var repair = Activator.CreateInstance(type, arguments)
            ?? throw new InvalidOperationException(
                $"The strategy '{type.Name}' could not be created.");

        return (T)repair;
    }

    private static object? ResolveConstructorArgument(
        Type strategyType,
        ParameterInfo parameter)
    {
        var parameterType = parameter.ParameterType;

        if (parameterType == typeof(ILoggerFactory))
        {
            return NullLoggerFactory.Instance;
        }

        if (parameterType == typeof(ILogger))
        {
            return NullLogger.Instance;
        }

        if (parameterType.IsGenericType &&
            parameterType.GetGenericTypeDefinition() ==
            typeof(ILogger<>))
        {
            return ResolveNullLogger(
                parameterType.GetGenericArguments()[0]);
        }

        throw new InvalidOperationException(
            $"The strategy '{strategyType.Name}' constructor parameter '{parameter.Name}' of type '{parameterType.Name}' cannot be resolved by the non-DI factory. Register the strategy with AddJsonRepair(Action<JsonRepairOptions>) instead.");
    }

    private static object ResolveNullLogger(
        Type category)
    {
        var loggerType = typeof(NullLogger<>)
            .MakeGenericType(category);
        var instance = loggerType.GetField(
            nameof(NullLogger<int>.Instance),
            BindingFlags.Public | BindingFlags.Static);

        return instance?.GetValue(null)
            ?? throw new InvalidOperationException(
                $"Could not create a NullLogger<{category.Name}>.");
    }

    private static bool TryParseNode(
        string json,
        out JsonNode? root)
    {
        try
        {
            root = JsonNode.Parse(json);
            return root is not null;
        }
        catch (JsonException)
        {
            root = null;
            return false;
        }
    }

    private readonly record struct RepairResult(
        TextRepairAttempt Attempt,
        string? Error);
}
