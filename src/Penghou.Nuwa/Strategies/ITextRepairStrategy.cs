namespace Penghou.Nuwa.Strategies;

/// <summary>
/// Fixes malformations that prevent the input from parsing as JSON at all
/// (e.g. C#-flavored verbatim strings, unbalanced brackets). Operates on raw text
/// because the input isn't valid JSON yet â€” there's no tree to walk.
/// </summary>
public interface ITextRepairStrategy
{
    string Name { get; }

    bool MightApply(string input);

    bool TryRepair(string input, out string repaired);
}
