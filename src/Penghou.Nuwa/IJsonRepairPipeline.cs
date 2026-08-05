namespace Penghou.Nuwa;

public interface IJsonRepairPipeline
{
    ValueTask<JsonRepairResult> RepairAsync(
        string input,
        JsonSchemaExpectation? expectation = null,
        CancellationToken cancellationToken = default);
}
