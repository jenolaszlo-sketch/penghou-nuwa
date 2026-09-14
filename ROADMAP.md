# Penghou.Nuwa Roadmap

## Status — 1.0 graduated

Nuwa 1.0 is the pipeline described above plus the usability and architecture
slices below. No new product scope was added for graduation.

Completed for 1.0:

- Usability and operational polish milestone (DI self-sufficiency, fluent
  configuration symmetry, Markdown transport handling, async/logging hygiene,
  behavioral coverage on .NET 8, 9, and 10).
- Architecture slice (injected parser seam, unified phase runners,
  factory/instance strategy registration, schema-expectation memoization,
  token-lookahead memoization, trusted internal result construction, causal
  success credit, output limits on the failure path, AI schema caching,
  dry applicability scans where already applied).
- Public API baseline graduation: the 1.0 contract is locked by
  Roslyn public API analyzer baselines
  (`src/Penghou.Nuwa/PublicAPI.Shipped.txt` and
  `src/Penghou.Nuwa.Extensions.AI/PublicAPI.Shipped.txt`, enforced with
  warnings-as-errors). The old reflection snapshot test was removed; the
  analyzer baselines cover the identical 42-type core contract in stricter
  detail. No public API changes were made during migration.
- Restore/build/package readiness: SourceLink upgraded past the
  `Microsoft.Build.Tasks.Git 8.0.0` advisory (NU1902), NuGet auditing intact,
  multi-targeting (`net8.0`/`net9.0`/`net10.0`), package validation enabled,
  deterministic builds, version `1.0.0`.

Explicitly deferred beyond 1.0 (non-blocking, no commitment implied):

- Property-based fuzzing and long-running adversarial corpora.
- Repository-wide `.editorconfig` and CI format verification.
- Full JSON Schema keyword validation (Nuwa remains repair-only).
- Later public API design: typed repair evidence, structured limit failures,
  explicit invalid-schema diagnostics.
- Remaining performance work: benchmarks before/after hot-path changes,
  further dry-scan/clone-avoidance extensions, parse-result reuse across the
  salvage boundary (would change the public strategy contract).
- Unified operation-context limits for remaining text-strategy search.
- Deferred investigations below (dogfood corpus, duplicate-property policy).

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
- Speculative text and extraction candidates no longer replace the original
  recovery path; tolerant parsing ranks both by parse success, structural
  errors, and correction count, and salvage can fall back to the original.
- A text strategy receives causal `Succeeded` credit only when its candidate
  lineage is accepted. Rejected intermediate candidates remain `Failed` with
  an explicit privacy-safe note.
- Concatenated top-level structural values are refused as ambiguous rather
  than silently selecting the first; a sole value followed by stray delimiters
  or prose remains recoverable.
- `IsRepairAccepted` distinguishes Nuwa syntax-plus-shape acceptance from
  syntax-only `Succeeded`; host deserialization and tool mapping remain later
  acceptance gates.
- Node-tree mutations form a bounded speculative lineage. Intermediate steps
  may temporarily increase structural errors, but Nuwa atomically selects a
  no-worse candidate or rolls the complete lineage back.
- Default schema-guided scalar-to-string coercion converts only JSON numbers
  and booleans using deterministic token spelling. Combined with JSON-string
  expansion, this repairs inputs such as `{"files":"[1, 2]"}` into
  `{"files":["1","2"]}` without stringifying nulls or composite values.
- Recovery logging distinguishes accepted repair from syntax recovery whose
  expected shape remains mismatched.

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

Completed in the first architecture slice:

- The tolerant parser is an injected pipeline dependency with a default.
- Factory-created schema expectations memoize property and item expectations;
  direct public-constructor instances retain uncached mutable-schema behavior.
- Token lookahead memoizes lexing by source offset.
- Trusted internal result construction avoids redundant consistency reparsing.
- Unchanged node-strategy output no longer receives causal success credit.
- Failure output is subject to the same output limit as successful repair.
- AI response/tool schemas are cached per wrapped client by schema identity.
- Optional-null removal performs a dry applicability scan before cloning.
- Enum fuzzy matching and strict unknown-property pruning perform dry
  applicability scans before cloning unchanged payload trees.
- Ordered strategy registration supports types, instances, and factories in
  standalone and dependency-injection construction; reflection remains a
  compatibility fallback.
- Text and salvage execution share one ordered phase runner; node execution
  uses a dedicated runner that preserves mutation budgets and causal reports.

### Schema expectation reuse (completed)

- Factory-created expectations own a normalized schema snapshot and cache its
  child expectations. Direct-constructor expectations deliberately retain
  live, uncached schema semantics for compatibility; both behaviors are
  documented and covered by tests.

### Parsing and allocation performance (deferred remainder)

- JavaScript template repair uses a single bounded builder rather than
  reconstructing the complete string after every literal, with a large-input
  regression test.
- Extend dry applicability scans to remaining node strategies only where
  profiling shows the extra traversal is cheaper than cloning; conversion and
  array-wrap scans can allocate or parse during detection and are not assumed
  to be wins.
- Add adversarial benchmarks before and after each hot-path optimization.

Salvage parse-result reuse remains intentionally deferred: `ITextRepair`
returns text and the pipeline owns parsing. Carrying a parsed artifact would
change the public strategy contract and should be justified by profiling
rather than introduced as a special-case side channel.

### Unified limits and causality (deferred)

- Extend the shared operation context to text-strategy work where iteration or
  speculative search is not already explicitly bounded.

## Later public API design (deferred beyond 1.0)

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

## Deferred investigations (beyond 1.0)

- Continue the dogfood malformed-output corpus from Guyabano:
  - add a minimized golden vector from workflow
    `01a052e4-6a8c-7542-9ed8-6ddb55ce2ebc`, task `TASK-TODOTESTS`, if a future
    privacy-controlled capture preserves the original arguments. The current
    evidence retains only hashes and the unmatched `]` position, so the exact
    payload cannot be reconstructed;
  - optionally select among multiple concatenated documents only when exactly
    one parses and matches the supplied expectation; until then Nuwa safely
    refuses every multi-document input as ambiguous;
  - investigate tolerant recovery that produces a valid JSON tree for the
    wrong tool/stage shape, including empty objects returned for schemas with
    required domain fields;
  - cover truncated nested arrays/objects where syntax recovery succeeds but
    required component relationship collections remain absent;
  - publish bounded, privacy-safe evidence for the winning candidate and its
    before/after schema error counts so hosts can explain why repair was
    accepted or refused;
  - maintain golden cases for malformed, concatenated, repaired-but-mismatched,
    and unambiguously repairable payloads observed during real code-generation
    runs.
- Duplicate-property recovery policy: retain strict failure or adopt
  deterministic last-wins behavior with an explicit correction record.
- Full JSON Schema keyword validation. Nuwa currently validates the structural
  subset needed to make safe repairs; it is not intended to replace a
  dedicated dialect-aware validator.
- Property-based fuzzing and long-running adversarial test corpora.
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
