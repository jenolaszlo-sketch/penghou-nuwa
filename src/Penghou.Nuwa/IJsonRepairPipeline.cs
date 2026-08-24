namespace Penghou.Nuwa;

public interface IJsonRepairPipeline
{
    ValueTask<JsonRepairResult> RepairAsync(
        string input,
        JsonSchemaExpectation? expectation = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Repairs a streamed payload, emitting stable-prefix preview deltas
    /// followed by one completed result. See
    /// <see cref="JsonRepairPipeline.RepairStreamAsync"/> for semantics.
    /// </summary>
    IAsyncEnumerable<JsonRepairStreamEvent> RepairStreamAsync(
        IAsyncEnumerable<string> chunks,
        JsonSchemaExpectation? expectation = null,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
