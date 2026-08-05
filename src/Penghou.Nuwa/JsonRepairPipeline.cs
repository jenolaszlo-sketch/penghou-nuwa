using Microsoft.Extensions.Logging;
using Penghou.Nuwa.Strategies;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa;

public sealed class JsonRepairPipeline(
    IReadOnlyList<ITextRepairStrategy> preprocessingStrategies,
    ITolerantJsonSyntaxTreeParser tolerantParser,
    IReadOnlyList<INodeRepairStrategy> nodeRepairStrategies,
    ILogger<JsonRepairPipeline> logger)
    : IJsonRepairPipeline
{
    public JsonRepairResult Repair(
        string input,
        JsonSchemaExpectation? expectation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        var attempts = new Dictionary<string, string>(
            StringComparer.Ordinal);

        if (TryParseNode(input, out var root))
        {
            return CreateResult(
                root!,
                expectation,
                attempts,
                textWasRepaired: false);
        }

        var current = input;
        var textWasRepaired = false;

        foreach (var strategy in preprocessingStrategies)
        {
            if (!strategy.MightApply(current))
            {
                attempts[strategy.Name] = "skipped";
                continue;
            }

            if (!strategy.TryRepair(
                    current,
                    out var repaired) ||
                string.IsNullOrWhiteSpace(repaired) ||
                string.Equals(
                    repaired,
                    current,
                    StringComparison.Ordinal))
            {
                attempts[strategy.Name] =
                    "failed to apply";
                continue;
            }

            current = repaired;
            textWasRepaired = true;

            if (TryParseNode(current, out root))
            {
                attempts[strategy.Name] = "succeeded";

                return CreateResult(
                    root!,
                    expectation,
                    attempts,
                    textWasRepaired);
            }

            attempts[strategy.Name] =
                "applied; JSON remained malformed";
        }

        var tolerantResult =
            tolerantParser.Parse(
                current,
                expectation);

        attempts["tolerant-syntax-tree"] =
            tolerantResult.Outcome;

        if (tolerantResult.Root is null)
        {
            logger.LogWarning(
                "Malformed JSON could not be repaired. Repair attempts: {@RepairAttempts}",
                attempts);

            return new JsonRepairResult(
                document: null,
                wasRepaired: false,
                attempts);
        }

        return CreateResult(
            tolerantResult.Root,
            expectation,
            attempts,
            textWasRepaired: true);
    }

    private JsonRepairResult CreateResult(
        JsonNode root,
        JsonSchemaExpectation? expectation,
        IDictionary<string, string> attempts,
        bool textWasRepaired)
    {
        var current = root;
        var nodeWasRepaired = false;

        if (expectation is not null)
        {
            foreach (var strategy in nodeRepairStrategies)
            {
                try
                {
                    if (!strategy.TryRepair(
                            current,
                            expectation,
                            out var repaired))
                    {
                        attempts[strategy.Name] =
                            "not needed";
                        continue;
                    }

                    current = repaired;
                    nodeWasRepaired = true;
                    attempts[strategy.Name] =
                        "succeeded";
                }
                catch (Exception ex)
                {
                    attempts[strategy.Name] =
                        $"failed with {ex.GetType().Name}: {ex.Message}";
                }
            }
        }

        var document = JsonDocument.Parse(
            current.ToJsonString());
        var wasRepaired =
            textWasRepaired ||
            nodeWasRepaired;

        if (wasRepaired)
        {
            logger.LogWarning(
                "Malformed JSON was repaired. Repair attempts: {@RepairAttempts}",
                attempts);
        }

        return new JsonRepairResult(
            document,
            wasRepaired,
            new Dictionary<string, string>(
                attempts,
                StringComparer.Ordinal));
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
}
