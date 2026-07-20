---
name: net-3x-additions
version: 3.0.0
description: >-
  Use when targeting System.CommandLine 3.x specifically — the members added over 2.x GA
  (Argument<T>.CaptureRemainingTokens, the AcceptOnlyFromAmong(StringComparer,...) overload,
  RootCommand.HelpName) and the current TFM targeting (net10.0 + netstandard2.0, no in-box net8.0).
  3.x is additive over 2.x: existing 2.x code compiles unchanged.
---

# System.CommandLine: 3.x additions over 2.x

3.x is **additive** over 2.x GA — no breaking changes. Upgrading 2.x → 3.x is a version bump with no
code changes; do not "modernize" working 2.x code. This skill lists what 3.x adds and the current
targeting.

> **Everything you need is in these skills.** Do NOT `web_search` / `web_fetch`. The base skill plus
> the domain skills are the current API; this one covers only the 3.x-specific delta.

## New members

```csharp
// 1. Greedy trailing argument — captures ALL remaining tokens (e.g. pass-through args):
var forwarded = new Argument<string[]>("args") { CaptureRemainingTokens = true };
runCmd.Arguments.Add(forwarded);
// $ tool run -- --any --thing here   ->  GetValue(forwarded) == ["--any","--thing","here"]

// 2. Case-insensitive constrained values — the StringComparer overload is new in 3.x
//    (the params-string-only overload already existed in 2.x):
var level = new Option<string>("--level");
level.AcceptOnlyFromAmong(StringComparer.OrdinalIgnoreCase, "debug", "info", "warn");

// 3. Custom program name in help/usage output:
var root = new RootCommand("My tool") { HelpName = "mytool" };
```

## Targeting

- The package targets **`net10.0`** and **`netstandard2.0`**. The in-box **`net8.0`** target was
  dropped — that is the only consumer-visible shift going 2.x → 3.x.
- Add it like any package: `<PackageReference Include="System.CommandLine" Version="3.0.0-*" />` (or
  `dotnet package add <proj> System.CommandLine`). It is **not** in the shared framework.

## Upgrading 2.x → 3.x

Bump the version, change no code. If you are coming from a 2.0.0-beta, that IS a breaking migration —
use beta-to-ga-migration, then optionally adopt the members above.
