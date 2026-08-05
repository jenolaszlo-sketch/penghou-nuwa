using Penghou.Nuwa.Strategies;
using System.Reflection;

namespace Penghou.Nuwa;

/// <summary>
/// Configures the ordered repair strategies used by the JSON repair pipeline.
/// Strategies are resolved from the container by type, so implementations may
/// inject services such as an <c>ILogger</c> or an HTTP client.
/// </summary>
public sealed class JsonRepairOptions
{
    private readonly List<Type> _textRepairs = [];
    private readonly List<Type> _salvageRepairs = [];
    private readonly List<Type> _nodeRepairs = [];

    public JsonRepairOptions()
    {
        _textRepairs.Add(typeof(MarkdownJsonFenceRepairStrategy));
        _textRepairs.Add(typeof(PseudoCSharpVerbatimStringRepairStrategy));
        _textRepairs.Add(typeof(PseudoJavaScriptTemplateStringRepairStrategy));
        _salvageRepairs.Add(typeof(SalvageRepairStrategy));
        _nodeRepairs.Add(typeof(SchemaGuidedOptionalNullRemovalStrategy));
        _nodeRepairs.Add(typeof(SchemaGuidedJsonStringExpansionStrategy));
    }

    /// <summary>
    /// Ordered text-repair strategies that run before tolerant parsing.
    /// </summary>
    public IReadOnlyList<Type> TextRepairs => _textRepairs;

    /// <summary>
    /// Ordered fallback strategies that run only when tolerant recovery fails.
    /// Lossy by design.
    /// </summary>
    public IReadOnlyList<Type> SalvageRepairs => _salvageRepairs;

    /// <summary>Ordered node-repair strategies that run against the recovered tree.</summary>
    public IReadOnlyList<Type> NodeRepairs => _nodeRepairs;

    public JsonRepairOptions AddTextRepair<T>() where T : class, ITextRepair
    {
        _textRepairs.Add(typeof(T));
        return this;
    }

    public JsonRepairOptions InsertTextRepairAfter<TAnchor, TNew>()
        where TAnchor : class, ITextRepair
        where TNew : class, ITextRepair
    {
        InsertAfter(
            _textRepairs,
            typeof(TAnchor),
            typeof(TNew),
            "text repair");
        return this;
    }

    public JsonRepairOptions RemoveTextRepair<T>() where T : class, ITextRepair
    {
        Remove(_textRepairs, typeof(T), "text repair");
        return this;
    }

    public JsonRepairOptions ClearTextRepairs()
    {
        _textRepairs.Clear();
        return this;
    }

    public JsonRepairOptions AddSalvageRepair<T>() where T : class, ITextRepair
    {
        _salvageRepairs.Add(typeof(T));
        return this;
    }

    public JsonRepairOptions RemoveSalvageRepair<T>() where T : class, ITextRepair
    {
        Remove(_salvageRepairs, typeof(T), "salvage repair");
        return this;
    }

    /// <summary>Disables the lossy fallback phase entirely.</summary>
    public JsonRepairOptions DisableSalvageFallback()
    {
        _salvageRepairs.Clear();
        return this;
    }

    public JsonRepairOptions AddNodeRepair<T>() where T : class, INodeRepair
    {
        _nodeRepairs.Add(typeof(T));
        return this;
    }

    public JsonRepairOptions InsertNodeRepairAfter<TAnchor, TNew>()
        where TAnchor : class, INodeRepair
        where TNew : class, INodeRepair
    {
        InsertAfter(
            _nodeRepairs,
            typeof(TAnchor),
            typeof(TNew),
            "node repair");
        return this;
    }

    public JsonRepairOptions RemoveNodeRepair<T>() where T : class, INodeRepair
    {
        Remove(_nodeRepairs, typeof(T), "node repair");
        return this;
    }

    public JsonRepairOptions ClearNodeRepairs()
    {
        _nodeRepairs.Clear();
        return this;
    }

    internal void Validate()
    {
        ValidateNoDuplicates(_textRepairs, "text repair");
        ValidateNoDuplicates(_salvageRepairs, "salvage repair");
        ValidateNoDuplicates(_nodeRepairs, "node repair");
        ValidateStrategies(_textRepairs, typeof(ITextRepair), "text repair");
        ValidateStrategies(_salvageRepairs, typeof(ITextRepair), "salvage repair");
        ValidateStrategies(_nodeRepairs, typeof(INodeRepair), "node repair");
    }

    private static void InsertAfter(
        List<Type> list,
        Type anchor,
        Type added,
        string label)
    {
        var index = list.IndexOf(anchor);

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"The {label} '{anchor.Name}' is not registered, so a strategy cannot be inserted after it.");
        }

        if (list.Contains(added))
        {
            throw new InvalidOperationException(
                $"The {label} '{added.Name}' is already registered.");
        }

        list.Insert(index + 1, added);
    }

    private static void Remove(
        List<Type> list,
        Type type,
        string label)
    {
        if (!list.Remove(type))
        {
            throw new InvalidOperationException(
                $"The {label} '{type.Name}' is not registered.");
        }
    }

    private static void ValidateStrategies(
        IReadOnlyList<Type> types,
        Type requiredInterface,
        string label)
    {
        foreach (var type in types)
        {
            if (!requiredInterface.IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    $"The {label} '{type.Name}' does not implement '{requiredInterface.Name}'.");
            }

            if (!type
                    .GetConstructors(
                        BindingFlags.Instance |
                        BindingFlags.Public)
                    .Any())
            {
                throw new InvalidOperationException(
                    $"The {label} '{type.Name}' has no public constructor and cannot be created. Register it with AddJsonRepair(Action<JsonRepairOptions>) using a public-constructor type instead.");
            }
        }
    }

    private static void ValidateNoDuplicates(
        List<Type> list,
        string label)
    {
        var duplicates = list
            .GroupBy(type => type)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.Name)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate {label} registrations: {string.Join(", ", duplicates)}.");
        }
    }
}
