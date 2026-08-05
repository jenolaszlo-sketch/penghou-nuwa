namespace Penghou.Nuwa;

public interface ITolerantJsonSyntaxTreeParser
{
    TolerantJsonSyntaxTreeParseResult Parse(
        string input,
        JsonSchemaExpectation? expectation = null);
}
