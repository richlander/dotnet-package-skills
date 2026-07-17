---
name: dotnet-10-11-strictness
version: 10.0.0
description: >-
  Use when targeting recent .NET runtimes (8/9/10+) and you need the newer, stricter System.Text.Json
  behavior or APIs a model trained on older docs may not know — the JsonSerializerOptions.Strict
  preset, AllowDuplicateProperties, RespectNullableAnnotations / RespectRequiredConstructorParameters,
  PipeReader overloads, and JsonMarshal. Requires the base `system-text-json` skill.
---

# Newer-runtime strictness & APIs (.NET 8 → 10)

The frontier often defaults to older STJ behavior. These are the recent, verified changes worth
reaching for on modern targets.

## `.NET 10`: the `Strict` preset (opt into best-practice defaults)

```csharp
var options = JsonSerializerOptions.Strict;   // a shared, read-only preset
```

`Strict` bundles the security/correctness-hardening options:

- `JsonUnmappedMemberHandling.Disallow` — JSON with an unknown property **throws** instead of silently
  dropping it.
- `AllowDuplicateProperties = false` — a payload with a repeated property name **throws** (mitigates
  the JSON-interoperability class of vulnerabilities; the default was last-one-wins).
- **Case-sensitive** property binding (preserved).
- `RespectNullableAnnotations = true` and `RespectRequiredConstructorParameters = true` — non-nullable
  members and required ctor parameters are enforced on deserialize.

`Strict` is **read-compatible with `Default`**: anything serialized with `Default` deserializes under
`Strict`. Prefer it for untrusted input rather than hand-assembling the same four options.

## Individual knobs (available without the whole preset)

- `new JsonSerializerOptions { AllowDuplicateProperties = false }` — also on `JsonDocumentOptions`.
- `RespectNullableAnnotations = true` (.NET 9+) — a `null` for a non-nullable reference member throws.
- `RespectRequiredConstructorParameters = true` (.NET 9+) — a missing required ctor parameter throws.
- `JsonUnmappedMemberHandling.Disallow` via `[JsonUnmappedMemberHandling(...)]` on a type or on options.

## `.NET 10`: `PipeReader` support

`JsonSerializer` gained `System.IO.Pipelines.PipeReader` overloads — deserialize directly off a pipe
without an intermediate `Stream` copy (ASP.NET Core request bodies now use this internally):

```csharp
var value = await JsonSerializer.DeserializeAsync<MyType>(pipeReader, options);
await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<Item>(pipeReader, options)) { }
```

## `JsonMarshal` (advanced / high-performance)

`System.Text.Json.JsonMarshal` exposes the raw backing UTF-8 bytes of a `JsonElement`
(`GetRawUtf8Value`) for zero-copy scenarios. Reach for it only in measured hot paths.

## AOT-safe string enums (.NET 9+)

On a source-gen context prefer `[JsonSourceGenerationOptions(UseStringEnumConverter = true)]` (see
source-generation-aot) over adding `new JsonStringEnumConverter()` to an options list — the latter is
not trim/AOT-safe. The generic `JsonStringEnumConverter<TEnum>` (.NET 8+) is the AOT-friendly
per-enum form.

> Reserved-metadata note: a member whose serialized name collides with a metadata property
> (`$type` / `$id` / `$ref`) is rejected on modern runtimes — rename the member or set a
> `[JsonPropertyName]` that doesn't collide.
