namespace Penghou.Nuwa.Strategies;

internal static class SchemaRepairDiagnostics
{
    private const int MaxEntries = 8;
    private const int MaxEntryLength = 256;

    public static string Join(IReadOnlyCollection<string> entries)
    {
        var selected = entries
            .Take(MaxEntries)
            .Select(entry => entry.Length <= MaxEntryLength
                ? entry
                : entry[..MaxEntryLength] + "…")
            .ToList();
        if (entries.Count > MaxEntries)
        {
            selected.Add($"{entries.Count - MaxEntries} additional mappings omitted");
        }

        return string.Join("; ", selected);
    }
}
