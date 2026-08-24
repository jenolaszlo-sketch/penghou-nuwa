namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Extracts a JSON payload from surrounding assistant prose, e.g.
/// <c>Here is the JSON you requested: {"a": 1}</c> or a sentence after the
/// closing brace. The strategy locates the first structure that opens with
/// <c>{</c> or <c>[</c> and whose string-aware balanced close reaches the end
/// of the message (allowing trailing whitespace, punctuation, or a Markdown
/// fence). If no balanced close exists — truncated output — the longest
/// prefix from the same opening token is extracted so downstream phases can
/// repair the remainder.
/// </summary>
public sealed class ProseWrapperExtractionStrategy
    : ITextRepair
{
    private const int MaxScanLength = 512 * 1024;

    public string Name => "prose-wrapper-extraction";

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

        var scanEnd = Math.Min(input.Length, MaxScanLength);
        for (var start = 0; start < scanEnd; start++)
        {
            var opening = input[start];
            if (opening is not ('{' or '['))
            {
                continue;
            }

            // The opening token must sit outside any string in the prose
            // prefix; approximate by requiring the prefix to contain no
            // straight quote imbalance. Prose quotes are typically curly.
            if (!PrefixIsQuoteBalanced(input, start))
            {
                continue;
            }

            var close = FindBalancedClose(
                input,
                start,
                scanEnd);
            if (close < 0)
            {
                // Truncated payload inside prose: extract the raw remainder
                // so tolerant recovery and salvage can still work on it.
                payload = input[start..].TrimEnd();
            }
            else
            {
                // Allow one trailing fence line or punctuation after close.
                var end = close + 1;
                while (end < input.Length &&
                       (char.IsWhiteSpace(input[end]) ||
                        input[end] is '`' or '~' or '.' or '!'))
                {
                    end++;
                }

                payload = input[start..(close + 1)];
                if (end < input.Length &&
                    !IsOnlyFenceOrWhitespace(input, close + 1))
                {
                    continue;
                }
            }

            if (payload.Length == 0)
            {
                continue;
            }

            // Only report a repair when something was actually stripped.
            var prefix = input[..start].Trim('`', '~', ' ', '\t', '\r', '\n');
            var suffix = input[(start + payload.Length)..]
                .Trim('`', '~', '.', '!', ' ', '\t', '\r', '\n');
            if (prefix.Length == 0 && suffix.Length == 0)
            {
                return false;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Scans from an opening token with string/escape awareness and returns
    /// the index of the matching close, or -1 when unbalanced within bounds.
    /// </summary>
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

    /// <summary>Heuristic: the candidate must not open inside quoted prose.</summary>
    private static bool PrefixIsQuoteBalanced(
        string input,
        int start)
    {
        var count = 0;
        for (var index = 0; index < start; index++)
        {
            if (input[index] == '"')
            {
                count++;
            }
        }

        return count % 2 == 0;
    }

    private static bool IsOnlyFenceOrWhitespace(
        string input,
        int start)
    {
        for (var index = start; index < input.Length; index++)
        {
            if (!char.IsWhiteSpace(input[index]) &&
                input[index] is not '`' and not '~')
            {
                return false;
            }
        }

        return true;
    }
}
