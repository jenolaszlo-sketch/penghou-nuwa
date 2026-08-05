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

or pin the version explicitly:

```xml
<PackageReference Include="Penghou.Nuwa" Version="0.2.0" />
```

Targets `net8.0`, `net9.0`, and `net10.0`.

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
}
```

## How it works

Repair runs through up to four stages:

1. **Text-repair strategies** — targeted rewrites of malformed *text* that
   cannot be a tree yet: Markdown JSON fences, C# verbatim strings
   (`@"..."`), and JavaScript template literals (`` `...` ``). Stops as soon
   as the text parses.
2. **Tolerant syntax-tree recovery** — a handwritten parser that builds a
   `JsonNode` while using container state, bounded lookahead, and the schema
   at the current path to recover punctuation (missing commas, closers,
   quotes, unquoted keys) without inventing semantic values.
3. **Self-contained text salvage** — a lossy fallback that runs only when
   recovery fails: strips comments, normalizes Python literals, converts
   single-quoted strings, quotes unquoted keys, and completes unclosed
   containers. No external JSON-repair dependency.
4. **Schema-guided node strategies** — fixes that survive as *valid but
   wrong-shaped* JSON: expanding a field that arrived as a JSON string back
   into an array or object, and removing optional `null` values that a strict
   schema rejects.

Every result carries a per-strategy audit. Each configured strategy is
reported exactly once, in order, with its status and an optional note:

```csharp
foreach (var report in result.TextRepairs)
{
    Console.WriteLine(
        $"{report.Name}: {report.Status}" +
        (report.Note is null ? "" : $" ({report.Note})"));
}

var winner = result.SucceededBy;   // strategy that produced the final document, if any
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

## Feedback and attribution

Penghou.Nuwa began as the repair pipeline of the Solo autonomous code
generation project. Its recovery parser is schema-aware and empirically tuned
against real model output (see the model compatibility notes in Solo).
