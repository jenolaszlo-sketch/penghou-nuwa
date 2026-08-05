namespace Penghou.Nuwa;

public interface IJsonRepairPipeline
{
    JsonRepairResult Repair(
        string input,
        JsonSchemaExpectation? expectation = null);
}
