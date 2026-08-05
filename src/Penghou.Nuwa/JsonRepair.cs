namespace Penghou.Nuwa;

/// <summary>
/// One-shot convenience entry point that builds a default pipeline per call.
/// Callers that repair repeatedly should construct a pipeline once (via
/// <see cref="JsonRepairPipeline.Create"/> or DI) and reuse it.
/// </summary>
public static class JsonRepair
{
    public static async ValueTask<JsonRepairResult> RepairAsync(
        string input,
        JsonSchemaExpectation? expectation = null,
        Action<JsonRepairOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var pipeline = JsonRepairPipeline.Create(
            configure);

        return await pipeline.RepairAsync(
            input,
            expectation,
            cancellationToken);
    }
}
