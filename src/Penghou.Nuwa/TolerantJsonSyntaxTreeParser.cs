using System.Text.Json;

namespace Penghou.Nuwa;

/// <summary>
/// Recovers a JSON syntax tree from malformed input. The handwritten parser
/// owns schema-aware token recovery. Text-level salvage is owned by the
/// pipeline's ordered fallback phase and runs only when recovery fails.
/// </summary>
internal sealed class TolerantJsonSyntaxTreeParser
    : ITolerantJsonSyntaxTreeParser
{
    public TolerantJsonSyntaxTreeParseResult Parse(
        string input,
        JsonSchemaExpectation? expectation,
        JsonRepairLimits limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
                        expectation,
                        limits,
                        cancellationToken)
                    .Parse();

            if (recovery.Root is not null)
            {
                return new TolerantJsonSyntaxTreeParseResult(
                    recovery.Root,
                    DescribeRecovery(recovery),
                    recovery.RepairCount,
                    recovery.SchemaStringRepairCount,
                    recovery.Repairs);
            }

            return new TolerantJsonSyntaxTreeParseResult(
                Root: null,
                Outcome:
                    "failed: handwritten recovery could not rebuild a syntax tree");
        }
        catch (JsonException ex)
        {
            return new TolerantJsonSyntaxTreeParseResult(
                Root: null,
                Outcome:
                    $"failed to materialize repaired syntax tree: {ex.Message}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonRepairLimitException)
        {
            throw;
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
}
