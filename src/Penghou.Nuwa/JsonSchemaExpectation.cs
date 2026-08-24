using System.Text.Json.Nodes;

using System.Text.Json;

namespace Penghou.Nuwa;

/// <summary>
/// Minimal shape description a node-repair strategy needs — not a full JSON Schema
/// implementation, just enough to know "this property should be an array/object".
/// Derived once from a tool's generated JSON Schema and reused across repair attempts.
/// </summary>
/// <remarks>
/// Factory methods normalize and own a snapshot of the supplied schema, and
/// memoize expectations for its children. Mutating the original schema after
/// construction therefore has no effect. The public primary constructor keeps
/// its historical live-schema semantics: when callers supply <see cref="Schema"/>
/// directly, child lookups are uncached and observe later mutations.
/// </remarks>
public sealed record JsonSchemaExpectation(
    IReadOnlyDictionary<string, JsonSchemaFieldKind> PropertyKinds,
    JsonNode? Schema = null,
    bool Nullable = false)
{
    private IReadOnlyDictionary<string, JsonSchemaExpectation>?
        CachedProperties
    { get; init; }

    private JsonSchemaExpectation? CachedItem { get; init; }

    /// <summary>
    /// The resolved branches of a <c>oneOf</c>/<c>anyOf</c> union schema, in
    /// declaration order. Empty when the schema is not a union. Branch-aware
    /// repair resolves the node against a single branch (via discriminator or
    /// shape) so content is only repaired using the schema that actually
    /// matches it.
    /// </summary>
    public IReadOnlyList<JsonSchemaBranch> Branches { get; init; } = [];

    /// <summary>
    /// Whether the schema allows the value to be null, either via an explicit
    /// "null" in the type union, a <c>nullable</c> keyword, or an untyped
    /// schema. Used to decide whether an explicit null on an optional property
    /// may be dropped.
    /// </summary>
    internal bool AllowsNull => Nullable;

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

        var schema = JsonSchemaNormalizer.Normalize(schemaNode);

        var propertyKinds = new Dictionary<string, JsonSchemaFieldKind>(StringComparer.Ordinal);

        if (schema is JsonObject schemaObject &&
            schemaObject["properties"] is JsonObject properties)
        {
            foreach (var (propertyName, propertySchema) in properties)
            {
                if (propertySchema is not JsonObject propertyObject)
                    continue;

                if (TryGetFieldKind(propertyObject, out var kind))
                    propertyKinds[propertyName] = kind;
            }
        }

        var expectation = new JsonSchemaExpectation(
            propertyKinds,
            schema,
            SchemaAllowsNull(schema))
        {
            Branches = BuildBranches(schema),
            CachedProperties = BuildPropertyExpectations(schema),
            CachedItem = BuildItemExpectation(schema)
        };
        return expectation;
    }

    private static IReadOnlyDictionary<string, JsonSchemaExpectation>
        BuildPropertyExpectations(JsonNode schema) =>
        schema is JsonObject schemaObject &&
        schemaObject["properties"] is JsonObject properties
            ? properties
                .Where(property => property.Value is not null)
                .ToDictionary(
                    property => property.Key,
                    property => FromSchemaNode(property.Value!),
                    StringComparer.Ordinal)
            : new Dictionary<string, JsonSchemaExpectation>(
                StringComparer.Ordinal);

    private static JsonSchemaExpectation? BuildItemExpectation(
        JsonNode schema) =>
        schema is JsonObject schemaObject &&
        schemaObject["items"] is { } itemSchema
            ? FromSchemaNode(itemSchema)
            : null;

    private static IReadOnlyList<JsonSchemaBranch> BuildBranches(
        JsonNode schema)
    {
        if (schema is not JsonObject schemaObject)
            return [];

        var union = schemaObject["oneOf"] as JsonArray
            ?? schemaObject["anyOf"] as JsonArray;

        if (union is null || union.Count == 0)
            return [];

        var branches = new List<JsonSchemaBranch>();

        foreach (var branchNode in union)
        {
            if (branchNode is not JsonObject branchObject)
                continue;

            var (discriminatorProperty, discriminatorValues) =
                FindDiscriminator(branchObject);

            branches.Add(
                new JsonSchemaBranch(
                    FromSchemaNode(branchObject),
                    discriminatorProperty,
                    discriminatorValues));
        }

        return branches;
    }

    private static (string? Property, IReadOnlySet<string>? Values)
        FindDiscriminator(JsonObject branch)
    {
        if (branch["properties"] is not JsonObject properties)
            return (null, null);

        foreach (var (propertyName, propertySchema) in properties)
        {
            if (propertySchema is not JsonObject propertyObject)
                continue;

            if (propertyObject["const"] is JsonValue constValue &&
                constValue.TryGetValue<string>(out var constText) &&
                !string.IsNullOrWhiteSpace(constText))
            {
                return (
                    propertyName,
                    new HashSet<string>(
                        [constText],
                        StringComparer.Ordinal));
            }

            if (propertyObject["enum"] is JsonArray enumArray)
            {
                var values = enumArray
                    .OfType<JsonValue>()
                    .Select(value =>
                        value.TryGetValue<string>(out var text)
                            ? text
                            : null)
                    .OfType<string>()
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToHashSet(StringComparer.Ordinal);

                if (values.Count > 0)
                {
                    return (propertyName, values);
                }
            }
        }

        return (null, null);
    }

    private static bool SchemaAllowsNull(JsonNode schema)
    {
        if (schema is not JsonObject schemaObject)
            return true;

        if (schemaObject["nullable"] is JsonValue nullableValue &&
            nullableValue.TryGetValue<bool>(out var isNullable) &&
            isNullable)
        {
            return true;
        }

        return schemaObject["type"] switch
        {
            null => true,
            JsonValue typeValue when typeValue.TryGetValue<string>(out var type) =>
                type == "null",
            JsonArray types =>
                types.Any(item =>
                    item is JsonValue itemValue &&
                    itemValue.TryGetValue<string>(out var itemType) &&
                    itemType == "null"),
            _ => false
        };
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

    /// <summary>
    /// Builds an expectation from a CLR type, so callers can point the repair
    /// pipeline at the shape a payload should deserialize into without hand-
    /// writing a JSON Schema. Property names follow <see cref="JsonSerializerOptions"/>
    /// naming policy unless overridden by a JSON property name attribute;
    /// enums map to strings. Use the options overload to match how the payload is
    /// actually serialized (e.g. a camelCase naming policy).
    /// </summary>
    public static JsonSchemaExpectation FromType<T>() =>
        FromType(typeof(T));

    public static JsonSchemaExpectation FromType<T>(
        JsonSerializerOptions? options) =>
        FromType(typeof(T), options);

    public static JsonSchemaExpectation FromType(Type type) =>
        FromType(type, null);

    public static JsonSchemaExpectation FromType(
        Type type,
        JsonSerializerOptions? options)
    {
        ArgumentNullException.ThrowIfNull(type);

        return FromSchemaNode(
            JsonSchemaFromTypeGenerator.Generate(
                type,
                options));
    }

    public JsonSchemaExpectation? GetProperty(
        string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            propertyName);

        if (CachedProperties is not null)
        {
            return CachedProperties.GetValueOrDefault(propertyName);
        }

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

    internal IReadOnlyList<string> GetRequiredPropertyNames() =>
        Schema is JsonObject schemaObject &&
        schemaObject["required"] is JsonArray required
            ? required
                .OfType<JsonValue>()
                .Select(value => value.TryGetValue<string>(out var name) ? name : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : [];

    public JsonSchemaExpectation? GetItem()
    {
        if (CachedItem is not null)
            return CachedItem;

        if (Schema is not JsonObject schemaObject ||
            schemaObject["items"] is not { } itemSchema)
        {
            return null;
        }

        return FromSchemaNode(itemSchema);
    }

    /// <summary>
    /// Resolves <paramref name="node"/> against this expectation's branches,
    /// returning the single branch expectation that best matches it, or
    /// <c>null</c> when the match is ambiguous. Non-union expectations always
    /// resolve to themselves. Discriminator properties (<c>name</c>,
    /// <c>tool</c>, <c>function.name</c>) are honored first; otherwise the most
    /// specific branch whose declared properties cover the node wins.
    /// </summary>
    internal JsonSchemaExpectation? TryResolveBranch(
        JsonNode node)
    {
        if (Branches.Count == 0)
            return this;

        if (node is not JsonObject obj)
            return null;

        var discriminator = GetDiscriminatorValue(obj);

        if (discriminator is not null)
        {
            var matches = Branches
                .Where(branch =>
                    branch.DiscriminatorValues?.Contains(
                        discriminator) == true)
                .ToList();

            if (matches.Count == 1)
                return matches[0].Expectation;

            return null;
        }

        var candidates = Branches
            .Where(branch =>
                NodeFitsBranch(
                    obj,
                    branch.Expectation))
            .ToList();

        return candidates.Count == 1
            ? candidates[0].Expectation
            : null;
    }

    /// <summary>
    /// Whether <paramref name="node"/> is structurally consistent with this
    /// expectation: every present key must be declared, and nested objects and
    /// arrays must recursively conform. Union expectations accept the node when
    /// any branch accepts it. Scalars and values pending a deeper
    /// double-serialization expansion are treated as conforming.
    /// </summary>
    internal bool Accepts(JsonNode node)
    {
        if (Branches.Count > 0)
        {
            return Branches.Any(branch =>
                branch.Expectation.Accepts(node));
        }

        return node switch
        {
            JsonObject obj => AcceptsObject(obj),
            JsonArray array => AcceptsArray(array),
            _ => true
        };
    }

    private bool AcceptsObject(JsonObject obj)
    {
        if (!HasDeclaredProperties)
            return true;

        foreach (var (key, value) in obj)
        {
            if (!DefinesProperty(key))
                return false;

            if (value is null ||
                value is not (JsonObject or JsonArray))
            {
                continue;
            }

            var propertyExpectation = GetProperty(key);

            if (propertyExpectation is not null &&
                !propertyExpectation.Accepts(value))
            {
                return false;
            }
        }

        return true;
    }

    private bool AcceptsArray(JsonArray array)
    {
        var itemExpectation = GetItem();

        if (itemExpectation is null)
            return true;

        foreach (var entry in array)
        {
            if (entry is null ||
                entry is not (JsonObject or JsonArray))
            {
                continue;
            }

            if (!itemExpectation.Accepts(entry))
                return false;
        }

        return true;
    }

    private static string? GetDiscriminatorValue(
        JsonObject obj)
    {
        if (TryGetStringProp(obj, "name", out var name))
            return name;

        if (TryGetStringProp(obj, "tool", out var tool))
            return tool;

        if (TryGetStringProp(obj, "function", out var function))
            return function;

        if (obj["function"] is JsonObject functionObject &&
            TryGetStringProp(
                functionObject,
                "name",
                out var nestedFunctionName))
        {
            return nestedFunctionName;
        }

        return null;
    }

    private static bool TryGetStringProp(
        JsonObject obj,
        string propertyName,
        out string value)
    {
        value = string.Empty;

        if (obj[propertyName] is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var text) &&
            !string.IsNullOrWhiteSpace(text))
        {
            value = text;
            return true;
        }

        return false;
    }

    private static bool NodeFitsBranch(
        JsonObject obj,
        JsonSchemaExpectation branch)
    {
        if (!branch.HasDeclaredProperties)
            return obj.Count == 0;

        foreach (var (key, _) in obj)
        {
            if (!branch.DefinesProperty(key))
                return false;
        }

        return true;
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

    /// <summary>
    /// Validates the types and required structural shape used by repair. This
    /// intentionally does not implement every JSON Schema keyword.
    /// </summary>
    public IReadOnlyList<string> ValidateShape(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (Branches.Count > 0)
        {
            var branchErrors = Branches
                .Select(branch => branch.Expectation.ValidateShape(node))
                .ToList();
            if (branchErrors.Any(errors => errors.Count == 0))
                return [];

            return
            [
                "$ did not match any oneOf/anyOf branch.",
                .. branchErrors.OrderBy(errors => errors.Count).First()
            ];
        }

        if (Schema is null)
            return [];

        var errors = new List<string>();
        ValidateNode(node, Schema, "$", errors);
        return errors;
    }

    /// <summary>
    /// Validates the structural subset used by repair. Use a dedicated JSON
    /// Schema validator when authoritative dialect validation is required.
    /// </summary>
    public IReadOnlyList<string> Validate(JsonNode node) =>
        ValidateShape(node);

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

        if (schemaObject["enum"] is JsonArray enumMembers &&
            !enumMembers.Any(member => JsonNode.DeepEquals(member, value)))
        {
            errors.Add($"{path} did not match an allowed enum value.");
            return;
        }

        if (schemaObject["const"] is { } constant &&
            !JsonNode.DeepEquals(constant, value))
        {
            errors.Add($"{path} did not match the required const value.");
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


            if (schemaObject["additionalProperties"] is JsonValue additional &&
                additional.TryGetValue<bool>(out var allowed) &&
                !allowed)
            {
                var declared = schemaObject["properties"] as JsonObject;
                foreach (var propertyName in valueObject.Select(property => property.Key))
                {
                    if (declared?.ContainsKey(propertyName) != true)
                        errors.Add($"{path}.{propertyName} is not declared by the schema.");
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

/// <summary>
/// One branch of a <c>oneOf</c>/<c>anyOf</c> union in a
/// <see cref="JsonSchemaExpectation"/>. The discriminator property (a
/// <c>const</c>/<c>enum</c> field) lets branch-aware repair pick the schema
/// that actually describes a node instead of falling back to a permissive
/// merged view.
/// </summary>
public sealed record JsonSchemaBranch(
    JsonSchemaExpectation Expectation,
    string? DiscriminatorProperty = null,
    IReadOnlySet<string>? DiscriminatorValues = null);
