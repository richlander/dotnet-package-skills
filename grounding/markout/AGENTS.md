---
name: markout
description: >-
  Markout is a source-generated .NET serializer that projects objects into Markdown (and
  ANSI terminal, plain text, pretty tables, TSV). Use it whenever a CLI/tool needs
  structured human- or agent-readable output instead of hand-built strings. It LOOKS like
  System.Text.Json source generation but its rules differ: there is NO reflection fallback,
  so every Serialize call requires a MarkoutSerializerContext — you must annotate each model
  with [MarkoutSerializable] and register it on a partial context with [MarkoutContext(typeof(T))],
  then call MarkoutSerializer.Serialize(value, Console.Out, MyContext.Default). The attribute
  vocabulary is Markout-specific ([MarkoutSection], TitleProperty, [MarkoutIgnoreInTable]),
  not the System.Text.Json one.
---

# Markout — produce Markdown/structured output from objects

Source-generated serializer. Default output is Markdown. Package `Markout` (includes the
source generator; no extra package needed).

## The required pattern (3 parts — all mandatory)

```csharp
using Markout;

// 1. Annotate every model type. List<T> -> table, scalar -> field.
[MarkoutSerializable(TitleProperty = nameof(Title))]   // TitleProperty -> the H1 heading
public class Report
{
    public string Title { get; set; } = "";
    public int Count { get; set; }
    [MarkoutSection(Name = "Items")]                    // -> "## Items" heading
    public List<Row>? Items { get; set; }              // List<T> -> a table
}

[MarkoutSerializable]
public class Row { public string Name { get; set; } = ""; public string Value { get; set; } = ""; }

// 2. Register EVERY type on a partial context (the source generator fills it in).
[MarkoutContext(typeof(Report))]
[MarkoutContext(typeof(Row))]
public partial class ReportContext : MarkoutSerializerContext { }

// 3. Serialize THROUGH the context.
MarkoutSerializer.Serialize(report, Console.Out, ReportContext.Default);
```

## Gotchas (where intuition from System.Text.Json is wrong)

- **No reflection fallback.** There is no `Serialize(obj)` overload. EVERY `MarkoutSerializer.Serialize`
  overload takes a `MarkoutSerializerContext` (or `MarkoutTypeInfo<T>`). Skipping the context does
  not compile. This is the #1 mistake.
- **Register every type.** A model used in output but missing `[MarkoutSerializable]` +
  `[MarkoutContext(typeof(T))]` will not serialize. The context class MUST be `partial`.
- **Attributes are Markout's, not System.Text.Json's:** `[MarkoutSerializable]` (not
  `[JsonSerializable]`), `[MarkoutContext]`, `[MarkoutSection(Name=...)]`, `TitleProperty`,
  `[MarkoutPropertyName]`, `[MarkoutIgnore]`. Do not use `Json*` attributes.
- **Rendering is driven by type, not markup:** `List<T>` -> table; scalar (`string`/`int`/`bool`) ->
  a `Field | Value` row; `[MarkoutSection(Name="X")]` -> a `## X` heading above the property.
- **Put `[MarkoutIgnoreInTable]` on non-tabular list properties** (`List<Metric>`, `List<Breakdown>`,
  `List<TreeNode>`, `List<Description>`, `Callout`) or they get mistreated as table columns.

## Built-in shape types (use as model properties for rich output)
`Metric` (bar chart), `Breakdown`/`Segment` (stacked bar), `Callout` (alert), `TreeNode`
(hierarchy), `Description` (term + text), `CodeSection` (code block). e.g. `new Metric("Build", 4.2)`.

## Other output formats (still Markdown by default)
Pass a formatter to change output: `new MarkdownFormatter()` (default), `PlainTextFormatter`,
`UnicodeFormatter`, `TableFormatter` (compact/TSV/JSONL via `MarkoutWriterOptions.TableMode`).
e.g. `MarkoutSerializer.Serialize(report, Console.Out, new TableFormatter(), ReportContext.Default);`
