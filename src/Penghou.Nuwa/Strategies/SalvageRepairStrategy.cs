using System.Text;
using System.Text.Json;

namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Self-contained text-level salvage pass for malformed JSON that the
/// handwritten recovery parser cannot rebuild. Handles the common "almost
/// JSON" shapes that models emit: comments, Python-style literals,
/// single-quoted strings, unquoted object keys and values, and raw control
/// characters inside strings. Lossy by design, so it runs only after recovery
/// has failed.
/// </summary>
public sealed class SalvageRepairStrategy
    : ITextRepair
{
    public string Name => "salvage";

    public ValueTask<TextRepairAttempt> RepairAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repaired = TryRepair(input);

        if (repaired is null ||
            string.Equals(repaired, input, StringComparison.Ordinal))
        {
            return new(new TextRepairAttempt(
                RepairOutcome.NotApplicable,
                null));
        }

        return new(new TextRepairAttempt(
            RepairOutcome.Repaired,
            repaired));
    }

    /// <summary>
    /// Returns the best-effort repaired text, or null when the input is
    /// empty or whitespace-only. The caller is responsible for attempting to
    /// materialize the result.
    /// </summary>
    public static string? TryRepair(
        string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var cleaned = Clean(
            input.TrimStart('\uFEFF').Trim());

        if (cleaned.Length == 0)
            return null;

        if (IsValidJson(cleaned))
            return cleaned;

        var atContainer = SkipToJsonStart(cleaned);

        if (atContainer > 0)
        {
            var trimmed = cleaned[atContainer..];

            if (IsValidJson(trimmed))
                return trimmed;

            cleaned = trimmed;
        }

        var closed = CompleteContainers(cleaned);

        return IsValidJson(closed)
            ? closed
            : cleaned;
    }

    private static string Clean(
        string input)
    {
        var output = new StringBuilder(input.Length);
        var singleQuoteBuffer = new StringBuilder();
        var inSingleQuote = false;
        var escaped = false;
        var index = 0;

        while (index < input.Length)
        {
            var current = input[index];

            if (inSingleQuote)
            {
                if (current == '\\' && !escaped)
                {
                    singleQuoteBuffer.Append(current);
                    escaped = true;
                    index++;
                    continue;
                }

                if (current == '\'' && !escaped)
                {
                    inSingleQuote = false;
                    AppendConvertedSingleQuoted(
                        output,
                        singleQuoteBuffer);
                    singleQuoteBuffer.Clear();
                    index++;
                    continue;
                }

                singleQuoteBuffer.Append(current);
                escaped = false;
                index++;
                continue;
            }

            if (current == '"')
            {
                index = AppendDoubleQuoted(
                    input,
                    output,
                    index);
                continue;
            }

            if (TrySkipComment(
                    input,
                    index,
                    output,
                    out var commentEnd))
            {
                index = commentEnd;
                continue;
            }

            if (current == '\'')
            {
                inSingleQuote = true;
                escaped = false;
                singleQuoteBuffer.Clear();
                index++;
                continue;
            }

            if (IsIdentifierStart(current))
            {
                index = AppendIdentifier(
                    input,
                    output,
                    index);
                continue;
            }

            if (IsInvalidOutsideString(current))
            {
                index++;
                continue;
            }

            output.Append(current);
            index++;
        }

        if (inSingleQuote)
        {
            AppendConvertedSingleQuoted(
                output,
                singleQuoteBuffer);
        }

        return output.ToString();
    }

    private static void AppendConvertedSingleQuoted(
        StringBuilder output,
        StringBuilder buffer)
    {
        var content = buffer.ToString();

        if (content.Contains('"'))
        {
            // Content embeds double quotes; converting would corrupt the
            // string, so leave the source spelling untouched.
            output.Append('\'');
            output.Append(content);
            output.Append('\'');
            return;
        }

        output.Append('"');
        output.Append(
            content.Replace("\\'", "'"));
        output.Append('"');
    }

    private static int AppendDoubleQuoted(
        string input,
        StringBuilder output,
        int start)
    {
        var index = start;
        output.Append('"');
        index++;
        var escaped = false;
        var closed = false;

        while (index < input.Length)
        {
            var current = input[index];

            if (escaped)
            {
                output.Append(current);
                escaped = false;
                index++;
                continue;
            }

            if (current == '\\')
            {
                output.Append(current);
                escaped = true;
                index++;
                continue;
            }

            if (current == '"')
            {
                output.Append('"');
                index++;
                closed = true;
                break;
            }

            if (IsRawControl(current))
            {
                AppendEscapedControl(
                    output,
                    current);
            }
            else
            {
                output.Append(current);
            }

            index++;
        }

        if (!closed)
        {
            // The model truncated before closing the string. Close it so the
            // salvage output can parse, mirroring the recovery parser.
            output.Append('"');
        }

        return index;
    }

    private static bool TrySkipComment(
        string input,
        int start,
        StringBuilder output,
        out int end)
    {
        end = start;

        if (input[start] == '#')
        {
            end = SkipLineComment(
                input,
                start + 1);
            output.Append(' ');
            return true;
        }

        if (input[start] != '/' ||
            start + 1 >= input.Length)
        {
            return false;
        }

        var next = input[start + 1];

        if (next == '/')
        {
            end = SkipLineComment(
                input,
                start + 2);
            output.Append(' ');
            return true;
        }

        if (next != '*')
            return false;

        end = SkipBlockComment(
            input,
            start + 2);

        // A space prevents tokens glued around the comment from joining.
        output.Append(' ');
        return true;
    }

    private static int SkipLineComment(
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

    private static int SkipBlockComment(
        string input,
        int start)
    {
        var index = start;

        while (index + 1 < input.Length &&
               !(input[index] == '*' &&
                 input[index + 1] == '/'))
        {
            index++;
        }

        return Math.Min(
            index + 2,
            input.Length);
    }

    private static int AppendIdentifier(
        string input,
        StringBuilder output,
        int start)
    {
        var index = start;

        while (index < input.Length &&
               IsIdentifierPart(input[index]))
        {
            index++;
        }

        var word = input[start..index];
        var next = NextNonWhitespace(
            input,
            index);

        if (next >= 0 &&
            input[next] == ':')
        {
            // Unquoted object key.
            output.Append('"');
            output.Append(word);
            output.Append('"');
            return index;
        }

        if (IsPythonLiteral(word))
        {
            output.Append(
                MapPythonLiteral(word));
            return index;
        }

        // A bare identifier in value position can only be a string in
        // salvage terms. Unknown literals are quoted rather than invented.
        output.Append('"');
        output.Append(word);
        output.Append('"');
        return index;
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

    private static int SkipToJsonStart(
        string input)
    {
        var inString = false;
        var escaped = false;

        for (var index = 0;
             index < input.Length;
             index++)
        {
            var current = input[index];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current is '{' or '[')
                return index;
        }

        return -1;
    }

    private static string CompleteContainers(
        string input)
    {
        const int maximumInsertedClosers = 16;
        var stack = new Stack<char>();
        var inString = false;
        var escaped = false;

        foreach (var current in input)
        {
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
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
                case '{':
                case '[':
                    stack.Push(current);
                    break;
                case '}':
                    if (stack.Count > 0 &&
                        stack.Peek() == '{')
                    {
                        stack.Pop();
                    }

                    break;
                case ']':
                    if (stack.Count > 0 &&
                        stack.Peek() == '[')
                    {
                        stack.Pop();
                    }

                    break;
            }
        }

        if (stack.Count == 0 ||
            stack.Count > maximumInsertedClosers)
        {
            return input;
        }

        var output = new StringBuilder(
            input,
            input.Length + stack.Count);

        while (stack.Count > 0)
        {
            output.Append(
                stack.Pop() == '{'
                    ? '}'
                    : ']');
        }

        return output.ToString();
    }

    private static void AppendEscapedControl(
        StringBuilder output,
        char value)
    {
        switch (value)
        {
            case '\b':
                output.Append("\\b");
                break;
            case '\f':
                output.Append("\\f");
                break;
            case '\n':
                output.Append("\\n");
                break;
            case '\r':
                output.Append("\\r");
                break;
            case '\t':
                output.Append("\\t");
                break;
            default:
                output.Append("\\u");
                output.Append(
                    ((int)value)
                    .ToString("x4"));
                break;
        }
    }

    private static bool IsValidJson(
        string text)
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

    private static bool IsRawControl(
        char value) =>
        value < 0x20;

    private static bool IsInvalidOutsideString(
        char value) =>
        value < 0x20 &&
        value is not ('\t' or '\n' or '\r' or ' ');

    private static bool IsIdentifierStart(
        char value) =>
        char.IsLetter(value) ||
        value is '_' or '$';

    private static bool IsIdentifierPart(
        char value) =>
        char.IsLetterOrDigit(value) ||
        value is '_' or '$' or '-';

    private static bool IsPythonLiteral(
        string word) =>
        word is
            "None" or
            "True" or
            "False" or
            "NaN" or
            "Infinity" or
            "-Infinity";

    private static string MapPythonLiteral(
        string word) =>
        word switch
        {
            "None" or "NaN" or "Infinity" or "-Infinity" => "null",
            "True" => "true",
            "False" => "false",
            _ => word
        };
}
