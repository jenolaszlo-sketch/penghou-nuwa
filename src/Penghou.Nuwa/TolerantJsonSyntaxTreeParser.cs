using System.Text.Json;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa;

/// <summary>
/// Recovers a JSON syntax tree from malformed input. The handwritten parser
/// owns schema-aware token recovery; a self-contained text-repair pass is the
/// fallback for malformed forms outside the recovery grammar.
/// </summary>
public sealed class TolerantJsonSyntaxTreeParser
    : ITolerantJsonSyntaxTreeParser
{
    public TolerantJsonSyntaxTreeParseResult Parse(
        string input,
        JsonSchemaExpectation? expectation = null)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new TolerantJsonSyntaxTreeParseResult(
                Root: null,
                Outcome: "failed: input was empty");
        }

        try
        {
            var recovery =
                new TolerantJsonRecoveryParser(
                        input,
                        expectation)
                    .Parse();

            if (recovery.Root is not null)
            {
                return new TolerantJsonSyntaxTreeParseResult(
                    recovery.Root,
                    DescribeRecovery(recovery));
            }

            var repairedJson =
                TolerantJsonTextRepair.TryRepair(
                    input);

            if (repairedJson is null)
            {
                return new TolerantJsonSyntaxTreeParseResult(
                    Root: null,
                    Outcome:
                        "failed: text repair produced no output");
            }

            var root = JsonNode.Parse(repairedJson);
            var outcome =
                $"succeeded: tolerant text repair; {DescribeRepair(
                    input,
                    repairedJson)}";

            return root is null
                ? new TolerantJsonSyntaxTreeParseResult(
                    Root: null,
                    Outcome:
                        "failed: text repair produced a null root")
                : new TolerantJsonSyntaxTreeParseResult(
                    Root: root,
                    Outcome: outcome);
        }
        catch (JsonException ex)
        {
            return new TolerantJsonSyntaxTreeParseResult(
                Root: null,
                Outcome:
                    $"failed to materialize repaired syntax tree: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Model output is untrusted. A tolerant-parser defect should be
            // reported as a failed repair, not escape the normalization path.
            return new TolerantJsonSyntaxTreeParseResult(
                Root: null,
                Outcome:
                    $"failed with {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string DescribeRecovery(
        TolerantJsonRecoveryResult recovery)
    {
        var outcome =
            $"succeeded: handwritten recovery repaired {recovery.RepairCount} token(s); recovered a syntax tree";

        if (recovery.SchemaStringRepairCount > 0)
        {
            outcome =
                $"succeeded: schema-guided recovery repaired {recovery.SchemaStringRepairCount} string value(s); {outcome}";
        }

        if (recovery.Repairs.Count > 0)
        {
            outcome =
                $"{outcome}; first correction: {recovery.Repairs[0]}";
        }

        return outcome;
    }

    private static string DescribeRepair(
        string input,
        string repaired)
    {
        var mismatch = FindFirstMismatch(input, repaired);

        if (mismatch < 0)
            return "succeeded: recovered a syntax tree";

        var found = mismatch < input.Length
            ? DescribeCharacter(input[mismatch])
            : "end of input";
        var replacement = mismatch < repaired.Length
            ? DescribeCharacter(repaired[mismatch])
            : "end of input";

        return
            $"succeeded: recovered a syntax tree; first correction at offset {mismatch} replaced {found} with {replacement}";
    }

    private static int FindFirstMismatch(
        string left,
        string right)
    {
        var commonLength = Math.Min(
            left.Length,
            right.Length);

        for (var index = 0; index < commonLength; index++)
        {
            if (left[index] != right[index])
                return index;
        }

        return left.Length == right.Length
            ? -1
            : commonLength;
    }

    private static string DescribeCharacter(char value) =>
        value switch
        {
            '\r' => "'\\r'",
            '\n' => "'\\n'",
            '\t' => "'\\t'",
            _ => $"'{value}'"
        };
}
