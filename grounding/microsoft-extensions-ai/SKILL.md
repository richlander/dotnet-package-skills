---
name: microsoft-extensions-ai
description: "Microsoft.Extensions.AI (IChatClient) usage and migration gotchas. Use when building or fixing chat clients, tool/function calling, or middleware pipelines with Microsoft.Extensions.AI — above all that automatic tool calling requires building the client with ChatClientBuilder(...).UseFunctionInvocation(); without it, tools placed in ChatOptions.Tools are silently never invoked, response.Text is empty, and no error is thrown. Also covers the preview→GA API renames (which have no [Obsolete] shims)."
---

<!-- GENERATED from AGENTS.md by `grounding sync-skill`. Do not edit. -->

# Microsoft.Extensions.AI usage & migration gotchas

`Microsoft.Extensions.AI` provides `IChatClient` (the chat abstraction) plus delegating
clients composed with `ChatClientBuilder`. GA shipped at **9.5.0**; the core API was renamed
during preview (see the migration table) with **no `[Obsolete]` shims** — old code does not
compile against current versions.

## Tool / function calling requires `UseFunctionInvocation` (silent gotcha)

Putting `AITool`s in `ChatOptions.Tools` does **not** make them run. The bare `IChatClient`
just forwards the tools to the model and returns the model's request to call them as
`FunctionCallContent` in `response.Messages` — it does **not** invoke your function. Symptom:
the call succeeds with **no exception**, but `response.Text` is **empty** and your function
never executes. Automatic invocation is a pipeline step you must add:

```csharp
IChatClient client = new ChatClientBuilder(innerClient)
    .UseFunctionInvocation()   // adds FunctionInvokingChatClient — REQUIRED for tools to run
    .Build();

var options = new ChatOptions { Tools = [AIFunctionFactory.Create(GetWeather)] };
ChatResponse response = await client.GetResponseAsync(messages, options);
```

`UseFunctionInvocation()` adds a `FunctionInvokingChatClient` that detects
`FunctionCallContent`, invokes the matching `AIFunction`, feeds the `FunctionResultContent`
back to the model, and loops until a final answer (up to `MaximumIterationsPerRequest`,
default 40). Without it in the pipeline, tools are silently inert.

## Pipeline ordering and composition
`ChatClientBuilder` applies `Use*` calls **outermost-first**: the first `Use*` added is the
first to see each request. A typical order is cache → function invocation → telemetry around
the inner client. Build once and reuse the resulting `IChatClient` (it is thread-safe);
`ChatOptions`, however, may be mutated by middleware, so don't share one instance across
concurrent calls.

## Preview → GA / current API migration (renamed, no shims)

| Old (≤ 9.1 preview) | Current (≥ 9.3 / GA) |
| --- | --- |
| `IChatClient.CompleteAsync(...)` | `GetResponseAsync(...)` |
| `IChatClient.CompleteStreamingAsync(...)` | `GetStreamingResponseAsync(...)` |
| `ChatCompletion` (type) | `ChatResponse` |
| `StreamingChatCompletionUpdate` | `ChatResponseUpdate` |
| `ChatCompletion.Choices` | `ChatResponse.Messages` |
| `ChatCompletion.Message` (single) | removed — use `response.Text` or `response.Messages[0]` |
| `ChatCompletion.CompletionId` | `ChatResponse.ResponseId` |
| `IChatClient.Metadata` (interface property) | removed — `client.GetService<ChatClientMetadata>()` |
| `ChatOptions/Response.ChatThreadId` | `ConversationId` (`[Obsolete]` shim in 9.5) |

`response.Text` concatenates **all** assistant messages; after multi-turn tool calls
`response.Messages` can hold several, so `Text` may include intermediate content.
