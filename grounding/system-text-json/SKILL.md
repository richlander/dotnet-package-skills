---
name: system-text-json
description: "System.Text.Json migration and gotchas. Use when migrating serialization code from Newtonsoft.Json to System.Text.Json, and for the silent compile-clean behavioral differences — above all that Newtonsoft deserializes case-insensitively by default while System.Text.Json is case-sensitive by default (camelCase JSON into PascalCase members silently yields null/default with no exception). Covers the JsonConvert/JsonSerializer and attribute mapping (JsonProperty -> JsonPropertyName, settings -> options), ignored public fields and non-public accessors, comment/trailing comma handling, and .NET 10 stricter defaults."
---

<!-- GENERATED from AGENTS.md by eng/sync-skill.sh. Do not edit. -->

# System.Text.Json migration & gotchas (Newtonsoft.Json → System.Text.Json)

## Migrating Newtonsoft.Json to System.Text.Json

System.Text.Json (STJ) ships in the .NET shared framework — on modern .NET you need **no
package**. Remove the `Newtonsoft.Json` package reference and `using Newtonsoft.Json;`; use
`System.Text.Json` (+ `System.Text.Json.Serialization` for attributes). Core mapping:

| Newtonsoft.Json | System.Text.Json |
| --------------- | ---------------- |
| `JsonConvert.SerializeObject(x)` | `JsonSerializer.Serialize(x)` |
| `JsonConvert.DeserializeObject<T>(s)` | `JsonSerializer.Deserialize<T>(s)` |
| `JsonSerializerSettings` | `JsonSerializerOptions` |
| `[JsonProperty("name")]` | `[JsonPropertyName("name")]` (on a record param: `[property: JsonPropertyName("name")]`) |
| `[JsonIgnore]` | `[JsonIgnore]` (different namespace) |
| `[JsonConstructor]` | `[JsonConstructor]` |
| `NullValueHandling.Ignore` | `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` |
| `Formatting.Indented` | `WriteIndented = true` |
| `Required.Always` on `[JsonProperty]` | `[JsonRequired]` or C# `required` |
| `ReferenceLoopHandling` / `PreserveReferencesHandling` | `ReferenceHandler.IgnoreCycles` / `.Preserve` |

## Gotchas (compile-clean but wrong)

- **Case sensitivity (the silent break).** Newtonsoft matches property names
  **case-insensitively by default**; STJ is **case-sensitive by default**. camelCase JSON
  deserialized into PascalCase members silently yields **null/0/default with no exception**.
  Fix: `new JsonSerializerOptions { PropertyNameCaseInsensitive = true }`, or construct with
  `JsonSerializerDefaults.Web` (case-insensitive + camelCase), or set
  `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`, or `[JsonPropertyName]` per member.
- **Public fields are ignored by default** (Newtonsoft includes them). Opt in with
  `IncludeFields = true` or `[JsonInclude]`.
- **Non-public getters/setters are ignored by default** (Newtonsoft uses them). Use
  `[JsonInclude]` or make the accessor public.
- **Comments and trailing commas throw by default** (Newtonsoft allows them). Opt in with
  `ReadCommentHandling = JsonCommentHandling.Skip` and `AllowTrailingCommas = true`.
- **`TimeSpan`, `DateTimeOffset` formats and `DateFormatString` differ**; round-tripping
  custom date formats needs a converter.

## .NET 10+ stricter defaults
- Duplicate JSON property names are **rejected** (previously last-one-wins); opt out with
  `AllowDuplicateProperties = true`.
- A member whose name collides with a metadata property (`$type`/`$id`/`$ref`) now throws.
