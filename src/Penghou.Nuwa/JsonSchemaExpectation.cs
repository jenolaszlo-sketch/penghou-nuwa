using System.Text.Json.Nodes;

using System.Text.Json;

namespace Penghou.Nuwa;

/// <summary>
/// Minimal shape description a node-repair strategy needs — not a full JSON Schema
/// implementation, just enough to know "this property should be an array/object".
/// Derived once from a tool's generated JSON Schema and reused across repair attempts.
/// </summary>
public sealed record JsonSchemaExpectation(
    IReadOnlyDictionary<string, JsonSchemaFieldKind> PropertyKinds,
    JsonNode? Schema = null)
{
    internal bool AllowsNull => Schema is not JsonObject schemaObject ||
        schemaObject["type"] is null ||
        schemaObject["type"] is JsonValue value &&
        value.TryGetValue<string>(out var type) && type == "null" ||
        schemaObject["type"] is JsonArray types &&
        types.Any(item =>
            item is JsonValue itemValue &&
            itemValue.TryGetValue<string>(out var itemType) &&
            itemType == "null");

    public JsonSchemaFieldKind? ExpectedKind =>
        Schema is JsonObject schemaObject &&
        TryGetFieldKind(
            schemaObject,
            out var kind)
            ? kind
            : null;

    /// <summary>
    /// Builds an expectation from a JSON Schema node (as produced by
    /// JsonSchemaGenerator). Only reads the top-level "properties" map — nested
    /// object/array shapes aren't tracked, since the double-serialized-field
    /// failure mode has only been observed on top-level tool arguments so far.
    /// </summary>
    public static JsonSchemaExpectation FromSchemaNode(JsonNode schemaNode)
    {
        ArgumentNullException.ThrowIfNull(schemaNode);

        if (schemaNode is not JsonObject schemaObject)
            return new JsonSchemaExpectation(
                new Dictionary<string, JsonSchemaFieldKind>(),
                schemaNode.DeepClone());

        if (schemaObject["properties"] is not JsonObject properties)
            return new JsonSchemaExpectation(
                new Dictionary<string, JsonSchemaFieldKind>(),
                schemaNode.DeepClone());

        var propertyKinds = new Dictionary<string, JsonSchemaFieldKind>(StringComparer.Ordinal);

        foreach (var (propertyName, propertySchema) in properties)
        {
            if (propertySchema is not JsonObject propertyObject)
                continue;

            if (TryGetFieldKind(propertyObject, out var kind))
                propertyKinds[propertyName] = kind;
        }

        return new JsonSchemaExpectation(
            propertyKinds,
            schemaNode.DeepClone());
    }

    public static JsonSchemaExpectation? FromSchemaJson(
        string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            return null;

        try
        {
            var schemaNode = JsonNode.Parse(schemaJson);

            return schemaNode is null
                ? null
                : FromSchemaNode(schemaNode);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public JsonSchemaExpectation? GetProperty(
        string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            propertyName);

        if (Schema is not JsonObject schemaObject ||
            schemaObject["properties"] is not
                JsonObject properties ||
            !properties.TryGetPropertyValue(
                propertyName,
                out var propertySchema) ||
            propertySchema is null)
        {
            return null;
        }

        return FromSchemaNode(propertySchema);
    }

    internal bool DefinesProperty(
        string propertyName) =>
        Schema is JsonObject schemaObject &&
        schemaObject["properties"] is
            JsonObject properties &&
            properties.ContainsKey(propertyName);

    internal bool HasDeclaredProperties =>
        Schema is JsonObject schemaObject &&
        schemaObject["properties"] is
            JsonObject properties &&
        properties.Count > 0;

    internal bool RequiresProperty(
        string propertyName) =>
        Schema is JsonObject schemaObject &&
        schemaObject["required"] is
            JsonArray required &&
        required.Any(item =>
            item is JsonValue value &&
            value.TryGetValue<string>(
                out var name) &&
            string.Equals(
                name,
                propertyName,
                StringComparison.Ordinal));

    public JsonSchemaExpectation? GetItem()
    {
        if (Schema is not JsonObject schemaObject ||
            schemaObject["items"] is not { } itemSchema)
        {
            return null;
        }

        return FromSchemaNode(itemSchema);
    }

    public IReadOnlySet<string> GetStringPropertyNames()
    {
        var names = new HashSet<string>(
            StringComparer.Ordinal);

        CollectStringPropertyNames(
            Schema,
            names);

        return names;
    }

    public IReadOnlyList<string> Validate(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (Schema is null)
            return [];

        var errors = new List<string>();
        ValidateNode(node, Schema, "$", errors);
        return errors;
    }

    private static void ValidateNode(
        JsonNode? value,
        JsonNode? schema,
        string path,
        ICollection<string> errors)
    {
        if (schema is not JsonObject schemaObject)
            return;

        if (!MatchesType(value, schemaObject["type"]))
        {
            errors.Add(
                $"{path} expected {DescribeType(schemaObject["type"])} but found {DescribeValue(value)}.");
            return;
        }

        if (value is JsonObject valueObject)
        {
            ValidateRequiredProperties(valueObject, schemaObject, path, errors);

            if (schemaObject["properties"] is JsonObject properties)
            {
                foreach (var (propertyName, propertySchema) in properties)
                {
                    if (valueObject.TryGetPropertyValue(propertyName, out var propertyValue))
                    {
                        ValidateNode(
                            propertyValue,
                            propertySchema,
                            $"{path}.{propertyName}",
                            errors);
                    }
                }
            }
        }

        if (value is JsonArray valueArray &&
            schemaObject["items"] is { } itemSchema)
        {
            for (var index = 0; index < valueArray.Count; index++)
            {
                ValidateNode(
                    valueArray[index],
                    itemSchema,
                    $"{path}[{index}]",
                    errors);
            }
        }
    }

    private static void ValidateRequiredProperties(
        JsonObject value,
        JsonObject schema,
        string path,
        ICollection<string> errors)
    {
        if (schema["required"] is not JsonArray required)
            return;

        foreach (var requiredNode in required)
        {
            var propertyName = requiredNode?.GetValue<string>();

            if (!string.IsNullOrWhiteSpace(propertyName) &&
                !value.ContainsKey(propertyName))
            {
                errors.Add($"{path}.{propertyName} is required.");
            }
        }
    }

    private static bool MatchesType(JsonNode? value, JsonNode? typeNode)
    {
        if (typeNode is null)
            return true;

        var allowedTypes = typeNode switch
        {
            JsonValue typeValue when typeValue.TryGetValue<string>(out var type) =>
                [type],
            JsonArray typeArray =>
                typeArray
                    .Select(item => item?.GetValue<string>())
                    .Where(item => item is not null)
                    .Cast<string>()
                    .ToArray(),
            _ => []
        };

        return !allowedTypes.Any() ||
            allowedTypes.Any(type => MatchesType(value, type));
    }

    private static bool MatchesType(JsonNode? value, string type)
    {
        if (value is null)
            return type == "null";

        return type switch
        {
            "object" => value is JsonObject,
            "array" => value is JsonArray,
            "string" => IsValueOfType<string>(value),
            "boolean" => IsValueOfType<bool>(value),
            "integer" => IsInteger(value),
            "number" => IsNumber(value),
            "null" => false,
            _ => true
        };
    }

    private static bool IsValueOfType<T>(JsonNode value) =>
        value is JsonValue jsonValue &&
        jsonValue.TryGetValue<T>(out _);

    private static bool IsInteger(JsonNode value) =>
        IsValueOfType<int>(value) ||
        IsValueOfType<long>(value);

    private static bool IsNumber(JsonNode value) =>
        IsInteger(value) ||
        IsValueOfType<float>(value) ||
        IsValueOfType<double>(value) ||
        IsValueOfType<decimal>(value);

    private static string DescribeType(JsonNode? typeNode) =>
        typeNode?.ToJsonString() ?? "any type";

    private static string DescribeValue(JsonNode? value)
    {
        if (value is null)
            return "null";

        return value switch
        {
            JsonObject => "object",
            JsonArray => "array",
            JsonValue jsonValue when jsonValue.TryGetValue<string>(out _) => "string",
            JsonValue jsonValue when jsonValue.TryGetValue<bool>(out _) => "boolean",
            JsonValue => "number",
            _ => "unknown"
        };
    }

    private static bool TryGetFieldKind(JsonObject propertySchema, out JsonSchemaFieldKind kind)
    {
        kind = default;

        // "type" should already be a plain string after JsonSchemaGenerator's
        // normalization, but tolerate a stray array defensively rather than throw.
        var typeValue = propertySchema["type"] switch
        {
            JsonValue value when value.TryGetValue<string>(out var s) => s,
            JsonArray array => array
                .Select(t => t?.GetValue<string>())
                .FirstOrDefault(t => t is not null and not "null"),
            _ => null
        };

        if (typeValue is null)
            return false;

        kind = typeValue switch
        {
            "string" => JsonSchemaFieldKind.String,
            "number" or "integer" => JsonSchemaFieldKind.Number,
            "boolean" => JsonSchemaFieldKind.Boolean,
            "object" => JsonSchemaFieldKind.Object,
            "array" => JsonSchemaFieldKind.Array,
            _ => JsonSchemaFieldKind.String // unknown kinds fall back to a safe default
        };

        return true;
    }

    private static void CollectStringPropertyNames(
        JsonNode? schema,
        ISet<string> names)
    {
        if (schema is not JsonObject schemaObject)
            return;

        if (schemaObject["properties"] is JsonObject properties)
        {
            foreach (var (propertyName, propertySchema)
                     in properties)
            {
                if (propertySchema is not JsonObject propertyObject)
                    continue;

                if (TryGetFieldKind(
                        propertyObject,
                        out var kind) &&
                    kind == JsonSchemaFieldKind.String)
                {
                    names.Add(propertyName);
                }

                CollectStringPropertyNames(
                    propertyObject,
                    names);
            }
        }

        CollectStringPropertyNames(
            schemaObject["items"],
            names);
    }
}

public enum JsonSchemaFieldKind
{
    String,
    Number,
    Boolean,
    Object,
    Array
}
