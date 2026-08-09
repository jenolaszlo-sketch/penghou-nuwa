namespace Penghou.Nuwa;

/// <summary>
/// Token cursor for damaged JSON. Quotes are intentionally emitted as
/// individual tokens because only the parser has enough container and schema
/// context to decide whether a quote opens, closes, or belongs to raw content.
/// </summary>
internal sealed class TolerantJsonTokenReader(
    string source)
{
    public string Source { get; } =
        source ??
        throw new ArgumentNullException(
            nameof(source));

    public int Position { get; private set; }

    public bool IsAtEnd =>
        Position >= Source.Length;

    public char Current =>
        IsAtEnd
            ? '\0'
            : Source[Position];

    public JsonToken Peek(
        int distance = 0) =>
        PeekFrom(
            Position,
            distance);

    public JsonToken PeekFrom(
        int start,
        int distance = 0)
    {
        var position = Math.Clamp(
            start,
            0,
            Source.Length);
        JsonToken token = default;

        for (var index = 0;
             index <= distance;
             index++)
        {
            token = ReadToken(
                Source,
                ref position);
        }

        return token;
    }

    public JsonToken Read()
    {
        var position = Position;
        var token = ReadToken(
            Source,
            ref position);
        Position = position;
        return token;
    }

    public void AdvanceCharacter()
    {
        if (!IsAtEnd)
            Position++;
    }

    public void AdvanceTo(
        int position) =>
        Position = Math.Clamp(
            position,
            Position,
            Source.Length);

    public void SkipWhitespace()
    {
        while (!IsAtEnd &&
               char.IsWhiteSpace(Current))
        {
            Position++;
        }
    }

    private static JsonToken ReadToken(
        string source,
        ref int position)
    {
        while (position < source.Length)
        {
            var c = source[position];

            if (char.IsWhiteSpace(c))
            {
                position++;
                continue;
            }

            if (c == '#')
            {
                position = SkipLineComment(
                    source,
                    position);
                continue;
            }

            if (c == '/' &&
                position + 1 < source.Length &&
                source[position + 1] is '/' or '*')
            {
                position = source[position + 1] == '/'
                    ? SkipLineComment(source, position)
                    : SkipBlockComment(source, position);
                continue;
            }

            break;
        }

        if (position >= source.Length)
        {
            return new JsonToken(
                JsonTokenKind.End,
                source.Length,
                0);
        }

        var start = position;
        var current = source[position++];
        var punctuation = current switch
        {
            '{' => JsonTokenKind.ObjectStart,
            '}' => JsonTokenKind.ObjectEnd,
            '[' => JsonTokenKind.ArrayStart,
            ']' => JsonTokenKind.ArrayEnd,
            ':' => JsonTokenKind.Colon,
            ',' => JsonTokenKind.Comma,
            '"' => JsonTokenKind.Quote,
            '\'' => JsonTokenKind.SingleQuote,
            _ => JsonTokenKind.Unknown
        };

        if (punctuation !=
            JsonTokenKind.Unknown)
        {
            return new JsonToken(
                punctuation,
                start,
                1);
        }

        if (current == '-' ||
            char.IsDigit(current))
        {
            while (position < source.Length &&
                   IsNumberCharacter(
                       source[position]))
            {
                position++;
            }

            return new JsonToken(
                JsonTokenKind.Number,
                start,
                position - start);
        }

        if (IsIdentifierStart(current))
        {
            while (position < source.Length &&
                   IsIdentifierPart(
                       source[position]))
            {
                position++;
            }

            var length = position - start;
            var kind = source.AsSpan(
                    start,
                    length) switch
            {
                "true" =>
                    JsonTokenKind.True,
                "false" =>
                    JsonTokenKind.False,
                "null" =>
                    JsonTokenKind.Null,
                _ =>
                    JsonTokenKind.Identifier
            };

            return new JsonToken(
                kind,
                start,
                length);
        }

        return new JsonToken(
            JsonTokenKind.Unknown,
            start,
            1);
    }

    private static bool IsNumberCharacter(
        char value) =>
        char.IsDigit(value) ||
        value is
            '-' or
            '+' or
            '.' or
            'e' or
            'E';

    private static int SkipLineComment(
        string source,
        int start)
    {
        var position = start;

        while (position < source.Length &&
               source[position] is not ('\r' or '\n'))
        {
            position++;
        }

        return position;
    }

    private static int SkipBlockComment(
        string source,
        int start)
    {
        var position = start + 2;

        while (position + 1 < source.Length &&
               !(source[position] == '*' &&
                 source[position + 1] == '/'))
        {
            position++;
        }

        return Math.Min(
            position + 2,
            source.Length);
    }

    private static bool IsIdentifierStart(
        char value) =>
        char.IsLetter(value) ||
        value is '_' or '$';

    private static bool IsIdentifierPart(
        char value) =>
        char.IsLetterOrDigit(value) ||
        value is '_' or '$' or '-';
}
