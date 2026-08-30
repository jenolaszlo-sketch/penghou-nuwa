namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Handles a complete top-level JSON value followed by a stray delimiter or
/// prose. Inputs containing another structural value are refused as ambiguous
/// instead of silently choosing the first document.
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

        var changed = TryExtract(input, out var repaired, out var note);

        return new(new TextRepairAttempt(
            changed
                ? RepairOutcome.Repaired
                : note is null
                    ? RepairOutcome.NotApplicable
                    : RepairOutcome.Failed,
            changed ? repaired : null,
            note));
    }

    internal static bool TryExtract(
        string input,
        out string payload,
        out string? note)
    {
        payload = string.Empty;
        note = null;

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

        if (ContainsAnotherStructure(input, close + 1))
        {
            note = "Multiple top-level structural values were present; selection would be ambiguous.";
            return false;
        }

        payload = input[..(close + 1)];
        return true;
    }

    private static bool ContainsAnotherStructure(
        string input,
        int start)
    {
        for (var index = start; index < input.Length; index++)
        {
            if (input[index] is '{' or '[')
                return true;
        }

        return false;
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
