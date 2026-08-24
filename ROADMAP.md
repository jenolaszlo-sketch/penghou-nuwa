# Penghou.Nuwa Roadmap

## Schema-guided reconciliation and coercion hardening

Extend deterministic schema repair without introducing model-based inference.
Nuwa should repair only when the supplied schema provides a unique,
explainable interpretation; ambiguous input remains unchanged and is reported
as a shape mismatch.

Implementation status:

- Phase 1 — strong-name required-property reconciliation: implemented.
- Phase 2 — coercion correctness and item-aware array wrapping: implemented.
- Phase 3 — separately opt-in structural inference and richer evidence:
  implemented.

### Required-property name reconciliation

- Consider only unknown source properties and missing required schema
  properties.
- Never overwrite an existing target or map multiple sources to one target.
- Accept casing, separator, missing/duplicated-character, and transposition
  mistakes only when there is one clearly superior name candidate.
- Require the source value to match the target schema or have a certified,
  lossless coercion available.
- Apply reconciliation before coercion and before destructive unknown-property
  pruning.
- Refuse unresolved `oneOf`/`anyOf` branch ambiguity.
- Keep weak-name structural inference separately opt-in. Permit it only when a
  distinctive nested object, array-item shape, or enum membership identifies
  exactly one missing required target; primitive type compatibility alone is
  insufficient.

### Safe coercion rules

- Distinguish `integer` from `number`; never truncate a fractional value,
  accept overflow, or create a non-finite number.
- Convert strings to numbers or booleans only under an explicit target schema
  and only when conversion is lossless and unambiguous.
- Reconcile enum values only when the closest permitted value is unique.
- Preserve required values and the existing optional-null semantics.
- Never collapse arrays into scalars.

### Scalar-to-array wrapping

Wrap a scalar only when it is compatible with the array's `items` schema:

```text
scalar already matches item schema
    -> wrap as the single item

scalar has an enabled, deterministic, lossless coercion to the item schema
    -> coerce and wrap atomically

scalar is incompatible or ambiguous
    -> leave unchanged and report the mismatch
```

For object and array item schemas, compatibility includes nested required
shape and child types, not merely the outer JSON kind. Nuwa must not report a
successful array repair when the wrapped item remains invalid.

### Diagnostics and acceptance

- Record privacy-safe evidence for renames and coercions: path, source and
  target names or types, deterministic similarity, compatibility, unique-best
  selection, and validation improvement. Do not log original values by
  default.
- Ensure each accepted mutation improves Nuwa's supported structural schema
  validation and does not discard information.
- Cover nested objects, arrays of objects, collisions, ambiguous candidates,
  unions, incompatible values, integer edge cases, rename-plus-coercion, and
  reconciliation-before-pruning with behavioral tests.
- Keep existing public APIs and repair behavior compatible; expose new policy
  controls only where callers need to opt into broader inference.
