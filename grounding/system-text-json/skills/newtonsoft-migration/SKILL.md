---
name: newtonsoft-migration
version: 10.0.0
description: >-
  Use when migrating serialization code from Newtonsoft.Json (Json.NET) to System.Text.Json —
  replacing JsonConvert / JsonSerializerSettings / [JsonProperty] and reconciling the behavioral
  differences that compile clean but silently change output (case-sensitivity, fields, nulls,
  comments, dates). Requires the base `system-text-json` skill.
---

# Newtonsoft.Json → System.Text.Json migration

Remove the `Newtonsoft.Json` package reference and `using Newtonsoft.Json;`. STJ is in the shared
framework — no replacement package needed. Then apply the API map **and** re-check the behavioral
defaults, because most breaks are silent (compile-clean, wrong output), not compiler errors.

## API map

| Newtonsoft.Json | System.Text.Json |
| --------------- | ---------------- |
| `JsonConvert.SerializeObject(x)` | `JsonSerializer.Serialize(x)` |
| `JsonConvert.DeserializeObject<T>(s)` | `JsonSerializer.Deserialize<T>(s)` |
| `JsonSerializerSettings` | `JsonSerializerOptions` |
| `[JsonProperty("name")]` | `[JsonPropertyName("name")]` (record param: `[property: JsonPropertyName("name")]`) |
| `[JsonIgnore]` | `[JsonIgnore]` (namespace `System.Text.Json.Serialization`) |
| `[JsonConstructor]` | `[JsonConstructor]` |
| `NullValueHandling.Ignore` | `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` |
| `DefaultValueHandling.Ignore` | `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault` |
| `Formatting.Indented` | `WriteIndented = true` |
| `Required.Always` on `[JsonProperty]` | `[JsonRequired]` or C# `required` |
| `ReferenceLoopHandling.Ignore` | `ReferenceHandler.IgnoreCycles` |
| `PreserveReferencesHandling.All` | `ReferenceHandler.Preserve` |
| `StringEnumConverter` | `JsonStringEnumConverter` |
| `JObject` / `JArray` / `JToken` | `JsonNode` / `JsonObject` / `JsonArray` (see dom-and-streaming) |
| `[JsonExtensionData]` | `[JsonExtensionData]` (dictionary must be `Dictionary<string, JsonElement>` or `object`) |

## Behavioral differences to reconcile (the silent ones)

- **Case-sensitivity.** Newtonsoft reads property names case-insensitively; STJ is case-sensitive by
  default → set `PropertyNameCaseInsensitive = true` (or `JsonSerializerDefaults.Web`). This is the
  single most common migration regression.
- **Fields.** Newtonsoft serializes public fields; STJ ignores them → `IncludeFields = true` /
  `[JsonInclude]`.
- **Non-public accessors.** Newtonsoft uses them; STJ ignores them → `[JsonInclude]` or make public.
- **Comments / trailing commas.** Newtonsoft allows; STJ throws → `ReadCommentHandling =
  JsonCommentHandling.Skip`, `AllowTrailingCommas = true`.
- **Dates.** No `DateFormatString`. STJ uses ISO 8601 round-trip; other formats need a custom
  `JsonConverter<DateTime>` (see converters-and-polymorphism). `TimeSpan` also differs.
- **Numbers.** Newtonsoft is lenient about quoted numbers; STJ rejects `"123"` into an `int` unless
  `NumberHandling = JsonNumberHandling.AllowReadingFromString`.
- **Missing vs null.** STJ leaves a member at its default when the JSON omits it (no `MissingMemberHandling`).

## Migration checklist

1. Swap package + usings + the API-map calls above.
2. Set `PropertyNameCaseInsensitive` (or `Web` defaults) if any input is camelCase.
3. Re-add field inclusion, comment/trailing-comma tolerance, and quoted-number handling **only if the
   old code relied on them** — don't loosen defaults blindly.
4. Replace `StringEnumConverter` with `JsonStringEnumConverter`; replace date-format strings with a converter.
5. Build **and run a round-trip test** — the breaks are silent, so a compile is not enough.
