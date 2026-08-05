using System.Text;
using System.Text.Json;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Converts JavaScript-style template literals used as pseudo-JSON values into
/// ordinary JSON strings. Models commonly use these for multiline generated
/// files even though backticks are not valid JSON.
/// </summary>
public sealed class PseudoJavaScriptTemplateStringRepairStrategy
    : ITextRepair
{
    public string Name =>
        "pseudo-javascript-template-string";

    public ValueTask<TextRepairAttempt> RepairAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(input) ||
            !input.Contains('`'))
        {
            return new(new TextRepairAttempt(
                RepairOutcome.NotApplicable,
                null));
        }

        var changed = TryRepair(input, out var repaired);

        return new(new TextRepairAttempt(
            changed
                ? RepairOutcome.Repaired
                : RepairOutcome.NotApplicable,
            changed ? repaired : null));
    }

    private static bool TryRepair(
        string input,
        out string repaired)
    {
        repaired = input;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var current = input;
        var searchStart = 0;
        var changed = false;

        while (TryFindTemplateLiteral(
                   current,
                   searchStart,
                   out var openingBacktick,
                   out var closingBacktick))
        {
            var rawValue = current[
                (openingBacktick + 1)..closingBacktick];
            var value =
                DecodeEscapedBackticks(rawValue);
            var jsonString =
                JsonSerializer.Serialize(value);

            current =
                current[..openingBacktick] +
                jsonString +
                current[(closingBacktick + 1)..];
            searchStart =
                openingBacktick +
                jsonString.Length;
            changed = true;
        }

        repaired = current;
        return changed;
    }

    private static bool TryFindTemplateLiteral(
        string input,
        int searchStart,
        out int openingBacktick,
        out int closingBacktick)
    {
        openingBacktick = -1;
        closingBacktick = -1;

        while (searchStart < input.Length)
        {
            var candidate = input.IndexOf(
                '`',
                searchStart);

            if (candidate < 0)
                return false;

            searchStart = candidate + 1;

            if (!LooksLikeJsonValuePosition(
                    input,
                    candidate))
            {
                continue;
            }

            if (!TryFindClosingBacktick(
                    input,
                    candidate + 1,
                    out closingBacktick))
            {
                continue;
            }

            openingBacktick = candidate;
            return true;
        }

        return false;
    }

    private static bool TryFindClosingBacktick(
        string input,
        int contentStart,
        out int closingBacktick)
    {
        closingBacktick = -1;

        for (var index = contentStart;
             index < input.Length;
             index++)
        {
            if (input[index] != '`' ||
                IsEscaped(input, index) ||
                !IsPlausibleJsonValueEnd(
                    input,
                    index))
            {
                continue;
            }

            closingBacktick = index;
            return true;
        }

        return false;
    }

    private static bool LooksLikeJsonValuePosition(
        string input,
        int backtickIndex)
    {
        var previous =
            PreviousNonWhitespace(
                input,
                backtickIndex - 1);

        return previous is ':' or '[' or ',';
    }

    private static bool IsPlausibleJsonValueEnd(
        string input,
        int backtickIndex)
    {
        var next = NextNonWhitespace(
            input,
            backtickIndex + 1);

        if (next < 0)
            return true;

        if (input[next] is '}' or ']')
            return true;

        if (input[next] != ',')
            return false;

        next = NextNonWhitespace(
            input,
            next + 1);

        if (next < 0)
            return true;

        if (input[next] is '{' or '[')
            return true;

        return LooksLikeJsonProperty(
            input,
            next);
    }

    private static bool LooksLikeJsonProperty(
        string input,
        int start)
    {
        if (start >= input.Length ||
            input[start] != '"')
        {
            return false;
        }

        for (var index = start + 1;
             index < input.Length;
             index++)
        {
            if (input[index] != '"' ||
                IsEscaped(input, index))
            {
                continue;
            }

            var next = NextNonWhitespace(
                input,
                index + 1);

            return next >= 0 &&
                input[next] == ':';
        }

        return false;
    }

    private static string DecodeEscapedBackticks(
        string input)
    {
        var output = new StringBuilder(
            input.Length);

        for (var index = 0;
             index < input.Length;
             index++)
        {
            if (input[index] == '\\' &&
                index + 1 < input.Length &&
                input[index + 1] == '`')
            {
                output.Append('`');
                index++;
                continue;
            }

            output.Append(input[index]);
        }

        return output.ToString();
    }

    private static char? PreviousNonWhitespace(
        string input,
        int start)
    {
        for (var index = start;
             index >= 0;
             index--)
        {
            if (!char.IsWhiteSpace(input[index]))
                return input[index];
        }

        return null;
    }

    private static int NextNonWhitespace(
        string input,
        int start)
    {
        for (var index = start;
             index < input.Length;
             index++)
        {
            if (!char.IsWhiteSpace(input[index]))
                return index;
        }

        return -1;
    }

    private static bool IsEscaped(
        string input,
        int index)
    {
        var slashCount = 0;

        for (var current = index - 1;
             current >= 0 &&
             input[current] == '\\';
             current--)
        {
            slashCount++;
        }

        return slashCount % 2 != 0;
    }
}
