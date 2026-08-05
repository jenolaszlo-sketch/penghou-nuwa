namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Fixes malformations that prevent the input from parsing as JSON at all
/// (e.g. C#-flavored verbatim strings, unbalanced brackets). Operates on raw text
/// because the input isn't valid JSON yet — there's no tree to walk.
/// </summary>
public interface ITextRepair
{
    string Name { get; }

    ValueTask<TextRepairAttempt> RepairAsync(
        string input,
        CancellationToken cancellationToken = default);
}
