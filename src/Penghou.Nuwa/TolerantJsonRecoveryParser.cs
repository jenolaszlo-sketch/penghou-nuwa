using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa;

/// <summary>
/// Handwritten recovery parser for damaged JSON. It builds a JsonNode directly
/// while using container state, bounded token lookahead, and the schema at the
/// current path to recover punctuation without inventing semantic values.
/// </summary>
internal sealed class TolerantJsonRecoveryParser
{
    /// <summary>Hard ceiling on nesting depth regardless of configuration, because each open container consumes CLR stack.</summary>
    internal const int HardMaxDepth = 512;

    private readonly TolerantJsonTokenReader _reader;
    private readonly Stack<JsonTokenKind> _closers =
        new();
    private readonly Stack<JsonSchemaExpectation?>
        _containerExpectations = new();
    private readonly List<string> _repairs =
        [];
    private int _schemaStringRepairCount;
    private readonly JsonSchemaExpectation? expectation;
    private readonly JsonRepairLimits limits;
    private readonly CancellationToken cancellationToken;
    private readonly int maxDepth;
    private readonly CorrectionBudget correctionBudget;

    internal TolerantJsonRecoveryParser(
        string input,
        JsonSchemaExpectation? expectation,
        JsonRepairLimits limits,
        CancellationToken cancellationToken)
        : this(
            input,
            expectation,
            limits,
            Math.Min(limits.MaxDepth, HardMaxDepth),
            new CorrectionBudget(Math.Min(limits.MaxCorrections, HardMaxCorrections)),
            cancellationToken)
    {
    }

    private TolerantJsonRecoveryParser(
        string input,
        JsonSchemaExpectation? expectation,
        JsonRepairLimits limits,
        int depthAllowance,
        CorrectionBudget correctionBudget,
        CancellationToken cancellationToken)
    {
        _reader = new TolerantJsonTokenReader(input);
        this.expectation = expectation;
        this.limits = limits;
        this.cancellationToken = cancellationToken;
        maxDepth = Math.Min(depthAllowance, HardMaxDepth);
        this.correctionBudget = correctionBudget;
    }

    private const int HardMaxCorrections = 100_000;

    private sealed class CorrectionBudget(int remaining)
    {
        public bool TryConsume()
        {
            if (remaining <= 0)
                return false;
            remaining--;
            return true;
        }
    }

    public TolerantJsonRecoveryResult Parse()
    {
        CheckWork();
        var root = ParseValue(expectation);

        if (!root.Succeeded)
            return Failed();

        while (true)
        {
            CheckWork();
            var trailing = _reader.Peek();

            if (trailing.Kind ==
                JsonTokenKind.End)
            {
                break;
            }

            if (trailing.Kind is
                JsonTokenKind.Comma or
                JsonTokenKind.ObjectEnd or
                JsonTokenKind.ArrayEnd)
            {
                _reader.Read();
                Record(
                    trailing.Start,
                    $"ignored unmatched {Describe(trailing.Kind)}");
                continue;
            }

            return Failed();
        }

        return new TolerantJsonRecoveryResult(
            root.Node,
            _repairs.Count,
            _schemaStringRepairCount,
            _repairs);
    }

    private NodeResult ParseValue(
        JsonSchemaExpectation? currentExpectation)
    {
        CheckWork();
        var token = _reader.Peek();
        var expectsString =
            currentExpectation?.ExpectedKind ==
            JsonSchemaFieldKind.String;

        if (expectsString &&
            ShouldRecoverRawString(token))
        {
            return ParseString(
                hasOpeningQuote:
                    token.Kind ==
                    JsonTokenKind.Quote,
                schemaGuided: true);
        }

        if ((currentExpectation?.ExpectedKind ==
                 JsonSchemaFieldKind.Array ||
             currentExpectation?.ExpectedKind ==
                 JsonSchemaFieldKind.Object) &&
            token.Kind is
                JsonTokenKind.Quote or
                JsonTokenKind.SingleQuote)
        {
            var stringResult =
                token.Kind == JsonTokenKind.Quote
                    ? ParseString(
                        hasOpeningQuote: true,
                        schemaGuided: false)
                    : ParseSingleQuotedString();

            if (stringResult.Succeeded &&
                TryGetStringNode(
                    stringResult.Node,
                    out var innerText))
            {
                var expanded =
                    ExpandContainerFromString(
                        innerText,
                        currentExpectation);

                if (expanded is not null)
                {
                    Record(
                        token.Start,
                        "expanded double-encoded JSON string into a schema-matching value");
                    return NodeResult.Success(expanded);
                }
            }

            return stringResult;
        }

        return token.Kind switch
        {
            JsonTokenKind.ObjectStart =>
                ParseObject(currentExpectation),
            JsonTokenKind.ArrayStart =>
                ParseArray(currentExpectation),
            JsonTokenKind.Quote =>
                ParseString(
                    hasOpeningQuote: true,
                    schemaGuided: false),
            JsonTokenKind.SingleQuote =>
                ParseSingleQuotedString(),
            JsonTokenKind.Number =>
                ParseNumber(),
            JsonTokenKind.True =>
                ParseLiteral(
                    token,
                    JsonValue.Create(true)),
            JsonTokenKind.False =>
                ParseLiteral(
                    token,
                    JsonValue.Create(false)),
            JsonTokenKind.Null =>
                ParseLiteral(
                    token,
                    node: null),
            _ => NodeResult.Failed
        };
    }

    private NodeResult ParseObject(
        JsonSchemaExpectation? currentExpectation)
    {
        EnsureCanEnterContainer();
        _reader.Read();
        _closers.Push(
            JsonTokenKind.ObjectEnd);
        _containerExpectations.Push(
            currentExpectation);
        var result = new JsonObject();

        while (true)
        {
            CheckWork();
            var token = _reader.Peek();

            if (token.Kind ==
                JsonTokenKind.End)
            {
                InsertCurrentCloser(
                    token.Start);
                return NodeResult.Success(
                    result);
            }

            if (token.Kind ==
                JsonTokenKind.ObjectEnd)
            {
                if (ShouldIgnorePrematureCloser(
                        token,
                        result,
                        currentExpectation))
                {
                    _reader.Read();
                    Record(
                        token.Start,
                        "ignored premature '}' after bounded lookahead found another property for the current object");
                    continue;
                }

                _reader.Read();
                _closers.Pop();
                _containerExpectations.Pop();
                return NodeResult.Success(
                    result);
            }

            if (token.Kind ==
                JsonTokenKind.ArrayEnd)
            {
                if (HasAncestorCloser(
                        token.Kind))
                {
                    InsertCurrentCloser(
                        token.Start);
                    return NodeResult.Success(
                        result);
                }

                _reader.Read();
                Record(
                    token.Start,
                    "ignored unmatched ']'");
                continue;
            }

            if (token.Kind ==
                JsonTokenKind.Comma)
            {
                _reader.Read();
                Record(
                    token.Start,
                    "ignored unexpected comma");
                continue;
            }

            if (TryFindAncestorPropertyOwner(
                    token,
                    currentExpectation,
                    out var ancestorPropertyName))
            {
                InsertCurrentCloserBeforeAncestorProperty(
                    token.Start,
                    ancestorPropertyName);
                return NodeResult.Success(
                    result);
            }

            if (!TryReadPropertyName(
                    out var propertyName))
            {
                return NodeResult.Failed;
            }

            token = _reader.Peek();

            if (token.Kind ==
                JsonTokenKind.Colon)
            {
                _reader.Read();
            }
            else
            {
                Record(
                    token.Start,
                    $"inserted ':' after property '{propertyName}'");
            }

            var propertyExpectation =
                currentExpectation?.GetProperty(
                    propertyName);
            var propertyValue =
                ParseValue(
                    propertyExpectation);

            if (!propertyValue.Succeeded ||
                result.ContainsKey(
                    propertyName))
            {
                return NodeResult.Failed;
            }

            result.Add(
                propertyName,
                propertyValue.Node);

            token = _reader.Peek();

            if (token.Kind ==
                JsonTokenKind.Comma)
            {
                _reader.Read();
                continue;
            }

            if (token.Kind is
                JsonTokenKind.ObjectEnd or
                JsonTokenKind.ArrayEnd or
                JsonTokenKind.End)
            {
                continue;
            }

            if (LooksLikePropertyAt(
                    token.Start))
            {
                Record(
                    token.Start,
                    "inserted ',' between object properties");
                continue;
            }

            return NodeResult.Failed;
        }
    }

    private NodeResult ParseArray(
        JsonSchemaExpectation? currentExpectation)
    {
        EnsureCanEnterContainer();
        _reader.Read();
        _closers.Push(
            JsonTokenKind.ArrayEnd);
        _containerExpectations.Push(
            currentExpectation);
        var result = new JsonArray();
        var itemExpectation =
            currentExpectation?.GetItem();

        while (true)
        {
            CheckWork();
            var token = _reader.Peek();

            if (TryFindAncestorPropertyOwner(
                    token,
                    currentExpectation,
                    out var ancestorPropertyName))
            {
                InsertCurrentCloserBeforeAncestorProperty(
                    token.Start,
                    ancestorPropertyName);
                return NodeResult.Success(
                    result);
            }

            if (token.Kind ==
                JsonTokenKind.End)
            {
                InsertCurrentCloser(
                    token.Start);
                return NodeResult.Success(
                    result);
            }

            if (token.Kind ==
                JsonTokenKind.ArrayEnd)
            {
                if (ShouldIgnorePrematureCloser(
                        token,
                        result,
                        currentExpectation))
                {
                    _reader.Read();
                    Record(
                        token.Start,
                        "ignored premature ']' after bounded lookahead found another value for the current array");
                    continue;
                }

                _reader.Read();
                _closers.Pop();
                _containerExpectations.Pop();
                return NodeResult.Success(
                    result);
            }

            if (token.Kind ==
                JsonTokenKind.ObjectEnd)
            {
                if (HasAncestorCloser(
                        token.Kind))
                {
                    InsertCurrentCloser(
                        token.Start);
                    return NodeResult.Success(
                        result);
                }

                _reader.Read();
                Record(
                    token.Start,
                    "ignored unmatched '}'");
                continue;
            }

            if (token.Kind ==
                JsonTokenKind.Comma)
            {
                _reader.Read();
                Record(
                    token.Start,
                    "ignored unexpected comma");
                continue;
            }

            var item = ParseValue(
                itemExpectation);

            if (!item.Succeeded)
                return NodeResult.Failed;

            result.Add(item.Node);
            token = _reader.Peek();

            if (TryFindAncestorPropertyOwner(
                    token,
                    currentExpectation,
                    out ancestorPropertyName))
            {
                InsertCurrentCloserBeforeAncestorProperty(
                    token.Start,
                    ancestorPropertyName);
                return NodeResult.Success(
                    result);
            }

            if (token.Kind ==
                JsonTokenKind.Comma)
            {
                _reader.Read();
                continue;
            }

            if (token.Kind is
                JsonTokenKind.ArrayEnd or
                JsonTokenKind.ObjectEnd or
                JsonTokenKind.End)
            {
                continue;
            }

            if (LooksLikeValueStart(
                    token,
                    itemExpectation))
            {
                Record(
                    token.Start,
                    "inserted ',' between array values");
                continue;
            }

            return NodeResult.Failed;
        }
    }

    private NodeResult ParseString(
        bool hasOpeningQuote,
        bool schemaGuided)
    {
        _reader.AdvanceTo(
            _reader.Peek().Start);
        var start = _reader.Position;

        if (hasOpeningQuote)
        {
            _reader.AdvanceCharacter();
        }
        else
        {
            Record(
                start,
                "inserted opening string quote");
        }

        var value = new StringBuilder();
        var recoveredString = !hasOpeningQuote;
        var decodeJsonEscapes =
            hasOpeningQuote ||
            schemaGuided &&
            RawStringHasJsonEncodingEvidence();

        while (!_reader.IsAtEnd)
        {
            var current = _reader.Current;

            if (current == '\\')
            {
                if (decodeJsonEscapes)
                {
                    AppendEscapedCharacter(
                        value);
                }
                else
                {
                    // A schema-guided raw string is source text, not a JSON
                    // string yet. Preserve paths, regexes, and language
                    // escapes exactly as the model emitted them.
                    value.Append('\\');
                    _reader.AdvanceCharacter();
                }

                continue;
            }

            if (current == '"')
            {
                if (IsClosingQuote(
                        _reader.Position))
                {
                    _reader.AdvanceCharacter();

                    if (schemaGuided &&
                        (recoveredString ||
                         !hasOpeningQuote))
                    {
                        _schemaStringRepairCount++;
                    }

                    return NodeResult.Success(
                        JsonValue.Create(
                            value.ToString()));
                }

                value.Append('"');
                Record(
                    _reader.Position,
                    "treated quote as embedded string content");
                recoveredString = true;
                _reader.AdvanceCharacter();
                continue;
            }

            value.Append(current);
            _reader.AdvanceCharacter();
        }

        Record(
            _reader.Position,
            "inserted closing string quote at end of input");

        if (schemaGuided)
            _schemaStringRepairCount++;

        return NodeResult.Success(
            JsonValue.Create(
            value.ToString()));
    }

    private bool RawStringHasJsonEncodingEvidence()
    {
        var source = _reader.Source;

        for (var position = _reader.Position;
             position < source.Length;
             position++)
        {
            if (source[position] == '"' &&
                IsClosingQuote(position))
            {
                return false;
            }

            if (source[position] != '\\' ||
                position + 1 >= source.Length)
            {
                continue;
            }

            // An escaped quote or backslash cannot be produced by plain
            // multi-line source alone. It is strong evidence that the model
            // omitted only the opening JSON quote while keeping the content
            // JSON-encoded. Control escapes in that same value must therefore
            // be decoded as well.
            if (source[position + 1] is
                '"' or '\\' or '/' or
                'b' or 'f' or 'r' or 'u')
            {
                return true;
            }

            position++;
        }

        return false;
    }

    private NodeResult ParseSingleQuotedString()
    {
        var opening = _reader.Read();
        var value = new StringBuilder();

        while (!_reader.IsAtEnd)
        {
            var current = _reader.Current;

            if (current == '\'')
            {
                _reader.AdvanceCharacter();
                Record(
                    opening.Start,
                    "converted single-quoted string");
                return NodeResult.Success(
                    JsonValue.Create(
                        value.ToString()));
            }

            if (current == '\\')
            {
                AppendEscapedCharacter(value, allowSingleQuote: true);
                continue;
            }

            value.Append(current);
            _reader.AdvanceCharacter();
        }

        return NodeResult.Failed;
    }

    private NodeResult ParseNumber()
    {
        var token = _reader.Read();
        var value = _reader.Source[
            token.Start..token.End];

        if (long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var integer))
        {
            return NodeResult.Success(
                JsonValue.Create(integer));
        }

        if (double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number))
        {
            return NodeResult.Success(
                JsonValue.Create(number));
        }

        return NodeResult.Failed;
    }

    private NodeResult ParseLiteral(
        JsonToken token,
        JsonNode? node)
    {
        _reader.AdvanceTo(
            token.End);
        return NodeResult.Success(node);
    }

    private bool TryReadPropertyName(
        out string propertyName)
    {
        propertyName = string.Empty;
        var token = _reader.Peek();

        if (token.Kind ==
            JsonTokenKind.Identifier)
        {
            _reader.Read();
            propertyName =
                _reader.Source[
                    token.Start..token.End];
            Record(
                token.Start,
                "accepted unquoted property name");
            return true;
        }

        if (token.Kind !=
            JsonTokenKind.Quote)
        {
            return false;
        }

        _reader.Read();
        var value = new StringBuilder();

        while (!_reader.IsAtEnd)
        {
            var current = _reader.Current;

            if (current == '\\')
            {
                AppendEscapedCharacter(
                    value);
                continue;
            }

            _reader.AdvanceCharacter();

            if (current == '"')
            {
                propertyName =
                    value.ToString();
                return true;
            }

            value.Append(current);
        }

        return false;
    }

    private void AppendEscapedCharacter(
        StringBuilder target,
        bool allowSingleQuote = false)
    {
        _reader.AdvanceCharacter();

        if (_reader.IsAtEnd)
        {
            target.Append('\\');
            return;
        }

        var escaped = _reader.Current;
        _reader.AdvanceCharacter();

        switch (escaped)
        {
            case '\'' when allowSingleQuote:
            case '"':
            case '\\':
            case '/':
                target.Append(escaped);
                return;
            case 'b':
                target.Append('\b');
                return;
            case 'f':
                target.Append('\f');
                return;
            case 'n':
                target.Append('\n');
                return;
            case 'r':
                target.Append('\r');
                return;
            case 't':
                target.Append('\t');
                return;
            case 'u':
                AppendUnicodeEscapeOrOriginal(
                    target);
                return;
            default:
                // Invalid JSON escapes are common inside generated source.
                // Retain the source spelling instead of silently deleting
                // information.
                target.Append('\\');
                target.Append(escaped);
                return;
        }
    }

    private void AppendUnicodeEscapeOrOriginal(
        StringBuilder target)
    {
        const int hexDigitCount = 4;
        var start = _reader.Position;

        if (start + hexDigitCount <=
            _reader.Source.Length &&
            ushort.TryParse(
                _reader.Source.AsSpan(
                    start,
                    hexDigitCount),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var codeUnit))
        {
            target.Append((char)codeUnit);
            _reader.AdvanceTo(
                start + hexDigitCount);
            return;
        }

        target.Append("\\u");
    }

    private JsonNode? ExpandContainerFromString(
        string innerText,
        JsonSchemaExpectation expectation)
    {
        var childExpectation =
            expectation.ExpectedKind ==
            JsonSchemaFieldKind.Array
                ? expectation.GetItem()
                : expectation;

        // Nested parses share the parent's remaining correction budget and a
        // depth allowance bounded by the enclosing depth, so double-encoded
        // levels cannot reset the resource limits.
        var recovery = new TolerantJsonRecoveryParser(
                innerText,
                childExpectation,
                limits,
                depthAllowance:
                    maxDepth - _closers.Count,
                correctionBudget,
                cancellationToken)
            .Parse();

        if (recovery.Root is null ||
            !MatchesContainerKind(
                recovery.Root,
                expectation.ExpectedKind))
        {
            return null;
        }

        _repairs.AddRange(recovery.Repairs);
        _schemaStringRepairCount++;
        return recovery.Root;
    }

    private static bool TryGetStringNode(
        JsonNode? node,
        out string value)
    {
        value = string.Empty;

        if (node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(
                out var parsed) ||
            parsed is null)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool MatchesContainerKind(
        JsonNode node,
        JsonSchemaFieldKind? kind) =>
        kind switch
        {
            JsonSchemaFieldKind.Array =>
                node is JsonArray,
            JsonSchemaFieldKind.Object =>
                node is JsonObject,
            _ => false
        };

    private bool ShouldRecoverRawString(
        JsonToken token)
    {
        if (token.Kind ==
            JsonTokenKind.Quote)
        {
            return true;
        }

        if (token.Kind is
            JsonTokenKind.Identifier or
            JsonTokenKind.Unknown)
        {
            return true;
        }

        if (token.Kind is
            JsonTokenKind.Number or
            JsonTokenKind.True or
            JsonTokenKind.False or
            JsonTokenKind.Null)
        {
            var following =
                _reader.PeekFrom(
                    token.End);

            return following.Kind is not (
                JsonTokenKind.Comma or
                JsonTokenKind.ObjectEnd or
                JsonTokenKind.ArrayEnd or
                JsonTokenKind.End);
        }

        return false;
    }

    private bool IsClosingQuote(
        int quotePosition)
    {
        var following =
            _reader.PeekFrom(
                quotePosition + 1);

        if (following.Kind ==
            JsonTokenKind.End)
        {
            return true;
        }

        if (following.Kind ==
            JsonTokenKind.Comma)
        {
            return IsValidContinuationAfterComma(
                following.End,
                _closers.ToArray(),
                stackIndex: 0);
        }

        if (following.Kind is
            JsonTokenKind.ObjectEnd or
            JsonTokenKind.ArrayEnd)
        {
            return IsValidCloserSequence(
                following.Start);
        }

        return false;
    }

    private bool IsValidCloserSequence(
        int start)
    {
        var closers =
            _closers.ToArray();
        var stackIndex = 0;
        var position = start;

        while (true)
        {
            var token =
                _reader.PeekFrom(
                    position);

            if (token.Kind is not (
                JsonTokenKind.ObjectEnd or
                JsonTokenKind.ArrayEnd))
            {
                if (token.Kind ==
                    JsonTokenKind.End)
                {
                    return true;
                }

                if (token.Kind ==
                    JsonTokenKind.Comma)
                {
                    if (stackIndex >= closers.Length)
                    {
                        var continuation =
                            _reader.PeekFrom(
                                token.End);

                        // The value can still end here when a later root
                        // member is separated by an accidental extra closer.
                        // The object parser will reconsider that closer using
                        // its schema-aware checkpoint.
                        return LooksLikePropertyAt(
                            continuation.Start);
                    }

                    return IsValidContinuationAfterComma(
                        token.End,
                        closers,
                        stackIndex);
                }

                return false;
            }

            var match = Array.IndexOf(
                closers,
                token.Kind,
                stackIndex);

            if (match >= 0)
            {
                stackIndex = match + 1;
            }

            position = token.End;
        }
    }

    private bool IsValidContinuationAfterComma(
        int start,
        IReadOnlyList<JsonTokenKind> closers,
        int stackIndex)
    {
        if (stackIndex >=
            closers.Count)
        {
            return false;
        }

        var next =
            _reader.PeekFrom(start);

        return closers[stackIndex] switch
        {
            JsonTokenKind.ObjectEnd =>
                next.Kind ==
                    JsonTokenKind.ObjectEnd ||
                LooksLikePropertyAt(
                    next.Start),
            JsonTokenKind.ArrayEnd =>
                next.Kind ==
                    JsonTokenKind.ArrayEnd ||
                LooksLikeValueStart(
                    next,
                    expectation: null),
            _ => false
        };
    }

    private const int MaxPropertyLookahead = 1024;

    private bool LooksLikePropertyAt(
        int start)
    {
        var token =
            _reader.PeekFrom(start);

        if (token.Kind ==
            JsonTokenKind.Identifier)
        {
            return _reader.PeekFrom(
                    token.End)
                .Kind ==
                JsonTokenKind.Colon;
        }

        if (token.Kind !=
            JsonTokenKind.Quote)
        {
            return false;
        }

        var position = token.End;
        var escaped = false;

        // Bound the raw quote scan so adversarial inputs cannot turn every
        // property/value boundary into an end-of-input walk.
        var scanEnd = Math.Min(
            _reader.Source.Length,
            token.End + MaxPropertyLookahead);

        while (position < scanEnd)
        {
            var current =
                _reader.Source[position++];

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

            if (current != '"')
                continue;

            return _reader.PeekFrom(
                    position)
                .Kind ==
                JsonTokenKind.Colon;
        }

        return false;
    }

    private static bool LooksLikeValueStart(
        JsonToken token,
        JsonSchemaExpectation? expectation) =>
        token.Kind is
            JsonTokenKind.ObjectStart or
            JsonTokenKind.ArrayStart or
            JsonTokenKind.Quote or
            JsonTokenKind.SingleQuote or
            JsonTokenKind.Number or
            JsonTokenKind.True or
            JsonTokenKind.False or
            JsonTokenKind.Null ||
        expectation?.ExpectedKind ==
            JsonSchemaFieldKind.String &&
        token.Kind is
            JsonTokenKind.Identifier or
            JsonTokenKind.Unknown;

    private bool HasAncestorCloser(
        JsonTokenKind closer) =>
        _closers
            .Skip(1)
            .Contains(closer);

    private bool ShouldIgnorePrematureCloser(
        JsonToken closer,
        JsonNode currentValue,
        JsonSchemaExpectation? currentExpectation)
    {
        const int minimumWinningMargin = 2;

        if (!TryReadContinuationAfterCloser(
                closer.End,
                out var continuation))
        {
            return false;
        }

        var currentScore = 0;
        var parentScore = 1;
        var isRoot = _closers.Count == 1;

        if (isRoot)
        {
            // A root closer followed by a comma and another member/value can
            // never be a valid completed JSON document.
            currentScore += 5;
        }

        if (currentValue is JsonObject currentObject &&
            continuation.PropertyName is { } propertyName)
        {
            if (currentObject.ContainsKey(propertyName))
                return false;

            if (currentExpectation?.DefinesProperty(
                    propertyName) == true)
            {
                currentScore += 4;
            }

            if (currentExpectation?.RequiresProperty(
                    propertyName) == true)
            {
                currentScore += 3;
            }

            var parentExpectation =
                GetParentContainerExpectation();

            if (parentExpectation?.DefinesProperty(
                    propertyName) == true)
            {
                parentScore += 4;
            }

            if (parentExpectation?.RequiresProperty(
                    propertyName) == true)
            {
                parentScore += 2;
            }
        }
        else if (currentValue is JsonArray &&
                 continuation.IsValue)
        {
            // Without a schema there is no safe way to move a value from a
            // parent array into a nested array. Root continuation is certain;
            // nested continuation needs the current schema to identify an
            // array while the parent is not an array.
            if (currentExpectation?.ExpectedKind ==
                JsonSchemaFieldKind.Array)
            {
                currentScore += 2;
            }

            if (GetParentContainerExpectation()?.ExpectedKind ==
                JsonSchemaFieldKind.Array)
            {
                parentScore += 3;
            }

            else if (!isRoot &&
                     GetParentCloser() ==
                         JsonTokenKind.ObjectEnd)
            {
                // A bare value cannot follow a comma in an object. The
                // current array is therefore the only structurally valid
                // owner of this continuation.
                currentScore += 4;
            }
        }

        return currentScore >=
            parentScore + minimumWinningMargin;
    }

    private JsonSchemaExpectation?
        GetParentContainerExpectation() =>
        _containerExpectations.Count < 2
            ? null
            : _containerExpectations
                .Skip(1)
                .First();

    private JsonTokenKind? GetParentCloser() =>
        _closers.Count < 2
            ? null
            : _closers
                .Skip(1)
                .First();

    private bool TryFindAncestorPropertyOwner(
        JsonToken token,
        JsonSchemaExpectation? currentExpectation,
        out string propertyName)
    {
        propertyName = string.Empty;

        var hasConstrainedCurrentShape =
            currentExpectation?.ExpectedKind ==
                JsonSchemaFieldKind.Array ||
            currentExpectation?.HasDeclaredProperties == true;

        if (!hasConstrainedCurrentShape ||
            !TryPeekPropertyName(
                token,
                maximumLookaheadTokens: 12,
                out var candidate) ||
            currentExpectation?.DefinesProperty(candidate) == true)
        {
            return false;
        }

        // The expectation stack mirrors the open-container stack. Skip the
        // current container and use the nearest ancestor that owns the
        // property. The token is deliberately left unread so each missing
        // intervening closer can unwind one frame and the owning object can
        // parse the property normally.
        if (!_containerExpectations
                .Skip(1)
                .Any(expectation =>
                    expectation?.DefinesProperty(candidate) == true))
        {
            return false;
        }

        propertyName = candidate;
        return true;
    }

    private bool TryReadContinuationAfterCloser(
        int start,
        out CloserContinuation continuation)
    {
        const int maximumLookaheadTokens = 12;
        continuation = default;
        var comma = _reader.PeekFrom(start);

        if (comma.Kind != JsonTokenKind.Comma)
            return false;

        var next = _reader.PeekFrom(
            comma.End);

        if (_closers.Peek() ==
            JsonTokenKind.ObjectEnd)
        {
            if (!TryPeekPropertyName(
                    next,
                    maximumLookaheadTokens,
                    out var propertyName))
            {
                return false;
            }

            continuation = new(
                propertyName,
                IsValue: false);
            return true;
        }

        if (!LooksLikeValueStart(
                next,
                _containerExpectations.Peek()?.GetItem()))
        {
            return false;
        }

        if (GetParentCloser() ==
                JsonTokenKind.ObjectEnd &&
            LooksLikePropertyAt(next.Start))
        {
            return false;
        }

        continuation = new(
            PropertyName: null,
            IsValue: true);
        return true;
    }

    private bool TryPeekPropertyName(
        JsonToken first,
        int maximumLookaheadTokens,
        out string propertyName)
    {
        propertyName = string.Empty;

        if (maximumLookaheadTokens < 2)
            return false;

        if (first.Kind == JsonTokenKind.Identifier)
        {
            if (_reader.PeekFrom(first.End).Kind !=
                JsonTokenKind.Colon)
            {
                return false;
            }

            propertyName = _reader.Source[
                first.Start..first.End];
            return true;
        }

        if (first.Kind != JsonTokenKind.Quote)
            return false;

        var position = first.End;
        var escaped = false;

        // A property name contributes an opening quote, its text, a closing
        // quote and a colon. Bound the raw scan too, so corrupt input cannot
        // turn lookahead into an unbounded speculative parse.
        const int maximumPropertyCharacters = 1024;
        var limit = Math.Min(
            _reader.Source.Length,
            position + maximumPropertyCharacters);

        while (position < limit)
        {
            var current = _reader.Source[position++];

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

            if (current != '"')
                continue;

            if (_reader.PeekFrom(position).Kind !=
                JsonTokenKind.Colon)
            {
                return false;
            }

            propertyName = _reader.Source[
                first.End..(position - 1)];
            return propertyName.Length > 0;
        }

        return false;
    }

    private void InsertCurrentCloser(
        int position)
    {
        var closer = _closers.Pop();
        _containerExpectations.Pop();
        Record(
            position,
            $"inserted missing {Describe(closer)}");
    }

    private void InsertCurrentCloserBeforeAncestorProperty(
        int position,
        string propertyName)
    {
        var closer = _closers.Pop();
        _containerExpectations.Pop();
        Record(
            position,
            $"inserted missing {Describe(closer)} before ancestor property '{propertyName}'");
    }

    private void Record(
        int position,
        string action)
    {
        CheckWork();
        if (!correctionBudget.TryConsume())
        {
            throw new JsonRepairLimitException(
                $"Tolerant recovery exceeded the maximum of {Math.Min(limits.MaxCorrections, HardMaxCorrections)} corrections.");
        }

        _repairs.Add($"offset {position}: {action}");
    }

    private void EnsureCanEnterContainer()
    {
        CheckWork();
        if (_closers.Count >= maxDepth)
        {
            throw new JsonRepairLimitException(
                $"Tolerant recovery exceeded the maximum nesting depth of {maxDepth}.");
        }
    }

    private void CheckWork() =>
        cancellationToken.ThrowIfCancellationRequested();

    private TolerantJsonRecoveryResult Failed() =>
        new(
            Root: null,
            RepairCount: _repairs.Count,
            SchemaStringRepairCount:
                _schemaStringRepairCount,
            Repairs: _repairs);

    private static string Describe(
        JsonTokenKind kind) =>
        kind switch
        {
            JsonTokenKind.ObjectEnd => "'}'",
            JsonTokenKind.ArrayEnd => "']'",
            JsonTokenKind.Comma => "','",
            _ => kind.ToString()
        };

    private readonly record struct NodeResult(
        bool Succeeded,
        JsonNode? Node)
    {
        public static NodeResult Failed =>
            new(false, null);

        public static NodeResult Success(
            JsonNode? node) =>
            new(true, node);
    }

    private readonly record struct CloserContinuation(
        string? PropertyName,
        bool IsValue);
}

internal sealed record TolerantJsonRecoveryResult(
    JsonNode? Root,
    int RepairCount,
    int SchemaStringRepairCount,
    IReadOnlyList<string> Repairs);
