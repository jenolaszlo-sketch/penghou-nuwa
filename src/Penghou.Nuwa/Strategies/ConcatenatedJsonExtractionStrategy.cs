namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Handles models that emit more than one top-level JSON value back to back,
/// e.g. <c>{"a":1}{"b":2}</c> or newline-delimited objects. The first
/// complete value is kept and the remainder discarded — the right behaviour
/// for tool arguments and single-response structured output, where the first
/// object is the answer and later objects are echoes or continuations.
/// </summary>
public sealed class ConcatenatedJsonExtractionStrategy
    : ITextRepair
{
    private const int MaxScanLength = 512 * 1024;

    public string Name => "concatenated-json";

    public ValueTask<TextRepairAttempt> RepairAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(input) ||
            input.Length > MaxScanLength)
        {
            return new(new TextRepairAttempt(
                RepairOutcome.NotApplicable,
                null));
        }

        var changed = TryExtract(input, out var repaired);

        return new(new TextRepairAttempt(
            changed ? RepairOutcome.Repaired : RepairOutcome.NotApplicable,
            changed ? repaired : null));
    }

    internal static bool TryExtract(
        string input,
        out string payload)
    {
        payload = string.Empty;

        var start = IndexOfFirstStructure(input);
        if (start < 0 ||
            input[start] is not ('{' or '['))
        {
            return false;
        }

        var close = FindBalancedClose(
            input,
            start,
            Math.Min(input.Length, MaxScanLength));
        if (close < 0)
        {
            // Truncated: not this strategy's case.
            return false;
        }

        // Anything after the close except separators/whitespace means a
        // second value follows.
        var index = close + 1;
        var hasTrailingContent = false;
        while (index < input.Length)
        {
            var current = input[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current is ',' or ';')
            {
                hasTrailingContent = true;
                index++;
                continue;
            }

            hasTrailingContent = true;
            break;
        }

        if (!hasTrailingContent)
        {
            return false;
        }

        payload = input[..(close + 1)];
        return true;
    }

    private static int IndexOfFirstStructure(string input)
    {
        for (var index = 0; index < input.Length; index++)
        {
            if (input[index] is '{' or '[' or '"')
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindBalancedClose(
        string input,
        int start,
        int end)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = start; index < end; index++)
        {
            var current = input[index];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (current)
            {
                case '"':
                    inString = true;
                    break;
                case '{' or '[':
                    depth++;
                    break;
                case '}' or ']':
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }

                    if (depth < 0)
                    {
                        return -1;
                    }

                    break;
            }
        }

        return -1;
    }
}
