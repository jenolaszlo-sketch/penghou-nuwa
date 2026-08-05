using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Penghou.Nuwa.Strategies;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa;

public sealed class JsonRepairPipeline(
    IReadOnlyList<ITextRepair> textRepairs,
    IReadOnlyList<ITextRepair> salvageRepairs,
    ITolerantJsonSyntaxTreeParser tolerantParser,
    IReadOnlyList<INodeRepair> nodeRepairs,
    ILogger<JsonRepairPipeline> logger)
    : IJsonRepairPipeline
{
    /// <summary>
    /// Builds a ready-to-use pipeline without a service collection. Strategies
    /// are resolved through public constructors whose parameters are
    /// satisfiable by the pipeline's parser or a null logger; strategies with
    /// other dependencies should be registered with
    /// <c>AddJsonRepair</c> instead.
    /// </summary>
    public static JsonRepairPipeline Create(
        Action<JsonRepairOptions>? configure = null)
    {
        var options = new JsonRepairOptions();
        configure?.Invoke(options);
        options.Validate();

        var parser = new TolerantJsonSyntaxTreeParser();

        return new JsonRepairPipeline(
            Instantiate<ITextRepair>(
                options.TextRepairs,
                parser),
            Instantiate<ITextRepair>(
                options.SalvageRepairs,
                parser),
            parser,
            Instantiate<INodeRepair>(
                options.NodeRepairs,
                parser),
            NullLogger<JsonRepairPipeline>.Instance);
    }

    public async ValueTask<JsonRepairResult> RepairAsync(
        string input,
        JsonSchemaExpectation? expectation = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        var textReports = new List<StrategyReport>();
        var textWasRepaired = false;
        var current = input;

        // Already valid JSON: no text repair needed.
        if (TryParseNode(current, out var root))
        {
            ReportSkipped(textReports, textRepairs);
            ReportSkipped(textReports, salvageRepairs);

            return await CreateResultAsync(
                root!,
                expectation,
                textReports,
                textWasRepaired: false,
                tolerantParse: null,
                cancellationToken);
        }

        // Ordered text-repair phase.
        var successIndex = -1;

        for (var index = 0; index < textRepairs.Count; index++)
        {
            var strategy = textRepairs[index];
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

            if (attempt.Outcome == RepairOutcome.NotApplicable)
            {
                textReports.Add(new StrategyReport(
                    strategy.Name,
                    StrategyStatus.NotApplicable,
                    Note: attempt.Note));
                continue;
            }

            if (attempt.Outcome == RepairOutcome.Failed ||
                string.IsNullOrWhiteSpace(attempt.Repaired) ||
                string.Equals(
                    attempt.Repaired,
                    current,
                    StringComparison.Ordinal))
            {
                textReports.Add(new StrategyReport(
                    strategy.Name,
                    StrategyStatus.Failed,
                    Note: attempt.Note));
                continue;
            }

            current = attempt.Repaired;
            textWasRepaired = true;
            textReports.Add(new StrategyReport(
                strategy.Name,
                StrategyStatus.Succeeded,
                current,
                attempt.Note));

            if (TryParseNode(current, out root))
            {
                successIndex = index;
                break;
            }
        }

        if (successIndex >= 0)
        {
            for (var index = successIndex + 1;
                 index < textRepairs.Count;
                 index++)
            {
                textReports.Add(new StrategyReport(
                    textRepairs[index].Name,
                    StrategyStatus.Skipped));
            }

            ReportSkipped(textReports, salvageRepairs);

            return await CreateResultAsync(
                root!,
                expectation,
                textReports,
                textWasRepaired,
                tolerantParse: null,
                cancellationToken);
        }

        // Tolerant recovery, then the ordered salvage fallback phase.
        var tolerantParse =
            tolerantParser.Parse(
                current,
                expectation);

        if (tolerantParse.Root is null)
        {
            var salvageSuccessIndex = -1;

            for (var index = 0; index < salvageRepairs.Count; index++)
            {
                var strategy = salvageRepairs[index];
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

                if (attempt.Outcome == RepairOutcome.NotApplicable)
                {
                    textReports.Add(new StrategyReport(
                        strategy.Name,
                        StrategyStatus.NotApplicable,
                        Note: attempt.Note));
                    continue;
                }

                if (attempt.Outcome == RepairOutcome.Failed ||
                    string.IsNullOrWhiteSpace(attempt.Repaired) ||
                    string.Equals(
                        attempt.Repaired,
                        current,
                        StringComparison.Ordinal))
                {
                    textReports.Add(new StrategyReport(
                        strategy.Name,
                        StrategyStatus.Failed,
                        Note: attempt.Note));
                    continue;
                }

                current = attempt.Repaired;
                textWasRepaired = true;
                textReports.Add(new StrategyReport(
                    strategy.Name,
                    StrategyStatus.Succeeded,
                    current,
                    attempt.Note));

                tolerantParse =
                    tolerantParser.Parse(
                        current,
                        expectation);

                if (tolerantParse.Root is not null)
                {
                    salvageSuccessIndex = index;
                    break;
                }
            }

            if (salvageSuccessIndex >= 0)
            {
                for (var index = salvageSuccessIndex + 1;
                     index < salvageRepairs.Count;
                     index++)
                {
                    textReports.Add(new StrategyReport(
                        salvageRepairs[index].Name,
                        StrategyStatus.Skipped));
                }
            }
        }
        else
        {
            // Recovery succeeded directly; salvage never ran.
            ReportSkipped(textReports, salvageRepairs);
        }

        if (tolerantParse.Root is null)
        {
            logger.LogWarning(
                "Malformed JSON could not be repaired. Text repairs: {@TextRepairs}",
                textReports);

            return new JsonRepairResult(
                document: null,
                root: null,
                repairedText: current,
                wasRepaired: false,
                textReports,
                [],
                tolerantParse);
        }

        return await CreateResultAsync(
            tolerantParse.Root,
            expectation,
            textReports,
            textWasRepaired: true,
            tolerantParse,
            cancellationToken);
    }

    private async ValueTask<JsonRepairResult> CreateResultAsync(
        JsonNode root,
        JsonSchemaExpectation? expectation,
        IReadOnlyList<StrategyReport> textReports,
        bool textWasRepaired,
        TolerantJsonSyntaxTreeParseResult? tolerantParse,
        CancellationToken cancellationToken)
    {
        var nodeReports = new List<StrategyReport>();
        var current = root;
        var nodeWasRepaired = false;

        if (expectation is not null)
        {
            foreach (var strategy in nodeRepairs)
            {
                NodeRepairAttempt attempt;

                try
                {
                    attempt = await strategy.RepairAsync(
                        current,
                        expectation,
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    nodeReports.Add(new StrategyReport(
                        strategy.Name,
                        StrategyStatus.Failed,
                        Note: exception.Message));
                    continue;
                }

                if (attempt.Outcome == RepairOutcome.NotApplicable)
                {
                    nodeReports.Add(new StrategyReport(
                        strategy.Name,
                        StrategyStatus.NotApplicable,
                        Note: attempt.Note));
                    continue;
                }

                if (attempt.Outcome == RepairOutcome.Failed ||
                    attempt.Repaired is null)
                {
                    nodeReports.Add(new StrategyReport(
                        strategy.Name,
                        StrategyStatus.Failed,
                        Note: attempt.Note));
                    continue;
                }

                current = attempt.Repaired;
                nodeWasRepaired = true;
                nodeReports.Add(new StrategyReport(
                    strategy.Name,
                    StrategyStatus.Succeeded,
                    Note: attempt.Note));
            }
        }

        var repairedText = current.ToJsonString();
        var document = JsonDocument.Parse(repairedText);
        var wasRepaired =
            textWasRepaired ||
            nodeWasRepaired;

        if (wasRepaired)
        {
            logger.LogWarning(
                "Malformed JSON was repaired. Text repairs: {@TextRepairs}",
                textReports);
        }

        return new JsonRepairResult(
            document,
            current,
            repairedText,
            wasRepaired,
            textReports,
            nodeReports,
            tolerantParse);
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
        catch (Exception exception)
        {
            return new RepairResult(
                default,
                exception.Message);
        }
    }

    private static IReadOnlyList<T> Instantiate<T>(
        IReadOnlyList<Type> types,
        ITolerantJsonSyntaxTreeParser parser)
        where T : class
    {
        var repairs = new T[types.Count];

        for (var index = 0; index < types.Count; index++)
        {
            repairs[index] = Instantiate<T>(
                types[index],
                parser);
        }

        return repairs;
    }

    private static T Instantiate<T>(
        Type type,
        ITolerantJsonSyntaxTreeParser parser)
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
                parameters[index],
                parser);
        }

        var repair = Activator.CreateInstance(type, arguments)
            ?? throw new InvalidOperationException(
                $"The strategy '{type.Name}' could not be created.");

        return (T)repair;
    }

    private static object? ResolveConstructorArgument(
        Type strategyType,
        ParameterInfo parameter,
        ITolerantJsonSyntaxTreeParser parser)
    {
        var parameterType = parameter.ParameterType;

        if (parameterType.IsAssignableFrom(
            parser.GetType()))
        {
            return parser;
        }

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
