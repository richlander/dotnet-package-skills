---
name: system-text-json
version: 10.0.0
description: >-
  Use when a .NET program (de)serializes JSON with System.Text.Json — reading/writing DTOs,
  configuring JsonSerializerOptions, migrating from Newtonsoft.Json, or hitting a compile-clean
  behavioral difference. The #1 trap: STJ matches property names CASE-SENSITIVELY by default, so
  camelCase JSON into PascalCase members silently yields null/default with no exception. Start here
  for the core shapes; branch to the domain skills for Newtonsoft migration, source generation /
  Native AOT, custom converters & polymorphism, DOM / streaming, and .NET 10+ strictness.
---

# System.Text.Json — (de)serialize JSON from .NET

`System.Text.Json` (STJ) ships in the .NET shared framework — on modern .NET you need **no package
reference** and no `using` beyond `System.Text.Json` (+ `System.Text.Json.Serialization` for the
attributes). Namespaces: `JsonSerializer`, `JsonSerializerOptions` live in `System.Text.Json`;
`[JsonPropertyName]`, `[JsonIgnore]`, `[JsonConverter]`, `JsonSerializerContext` live in
`System.Text.Json.Serialization`.

> **Everything you need is in these skills.** Do NOT `web_search` / `web_fetch` for STJ usage — this
> base skill plus the domain skills below are authoritative. STJ is NOT Newtonsoft.Json: web
> snippets frequently mix the two APIs (`JsonConvert`, `JsonProperty`, `JObject`) which do not exist
> here. Pull the matching domain skill instead.

## The core pattern

```csharp
using System.Text.Json;

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,   // PascalCase members -> camelCase JSON
    PropertyNameCaseInsensitive = true,                  // read camelCase/PascalCase either way
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
};

string json = JsonSerializer.Serialize(value, options);
var back  = JsonSerializer.Deserialize<MyType>(json, options);
```

- `JsonSerializerOptions` is STJ's `JsonSerializerSettings`. **Build it once and reuse it** — a fresh
  options instance is expensive (it caches per-type metadata on first use). A cached `static readonly`
  instance is the norm; options become effectively read-only after first (de)serialization.
- `JsonSerializerDefaults.Web` (`new JsonSerializerOptions(JsonSerializerDefaults.Web)`) is a shortcut
  for the common web shape: camelCase naming **and** case-insensitive reads in one.

## Gotchas (compile-clean but wrong)

- **Case sensitivity — the silent break.** STJ matches property names **case-sensitively by default**
  (Newtonsoft is case-insensitive). camelCase JSON into PascalCase members yields **null/0/default,
  no exception**. Fix with `PropertyNameCaseInsensitive = true`, `JsonSerializerDefaults.Web`,
  `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`, or `[JsonPropertyName("...")]` per member.
- **Public fields are ignored by default** (Newtonsoft includes them). Opt in with
  `IncludeFields = true` or `[JsonInclude]` on the field.
- **Non-public getters/setters are ignored.** Use `[JsonInclude]` or make the accessor public.
- **Comments and trailing commas throw by default.** Opt in with
  `ReadCommentHandling = JsonCommentHandling.Skip` and `AllowTrailingCommas = true`.
- **Enums serialize as numbers by default.** For string enums use `JsonStringEnumConverter` (see
  converters-and-polymorphism).
- **`required` members and non-nullable reference types** are not enforced on deserialize unless you
  opt in (`[JsonRequired]`, or .NET 9+ `RespectNullableAnnotations`/`RespectRequiredConstructorParameters`).

## Which skill for what

Pull the matching domain skill; don't hand-roll what the library owns:

- **newtonsoft-migration** — the `JsonConvert`/`JsonProperty`/`JsonSerializerSettings` → STJ mapping
  table and every behavioral difference that compiles clean but changes output.
- **source-generation-aot** — `JsonSerializerContext` + `[JsonSerializable]`; the ONLY supported path
  under Native AOT / trimming (reflection serialization throws at run time). `[JsonSourceGenerationOptions]`.
- **converters-and-polymorphism** — `JsonConverter<T>` (Read/Write), `JsonStringEnumConverter`,
  `[JsonPolymorphic]` / `[JsonDerivedType]`, extension data.
- **dom-and-streaming** — `JsonNode`/`JsonObject`/`JsonArray`, `JsonDocument`, `Utf8JsonReader`/
  `Utf8JsonWriter`, `DeserializeAsyncEnumerable`, byte/`Stream` overloads for hot paths.
- **dotnet-10-11-strictness** — newer-runtime default changes (duplicate-property rejection, metadata
  collisions) and recent APIs the model may not know.
