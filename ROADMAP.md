# Penghou.Nuwa Roadmap

## Direction

Nuwa is a deterministic JSON repair library for model-produced structured
output. It should preserve received information, make only explainable
repairs, expose honest diagnostics, and remain safe on malformed or hostile
input. Model-based inference and open-ended semantic guessing remain outside
its scope.

## Shipped foundation

### Robust recovery

- Shared work and correction budgets for tolerant and nested recovery.
- Defensive depth and lookahead limits.
- Consistent single-quote escape handling.
- Privacy-safe strategy reports and cancellation throughout recovery loops.
- Guarded AI middleware that does not rewrite ordinary scalar prose as JSON.

### Schema-guided reconciliation and coercion

- Strong-name reconciliation from unknown properties to uniquely matching
  missing required properties.
- Separately opt-in structural reconciliation using distinctive object shape,
  array-item shape, enum membership, or const evidence.
- Ambiguous candidates and unresolved `oneOf`/`anyOf` branches are refused.
- Reconciliation never overwrites an existing property or maps competing
  sources to one target.
- Integer-aware, finite, lossless string-to-number conversion.
- Deterministic string-to-boolean and unique enum reconciliation.
- Scalar-to-array wrapping only after complete item-schema validation, with
  safe coercion and wrapping applied atomically.
- `enum`, `const`, required properties, nested types, array items, and strict
  `additionalProperties:false` objects included in supported shape validation.
- One correction budget covers tolerant parsing and node-tree mutations.
- Canonical strategy order: strong-name reconciliation, structural
  reconciliation, coercion, then destructive unknown-property pruning.
- Bounded diagnostic evidence and a larger confidence penalty for broader
  structural inference.

## Completed milestone — usability and operational polish

Completed as a compatible, independently reviewable milestone.

### Dependency injection

- Make `AddJsonRepair()` self-sufficient when the host has not called
  `AddLogging()`, using a null logger fallback.
- Add integration coverage for registration with and without logging.

### Fluent configuration

- Add `InsertSalvageRepairAfter<TAnchor, TNew>()` for symmetry with text and
  node repair configuration.
- Keep duplicate detection and deterministic ordering unchanged.

### Markdown transport handling

- Recognize the first valid closing fence line rather than requiring it to be
  the final non-whitespace line.
- Preserve the fenced JSON body while deliberately excluding trailing prose.
- Cover backtick and tilde fences, longer closing markers, CRLF, whitespace,
  and marker-like content inside the JSON body.

### Async and logging hygiene

- Apply `ConfigureAwait(false)` consistently within library awaits and async
  enumeration.
- Avoid constructing repair summaries when the relevant log level is
  disabled.

### Missing behavioral coverage

- Cover both AI expectation resolvers.
- Cover `RepairJsonLookingTextWithoutResponseFormat` enabled and disabled.
- Retain multi-target coverage on .NET 8, 9, and 10.

## Architecture milestone — extensibility and predictable cost

Implement these separately from the usability milestone because they affect
construction, execution, or hot-path behavior.

### Parser and strategy construction

- Use the existing `ITolerantJsonSyntaxTreeParser` seam as an injected pipeline
  dependency with a default implementation.
- Design factory or instance-based strategy registration to replace the
  fewest-constructor reflection heuristic without breaking existing fluent
  configuration.
- Consolidate duplicated text, salvage, and node phase bookkeeping only when
  causal reporting and skipped-strategy behavior remain explicit.

### Schema expectation reuse

- Memoize property and item expectations so recursive repair does not
  repeatedly normalize the same schema nodes.
- Define cache ownership and mutation semantics first because `Schema` is
  currently publicly reachable as a mutable `JsonNode`.
- Cache AI tool expectations by tool identity and schema identity where the
  connector exposes stable references.

### Parsing and allocation performance

- Add lazy token memoization so repeated lookahead does not re-lex the same
  offsets.
- Replace repeated string reconstruction in template/verbatim strategies with
  bounded single-pass builders where benchmarks show value.
- Reuse parse results inside salvage strategies.
- Avoid full-tree clones for node strategies that can determine
  `NotApplicable` with a dry run.
- Remove redundant `JsonNode` serialization/parsing on trusted internal
  success paths while keeping validation on public factories.
- Add adversarial benchmarks before and after each hot-path optimization.

### Unified limits and causality

- Extend the shared operation context to text-strategy work where iteration or
  speculative search is not already explicitly bounded.
- Ensure failure results cannot bypass output limits.
- Make `SucceededBy` identify the strategy that causally changed the accepted
  artifact rather than relying on report position.

## Later public API design

These items should be designed together to avoid accumulating loosely related
diagnostic properties.

### Typed repair evidence

- Introduce a privacy-safe evidence model suitable for telemetry, containing
  path, operation kind, matching reason, deterministic distance where
  relevant, unique-best decision, and before/after validation counts.
- Keep `StrategyReport.Note` for compatibility and human-readable summaries.
- Never include original property values or payload fragments by default.

### Structured limit failures

- Add limit kind, configured limit, and observed value to
  `JsonRepairLimitException` so callers can react without parsing messages.
- Preserve the existing exception type and message behavior where practical.

### Invalid schema diagnostics

- Replace the silent `FromSchemaJson` parse downgrade with an explicit
  diagnostic or a separate throwing/try-create API.
- Do not turn malformed schemas into payload repair failures without an
  intentional compatibility decision.

## Deferred investigations

- Duplicate-property recovery policy: retain strict failure or adopt
  deterministic last-wins behavior with an explicit correction record.
- Full JSON Schema keyword validation. Nuwa currently validates the structural
  subset needed to make safe repairs; it is not intended to replace a
  dedicated dialect-aware validator.
- Property-based fuzzing and long-running adversarial test corpora.
- File-based public API analyzer baselines versus the existing reflection
  snapshot test.
- Repository-wide `.editorconfig` and CI format verification.

## Acceptance principles

- Every accepted mutation must preserve information or be explicitly
  classified as lossy.
- Schema-guided mutations must improve Nuwa's supported validation result.
- Ambiguity must be reported or left unrepaired, never guessed.
- Normal APIs remain deterministic and provider-independent.
- Diagnostics remain bounded and privacy-safe.
- Disabled optional behavior must remain inexpensive.
- New public API requires contract tests and migration-safe defaults.
