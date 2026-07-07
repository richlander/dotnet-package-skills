---
name: markout
description: "Source-generated .NET serializer that renders annotated objects as Markdown. Reach for it when a CLI or tool needs structured, readable output instead of hand-built strings. It looks like System.Text.Json source generation but the rules differ — there is NO reflection fallback, so it needs a generated MarkoutSerializerContext and Markout-specific attributes. Pinned to Markout 0.13.7."
---

# Markout (0.13.7) — source-generated Markdown serializer

Annotate plain C# classes; a source generator renders them to Markdown. Looks like
`System.Text.Json` source-gen but the attributes and rules are Markout's own.

## The required pattern (3 parts — all mandatory)

1. `[MarkoutSerializable]` on the model class (`partial` not required on the model).
2. `[MarkoutContext(typeof(TModel))]` on a **`partial`** class deriving `MarkoutSerializerContext`.
3. Serialize through that context: `MarkoutSerializer.Serialize(model, Console.Out, MyContext.Default);`

There is **no** `Serialize(obj)` reflection overload — every overload needs the context. Skipping it
does not compile (the #1 mistake).

```csharp
using Markout;
MarkoutSerializer.Serialize(svc, Console.Out, SvcContext.Default);

[MarkoutSerializable(TitleProperty = nameof(Name))]   // -> the Name value renders as the H1
public partial class Svc { public required string Name { get; init; } public required int Replicas { get; init; } }

[MarkoutContext(typeof(Svc))] public partial class SvcContext : MarkoutSerializerContext { }
```

## Rendering is driven by type, not markup

- Scalar (`string`/`int`/`bool`) -> a `| Field | Value |` table row.
- `[MarkoutSerializable(TitleProperty = nameof(X))]` -> the `X` value is the `#` H1 (excluded from the table).
- `[MarkoutSection(Name = "X")]` on a property -> a `## X` heading above it.
- `List<T>` -> a Markdown table.

## TreeNode (indented hierarchy) — mind the constructor

`TreeNode` renders an indented tree. In 0.13.7 the constructors are:

```csharp
TreeNode(string text, string? badge = null)
TreeNode(string text, string? badge, ReadOnlySpan<TreeNode> children)
```

**Children is the THIRD argument, after `badge`.** The natural `new TreeNode("myapp", [child])` does
NOT compile — the second positional arg is the `badge` string, not children. Pass `badge` (or `null`)
first, then the children collection:

```csharp
new TreeNode("myapp", null, [
    new TreeNode("Newtonsoft.Json", "pkg"),
    new TreeNode("Serilog", "pkg", [ new TreeNode("Serilog.Sinks.Console") ]),
])
```

The ctor takes a `ReadOnlySpan<TreeNode>` (so a `[ ... ]` collection expression works), while the
`Children` property is `List<TreeNode>?` — don't confuse the two.
