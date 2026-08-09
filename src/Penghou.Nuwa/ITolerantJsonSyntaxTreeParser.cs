namespace Penghou.Nuwa;

internal interface ITolerantJsonSyntaxTreeParser
{
    TolerantJsonSyntaxTreeParseResult Parse(
        string input,
        JsonSchemaExpectation? expectation,
        JsonRepairLimits limits,
        CancellationToken cancellationToken);
}
