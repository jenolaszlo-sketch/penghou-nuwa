namespace Penghou.Nuwa.Strategies;

using System.Text;

/// <summary>
/// Normalizes Unicode characters that models frequently emit in place of
/// ASCII JSON structure: curly quotes used as string delimiters, full-width
/// CJK brackets and punctuation, byte-order marks, and zero-width spaces.
///
/// This is deliberately conservative about letters and digits — only
/// delimiter-class characters are rewritten. Curly quotes inside intended
/// string content change spelling; that tradeoff is accepted because such
/// content would not parse as JSON delimiters anyway.
/// </summary>
public sealed class UnicodeDelimiterNormalizationStrategy
    : ITextRepair
{
    public string Name => "unicode-delimiter-normalization";

    public ValueTask<TextRepairAttempt> RepairAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(input) ||
            !RequiresNormalization(input))
        {
            return new(new TextRepairAttempt(
                RepairOutcome.NotApplicable,
                null));
        }

        var builder = new StringBuilder(input.Length);
        var changed = false;

        foreach (var current in input)
        {
            if (current == '\uFEFF' ||
                current is >= '\u200B' and <= '\u200D')
            {
                // Byte-order mark and zero-width characters never carry
                // meaning in model-produced JSON payloads.
                changed = true;
                continue;
            }

            var mapped = MapDelimiter(current);
            if (mapped != current)
            {
                changed = true;
                builder.Append(mapped);
                continue;
            }

            builder.Append(current);
        }

        if (!changed)
        {
            return new(new TextRepairAttempt(
                RepairOutcome.NotApplicable,
                null));
        }

        var repaired = builder.ToString();
        return new(new TextRepairAttempt(
            string.Equals(repaired, input, StringComparison.Ordinal)
                ? RepairOutcome.NotApplicable
                : RepairOutcome.Repaired,
            string.Equals(repaired, input, StringComparison.Ordinal)
                ? null
                : repaired));
    }

    private static bool RequiresNormalization(string input)
    {
        foreach (var current in input)
        {
            if (current == '\uFEFF' ||
                current is >= '\u200B' and <= '\u200D' ||
                MapDelimiter(current) != current)
            {
                return true;
            }
        }

        return false;
    }

    private static char MapDelimiter(char current) => current switch
    {
        // Curly double quotes used as JSON string delimiters.
        '\u201C' or '\u201D' or '\u201E' or '\u201F' => '"',
        // Curly single quotes map to straight single quotes, which the
        // tolerant parser already understands as pseudo-string bounds.
        '\u2018' or '\u2019' or '\u201B' => '\'',
        // Full-width CJK forms of structural characters.
        '\uFF5B' => '{',
        '\uFF5D' => '}',
        '\uFF3B' => '[',
        '\uFF3D' => ']',
        '\uFF08' => '(',
        '\uFF09' => ')',
        '\uFF1A' => ':',
        '\uFF0C' => ',',
        _ => current
    };
}
