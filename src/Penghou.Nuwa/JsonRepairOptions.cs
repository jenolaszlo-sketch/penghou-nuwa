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
    private readonly Dictionary<Type, Func<object>> _strategyFactories = [];

    public JsonRepairOptions()
    {
        _textRepairs.Add(typeof(MarkdownJsonFenceRepairStrategy));
        _textRepairs.Add(typeof(UnicodeDelimiterNormalizationStrategy));
        _textRepairs.Add(typeof(XmlWrappedExtractionStrategy));
        _textRepairs.Add(typeof(ConcatenatedJsonExtractionStrategy));
        _textRepairs.Add(typeof(ProseWrapperExtractionStrategy));
        _textRepairs.Add(typeof(PseudoCSharpVerbatimStringRepairStrategy));
        _textRepairs.Add(typeof(PseudoJavaScriptTemplateStringRepairStrategy));
        _salvageRepairs.Add(typeof(SalvageRepairStrategy));
        _nodeRepairs.Add(typeof(SchemaGuidedOptionalNullRemovalStrategy));
        _nodeRepairs.Add(typeof(SchemaGuidedJsonStringExpansionStrategy));
    }

    /// <summary>
    /// Resource limits applied to each repair operation.
    /// </summary>
    public JsonRepairLimits Limits { get; set; } = JsonRepairLimits.Default;

    /// <summary>
    /// Whether input that ends mid-value (a truncated generation) may be
    /// salvaged by dropping the incomplete trailing property or element while
    /// keeping everything before it. The dropped fragment is always recorded
    /// as a tolerant-recovery correction. Defaults to <c>true</c>.
    /// </summary>
    public bool AllowTruncationSalvage { get; set; } = true;

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

    public JsonRepairOptions AddTextRepair<T>(Func<T> factory)
        where T : class, ITextRepair =>
        AddFactory(_textRepairs, factory, "text repair");

    public JsonRepairOptions AddTextRepair(ITextRepair instance) =>
        AddInstance(_textRepairs, instance, "text repair");

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
        RemoveFactories(_textRepairs);
        _textRepairs.Clear();
        return this;
    }

    public JsonRepairOptions AddSalvageRepair<T>() where T : class, ITextRepair
    {
        _salvageRepairs.Add(typeof(T));
        return this;
    }

    public JsonRepairOptions AddSalvageRepair<T>(Func<T> factory)
        where T : class, ITextRepair =>
        AddFactory(_salvageRepairs, factory, "salvage repair");

    public JsonRepairOptions AddSalvageRepair(ITextRepair instance) =>
        AddInstance(_salvageRepairs, instance, "salvage repair");

    public JsonRepairOptions InsertSalvageRepairAfter<TAnchor, TNew>()
        where TAnchor : class, ITextRepair
        where TNew : class, ITextRepair
    {
        InsertAfter(
            _salvageRepairs,
            typeof(TAnchor),
            typeof(TNew),
            "salvage repair");
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
        RemoveFactories(_salvageRepairs);
        _salvageRepairs.Clear();
        return this;
    }

    /// <summary>
    /// Registers the schema-guided coercion strategies: array wrapping,
    /// string-to-number and string-to-boolean conversion, enum fuzzy
    /// matching, and unknown-property pruning for strict contracts. Off by
    /// default so repairs stay structurally conservative; enable when the
    /// wire schema is authoritative and typed coercion is acceptable.
    /// </summary>
    public JsonRepairOptions EnableSchemaCoercions()
    {
        foreach (var strategyType in new[]
                 {
                     typeof(SchemaGuidedArrayWrapStrategy),
                     typeof(SchemaGuidedStringToNumberCoercionStrategy),
                     typeof(SchemaGuidedStringToBooleanCoercionStrategy),
                     typeof(SchemaGuidedEnumFuzzyMatchStrategy),
                     typeof(SchemaGuidedUnknownPropertyPruneStrategy)
                 })
        {
            if (!_nodeRepairs.Contains(strategyType))
            {
                _nodeRepairs.Add(strategyType);
            }
        }

        return this;
    }

    /// <summary>
    /// Enables conservative reconciliation of unknown property names to
    /// uniquely matching missing required schema properties. The strategy
    /// never overwrites an existing target and runs before coercion or
    /// unknown-property pruning regardless of configuration call order.
    /// </summary>
    public JsonRepairOptions EnableRequiredPropertyReconciliation()
    {
        var strategyType =
            typeof(SchemaGuidedRequiredPropertyReconciliationStrategy);
        if (_nodeRepairs.Contains(strategyType))
            return this;

        InsertBeforeFirst(
            strategyType,
            typeof(SchemaGuidedStructuralPropertyReconciliationStrategy),
            typeof(SchemaGuidedArrayWrapStrategy),
            typeof(SchemaGuidedStringToNumberCoercionStrategy),
            typeof(SchemaGuidedStringToBooleanCoercionStrategy),
            typeof(SchemaGuidedEnumFuzzyMatchStrategy),
            typeof(SchemaGuidedUnknownPropertyPruneStrategy));
        return this;
    }

    /// <summary>
    /// Enables broader reconciliation based on a uniquely identifying nested
    /// object shape, array-item shape, or exact enum membership. Primitive
    /// type compatibility alone never qualifies. This policy is separate from
    /// strong-name reconciliation because property names may be unrelated.
    /// </summary>
    public JsonRepairOptions EnableStructuralPropertyReconciliation()
    {
        var strategyType =
            typeof(SchemaGuidedStructuralPropertyReconciliationStrategy);
        if (_nodeRepairs.Contains(strategyType))
            return this;

        InsertBeforeFirst(
            strategyType,
            typeof(SchemaGuidedArrayWrapStrategy),
            typeof(SchemaGuidedStringToNumberCoercionStrategy),
            typeof(SchemaGuidedStringToBooleanCoercionStrategy),
            typeof(SchemaGuidedEnumFuzzyMatchStrategy),
            typeof(SchemaGuidedUnknownPropertyPruneStrategy));
        return this;
    }

    private void InsertBeforeFirst(Type strategyType, params Type[] anchors)
    {
        var anchorSet = anchors.ToHashSet();
        var insertionIndex = _nodeRepairs.FindIndex(anchorSet.Contains);
        _nodeRepairs.Insert(
            insertionIndex < 0 ? _nodeRepairs.Count : insertionIndex,
            strategyType);
    }

    public JsonRepairOptions AddNodeRepair<T>() where T : class, INodeRepair
    {
        _nodeRepairs.Add(typeof(T));
        return this;
    }

    public JsonRepairOptions AddNodeRepair<T>(Func<T> factory)
        where T : class, INodeRepair =>
        AddFactory(_nodeRepairs, factory, "node repair");

    public JsonRepairOptions AddNodeRepair(INodeRepair instance) =>
        AddInstance(_nodeRepairs, instance, "node repair");

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
        RemoveFactories(_nodeRepairs);
        _nodeRepairs.Clear();
        return this;
    }

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Limits);
        Limits.Validate();
        ValidateNoDuplicates(_textRepairs, "text repair");
        ValidateNoDuplicates(_salvageRepairs, "salvage repair");
        ValidateNoDuplicates(_nodeRepairs, "node repair");
        ValidateStrategies(_textRepairs, typeof(ITextRepair), "text repair");
        ValidateStrategies(_salvageRepairs, typeof(ITextRepair), "salvage repair");
        ValidateStrategies(_nodeRepairs, typeof(INodeRepair), "node repair");
    }

    internal bool TryCreateStrategy(Type type, out object strategy)
    {
        if (_strategyFactories.TryGetValue(type, out var factory))
        {
            strategy = factory() ??
                throw new InvalidOperationException(
                    $"The strategy factory for '{type.Name}' returned null.");
            return true;
        }

        strategy = null!;
        return false;
    }

    private JsonRepairOptions AddFactory<T>(
        List<Type> list,
        Func<T> factory,
        string label)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        var type = typeof(T);
        if (list.Contains(type))
            throw new InvalidOperationException($"The {label} '{type.Name}' is already registered.");
        list.Add(type);
        _strategyFactories.Add(type, () => factory());
        return this;
    }

    private JsonRepairOptions AddInstance<T>(
        List<Type> list,
        T instance,
        string label)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        var type = instance.GetType();
        if (list.Contains(type))
            throw new InvalidOperationException($"The {label} '{type.Name}' is already registered.");
        list.Add(type);
        _strategyFactories.Add(type, () => instance);
        return this;
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

    private void Remove(
        List<Type> list,
        Type type,
        string label)
    {
        if (!list.Remove(type))
        {
            throw new InvalidOperationException(
                $"The {label} '{type.Name}' is not registered.");
        }

        _strategyFactories.Remove(type);
    }

    private void RemoveFactories(IEnumerable<Type> types)
    {
        foreach (var type in types)
            _strategyFactories.Remove(type);
    }

    private void ValidateStrategies(
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

            if (!_strategyFactories.ContainsKey(type) &&
                !type
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
