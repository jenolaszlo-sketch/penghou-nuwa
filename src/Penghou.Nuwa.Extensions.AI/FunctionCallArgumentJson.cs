using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Penghou.Nuwa.Extensions.AI;

/// <summary>
/// Converts tool-call argument dictionaries to and from JSON without
/// reflection-based <see cref="JsonSerializer"/> calls, so the middleware
/// stays compatible with trimming and Native AOT.
/// </summary>
internal static class FunctionCallArgumentJson
{
    /// <summary>
    /// Maximum nesting depth accepted when converting, matching the
    /// <see cref="JsonSerializer"/> default.
    /// </summary>
    private const int MaxDepth = 64;

    /// <summary>
    /// Serializes an argument dictionary to JSON text. Values are limited to
    /// JSON-shaped data: primitives, <see cref="JsonElement"/>,
    /// <see cref="JsonNode"/>, nested dictionaries and enumerables. Anything
    /// else falls back to its string representation instead of failing.
    /// </summary>
    public static string ToJson(IDictionary<string, object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return ToObject(arguments, 0).ToJsonString();
    }

    /// <summary>
    /// Parses repaired JSON text back into an argument dictionary, mirroring
    /// the previous reflection-based behavior where every value is boxed as a
    /// <see cref="JsonElement"/>. Returns null when the text is not a JSON
    /// object, in which case the caller keeps the original arguments.
    /// </summary>
    public static Dictionary<string, object?>? FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return null;

        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in document.RootElement.EnumerateObject())
            arguments[property.Name] = property.Value.Clone();

        return arguments;
    }

    private static JsonObject ToObject(
        IDictionary<string, object?> dictionary,
        int depth)
    {
        var node = new JsonObject();

        foreach (var (key, value) in dictionary)
            node[key] = ToNode(value, depth + 1);

        return node;
    }

    private static JsonArray ToArray(IEnumerable items, int depth)
    {
        var node = new JsonArray();

        foreach (var item in items)
            node.Add(ToNode(item, depth + 1));

        return node;
    }

    private static JsonNode? ToNode(object? value, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new JsonException(
                $"Function call arguments exceed the maximum supported nesting depth of {MaxDepth}.");
        }

        return value switch
        {
            null => null,
            JsonNode node => node.DeepClone(),
            JsonElement element => element.ValueKind == JsonValueKind.Undefined
                ? null
                : JsonNode.Parse(element.GetRawText()),
            string text => JsonValue.Create(text),
            bool flag => JsonValue.Create(flag),
            byte number => JsonValue.Create(number),
            sbyte number => JsonValue.Create(number),
            short number => JsonValue.Create(number),
            ushort number => JsonValue.Create(number),
            int number => JsonValue.Create(number),
            uint number => JsonValue.Create(number),
            long number => JsonValue.Create(number),
            ulong number => JsonValue.Create(number),
            float number => JsonValue.Create(number),
            double number => JsonValue.Create(number),
            decimal number => JsonValue.Create(number),
            char letter => JsonValue.Create(letter.ToString()),
            DateTime moment => JsonValue.Create(moment),
            DateTimeOffset moment => JsonValue.Create(moment),
            Guid identifier => JsonValue.Create(identifier),
            byte[] bytes => JsonValue.Create(Convert.ToBase64String(bytes)),
            IDictionary<string, object?> dictionary => ToObject(dictionary, depth),
            IEnumerable items => ToArray(items, depth),
            _ => JsonValue.Create(value.ToString()),
        };
    }
}
