---
name: actions-and-invocation
version: 2.0.0
description: >-
  Use when wiring behavior and running a System.CommandLine app — SetAction (sync and async
  signatures), reading values with parseResult.GetValue, Parse(args) then Invoke/InvokeAsync, exit
  codes, and handling parse errors via ParseResult.Errors / result.AddError. SetHandler and
  positional binding are gone.
---

# System.CommandLine: actions & invocation

Behavior attaches to a command with `SetAction`. You run the app by `Parse`-ing args into a
`ParseResult`, then `Invoke`/`InvokeAsync`. `SetHandler`, delegate parameter binding, and `IConsole`
were removed at GA — do not use them.

> **Everything you need is in these skills.** Do NOT `web_search` / `web_fetch` — nearly all samples
> show `SetHandler(...)` with positionally-bound parameters, which no longer exists.

## SetAction signatures

```csharp
// Sync — return int is the exit code (void overload exists too; implies 0):
command.SetAction(parseResult =>
{
    var name = parseResult.GetValue(nameOption)!;   // read by identity
    Console.WriteLine($"Hello {name}");
    return 0;
});

// Async — receives a CancellationToken; return Task<int> (or Task):
command.SetAction(async (parseResult, cancellationToken) =>
{
    var url = parseResult.GetValue(urlOption)!;
    await DoWorkAsync(url, cancellationToken);
    return 0;
});
```

- Read every input from the `ParseResult` by the option/argument instance:
  `parseResult.GetValue(theOption)`. No parameters are injected.
- The `int` return value is the process exit code. Use the async overload whenever you `await`.

## Parsing and running

```csharp
// One-shot:
return await root.Parse(args).InvokeAsync();     // sync equivalent: root.Parse(args).Invoke()

// Or inspect before invoking:
ParseResult result = root.Parse(args);
if (result.Errors.Count > 0)
{
    foreach (var e in result.Errors) Console.Error.WriteLine(e.Message);
    return 1;
}
return await result.InvokeAsync();
```

- `command.Parse(args)` returns a `ParseResult`; `Invoke()` / `InvokeAsync()` then run the matched
  command's action (and built-in `--help` / `--version` / error reporting).
- **Migration:** `command.Invoke(args)` / `InvokeAsync(args)` (arg-taking overloads) are gone — always
  `Parse(args)` first, then invoke the result.

## Errors & exit codes

- **User-input errors:** report with `result.AddError("...")` from a `CustomParser`/validator (see
  options-and-arguments). They land in `ParseResult.Errors`, are printed by the invoker, and produce a
  non-zero exit code automatically. Do not `throw` for bad input.
- **Action outcome:** return the exit code you want from `SetAction`.
- `ParseResult.Errors` is the list to check when you parse-then-decide manually.
