namespace Penghou.Nuwa.Strategies;

using System.Text;

/// <summary>
/// Extracts a JSON payload wrapped in an XML/HTML-style element, e.g.
/// <c>&lt;answer&gt;{"a":1}&lt;/answer&gt;</c> or
/// <c>&lt;result&gt;&lt;?[CDATA[{"a":1}]]&gt;&lt;/result&gt;</c>.
///
/// The strategy only fires when the wrapper actually encloses the payload:
/// content before the opening tag or after the closing tag must be empty or
/// fence-like, otherwise the input is left for later strategies.
/// </summary>
public sealed class XmlWrappedExtractionStrategy
    : ITextRepair
{
    public string Name => "xml-wrapped-extraction";

    public ValueTask<TextRepairAttempt> RepairAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(input) ||
            !input.Contains('<'))
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

        var openingStart = input.IndexOf('<');
        if (openingStart < 0)
        {
            return false;
        }

        var nameEnd = input.IndexOf('>', openingStart + 1);
        if (nameEnd < 0)
        {
            return false;
        }

        var tagName = ReadTagName(
            input,
            openingStart + 1,
            nameEnd);
        if (tagName.Length == 0 ||
            input[openingStart + 1] == '/')
        {
            return false;
        }

        var bodyStart = nameEnd + 1;

        var closingTag = "</" + tagName;
        var closingStart = input.IndexOf(
            closingTag,
            bodyStart,
            StringComparison.OrdinalIgnoreCase);
        if (closingStart < 0)
        {
            return false;
        }

        payload = input[bodyStart..closingStart].Trim();

        // A CDATA section inside the element wraps the payload itself.
        if (payload.StartsWith("<![CDATA[", StringComparison.Ordinal) &&
            payload.EndsWith("]]>", StringComparison.Ordinal))
        {
            payload = payload[9..^3].Trim();
        }

        return IsPayloadBoundary(input, SkipClosingTag(input, closingStart)) &&
               !string.IsNullOrEmpty(payload);
    }

    private static int SkipClosingTag(
        string input,
        int closingStart)
    {
        var end = input.IndexOf('>', closingStart);
        return end < 0 ? input.Length : end + 1;
    }

    private static string ReadTagName(
        string input,
        int start,
        int end)
    {
        var builder = new StringBuilder();
        for (var index = start; index < end; index++)
        {
            var current = input[index];
            if (current is ' ' or '\t' or '\r' or '\n' or '/')
            {
                break;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The extraction only applies when the element encloses essentially the
    /// whole message: leading content before the tag and trailing content
    /// after the close must be whitespace or a Markdown fence remnant.
    /// </summary>
    private static bool IsPayloadBoundary(
        string input,
        int end)
    {
        for (var index = end; index < input.Length; index++)
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
