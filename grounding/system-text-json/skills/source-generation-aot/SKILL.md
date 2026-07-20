---
name: source-generation-aot
version: 10.0.0
description: >-
  Use when System.Text.Json runs under Native AOT or trimming (PublishAot / PublishTrimmed), or when
  you want the faster, reflection-free serialization path — i.e. a JsonSerializerContext with
  [JsonSerializable]. Reflection-based JsonSerializer compiles but THROWS at run time under AOT; the
  source generator is the only supported path. Requires the base `system-text-json` skill.
---

# Source generation & Native AOT

Reflection-based `JsonSerializer.Serialize<T>(value)` / `Deserialize<T>(string)` is **disabled under
Native AOT (`PublishAot=true`) and trimming**. It still **compiles** (only an `IL3050`/`IL2026`
warning) but **throws `InvalidOperationException` at run time**: *"Reflection-based serialization has
been disabled…"*. Do **not** "fix" it by removing `PublishAot` or setting
`JsonSerializerIsReflectionEnabledByDefault=true` — that reintroduces the AOT break. Use the source
generator.

## The pattern

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

[JsonSerializable(typeof(List<Advisory>))]   // one entry per ROOT type you (de)serialize
[JsonSerializable(typeof(Advisory))]
internal partial class AppJsonContext : JsonSerializerContext { }   // MUST be partial

// Deserialize / serialize through the generated, strongly-typed property:
var items = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListAdvisory);
string s  = JsonSerializer.Serialize(items, AppJsonContext.Default.ListAdvisory);
```

- The generated property is named after the type: `List<Advisory>` → `ListAdvisory`, `Advisory` →
  `Advisory`. Pass `Context.Default.<TypeName>` to the `JsonTypeInfo<T>` overload.
- Alternatively wire it into an options object:
  `var options = new JsonSerializerOptions { TypeInfoResolver = AppJsonContext.Default };` then call
  the normal `Serialize/Deserialize<T>(value, options)` overloads.
- Register **every root type** you pass to `Serialize`/`Deserialize`. Nested/property types are
  discovered automatically; a missing ROOT type is a build-time source-gen error or a run-time throw.

## Configure the context with `[JsonSourceGenerationOptions]`

Options that would go on a `JsonSerializerOptions` instance move onto the context via attribute —
this is how a real AOT tool sets naming/nulls/enum handling (matches how CLIs shape their JSON output):

```csharp
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,   // or CamelCase
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    UseStringEnumConverter = true)]                                // .NET 9+: enums as strings, AOT-safe
[JsonSerializable(typeof(Report))]
internal partial class ReportContext : JsonSerializerContext { }
```

- **Separate contexts for separate output shapes.** A tool that emits both indented "pretty" JSON and
  compact JSONL typically declares two contexts (one `WriteIndented = true`, one not; compact modes
  often use `WhenWritingDefault`). Options are baked into the context at generation time, so you pick
  the shape by choosing the context.
- **`UseStringEnumConverter = true`** is the AOT-safe way to get string enums — the reflection-based
  `new JsonStringEnumConverter()` added to an options list is NOT AOT-safe for source-gen contexts.

## Gotchas

- The context class **must be `partial`** and typically `internal`/`private`. A non-partial context
  won't compile (the generator can't extend it).
- Under AOT, do **not** add converters that rely on reflection; prefer attribute-driven configuration.
- `JsonSerializerContext` metadata is generated at build time — after changing types, rebuild.
