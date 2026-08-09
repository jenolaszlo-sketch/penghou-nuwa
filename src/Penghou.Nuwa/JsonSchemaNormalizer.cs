using System.Text.Json.Nodes;

namespace Penghou.Nuwa;

/// <summary>
/// Resolves a JSON Schema document into a flat, self-contained tree that the
/// repair pipeline's narrow reader understands. Local <c>$ref</c> pointers are
/// inlined; genuine cycles are cut with a type-only stub so recursive schemas
/// terminate; <c>oneOf</c>/<c>anyOf</c>/<c>allOf</c>/<c>enum</c>/<c>nullable</c>
/// are reduced to the canonical <c>type</c> form (<c>string</c> or array of
/// strings, with <c>null</c> included when the schema allows it).
/// </summary>
/// <remarks>
/// This is a repair aid, not a model-facing schema generator: only the shape
/// information the repair strategies need (types, properties, required, items,
/// additionalProperties, nullability) is preserved. External (unresolvable)
/// <c>$ref</c>s become opaque stubs so the pipeline never crashes on exotic
/// schemas.
/// <para>
/// Union keywords are preserved as well: the <c>oneOf</c>/<c>anyOf</c> array
/// is kept with each branch normalized in place, and object-shaped branches
/// contribute their declared properties to the canonical view. This lets the
/// repair strategies pick a specific branch (via discriminator or shape) while
/// still reading a merged, backward-compatible top-level schema.
/// </para>
/// </remarks>
internal static class JsonSchemaNormalizer
{
    private const int MaxDepth = 64;

    public static JsonNode Normalize(JsonNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var state = new State(root);

        return state.NormalizeNode(root, 0);
    }

    private sealed class State
    {
        private readonly JsonNode _root;
        private readonly HashSet<string> _refStack =
            new(StringComparer.Ordinal);

        public State(JsonNode root)
        {
            _root = root;
        }

        public JsonNode NormalizeNode(JsonNode? node, int depth)
        {
            if (node is not JsonObject obj)
            {
                return node?.DeepClone() ?? new JsonObject();
            }

            if (depth >= MaxDepth)
            {
                return Typed("object");
            }

            // A pure $ref: inline the target wholesale.
            if (TryGetRef(obj, out var reference) && obj.Count == 1)
            {
                return Resolve(reference, depth);
            }

            // $ref combined with siblings (OpenAPI "extends" style): inline the
            // target first, then overlay the sibling keywords on top.
            var result = new JsonObject();

            if (TryGetRef(obj, out var mixedReference))
            {
                var resolved = Resolve(mixedReference, depth);

                if (resolved is JsonObject resolvedObject)
                {
                    foreach (var (key, value) in resolvedObject)
                    {
                        result[key] = value?.DeepClone();
                    }
                }

                foreach (var (key, value) in obj)
                {
                    if (key == "$ref")
                    {
                        continue;
                    }

                    result[key] = value?.DeepClone();
                }
            }
            else
            {
                foreach (var (key, value) in obj)
                {
                    result[key] = value?.DeepClone();
                }
            }

            // OpenAPI "allOf extends" pattern: merge every branch, then remove.
            if (result["allOf"] is JsonArray allOf && allOf.Count > 0)
            {
                MergeAllOf(result, allOf, depth);
            }

            // Recurse into the shape keywords.
            if (result["properties"] is JsonObject properties)
            {
                var normalized = new JsonObject();

                foreach (var (name, child) in properties)
                {
                    normalized[name] = NormalizeNode(child, depth + 1);
                }

                result["properties"] = normalized;
            }

            if (result["items"] is { } items)
            {
                result["items"] = NormalizeNode(items, depth + 1);
            }

            if (result["additionalProperties"] is JsonObject additional)
            {
                result["additionalProperties"] =
                    NormalizeNode(additional, depth + 1);
            }

            // Normalize union branches in place so they can be surfaced to the
            // repair strategies as a per-branch view.
            NormalizeUnionKeywords(result, depth);

            InferType(result, depth);

            result.Remove("allOf");
            result.Remove("enum");

            return result;
        }

        private void NormalizeUnionKeywords(
            JsonObject result,
            int depth)
        {
            if (result["oneOf"] is JsonArray oneOf)
            {
                var normalized =
                    NormalizeUnionBranches(oneOf, depth);

                if (normalized is not null)
                {
                    result["oneOf"] = normalized;
                }
                else
                {
                    result.Remove("oneOf");
                }

                return;
            }

            if (result["anyOf"] is JsonArray anyOf)
            {
                var normalized =
                    NormalizeUnionBranches(anyOf, depth);

                if (normalized is not null)
                {
                    result["anyOf"] = normalized;
                }
                else
                {
                    result.Remove("anyOf");
                }
            }
        }

        private JsonArray? NormalizeUnionBranches(
            JsonArray union,
            int depth)
        {
            var normalized = new JsonArray();

            foreach (var branch in union)
            {
                if (branch is not JsonObject branchObject)
                {
                    continue;
                }

                if (NormalizeNode(
                        branchObject,
                        depth + 1) is { } effective)
                {
                    normalized.Add(effective);
                }
            }

            return normalized.Count > 0
                ? normalized
                : null;
        }

        private JsonNode Resolve(string reference, int depth)
        {
            if (!TryResolvePointer(reference, out var target))
            {
                // Unresolvable (external URI or missing local pointer): opaque
                // stub, kind unknown. The pipeline treats it as untyped.
                return new JsonObject();
            }

            if (!_refStack.Add(reference))
            {
                // Genuine cycle: cut with a type-only stub so recursion stops.
                return GetTypeStub(target!);
            }

            try
            {
                return NormalizeNode(target, depth + 1);
            }
            finally
            {
                _refStack.Remove(reference);
            }
        }

        private bool TryResolvePointer(
            string reference,
            out JsonNode? target)
        {
            target = null;

            if (reference == "#")
            {
                target = _root;
                return true;
            }

            var pointer = reference.StartsWith('#')
                ? reference[1..]
                : reference;

            if (pointer.Length == 0)
            {
                target = _root;
                return true;
            }

            if (!pointer.StartsWith('/'))
            {
                return false;
            }

            JsonNode? current = _root;

            foreach (var segment in pointer
                         .Split('/')
                         .Skip(1))
            {
                var key = segment
                    .Replace("~1", "/")
                    .Replace("~0", "~");

                if (current is JsonObject currentObject &&
                    currentObject.TryGetPropertyValue(key, out var next))
                {
                    current = next;
                    continue;
                }

                if (current is JsonArray currentArray &&
                    int.TryParse(key, out var index) &&
                    index >= 0 &&
                    index < currentArray.Count)
                {
                    current = currentArray[index];
                    continue;
                }

                return false;
            }

            target = current;
            return current is not null;
        }

        private void MergeAllOf(
            JsonObject result,
            JsonArray allOf,
            int depth)
        {
            var typeNodes = new List<JsonNode?>();
            var nullable = false;

            foreach (var branch in allOf)
            {
                if (branch is null)
                {
                    continue;
                }

                // Normalize lazily against the same root, but avoid a fresh
                // State so $ref cycle detection stays consistent.
                if (branch is not JsonObject branchObject)
                {
                    continue;
                }

                var effective = ResolveLocalBranch(branchObject, depth);

                if (effective["type"] is { } branchType)
                {
                    typeNodes.Add(branchType);
                }

                if (effective["nullable"] is JsonValue nullableValue &&
                    nullableValue.TryGetValue<bool>(out var isNullable) &&
                    isNullable)
                {
                    nullable = true;
                }

                if (effective["properties"] is JsonObject branchProperties)
                {
                    var merged = result["properties"] is JsonObject existing
                        ? existing
                        : new JsonObject();

                    foreach (var (name, value) in branchProperties)
                    {
                        merged[name] = value?.DeepClone();
                    }

                    result["properties"] = merged;
                }

                if (effective["required"] is JsonArray branchRequired)
                {
                    var required = result["required"] is JsonArray existing
                        ? existing
                        : new JsonArray();

                    foreach (var name in branchRequired)
                    {
                        if (name is null || required.Contains(name))
                        {
                            continue;
                        }

                        required.Add(name.DeepClone());
                    }

                    result["required"] = required;
                }
            }

            if (typeNodes.Count > 0)
            {
                result["type"] = UnionTypes(typeNodes);
            }

            if (nullable)
            {
                result["nullable"] = true;
            }
        }

        /// <summary>
        /// Normalizes an <c>allOf</c> branch against the root without the
        /// depth bookkeeping of the public path. Shared so cycle detection
        /// remains correct when branches themselves carry <c>$ref</c>s.
        /// </summary>
        private JsonObject ResolveLocalBranch(
            JsonObject branch,
            int depth)
        {
            if (TryGetRef(branch, out var reference) &&
                branch.Count == 1)
            {
                return Resolve(reference, depth) as JsonObject
                    ?? new JsonObject();
            }

            return NormalizeNode(branch, depth + 1) as JsonObject
                ?? new JsonObject();
        }

        private static void InferType(
            JsonObject result,
            int depth)
        {
            if (result["type"] is not null)
            {
                EnsureNullableType(result);
                return;
            }

            // enum without an explicit type implies strings.
            if (result["enum"] is not null)
            {
                result["type"] = "string";
                return;
            }

            // Union keywords: collect each branch's effective type and fold the
            // declared shapes of object branches into the canonical view.
            if (result["oneOf"] is JsonArray oneOf)
            {
                result["type"] = UnionBranchTypes(oneOf, depth);
                MergeObjectBranches(result, oneOf);
                return;
            }

            if (result["anyOf"] is JsonArray anyOf)
            {
                result["type"] = UnionBranchTypes(anyOf, depth);
                MergeObjectBranches(result, anyOf);
                return;
            }

            // Shape keywords imply the container kind.
            if (result["properties"] is not null ||
                result["additionalProperties"] is not null)
            {
                result["type"] = "object";
                return;
            }

            if (result["items"] is not null)
            {
                result["type"] = "array";
                return;
            }

            // Untyped schema: null is allowed conservatively.
            result["nullable"] = true;
        }

        private static JsonNode? UnionBranchTypes(
            JsonArray branches,
            int depth)
        {
            var types = new List<JsonNode?>();

            foreach (var branch in branches)
            {
                if (branch is not JsonObject branchObject)
                {
                    continue;
                }

                if (branchObject["type"] is { } branchType)
                {
                    types.Add(branchType);
                }
            }

            if (types.Count == 0)
            {
                return null;
            }

            return UnionTypes(types);
        }

        private static void MergeObjectBranches(
            JsonObject result,
            JsonArray branches)
        {
            foreach (var branch in branches)
            {
                if (branch is not JsonObject branchObject ||
                    !IncludesObject(branchObject))
                {
                    continue;
                }

                MergeObjectBranchShape(result, branchObject);
            }
        }

        private static bool IncludesObject(
            JsonObject branch)
        {
            if (branch["properties"] is not null)
            {
                return true;
            }

            return branch["type"] switch
            {
                JsonValue value when
                    value.TryGetValue<string>(
                        out var single) =>
                    single == "object",
                JsonArray array =>
                    array.Any(item =>
                        item is JsonValue itemValue &&
                        itemValue.TryGetValue<string>(
                            out var itemType) &&
                        itemType == "object"),
                _ => false
            };
        }

        private static void MergeObjectBranchShape(
            JsonObject result,
            JsonObject branch)
        {
            if (branch["properties"] is JsonObject branchProperties)
            {
                var merged =
                    result["properties"] is JsonObject existing
                        ? existing
                        : new JsonObject();

                foreach (var (name, value) in branchProperties)
                {
                    if (!merged.ContainsKey(name))
                    {
                        merged[name] = value?.DeepClone();
                    }
                }

                result["properties"] = merged;
            }

            if (branch["required"] is JsonArray branchRequired)
            {
                var required =
                    result["required"] is JsonArray existing
                        ? existing
                        : new JsonArray();

                foreach (var name in branchRequired)
                {
                    if (name is null || required.Contains(name))
                    {
                        continue;
                    }

                    required.Add(name.DeepClone());
                }

                result["required"] = required;
            }
        }

        private static JsonNode UnionTypes(
            List<JsonNode?> types)
        {
            var seen = new List<string>();
            var hasNull = false;

            foreach (var type in types)
            {
                if (type is JsonValue value &&
                    value.TryGetValue<string>(out var single) &&
                    !seen.Contains(single))
                {
                    if (single == "null")
                    {
                        hasNull = true;
                    }
                    else
                    {
                        seen.Add(single);
                    }

                    continue;
                }

                if (type is JsonArray array)
                {
                    foreach (var item in array)
                    {
                        if (item is null)
                        {
                            continue;
                        }

                        var itemType = item.GetValue<string>();

                        if (itemType == "null")
                        {
                            hasNull = true;
                        }
                        else if (!seen.Contains(itemType))
                        {
                            seen.Add(itemType);
                        }
                    }
                }
            }

            if (hasNull)
            {
                seen.Add("null");
            }

            if (seen.Count == 1)
            {
                return seen[0];
            }

            var result = new JsonArray();

            foreach (var item in seen)
            {
                result.Add(item);
            }

            return result;
        }

        private static void EnsureNullableType(
            JsonObject result)
        {
            if (result["nullable"] is JsonValue nullableValue &&
                nullableValue.TryGetValue<bool>(out var isNullable) &&
                isNullable &&
                result["type"] is { } type)
            {
                result["type"] = AppendNull(type);
            }
        }

        private static JsonNode AppendNull(JsonNode type)
        {
            if (type is JsonValue value &&
                value.TryGetValue<string>(out var single))
            {
                if (single == "null")
                {
                    return type;
                }

                return new JsonArray { single, "null" };
            }

            if (type is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is JsonValue itemValue &&
                        itemValue.TryGetValue<string>(out var itemType) &&
                        itemType == "null")
                    {
                        return type;
                    }
                }

                var result = new JsonArray();

                foreach (var item in array)
                {
                    result.Add(item?.DeepClone());
                }

                result.Add("null");
                return result;
            }

            return type;
        }

        private static JsonNode GetTypeStub(JsonNode target)
        {
            if (target is not JsonObject targetObject)
            {
                return Typed("object");
            }

            if (targetObject["type"] is JsonValue value &&
                value.TryGetValue<string>(out var single))
            {
                return Typed(single);
            }

            return targetObject["type"] is JsonArray array
                ? array.DeepClone()
                : Typed("object");
        }

        private static bool TryGetRef(
            JsonObject obj,
            out string reference)
        {
            reference = string.Empty;

            if (obj["$ref"] is JsonValue value &&
                value.TryGetValue<string>(out var referenceValue) &&
                !string.IsNullOrWhiteSpace(referenceValue))
            {
                reference = referenceValue;
                return true;
            }

            return false;
        }

        private static JsonObject Typed(string type) =>
            new() { ["type"] = type };
    }
}
