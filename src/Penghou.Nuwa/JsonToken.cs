namespace Penghou.Nuwa;

internal enum JsonTokenKind
{
    End,
    ObjectStart,
    ObjectEnd,
    ArrayStart,
    ArrayEnd,
    Colon,
    Comma,
    Quote,
    SingleQuote,
    Number,
    True,
    False,
    Null,
    Identifier,
    Unknown
}

internal readonly record struct JsonToken(
    JsonTokenKind Kind,
    int Start,
    int Length)
{
    public int End => Start + Length;
}
