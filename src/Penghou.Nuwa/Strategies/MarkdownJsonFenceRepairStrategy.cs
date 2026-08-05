using System;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Removes an outer Markdown JSON code fence.
///
/// Supported cases include:
/// - Complete JSON fences.
/// - Missing closing fences.
/// - Leading or trailing whitespace.
/// - Backtick and tilde fences.
/// - Case-insensitive JSON language identifiers.
///
/// This strategy only removes the transport wrapper. It does not attempt to
/// repair the JSON contained inside the fence.
/// </summary>
public sealed class MarkdownJsonFenceRepairStrategy
    : ITextRepairStrategy
{
    public string Name => "markdown-json-fence";

    public bool MightApply(string text)
    {
        return text.Contains("```", StringComparison.Ordinal);
    }

    public bool TryRepair(
        string input,
        out string repaired)
    {
        repaired = input;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (!TryFindOpeningFence(
                input,
                out var bodyStart,
                out var marker,
                out var minimumFenceLength))
        {
            return false;
        }

        var bodyEnd = input.Length;

        if (TryFindClosingFence(
                input,
                bodyStart,
                marker,
                minimumFenceLength,
                out var closingFenceStart))
        {
            bodyEnd = closingFenceStart;
        }

        repaired = input[bodyStart..bodyEnd].Trim();

        return !string.Equals(
            repaired,
            input,
            StringComparison.Ordinal);
    }

    private static bool TryFindOpeningFence(
        string input,
        out int bodyStart,
        out char marker,
        out int fenceLength)
    {
        bodyStart = 0;
        marker = default;
        fenceLength = 0;

        var lineStart = SkipLeadingWhitespaceAndBom(
            input);

        if (lineStart >= input.Length)
        {
            return false;
        }

        var lineEnd = FindLineEnd(
            input,
            lineStart);

        var contentEnd = TrimLineEndWhitespace(
            input,
            lineStart,
            lineEnd);

        if (!TryParseOpeningFence(
                input,
                lineStart,
                contentEnd,
                out marker,
                out fenceLength))
        {
            return false;
        }

        bodyStart = SkipLineEnding(
            input,
            lineEnd);

        return true;
    }

    private static bool TryParseOpeningFence(
        string input,
        int start,
        int end,
        out char marker,
        out int fenceLength)
    {
        marker = default;
        fenceLength = 0;

        if (start >= end)
        {
            return false;
        }

        var first = input[start];

        if (first is not ('`' or '~'))
        {
            return false;
        }

        var index = start;

        while (index < end &&
               input[index] == first)
        {
            index++;
        }

        fenceLength = index - start;

        if (fenceLength < 3)
        {
            return false;
        }

        var languageStart = SkipWhitespace(
            input,
            index,
            end);

        var languageEnd = TrimWhitespaceEnd(
            input,
            languageStart,
            end);

        if (languageStart < languageEnd &&
            !IsJsonLanguage(
                input.AsSpan(
                    languageStart,
                    languageEnd - languageStart)))
        {
            return false;
        }

        marker = first;
        return true;
    }

    private static bool TryFindClosingFence(
        string input,
        int bodyStart,
        char marker,
        int minimumFenceLength,
        out int closingFenceStart)
    {
        closingFenceStart = 0;

        var contentEnd = TrimWhitespaceEnd(
            input,
            bodyStart,
            input.Length);

        if (contentEnd <= bodyStart)
        {
            return false;
        }

        var lineStart = FindLineStart(
            input,
            bodyStart,
            contentEnd);

        var fenceStart = SkipHorizontalWhitespace(
            input,
            lineStart,
            contentEnd);

        var fenceEnd = TrimHorizontalWhitespaceEnd(
            input,
            fenceStart,
            contentEnd);

        if (!IsClosingFence(
                input,
                fenceStart,
                fenceEnd,
                marker,
                minimumFenceLength))
        {
            return false;
        }

        closingFenceStart = lineStart;
        return true;
    }

    private static bool IsClosingFence(
        string input,
        int start,
        int end,
        char marker,
        int minimumFenceLength)
    {
        if (end - start < minimumFenceLength)
        {
            return false;
        }

        for (var index = start;
             index < end;
             index++)
        {
            if (input[index] != marker)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsJsonLanguage(
        ReadOnlySpan<char> language)
    {
        return language.Equals(
                   "json",
                   StringComparison.OrdinalIgnoreCase) ||
               language.Equals(
                   "jsonc",
                   StringComparison.OrdinalIgnoreCase) ||
               language.Equals(
                   "application/json",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static int SkipLeadingWhitespaceAndBom(
        string input)
    {
        var index = 0;

        if (input.Length > 0 &&
            input[0] == '\uFEFF')
        {
            index++;
        }

        while (index < input.Length &&
               char.IsWhiteSpace(input[index]))
        {
            index++;
        }

        return index;
    }

    private static int FindLineEnd(
        string input,
        int start)
    {
        var index = start;

        while (index < input.Length &&
               input[index] is not ('\r' or '\n'))
        {
            index++;
        }

        return index;
    }

    private static int SkipLineEnding(
        string input,
        int index)
    {
        if (index < input.Length &&
            input[index] == '\r')
        {
            index++;
        }

        if (index < input.Length &&
            input[index] == '\n')
        {
            index++;
        }

        return index;
    }

    private static int FindLineStart(
        string input,
        int minimum,
        int end)
    {
        var index = end;

        while (index > minimum)
        {
            var previous = input[index - 1];

            if (previous is '\r' or '\n')
            {
                break;
            }

            index--;
        }

        return index;
    }

    private static int SkipWhitespace(
        string input,
        int start,
        int end)
    {
        var index = start;

        while (index < end &&
               char.IsWhiteSpace(input[index]))
        {
            index++;
        }

        return index;
    }

    private static int SkipHorizontalWhitespace(
        string input,
        int start,
        int end)
    {
        var index = start;

        while (index < end &&
               input[index] is ' ' or '\t')
        {
            index++;
        }

        return index;
    }

    private static int TrimWhitespaceEnd(
        string input,
        int minimum,
        int end)
    {
        var index = end;

        while (index > minimum &&
               char.IsWhiteSpace(input[index - 1]))
        {
            index--;
        }

        return index;
    }

    private static int TrimHorizontalWhitespaceEnd(
        string input,
        int minimum,
        int end)
    {
        var index = end;

        while (index > minimum &&
               input[index - 1] is ' ' or '\t')
        {
            index--;
        }

        return index;
    }

    private static int TrimLineEndWhitespace(
        string input,
        int minimum,
        int end)
    {
        var index = end;

        while (index > minimum &&
               input[index - 1] is ' ' or '\t')
        {
            index--;
        }

        return index;
    }
}
