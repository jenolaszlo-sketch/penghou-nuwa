using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Penghou.Nuwa;

/// <summary>
/// Builds a minimal JSON Schema node from a CLR type via reflection, so a
/// <see cref="JsonSchemaExpectation"/> can be derived straight from the shape a
/// payload should deserialize into. Emits the same narrow schema subset that
/// <see cref="JsonSchemaExpectation.FromSchemaNode"/> understands: type,
/// properties, required, items and additionalProperties.
/// </summary>
internal static class JsonSchemaFromTypeGenerator
{
    private static readonly JsonSerializerOptions DefaultOptions = new();

    public static JsonObject Generate(
        Type type,
        JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(type);

        return Build(
            type,
            options ?? DefaultOptions,
            new HashSet<Type>());
    }

    private static JsonObject Build(
        Type type,
        JsonSerializerOptions options,
        HashSet<Type> visited)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            type = underlying;

        if (IsStringLike(type))
            return Typed("string");

        if (type == typeof(bool))
            return Typed("boolean");

        if (type.IsEnum)
            return Typed("string");

        if (IsIntegral(type))
            return Typed("integer");

        if (IsFloating(type))
            return Typed("number");

        if (TryGetDictionaryValueType(type, out var valueType))
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = Build(valueType, options, visited)
            };
        }

        if (TryGetElementType(type, out var elementType))
        {
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = Build(elementType, options, visited)
            };
        }

        if (type.IsArray)
            return new JsonObject();

        if (type.IsClass && !visited.Add(type))
        {
            // Self-referencing type: stop descending to avoid infinite recursion.
            return Typed("object");
        }

        try
        {
            return BuildObject(type, options, visited);
        }
        finally
        {
            visited.Remove(type);
        }
    }

    private static JsonObject BuildObject(
        Type type,
        JsonSerializerOptions options,
        HashSet<Type> visited)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in type.GetProperties(
                     BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 ||
                property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            {
                continue;
            }

            var name = options.PropertyNamingPolicy?.ConvertName(property.Name)
                ?? property.Name;

            if (property.GetCustomAttribute<JsonPropertyNameAttribute>() is { } jsonName &&
                jsonName.Name is not null)
            {
                name = jsonName.Name;
            }

            if (!propertyNames.Add(name))
                continue;

            properties[name] = Build(
                property.PropertyType,
                options,
                visited);

            if (IsRequired(property))
                required.Add(name);
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
            schema["required"] = required;

        return schema;
    }

    private static bool IsRequired(PropertyInfo property)
    {
        if (property.GetCustomAttribute<JsonRequiredAttribute>() is not null)
            return true;

        if (property.SetMethod?
                .GetCustomAttributes()
                .Any(attribute =>
                    attribute.GetType().Name == "RequiredMemberAttribute") ==
            true)
        {
            return true;
        }

        var type = property.PropertyType;

        if (Nullable.GetUnderlyingType(type) is not null)
            return false;

        return type.IsValueType;
    }

    private static bool IsStringLike(Type type) =>
        type == typeof(string) ||
        type == typeof(char) ||
        type == typeof(Guid) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(TimeSpan) ||
        type == typeof(Uri) ||
        type == typeof(byte[]);

    private static bool IsIntegral(Type type) =>
        type == typeof(sbyte) ||
        type == typeof(byte) ||
        type == typeof(short) ||
        type == typeof(ushort) ||
        type == typeof(int) ||
        type == typeof(uint) ||
        type == typeof(long) ||
        type == typeof(ulong) ||
        type == typeof(nint) ||
        type == typeof(nuint);

    private static bool IsFloating(Type type) =>
        type == typeof(float) ||
        type == typeof(double) ||
        type == typeof(decimal);

    private static bool TryGetDictionaryValueType(
        Type type,
        out Type valueType)
    {
        foreach (var candidate in EnumerateSelfAndInterfaces(type))
        {
            if (candidate.IsGenericType &&
                (candidate.GetGenericTypeDefinition() ==
                     typeof(IDictionary<,>) ||
                 candidate.GetGenericTypeDefinition() ==
                     typeof(IReadOnlyDictionary<,>)))
            {
                valueType = candidate.GetGenericArguments()[1];
                return true;
            }
        }

        valueType = null!;
        return false;
    }

    private static bool TryGetElementType(
        Type type,
        out Type elementType)
    {
        if (type != typeof(string) && type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        if (type != typeof(string) &&
            type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(List<>))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }

        foreach (var candidate in EnumerateSelfAndInterfaces(type))
        {
            if (candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                elementType = candidate.GetGenericArguments()[0];
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    private static IEnumerable<Type> EnumerateSelfAndInterfaces(
        Type type)
    {
        yield return type;

        foreach (var candidate in type.GetInterfaces())
        {
            yield return candidate;
        }
    }

    private static JsonObject Typed(string type) =>
        new() { ["type"] = type };
}
