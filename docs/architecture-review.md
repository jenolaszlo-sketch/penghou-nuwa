# Architecture & quality review — findings

Reviewed: 2026-08, branch `main`, 152 test cases across 16 test files, CI matrix
net8.0/net9.0/net10.0. Read-only audit; no code changes accompany this document.

Scope: the JSON repair pipeline (`JsonRepairPipeline`,
`TolerantJsonRecoveryParser`, `TolerantJsonTokenReader`,
`JsonSchemaExpectation`), the six repair strategies, options/result model, DI
extensions, and the `Extensions.AI` decorator.

## Summary

Nuwa is a focused, well-tested library whose core ideas are sound: an explicit,
deterministic strategy pipeline over a tolerant recovery parser, honest phase
reporting, security-conscious diagnostics, and disciplined cancellation. The
dominant findings are **robustness gaps against adversarial input** (unbounded
speculative search, uncapped lookahead, per-level budget resets) and **silent
output mutation in the AI decorator** — both fixable without changing the
architecture.

## P0 — Correctness / robustness risks

> **Resolved since review** — all five P0 findings are fixed
> (`Harden repair pipeline against unbounded work and data corruption`):
>
> 1. The verbatim strategy now carries one shared `MaxTotalExpansions` budget
>    across the whole recursion and checks cancellation inside candidate loops.
> 2. Single-quote escapes are decoded consistently (`\n` → newline, `\t`,
>    `\r`, `\b`, `\f`, `\0`; unknown escapes keep prior behaviour) instead of
>    silently dropping the backslash.
> 3. Nested double-encoding parses share the parent's remaining correction
>    budget and a depth allowance bounded by enclosing depth, plus a hard
>    `HardMaxDepth = 512` ceiling regardless of configuration.
> 4. The property lookahead scan is capped at 1024 characters like its twin.
> 5. The AI decorator only replaces message text when a schema matched or the
>    repaired output is structurally plausible (object/array root); scalar-only
>    rewrites of prose are suppressed.
>
> Regression coverage: `tests/Penghou.Nuwa.Tests/RepairRobustnessTests.cs`
> (adversarial termination, shared-budget shape, hard depth cap, escape
> decoding) and the scalar-prose decorator test in
> `JsonRepairChatClientTests.cs`.

### 1. Unbounded speculative search in the verbatim-string strategy

`Strategies/PseudoCSharpVerbatimStringRepairStrategy.cs` (`TryRepairFrom`)
recurses into up to `MaxCandidatesPerLiteral = 64` end candidates at up to
`MaxRepairs = 32` depth — a theoretical 64-way branching tree — with no global
work counter and no cancellation checks past method entry. A crafted input with
many `@"` sequences in value position can burn effectively unbounded CPU.

**Opportunity:** plumb a shared step/token budget through `JsonRepairLimits`
and check cancellation inside candidate loops.

### 2. Single-quote escape decoding silently destroys data

`TolerantJsonRecoveryParser.ParseSingleQuotedString` consumes the backslash and
appends the next raw character (`\n` → `n`, `\u0041` → `u0041`), inconsistent
with `SalvageRepairStrategy.AppendConvertedSingleQuoted`, which preserves
escape spelling. The same input recovers to different values depending on which
phase wins.

**Opportunity:** decode escapes exactly like `AppendEscapedCharacter` does (or
preserve spelling consistently in both paths).

### 3. Limits reset on nested schema-guided expansion

`ExpandContainerFromString` constructs a fresh `TolerantJsonRecoveryParser`
with fresh stacks/lists per double-encoding level, so `MaxDepth` and
`MaxCorrections` reset each time. Combined with `JsonRepairLimits.Validate()`
only requiring values `> 0`, a caller-raised `MaxDepth` can drive CLR recursion
into an uncatchable `StackOverflowException`.

**Opportunity:** share one budget/context object across nested parses and cap
`MaxDepth` defensively.

### 4. Uncapped lookahead scan on the property hot path

`LooksLikePropertyAt` walks raw quotes to end-of-input with no bound — unlike
its twin, which got a 1024-character cap — and runs once per property/value
boundary atop a token reader that re-lexes from scratch on every `PeekFrom`.
Adversarial inputs make recovery O(n²)+.

**Opportunity:** apply the same lookahead bound and memoize tokens lazily.

### 5. Silent prose rewrite in the AI decorator

`JsonRepairChatClient.RepairTextAsync` replaces `text.Text` whenever a repair
merely *succeeded* — assistant prose like `"True"` or `"None"` under
`ChatResponseFormatJson` becomes `true`/`null` via Python-literal salvage.
Require structural plausibility (object/array root, or schema `Matched`) before
mutating message content.

## P1 — Architecture & design

6. **Injected-parser seam exists but is unused** — `ITolerantJsonSyntaxTreeParser` is defined and `JsonRepairPipeline` hard-news `TolerantJsonSyntaxTreeParser`. Make it a constructor dependency with a DI default to unlock mocking.
7. **Three near-duplicate phase loops** (text/salvage/node phases each hand-roll try/report/skip bookkeeping, ~150 duplicated lines). Extract one ordered-phase runner parameterized by attempt kind.
8. **Reflection-based configuration** — `JsonRepairOptions` stores `Type`s, forcing a "fewest-constructor" heuristic in `Instantiate` plus a public-constructor constraint. Accepting factories/instances would delete the reflection path.
9. **Schema expectations recomputed per lookup** — `GetProperty`/`GetItem` re-run normalize-on-access per property per node during recovery and both node strategies. Memoize child expectations on first use.
10. **Limits enforced asymmetrically** — input/output gates exist on success but the failure path returns unchecked `RepairedText`; text strategies have no work budgets at all. Route all phases through one limit context.
11. **Per-call schema derivation in the AI decorator** — tool schemas re-normalize on every function call/update. Cache expectations keyed by tool name + schema reference.
12. **`SucceededBy` credit is positional, not causal** — last successful report wins, which can misattribute when a node strategy "succeeded" unchanged. Return the strategy that actually changed the accepted artifact (or document the approximation).

## P2 — Performance & allocations

13. **Multiple JSON materializations per successful repair** — pipeline does `ToJsonString()` + `JsonDocument.Parse`, then the result constructor re-validates via `JsonNode.Parse` ×2 + two `DeepEquals` even on the trusted internal path. Skip validation internally; keep it on the public factory.
14. **No token memoization** — every peek re-runs lexing from the offset; quote heuristics pay repeated lexing. A lazily built token array makes lookahead O(1).
15. **O(n·k) string rebuild loops** — template-literal replacement rebuilds per literal; verbatim candidates concatenate prefix+json+suffix per branch. Single-pass span/StringBuilder rewrite removes quadratic copying.
16. **Up to three full validity parses inside salvage** — reuse one parse result.
17. **Full-tree `DeepClone` before NotApplicable decisions** in both schema-guided node strategies — dry-run detect first, clone only on change.
18. **Log arguments computed unconditionally** — wrap `Summarize(...)` calls in level checks.

(Positive: zero `Regex` usage anywhere in `src\` — no catastrophic-backtracking regex risk exists.)

## P3 — Usability, polish, tests

19. **Dead `StrategyReport.Repaired`** — always null by design and regression-tested as such. Remove it or rename to make "payloads are never echoed" explicit.
20. **Asymmetric fluent API** — `InsertTextRepairAfter`/`InsertNodeRepairAfter` exist but salvage has only Add/Remove/Clear; add `InsertSalvageRepairAfter`.
21. **DI extension requires `ILogger<JsonRepairPipeline>` implicitly** — fails obscurely without `AddLogging()`; default to `NullLoggerFactory` so `AddJsonRepair` is self-sufficient.
22. **Markdown closing fence must be the final line** — trailing prose after ```` ``` ```` keeps the fence in the body; scan for the first valid closing fence line.
23. **`JsonRepairLimitException` carries no structure** — add limit kind/configured-vs-actual so callers can react programmatically; strategy notes are likewise free-text.
24. **Invalid schema JSON silently null** — `FromSchemaJson` swallows `JsonException` and downgrades schema-guided repair without any diagnostic. Surface it to shape status/notifications.
25. **Missing `ConfigureAwait(false)`** in pipeline awaits — inconsistent with the Extensions.AI project's discipline.
26. **Duplicate-key abort vs STJ last-wins** — a repeated property fails whole-object recovery; consider last-wins plus a recorded correction.
27. **Test gaps** — behavioral coverage missing for `MaxCorrections`, failure-path `MaxOutputLength`, mid-recovery cancellation; AI tests omit both expectation resolvers and `RepairJsonLookingTextWithoutResponseFormat`; no fuzz/property-based tests or benchmarks for a parser whose whole job is hostile input. (Escape inconsistency #2, nested-budget reset #3, and the prose-rewrite case #5 now have regression tests.)

## Release engineering

- CI already runs a three-TFM build/test matrix with coverage — good.
- **API surface is snapshotted by test** (`PublicApiContractTests`) rather than the `PublicApiAnalyzers` baseline files used in sibling repos; either approach works, but the file-based baseline gives review-visible diffs on API change.
- No `.editorconfig`; format verify not in CI.
- Package validation enabled — good.

## Done well (preserve)

1. Explicit, validated strategy ordering with duplicate detection and fail-fast construction — fully deterministic and reproducible.
2. Clean phase separation with honest economics: lossless-tolerant recovery strictly precedes documented-lossy salvage; node repair requires a schema; every configured strategy reports exactly once in configuration order.
3. Disciplined cancellation and exception containment: `CheckWork()` woven through every loop; `OperationCanceledException`/limit exceptions deliberately rethrown; strategy faults degrade to failed reports.
4. Security-conscious diagnostics: payload text never reaches logs or reports (regression-tested), input length gated up front, untrusted-input defects convert to failed outcomes.
5. Strong hygiene: nullable + warnings-as-errors, sealed-by-default types, records for diagnostics/hot tokens, multi-targeting, an API-surface snapshot test, and genuinely tricky behaviors locked down by targeted tests.

## Suggested priority

1. ~~**P0 robustness**: shared work/cancellation budget across speculative strategies (#1), consistent single-quote escapes (#2), shared nested-parse budget + defensive depth cap (#3).~~ **Done.**
2. ~~**AI decorator guard**: require structural plausibility before mutating message text (#5).~~ **Done.**
3. **Hot-path memoization**: property lookahead is now capped (#4); token memoization remains (#14).
4. **Design cleanup**: injected parser seam, unified phase runner, factory-based options, expectation memoization (#6–#9).
5. **Ergonomics/tests**: limit exception structure, schema-parse diagnostics, DI self-sufficiency, fuzz tests, missing behavioral tests (#19–#27).
