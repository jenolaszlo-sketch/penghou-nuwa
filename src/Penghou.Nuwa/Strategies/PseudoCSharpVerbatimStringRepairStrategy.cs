using System.Text;
using System.Text.Json;

namespace Penghou.Nuwa.Strategies;

public sealed class PseudoCSharpVerbatimStringRepairStrategy
    : ITextRepairStrategy
{
    public string Name => "pseudo-csharp-verbatim-string";

    private const int MaxRepairs = 32;
    private const int MaxCandidatesPerLiteral = 64;

    public bool MightApply(string text)
    {
        return text.Contains("@\"", StringComparison.Ordinal)
            || text.Contains("@$", StringComparison.Ordinal); // covers $@"" as well
    }

    public bool TryRepair(string input, out string repaired)
    {
        repaired = input;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        if (!TryRepairFrom(input, searchStart: 0, depth: 0, out var candidate))
            return false;

        repaired = candidate;

        // Important:
        // Return true when the strategy changed the text.
        // Do NOT require final JSON validity here, because the tolerant parser
        // and text-repair fallback may still need to repair trailing commas,
        // missing braces, etc.
        return !string.Equals(repaired, input, StringComparison.Ordinal);
    }

    private static bool TryRepairFrom(string text, int searchStart, int depth, out string repaired)
    {
        repaired = text;

        if (depth >= MaxRepairs)
            return false;

        var start = FindNextPseudoVerbatimStart(text, searchStart);

        if (start < 0)
            return false;

        var contentStart = start + 2;
        var endCandidates = FindEndCandidates(text, contentStart)
            .OrderBy(static x => x)              // nearest terminator first
            .Take(MaxCandidatesPerLiteral)
            .ToArray();

        string? bestPartial = null;

        foreach (var end in endCandidates)
        {
            var value = text[contentStart..end].Replace("\"\"", "\"");
            var jsonString = JsonSerializer.Serialize(value);
            var candidate = text[..start] + jsonString + text[(end + 1)..];

            if (IsValidJsonText(candidate))
            {
                repaired = candidate;
                return true;
            }

            bestPartial ??= candidate;   // keep the nearest-split conversion even if not fully valid yet

            if (TryRepairFrom(candidate, start + jsonString.Length, depth + 1, out var nested))
            {
                repaired = nested;
                return true;
            }
        }

        if (bestPartial is not null)
        {
            repaired = bestPartial;
            return true;   // changed, not yet valid — the fallback handles the rest
        }

        return TryRepairFrom(text, start + 2, depth, out repaired);
    }

    private static int FindNextPseudoVerbatimStart(string text, int searchStart)
    {
        var index = searchStart;

        while (index < text.Length)
        {
            var found = text.IndexOf("@\"", index, StringComparison.Ordinal);

            if (found < 0)
                return -1;

            if (LooksLikeJsonValuePosition(text, found))
                return found;

            index = found + 2;
        }

        return -1;
    }

    private static bool LooksLikeJsonValuePosition(string text, int atSignIndex)
    {
        var previous = PreviousNonWhitespace(text, atSignIndex - 1);

        // Most common:
        // "content": @"..."
        //
        // Also allow array values:
        // [ @"..." ]
        //
        // Comma is allowed for cases like:
        // [ "x", @"..." ]
        return previous is ':' or '[' or ',';
    }

    private static char? PreviousNonWhitespace(string text, int index)
    {
        for (var i = index; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(text[i]))
                return text[i];
        }

        return null;
    }

    private static IEnumerable<int> FindEndCandidates(string text, int contentStart)
    {
        for (var i = contentStart; i < text.Length; i++)
        {
            if (text[i] != '"')
                continue;

            var next = NextNonWhitespaceIndex(text, i + 1);

            if (next < 0)
            {
                yield return i;
                continue;
            }

            var nextChar = text[next];

            // End of JSON value:
            // "content": @"..."}
            // "content": @"..."]
            if (nextChar is '}' or ']')
            {
                yield return i;
                continue;
            }

            // End before next property:
            // "content": @"...", "path": "..."
            if (nextChar == ',' && LooksLikePropertyAfterComma(text, next + 1))
            {
                yield return i;
                continue;
            }

            // End before another object/array item:
            // [ @"...", { ... } ]
            // [ @"...", [ ... ] ]
            if (nextChar == ',' && LooksLikeValueAfterComma(text, next + 1))
            {
                yield return i;
            }
        }
    }

    private static int NextNonWhitespaceIndex(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
                return i;
        }

        return -1;
    }

    private static bool LooksLikePropertyAfterComma(string text, int start)
    {
        var i = NextNonWhitespaceIndex(text, start);

        if (i < 0 || text[i] != '"')
            return false;

        i++;

        while (i < text.Length)
        {
            if (text[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (text[i] == '"')
                break;

            i++;
        }

        if (i >= text.Length || text[i] != '"')
            return false;

        i++;

        i = NextNonWhitespaceIndex(text, i);

        return i >= 0 && text[i] == ':';
    }

    private static bool LooksLikeValueAfterComma(string text, int start)
    {
        var i = NextNonWhitespaceIndex(text, start);

        if (i < 0)
            return false;

        return text[i] is '{' or '[' or '"' or '@' or '-' ||
               char.IsDigit(text[i]) ||
               StartsWithLiteral(text, i, "true") ||
               StartsWithLiteral(text, i, "false") ||
               StartsWithLiteral(text, i, "null");
    }

    private static bool StartsWithLiteral(string text, int index, string literal)
    {
        return index + literal.Length <= text.Length &&
               string.Compare(
                   text,
                   index,
                   literal,
                   0,
                   literal.Length,
                   StringComparison.Ordinal) == 0;
    }

    private static bool IsValidJsonText(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
