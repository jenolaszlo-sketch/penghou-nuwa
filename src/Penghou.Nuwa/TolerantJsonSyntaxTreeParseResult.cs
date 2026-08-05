using System.Text.Json.Nodes;

namespace Penghou.Nuwa;

public sealed record TolerantJsonSyntaxTreeParseResult(
    JsonNode? Root,
    string Outcome)
{
    public bool Succeeded => Root is not null;
}
