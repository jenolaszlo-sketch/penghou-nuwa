using System.Diagnostics.CodeAnalysis;

namespace Penghou.Nuwa;

/// <summary>
/// One-shot convenience entry point that builds a default pipeline per call.
/// Callers that repair repeatedly should construct a pipeline once (via
/// <see cref="JsonRepairPipeline.Create"/> or DI) and reuse it.
/// </summary>
public static class JsonRepair
{
    private const string ReflectionRegistrationWarning =
        "Instantiating repair strategies by type uses runtime activation. " +
        "Register strategies by instance or factory, or resolve the pipeline " +
        "from dependency injection, for trimmed or Native AOT applications.";

    /// <remarks>
    /// Builds the default pipeline through runtime strategy activation, so it
    /// is not trim- or Native AOT-safe. For ahead-of-time compiled apps,
    /// construct a <see cref="JsonRepairPipeline"/> from explicitly registered
    /// strategies and reuse it.
    /// </remarks>
    [RequiresUnreferencedCode(ReflectionRegistrationWarning)]
    [RequiresDynamicCode(ReflectionRegistrationWarning)]
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
