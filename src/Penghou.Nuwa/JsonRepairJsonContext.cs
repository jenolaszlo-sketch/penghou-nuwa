using System.Text.Json.Serialization;

namespace Penghou.Nuwa;

/// <summary>
/// Source-generated serialization metadata for the few values Nuwa serializes
/// internally. Using a <see cref="JsonSerializerContext"/> keeps those call
/// sites compatible with trimming and Native AOT instead of relying on
/// reflection-based <c>JsonSerializer</c> overloads.
/// </summary>
[JsonSerializable(typeof(string))]
internal sealed partial class JsonRepairJsonContext : JsonSerializerContext
{
}
