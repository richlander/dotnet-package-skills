---
name: system-commandline-grounding
description: Guidance for using System.CommandLine 2.0 (GA): core types (RootCommand, Command, Option<T>, Argument<T>, SetAction, ParseResult.GetValue) and how to migrate to it from System.CommandLine 2.0.0-beta or from McMaster.Extensions.CommandLineUtils. Use when creating, updating, or migrating a .NET command-line application that uses System.CommandLine.
---

<!-- GENERATED from AGENTS.md by eng/sync-skill.sh. Do not edit. -->

# System.CommandLine (2.0 GA) — agent grounding

This file teaches agents how to use **System.CommandLine 2.0** (the stable GA release).
The 2.0 API differs substantially from older `2.0.0-beta*` builds and from third-party
parsers such as `McMaster.Extensions.CommandLineUtils`. Use this guidance when creating
or migrating a command-line app that targets System.CommandLine.

## Package

```xml
<PackageReference Include="System.CommandLine" Version="2.0.0" />
```

## Core types (2.0 GA)

| Concept            | Type / member                                   |
| ------------------ | ----------------------------------------------- |
| Root command       | `RootCommand`                                   |
| Subcommand         | `Command` (add via `rootCommand.Subcommands.Add` or `command.Add`) |
| Named option       | `Option<T>` (e.g. `new Option<int>("--times")`) |
| Positional arg     | `Argument<T>` (e.g. `new Argument<string>("name")`) |
| Attach handler     | `command.SetAction(parseResult => { ... })`     |
| Read a value       | `parseResult.GetValue(option)` / `GetValue(argument)` |
| Parse + invoke     | `rootCommand.Parse(args).Invoke()`              |

Key shape of a 2.0 program:

```csharp
using System.CommandLine;

var nameArg = new Argument<string>("name") { Description = "Name to greet." };
var timesOpt = new Option<int>("--times", "-t") { Description = "How many times.", DefaultValueFactory = _ => 1 };

var greet = new Command("greet", "Print a greeting.") { nameArg, timesOpt };
greet.SetAction(parseResult =>
{
    var name = parseResult.GetValue(nameArg);
    var times = parseResult.GetValue(timesOpt);
    for (var i = 0; i < times; i++)
        Console.WriteLine($"Hello, {name}.");
    return 0;
});

var root = new RootCommand("Sample app") { greet };
return root.Parse(args).Invoke();
```

## Migrating FROM `2.0.0-beta4` (breaking changes)

| Beta4                                          | 2.0 GA                                  |
| ---------------------------------------------- | --------------------------------------- |
| `command.AddOption(opt)` / `AddArgument(arg)`  | collection initializer or `command.Add` |
| `command.AddCommand(sub)`                       | `command.Subcommands.Add(sub)` / `Add`  |
| `command.SetHandler(...)`                       | `command.SetAction(parseResult => ...)` |
| `context.ParseResult.GetValueForOption(opt)`    | `parseResult.GetValue(opt)`             |
| `IConsole` / `InvocationContext`                | use `Console` directly; action receives `ParseResult` |
| `new Option<T>("--name", "desc")`               | `new Option<T>("--name") { Description = "desc" }` |
| Aliases as constructor string `"-t\|--times"`   | pass aliases as extra ctor params: `new Option<int>("--times", "-t")` |

## Migrating FROM `McMaster.Extensions.CommandLineUtils`

| McMaster                                        | System.CommandLine 2.0                  |
| ----------------------------------------------- | --------------------------------------- |
| `CommandLineApplication`                        | `RootCommand`                           |
| `app.Command("greet", c => { ... })`            | `new Command("greet", ...)` added to root |
| `command.Argument("name", "desc")`              | `new Argument<string>("name")`          |
| `command.Option<int>("-t\|--times", ...)`       | `new Option<int>("--times", "-t")`      |
| `option.HasValue()` / `option.ParsedValue`      | `parseResult.GetValue(option)`          |
| `command.OnExecute(() => 0)`                    | `command.SetAction(pr => 0)`            |
| `app.Execute(args)`                             | `rootCommand.Parse(args).Invoke()`      |
| `app.Out` / `app.Error` (TextWriter injection)  | write to `Console`; for tests redirect `Console.SetOut`/`SetError`, or inject a `TextWriter` through your own action |

### Testing note
McMaster apps commonly expose a `Build(TextWriter out, TextWriter err)` seam and call
`app.Execute(...)`. With System.CommandLine, keep behavior identical and update the test
seam to parse+invoke: capture output via an injected `TextWriter` passed into your actions,
or via `Console.SetOut`/`Console.SetError` around `root.Parse(args).Invoke()`.

## Gotchas
- Options are referenced by **identity** (the `Option<T>`/`Argument<T>` instance), not by
  name string — keep references to pass to `parseResult.GetValue(...)`.
- Default values use `DefaultValueFactory`, not a plain default parameter.
- There is no `IConsole` in GA; don't reintroduce it during migration.
