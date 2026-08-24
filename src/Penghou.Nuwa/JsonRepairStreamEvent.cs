namespace Penghou.Nuwa;

/// <summary>
/// An event emitted while repairing a streamed JSON payload.
/// </summary>
public abstract record JsonRepairStreamEvent;

/// <summary>
/// A verbatim slice of the accumulated input that is stable: it lies outside
/// any open string, ends on a complete token boundary, and sits far enough
/// from the stream tail that punctuation repairs cannot alter it. Delta text
/// is a preview — the <see cref="JsonRepairStreamCompleted"/> event carries
/// the authoritative repaired result.
/// </summary>
public sealed record JsonRepairStreamDelta(
    int Offset,
    string Text) : JsonRepairStreamEvent;

/// <summary>
/// The final repair outcome for the fully accumulated payload.
/// </summary>
public sealed record JsonRepairStreamCompleted(
    JsonRepairResult Result) : JsonRepairStreamEvent;
