---
name: dom-and-streaming
version: 10.0.0
description: >-
  Use when System.Text.Json work is not a plain POCO round-trip — inspecting/mutating JSON without a
  model (JsonNode / JsonDocument), reading or writing at high throughput (Utf8JsonReader /
  Utf8JsonWriter, byte and Stream overloads), or streaming large sequences
  (DeserializeAsyncEnumerable). Requires the base `system-text-json` skill.
---

# DOM & streaming APIs

## Mutable DOM — `JsonNode` (the `JObject` replacement)

Use when you need to read/modify JSON without a CLR type.

```csharp
JsonNode root = JsonNode.Parse(json)!;
string name = (string)root["user"]!["name"]!;      // index like a dictionary/array
root["user"]!["active"] = true;                    // mutate
root["tags"]!.AsArray().Add("new");
string outJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
```

- `JsonNode` (mutable) → `JsonObject` / `JsonArray` / `JsonValue`. This is the Newtonsoft `JObject`/
  `JArray`/`JToken` analog.
- `node.GetValue<T>()` or explicit casts pull typed values; missing paths return `null` (guard with `!`).

## Read-only DOM — `JsonDocument` / `JsonElement`

Fastest way to **inspect** JSON you won't mutate. `JsonDocument` is `IDisposable` (it rents pooled
buffers) — dispose it, and don't let a `JsonElement` outlive its document:

```csharp
using JsonDocument doc = JsonDocument.Parse(json);
JsonElement root = doc.RootElement;
foreach (JsonElement item in root.GetProperty("items").EnumerateArray())
    Console.WriteLine(item.GetProperty("id").GetInt32());
```

## Low-level `Utf8JsonReader` / `Utf8JsonWriter` (hottest paths)

Allocation-free forward-only read/write over UTF-8 bytes — reach for these only when profiling
demands it (custom converters use them internally):

```csharp
var writer = new Utf8JsonWriter(bufferWriter);
writer.WriteStartObject();
writer.WriteString("name", "Ada");
writer.WriteNumber("count", 3);
writer.WriteEndObject();
writer.Flush();

var reader = new Utf8JsonReader(utf8Bytes);
while (reader.Read()) { /* switch on reader.TokenType */ }
```

## Bytes & streams — skip the intermediate string

For I/O, prefer the UTF-8 / `Stream` overloads over `Serialize`→`string`→`Encoding.UTF8.GetBytes`:

```csharp
byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(value, options);
await JsonSerializer.SerializeAsync(stream, value, options);
var value = await JsonSerializer.DeserializeAsync<MyType>(stream, options);
```

## Streaming large arrays — `DeserializeAsyncEnumerable`

Process a huge top-level JSON array without buffering it all in memory:

```csharp
await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<Item>(stream, options))
    Handle(item);   // yields items as they are parsed
```

- Only the current item is materialized — ideal for large feeds/logs.
- All of the above compose with source generation: pass a `JsonTypeInfo<T>` from your context
  (see source-generation-aot) instead of `options` to stay AOT-safe.
