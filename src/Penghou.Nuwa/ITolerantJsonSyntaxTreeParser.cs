namespace Penghou.Nuwa;

internal interface ITolerantJsonSyntaxTreeParser
{
    TolerantJsonSyntaxTreeParseResult Parse(
        string input,
        JsonSchemaExpectation? expectation = null);
}
