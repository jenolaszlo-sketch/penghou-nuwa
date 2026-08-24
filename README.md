# Penghou.Nuwa

[![NuGet](https://img.shields.io/nuget/v/Penghou.Nuwa)](https://www.nuget.org/packages/Penghou.Nuwa)
[![CI](https://github.com/jenolaszlo-sketch/penghou-nuwa/actions/workflows/ci.yml/badge.svg)](https://github.com/jenolaszlo-sketch/penghou-nuwa/actions/workflows/ci.yml)
[![License](https://img.shields.io/github/license/jenolaszlo-sketch/penghou-nuwa)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

Penghou.Nuwa is a schema-aware **JSON repair and structured-output recovery**
pipeline for .NET, designed around malformed output observed from real
language models. It does more than fix text that fails to parse: it recovers
*valid-but-wrong-shaped* JSON, double-serialized fields, schema mismatches,
optional-null rejections, Markdown fences, truncated structures, and malformed
tool arguments into a `System.Text.Json` document — instead of failing.

That includes the classic failure modes — unescaped quotes inside file
contents, truncated tool-call arguments, Markdown fences, template literals,
single quotes, Python literals, and missing brackets — as well as valid JSON
that a strict consumer rejects.

## Install

```
dotnet add package Penghou.Nuwa
```

or pin the version explicitly:

```xml
<PackageReference Include="Penghou.Nuwa" Version="0.6.0" />
```

Targets `net8.0`, `net9.0`, and `net10.0`. For Microsoft.Extensions.AI
pipeline integration (OpenAI, Anthropic, Azure OpenAI, Ollama, Semantic
Kernel), also install `Penghou.Nuwa.Extensions.AI` — see the section below.

## Quick start

The one-shot helper builds a default pipeline for each call — fine for
occasional use:

```csharp
using Penghou.Nuwa;

using var result = await JsonRepair.RepairAsync(
    """{"files":[{"path":"Program.cs","content": using System; var message = "hello"; }]}""");

if (result.Succeeded)
{
    var root = result.GetRootOrThrow();
    Console.WriteLine(result.RepairedText);
    Console.WriteLine(result.WasRepaired);   // true
}
```

For repeated calls, build a pipeline once and reuse it:

```csharp
var pipeline = JsonRepairPipeline.Create();

// Or with configuration:
var pipeline = JsonRepairPipeline.Create(options =>
{
    options.RemoveTextRepair<PseudoCSharpVerbatimStringRepairStrategy>();
    options.DisableSalvageFallback();
    options.Limits = new JsonRepairLimits
    {
        MaxInputLength = 1_000_000,
        MaxOutputLength = 2_000_000,
        MaxDepth = 128,
        MaxCorrections = 10_000
    };
});
```

### Dependency injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using Penghou.Nuwa;
using Penghou.Nuwa.Extensions;

var services = new ServiceCollection();
services.AddLogging();
services.AddJsonRepair(); // or AddJsonRepair(options => ...)

using var provider = services.BuildServiceProvider();
var pipeline = provider.GetRequiredService<IJsonRepairPipeline>();

using var result = await pipeline.RepairAsync(input);

if (result.Succeeded)
{
    Console.WriteLine(result.GetRepairedTextOrThrow());
}
```

### Schema-guided repair

Pass the JSON Schema of the expected shape (for example the tool-arguments
schema) to enable schema-aware recovery and the node-repair phase:

```csharp
var expectation = JsonSchemaExpectation.FromSchemaJson(schemaJson);

using var result = await pipeline.RepairAsync(input, expectation);

if (result.Succeeded)
{
    var files = result.GetRootOrThrow()["files"];

    if (result.ShapeStatus == JsonRepairShapeStatus.Mismatched)
        Console.WriteLine(string.Join(Environment.NewLine, result.ShapeErrors));
}
```

No JSON Schema handy? Derive the expectation directly from the CLR type the
payload should deserialize into — property kinds (string/number/boolean/
object/array) and required flags are read via reflection, so repair gets the
same shape guidance with zero schema plumbing:

```csharp
var expectation = JsonSchemaExpectation.FromType<FilePatchArguments>();

// Match how the payload is actually serialized (e.g. camelCase):
var camelCase = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var expectation = JsonSchemaExpectation.FromType<FilePatchArguments>(camelCase);
```

`FromType` honors `[JsonPropertyName]`, `[JsonIgnore]`, and `[JsonRequired]`
attributes, maps enums to strings, and recurses into nested objects, arrays,
and dictionaries. Schema-guided repair is best-effort when no expectation is
passed at all — text repair and tolerant recovery still run.

Schema handling is **repair-only**: `FromSchemaJson`/`FromSchemaNode` extract
the shape facts the recovery pipeline needs (types, properties, required,
items, nullability) and normalize common constructs — local `$ref`/`$defs`
pointers are inlined, `oneOf`/`anyOf`/`allOf`/`enum` are reduced to a
canonical type form, and recursive references are cut so repair terminates.
This is intentionally **not** a JSON Schema dialect converter or validator for
model-facing output; do not feed a `JsonSchemaExpectation.Schema` back to an
LLM or use it as the authoritative contract for a remote API.

`Succeeded` means Nuwa recovered syntactically valid JSON. When an expectation
is supplied, `ShapeStatus` separately reports whether the result matches the
types and required structural shape used during recovery. Use a dedicated JSON
Schema validator when authoritative dialect validation is required.

## New in 0.6: truncation salvage, payload extraction, coercions, confidence, streaming

### Truncation-aware partial salvage

Generations cut off mid-payload no longer fail outright. Every complete
property or element is kept and the torn tail is dropped (recorded as a
correction):

```csharp
using var result = await pipeline.RepairAsync("""{"count": 42, "note": """);
// Succeeded == true; Root == {"count": 42}
// result.TolerantRecovery.Corrections notes the dropped property.
```

Disable with `options.AllowTruncationSalvage = false` when partial payloads
are worse than failures.

### Payload extraction

Models routinely wrap JSON in prose, XML elements, or emit several objects
back to back. Default text strategies now handle these before parsing:

- `"Here is the JSON: {...}"` — prose prefix/suffix stripping
  (`prose-wrapper-extraction`)
- `<answer>{...}</answer>` and CDATA bodies (`xml-wrapped-extraction`)
- `{"first": 1}{"second": 2}` — concatenated values keep the first object
  (`concatenated-json`)
- Fences tagged with arbitrary payload labels (```` ```tool_call ````) are
  unwrapped; programming-language fences (`csharp`, `python`, ...) remain
  untouched.

Unicode delimiters are normalized too: curly quotes used as string bounds,
full-width CJK brackets/colons/commas, BOM and zero-width characters
(`unicode-delimiter-normalization`).

### Schema-guided coercions (opt-in)

When the wire schema is authoritative, typed coercion can rescue common
near-misses:

```csharp
var pipeline = JsonRepairPipeline.Create(options =>
    options.EnableSchemaCoercions());

// {"count": "42"}      -> {"count": 42}        string-to-number
// {"flag": "True"}     -> {"flag": true}       string-to-boolean
// {"tags": "red"}      -> {"tags": ["red"]}    item-compatible array wrap
// {"status": "Actve"}  -> {"status": "Active"} enum fuzzy match (distance <= 2)
// extra properties     -> removed              when additionalProperties:false
```

Off by default; repairs stay structurally conservative unless you opt in.
Scalar-to-array wrapping validates the proposed element against the complete
`items` schema first. A deterministic string-to-number or string-to-boolean
conversion may be applied atomically; incompatible values remain unchanged.

Missing required properties can also be reconciled from a uniquely matching
unknown property name:

```csharp
var pipeline = JsonRepairPipeline.Create(options =>
    options.EnableRequiredPropertyReconciliation());

// {"qurey":"weather"} -> {"query":"weather"}
```

This policy is opt-in and conservative: the target must be required and
missing, the source must be unknown, the name match must be uniquely best,
and the value must satisfy the target schema directly or through a certified
lossless coercion. Existing properties are never overwritten, and ambiguous
union branches are left unchanged.

For contracts with distinctive nested shapes or enum values, a broader policy
can reconcile unrelated property names:

```csharp
var pipeline = JsonRepairPipeline.Create(options =>
    options.EnableStructuralPropertyReconciliation());
```

This remains separately opt-in. It requires exactly one compatible missing
required target, refuses primitive type compatibility by itself, and applies
the mapping only when supported schema-shape errors decrease. Repair reports
include privacy-safe evidence such as the path, reason, uniqueness decision,
and before/after error counts; property values are not included.

### Repair confidence

Every result carries a deterministic heuristic 0–1 score derived from its own
diagnostics — unchanged valid JSON scores 1.0, each mutation reduces it, and
lossy salvage or a shape mismatch reduce it sharply:

```csharp
if (result.IsConfident(0.8))
{
    // gate downstream execution like any constrained extractor would
}
```

`JsonRepairNotification.Confidence` surfaces the same value to the
Microsoft.Extensions.AI middleware audit.

### Streaming repair

Repair a payload as chunks arrive. Stable-prefix deltas give consumers a live
preview; one completed event carries the authoritative result:

```csharp
await foreach (var streamEvent in pipeline.RepairStreamAsync(modelChunks))
{
    if (streamEvent is JsonRepairStreamDelta delta)
        RenderPreview(delta.Offset, delta.Text);   // verbatim input slice
    else if (streamEvent is JsonRepairStreamCompleted completed)
        Use(completed.Result);                      // full repair outcome
}
```

Deltas never appear inside open strings and keep a holdback margin from the
tail so pending punctuation repairs cannot invalidate them — treat them as
preview only.

## Microsoft.Extensions.AI integration

The companion package **`Penghou.Nuwa.Extensions.AI`** drops Nuwa repair into
any Microsoft.Extensions.AI chat-client pipeline — OpenAI, Anthropic, Azure
OpenAI, Ollama, Semantic Kernel, and anything else that exposes an
`IChatClient`. It is a small middleware that repairs JSON after the connector
has done its work, so you get the fixes without forking provider SDKs.

```xml
<PackageReference Include="Penghou.Nuwa.Extensions.AI" Version="0.6.0" />
```

Two things get repaired, transparently:

- **Tool-call arguments.** The OpenAI/Anthropic connector already parses
  arguments eagerly. When the model emits *valid-but-wrong-shaped* JSON (a
  field double-serialized as a string, an optional `null` a strict schema
  rejects, a wrong property kind), the middleware re-serializes the parsed
  arguments and runs the schema-guided node-repair phase against the matching
  tool's schema, then swaps in the corrected arguments before your tool
  invocation code sees them. When a provider preserves the raw arguments text
  on the call content, that text is repaired too.
- **Structured-output text.** Assistant `TextContent` is repaired when
  `ChatOptions.ResponseFormat` requests JSON, so truncated or fenced JSON is
  recovered before it reaches your deserializer. When the response format has
  a schema, that schema guides repair. JSON-looking ordinary chat is unchanged
  unless `RepairJsonLookingTextWithoutResponseFormat` is enabled.

### Quick start

Wrap any `IChatClient`. The `UseJsonRepair` extension is available both on
`IChatClient` and on the pipeline builder:

```csharp
using Microsoft.Extensions.AI;
using OpenAI;
using Penghou.Nuwa.Extensions.AI;

OpenAIClient openAi = new(apiKey);
IChatClient inner = openAi.AsIChatClient("gpt-4o");

// Wrap directly...
IChatClient client = inner.UseJsonRepair();

// ...or as a pipeline stage, so repair runs on its way through:
IChatClient pipeline = new ChatClientBuilder(inner)
    .UseFunctionInvocation()
    .UseJsonRepair()
    .Build();

ChatResponse response = await pipeline.GetResponseAsync(
    "List the files in src/ and print them as JSON.",
    new ChatOptions { Tools = [tool], ResponseFormat = new ChatResponseFormatJson() });
```

With Anthropic, wrap the same way:

```csharp
AnthropicClient anthropic = new(apiKey);
IChatClient client = anthropic.AsIChatClient("claude-3-5-sonnet-latest").UseJsonRepair();
```

Tool schemas are read from `ChatOptions.Tools` automatically — the middleware
matches a function call to the `AIFunctionDeclaration` with the same name and
uses its `JsonSchema`. You can override that with
`JsonRepairChatClientOptions.FunctionCallExpectationResolver`, or point repair
at a CLR type with `JsonSchemaExpectation.FromType<T>()`:

```csharp
IChatClient client = inner.UseJsonRepair(
    new JsonRepairChatClientOptions
    {
        FunctionCallExpectationResolver = call =>
            JsonSchemaExpectation.FromType<FilePatchArguments>()
    });
```

Configure the underlying Nuwa pipeline just like the core package:

```csharp
IChatClient client = inner.UseJsonRepair(options =>
{
    options.RemoveTextRepair<PseudoCSharpVerbatimStringRepairStrategy>();
    options.DisableSalvageFallback();
});
```

### Notes

- Streaming responses repair *completed* tool-call arguments (the accumulated
  update a connector emits at the end of a call). Streaming text is not
  repaired because fragments split JSON mid-token; use the non-streaming path
  for structured-output text.
- The middleware only rewrites JSON that is actually repaired
  (`JsonRepairResult.WasRepaired`) — already-valid arguments and prose text
  pass through untouched.
- Set `JsonRepairChatClientOptions.RepairCompleted` to surface the compact
  repair audit to telemetry or a UI. Payload text is not included.
- Genuinely malformed tool arguments are recoverable only when the provider
  keeps the raw text on the call content; OpenAI's connector does not, so for
  that specific case the existing parse-failure behavior is preserved.

## How it works

Repair runs through up to four stages:

1. **Text-repair strategies** — targeted rewrites of malformed *text* that
   cannot be a tree yet: Markdown JSON fences (including arbitrary payload
   tags), prose wrappers, XML/CDATA elements, concatenated values, Unicode
   delimiter normalization, C# verbatim strings (`@"..."`), and JavaScript
   template literals (`` `...` ``). Stops as soon as the text parses.
2. **Tolerant syntax-tree recovery** — a handwritten parser that builds a
   `JsonNode` while using container state, bounded lookahead, and the schema
   at the current path to recover punctuation (missing commas, closers,
   quotes, unquoted keys) without inventing semantic values. Truncated
   generations are salvaged by keeping every completed property/element.
3. **Self-contained text salvage** — a lossy fallback that runs only when
   recovery fails: strips comments, normalizes Python literals, converts
   single-quoted strings, quotes unquoted keys, and completes unclosed
   containers. No external JSON-repair dependency.
4. **Schema-guided node strategies** — fixes that survive as *valid but
   wrong-shaped* JSON: expanding a field that arrived as a JSON string back
   into an array or object, removing optional `null` values that a strict
   schema rejects, and — when coercions are enabled — array wrapping,
   string-to-number/boolean conversion, enum fuzzy matching, and
   unknown-property pruning for strict contracts.

Every result carries a per-strategy audit. Each configured strategy is
reported exactly once, in order, with its status and an optional note:

```csharp
var original = result.OriginalText;   // the exact input you passed in
var repaired = result.RepairedText;   // best-effort output (valid JSON when Succeeded)

foreach (var report in result.TextRepairs)
{
    Console.WriteLine(
        $"{report.Name}: {report.Status}" +
        (report.Note is null ? "" : $" ({report.Note})"));
}

var winner = result.SucceededBy;   // strategy that produced the final document, if any
var recovery = result.TolerantRecovery; // token-level corrections, when tolerant parsing ran
```

### Resource management and cancellation

`JsonRepairResult` wraps a `JsonDocument` and is `IDisposable` — dispose it to
free the underlying buffer. `Root` and `RepairedText` are independent of the
document and stay valid after disposal:

```csharp
using var result = await pipeline.RepairAsync(input);
// result.Root / result.RepairedText remain usable after the using block.
```

Repair is cooperative and accepts a `CancellationToken`. For a hard timeout,
cancel with a linked token:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

using var result = await pipeline.RepairAsync(
    input,
    expectation,
    cts.Token);
```

## Customization

Configure the strategy lists with `JsonRepairOptions`, through either
`AddJsonRepair` or `JsonRepairPipeline.Create`:

```csharp
services.AddJsonRepair(options =>
{
    // Insert a custom strategy after a default one.
    options.InsertTextRepairAfter<MarkdownJsonFenceRepairStrategy, MyFenceStrategy>();

    // Turn off the lossy fallback phase entirely.
    options.DisableSalvageFallback();

    // Replace the node strategies.
    options.ClearNodeRepairs();
    options.AddNodeRepair<MyNodeStrategy>();
});
```

Implement the `ITextRepair` / `INodeRepair` contracts. Return
`RepairOutcome.NotApplicable` to decline, `Repaired` with the repaired
text/tree to apply, or `Failed`. `Note` carries optional diagnostic detail:

```csharp
public sealed class MyFenceStrategy : ITextRepair
{
    public string Name => "my-fence";

    public ValueTask<TextRepairAttempt> RepairAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        if (!input.StartsWith("[BEGIN]"))
        {
            return new(new TextRepairAttempt(
                RepairOutcome.NotApplicable,
                Repaired: null));
        }

        return new(new TextRepairAttempt(
            RepairOutcome.Repaired,
            input.Replace("[BEGIN]", "").Replace("[END]", "")));
    }
}
```

Strategies can depend on injected services (including `ILogger<T>`); register
them by type through `JsonRepairOptions` and they are resolved from the
container.

Node strategies run against the parsed tree and use the expectation to detect
*valid but wrong-shaped* JSON. Return the replacement node with
`RepairOutcome.Repaired`:

```csharp
public sealed class MyNodeStrategy : INodeRepair
{
    public string Name => "my-node";

    public ValueTask<NodeRepairAttempt> RepairAsync(
        JsonNode node,
        JsonSchemaExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        if (node["files"] is JsonValue value &&
            value.TryGetValue<string>(out var text))
        {
            var array = JsonNode.Parse(text) as JsonArray;

            if (array is not null)
            {
                node["files"] = array;
                return new(new NodeRepairAttempt(
                    RepairOutcome.Repaired,
                    node));
            }
        }

        return new(new NodeRepairAttempt(
            RepairOutcome.NotApplicable,
            null));
    }
}
```

Strategies may be registered by type, instance, or factory. Instance and
factory registration avoids reflection and supports dependencies captured by
the host while preserving explicit strategy order:

```csharp
var pipeline = JsonRepairPipeline.Create(options =>
{
    options.AddNodeRepair(new MyNodeStrategy());
});

// A factory is useful when construction captures host-owned dependencies:
var otherPipeline = JsonRepairPipeline.Create(options =>
    options.AddNodeRepair(() => new MyNodeStrategy()));
```

The existing `AddTextRepair<T>()`, `AddSalvageRepair<T>()`, and
`AddNodeRepair<T>()` APIs remain available. Constructor-based creation is now
the compatibility fallback rather than the only standalone construction path.

## Roadmap

See the [roadmap](ROADMAP.md) for shipped foundations, upcoming usability
work, architecture improvements, and later public API design.

## Feedback and attribution

Penghou.Nuwa began as the repair pipeline of the Solo autonomous code
generation project. Its recovery parser is schema-aware and empirically tuned
against real model output.
