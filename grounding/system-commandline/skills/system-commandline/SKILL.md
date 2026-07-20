---
name: system-commandline
version: 2.0.0
description: >-
  Use when building or modifying a .NET command-line app with System.CommandLine (RootCommand,
  Command, Option<T>, Argument<T>) on the current GA / 2.x–3.x API. This library had a large breaking
  redesign at 2.0 GA, so training data and web snippets are FULL of the removed beta stack
  (SetHandler, AddOption, BinderBase, IConsole). The #1 silent trap: the Option/Argument constructor's
  2nd positional argument is an ALIAS, not a description. Start here; branch to the domain skills for
  beta→GA migration, options/arguments, actions/invocation, subcommands/help, and 3.x additions.
---

# System.CommandLine — parse & dispatch a .NET CLI (current API)

`System.CommandLine` (namespace `System.CommandLine`) parses arguments into a command tree and runs
an action. The 2.0 GA redesign **removed** the old invocation/binding stack, so most remembered
snippets do not compile. Pin the current shapes below.

> **Everything you need is in these skills.** Do NOT `web_search` / `web_fetch` for System.CommandLine
> usage — the web is dominated by the pre-GA beta API (`SetHandler`, `AddOption`, `AddCommand`,
> `BinderBase<T>`, `IConsole`, `getDefaultValue:` ctor args) that **no longer exists**. This base
> skill plus the domain skills are the current, authoritative API. Pull the matching domain skill.

## The core pattern (current API)

```csharp
using System.CommandLine;

// 1. Declare options/arguments. KEEP the instances — you read values back by identity.
var nameOption = new Option<string>("--name")            // "--name" is the name; extra strings are ALIASES
{
    Description = "Who to greet",                        // description is a PROPERTY, not a ctor arg
    Required = true,
};
nameOption.Aliases.Add("-n");
var countOption = new Option<int>("--count") { DefaultValueFactory = _ => 1 };

// 2. Build the command tree.
var root = new RootCommand("Greeter sample");
root.Options.Add(nameOption);
root.Options.Add(countOption);

// 3. Wire behavior with SetAction; read parsed values from the ParseResult by instance.
root.SetAction(parseResult =>
{
    string name = parseResult.GetValue(nameOption)!;
    int count = parseResult.GetValue(countOption);
    for (int i = 0; i < count; i++) Console.WriteLine($"Hello, {name}!");
    return 0;                                            // exit code
});

// 4. Parse then invoke.
return await root.Parse(args).InvokeAsync();
```

## Gotchas (compile-clean but wrong, or removed-API)

- **`new Option<T>("--name", "description")` is WRONG.** The 2nd positional arg is an **alias**, so the
  description becomes a bogus alias and the help text is lost. Use `new Option<T>("--name") {
  Description = "..." }`; pass real aliases as extra strings (`new Option<T>("--name", "-n")`) or via
  `.Aliases.Add(...)`. Same for `Argument<T>`.
- **Read values by identity.** `parseResult.GetValue(theOptionInstance)` — keep the exact instance you
  added. There is no delegate-parameter binding anymore.
- **`SetHandler` is gone.** Use `SetAction(parseResult => ...)` (sync) or
  `SetAction(async (parseResult, ct) => ...)` (async). See actions-and-invocation.
- **`AddOption` / `AddArgument` / `AddCommand` are gone.** Use the `.Options` / `.Arguments` /
  `.Subcommands` collections: `root.Options.Add(o)`, `cmd.Subcommands.Add(sub)`.
- **`Required`, not `IsRequired`.** Default values are `DefaultValueFactory = _ => v`, not
  `getDefaultValue:` / `SetDefaultValue(...)`.
- **`IConsole` / `BinderBase<T>` / `HelpBuilder` are gone.** Use `Console` directly; customize help via
  a `HelpAction` (see subcommands-and-help).

## Which skill for what

- **beta-to-ga-migration** — the full 2.0.0-beta4 → GA/3.x rename table for upgrading old CLI code.
- **options-and-arguments** — `Option<T>`/`Argument<T>` declaration: aliases, defaults, `Required`,
  arity, `AcceptOnlyFromAmong`, custom parsers, `Validators.Add`.
- **actions-and-invocation** — `SetAction` sync/async signatures, `parseResult.GetValue`, exit codes,
  `Parse(...).Invoke()/InvokeAsync()`, `ParseResult.Errors`.
- **subcommands-and-help** — command hierarchy, recursive (global) options, and customizing help.
- **net-3x-additions** — members added in 3.x over 2.x (additive; no breaks) and current TFM targeting.
