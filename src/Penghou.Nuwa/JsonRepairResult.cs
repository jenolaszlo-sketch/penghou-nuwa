using System.Text.Json;

namespace Penghou.Nuwa;

public sealed class JsonRepairResult(
    JsonDocument? document,
    bool wasRepaired,
    IReadOnlyDictionary<string, string> attempts)
    : IDisposable
{
    public JsonDocument? Document { get; } = document;

    public bool Succeeded => Document is not null;

    public bool WasRepaired { get; } = wasRepaired;

    public IReadOnlyDictionary<string, string> Attempts { get; } = attempts;

    public void Dispose() => Document?.Dispose();
}
