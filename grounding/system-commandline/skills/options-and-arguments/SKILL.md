---
name: options-and-arguments
version: 2.0.0
description: >-
  Use when declaring or configuring System.CommandLine inputs — Option<T> and Argument<T>: names vs
  aliases, Description, Required, DefaultValueFactory, Arity, constrained values via
  AcceptOnlyFromAmong, file/dir validation, and custom parsing/validation with CustomParser and
  Validators.Add. Covers the constructor-alias gotcha in depth.
---

# System.CommandLine: options & arguments

`Option<T>` = a named input (`--name`, `-n`). `Argument<T>` = a positional input. Both are declared as
objects you keep and later read by identity via `parseResult.GetValue(instance)`.

> **Everything you need is in these skills.** Do NOT `web_search` / `web_fetch` — web samples use the
> removed beta ctor/property shapes (`getDefaultValue:`, `IsRequired`, `ExistingOnly`).

## Declaring options

```csharp
var name = new Option<string>("--name")          // first string = the option's name
{
    Description = "Who to greet",                // description is a PROPERTY (never a ctor arg)
    Required = true,                             // NOT IsRequired
};
name.Aliases.Add("-n");                          // add aliases after construction ...

var count = new Option<int>("--count", "-c")     // ... or as extra ctor strings (all ALIASES)
{
    DefaultValueFactory = _ => 1,                // NOT getDefaultValue: / SetDefaultValue
    Arity = ArgumentArity.ExactlyOne,
};
```

- **Ctor-alias gotcha:** `new Option<T>("--name", "description")` compiles but the 2nd string is an
  **alias**, silently dropping your help text. Put text in `{ Description = ... }`; extra ctor strings
  are always aliases.
- `Required` makes an option mandatory. Optional options should set a `DefaultValueFactory`.
- `Arity` (`ArgumentArity.Zero/ZeroOrOne/ExactlyOne/ZeroOrMore/OneOrMore`) controls token counts;
  `Option<T[]>` / `Option<List<T>>` collect multiple values.

## Declaring arguments (positional)

```csharp
var path = new Argument<FileInfo>("path")
{
    Description = "Input file",
    Arity = ArgumentArity.ExactlyOne,
};
path.AcceptExistingOnly();                        // NOT ExistingOnly(); also on Argument<DirectoryInfo>
```

## Constrained values

```csharp
var level = new Option<string>("--level");
level.AcceptOnlyFromAmong("debug", "info", "warn");                       // 2.x+ (case-sensitive)
level.AcceptOnlyFromAmong(StringComparer.OrdinalIgnoreCase, "debug", "info", "warn"); // 3.x comparer overload
```

## Custom parsing & validation

```csharp
// Turn a raw token into T (and report parse failures):
var port = new Option<int>("--port")
{
    CustomParser = result =>
    {
        if (int.TryParse(result.Tokens[0].Value, out var p) && p is > 0 and < 65536) return p;
        result.AddError("--port must be 1..65535");
        return 0;
    },
};

// Cross-cutting/range validation on an already-parsed value:
port.Validators.Add(result =>
{
    if (result.GetValue(port) == 0) result.AddError("--port is required and must be valid");
});
```

- Report bad input with `result.AddError(...)` — do **not** throw for user-input errors; errors surface
  through `ParseResult.Errors` and set a non-zero exit code (see actions-and-invocation).

## Reading values

Always by identity: `string n = parseResult.GetValue(name)!;`. There is no positional binding — keep
the instance you added to the command.
