# Penghou.Nuwa

A repair and recovery pipeline for malformed JSON and structured output
produced by language models.

Language models regularly emit JSON that does not parse: unescaped quotes
inside file contents, truncated tool-call arguments, Markdown fences, template
literals, single quotes, Python literals, and missing brackets. Penghou.Nuwa
recovers a `System.Text.Json` document from that output instead of failing.

## Install

```
dotnet add package Penghou.Nuwa
```

Targets `net8.0`, `net9.0`, and `net10.0`.

## Quick start

```csharp
using Penghou.Nuwa;
using Penghou.Nuwa.Extensions;

var services = new ServiceCollection();
services.AddLogging();
services.AddJsonRepair();

using var provider = services.BuildServiceProvider();
var pipeline = provider.GetRequiredService<IJsonRepairPipeline>();

using var result = pipeline.Repair(
    """{"files":[{"path":"Program.cs","content": using System; var message = "hello"; }]}""");

if (result.Succeeded)
{
    var root = result.Document.RootElement;
    Console.WriteLine(result.WasRepaired);   // true
}
```

The pipeline is layered. Each layer is optional and composable, so you can
call any stage directly:

```csharp
// Schema-guided recovery is the most powerful path. Pass the JSON Schema of
// the expected shape (e.g. the tool arguments schema) to drive repairs.
var expectation = JsonSchemaExpectation.FromSchemaJson(schemaJson);
var parse = new TolerantJsonSyntaxTreeParser().Parse(input, expectation);
```

## How it works

Repair runs through up to four stages, stopping as soon as the input parses:

1. **Text-repair strategies** — targeted rewrites of malformed *text* that
   cannot be a tree yet: Markdown JSON fences, C# verbatim strings
   (`@"..."`), and JavaScript template literals (`` `...` ``).
2. **Tolerant syntax-tree recovery** — a handwritten parser that builds a
   `JsonNode` while using container state, bounded lookahead, and the schema
   at the current path to recover punctuation (missing commas, closers,
   quotes, unquoted keys) without inventing semantic values.
3. **Schema-guided node strategies** — fixes that survive as *valid but
   wrong-shaped* JSON: expanding a field that arrived as a JSON string back
   into an array or object, and removing optional `null` values that a strict
   schema rejects.
4. **Self-contained text salvage** — a final pass that strips comments,
   normalizes Python literals, converts single-quoted strings, quotes
   unquoted keys, and completes unclosed containers. No external JSON-repair
   dependency.

Every result records `Attempts`, a per-stage audit of what ran and whether it
applied:

```csharp
foreach (var (stage, outcome) in result.Attempts)
    Console.WriteLine($"{stage}: {outcome}");
```

## Customization

Register your own strategies with the same `ITextRepairStrategy` /
`INodeRepairStrategy` contracts, or compose `JsonRepairPipeline` directly
with the order you want:

```csharp
var pipeline = new JsonRepairPipeline(
    preprocessingStrategies,
    tolerantParser,
    nodeRepairStrategies,
    logger);
```

## Feedback and attribution

Penghou.Nuwa began as the repair pipeline of the [Solo] autonomous code
generation project. Its recovery parser is schema-aware and empirically tuned
against real model output (see the model compatibility notes in Solo).

[Solo]: https://github.com/your-account/solo
