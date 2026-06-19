# System.CommandLine migration & gotchas (2.0.0-beta → GA / 3.x)

## Migrating System.CommandLine 2.0.0-beta (beta1–beta4) to GA / 3.x

beta4 → 2.0 GA was a breaking redesign: the old invocation/binding stack was removed, so
beta code does **not** compile against GA. The same mapping applies when migrating beta
code to 3.x (3.x is additive over GA). Set the package version, then apply:

| 2.0.0-beta4 | 2.x / 3.x GA |
| ----------- | ------------ |
| `AddOption` / `AddArgument` / `AddCommand` | `Options.Add` / `Arguments.Add` / `Subcommands.Add` |
| `AddGlobalOption(o)` | `o.Recursive = true;` then `Options.Add(o)` |
| `SetHandler(...)` / `Handler.SetHandler(...)` | `command.SetAction(parseResult => ...)` |
| `command.Invoke(args)` / `InvokeAsync(args)` | `command.Parse(args).Invoke()` / `.InvokeAsync()` |
| handler params bound by position | `parseResult.GetValue(option)` inside the action |
| `getDefaultValue: () => v` ctor arg / `SetDefaultValue` / `SetDefaultValueFactory` | `DefaultValueFactory = _ => v` |
| `IsRequired` | `Required` |
| `ExistingOnly()` | `AcceptExistingOnly()` |
| `ArgumentHelpName` | `HelpName` |
| binding: `BinderBase<T>` / `BindingContext` / `IValueDescriptor` | `parseResult.GetValue(option)` |
| `IConsole` / `HelpBuilder` | removed; use `Console`, customize help via `HelpAction` |

## Gotchas (compile-clean but wrong)

- `new Option<T>("--name", "description")`: the **2nd positional ctor arg is an alias, not a
  description** (a silent shift since beta4). Keeping the beta form compiles but registers the
  description as a bogus alias and drops the help text. Use
  `new Option<T>("--name") { Description = "..." }`; pass real aliases as
  `new Option<T>("--name", "-n")`. Same rule for `Argument<T>`.
- Options/arguments are referenced by **identity**: keep the instance you `.Add`ed and pass it
  to `parseResult.GetValue(instance)`.

## Upgrading 2.x → 3.x (NOT beta)

Drop-in: bump the version, change no code (3.x is additive, no API breaks). Do **not**
"modernize" working 2.x patterns. The only consumer-visible shift is the dropped in-box
`net8.0` target; the package targets `net10.0` and `netstandard2.0`.

## New API members in 3.x (additive over 2.x)

- `Argument<T>.CaptureRemainingTokens` — greedy arg capturing all remaining tokens.
- `Option<T>`/`Argument<T>` `.AcceptOnlyFromAmong(StringComparer, params string[])` —
  case-insensitive constrained values (the `params string[]` overload already existed in 2.x).
- `RootCommand.HelpName` — custom root-command name in help output.
