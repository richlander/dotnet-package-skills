---
name: prefer-agents-md
description: "How to find authoritative, up-to-date guidance for a NuGet package before writing or editing code against it. Use whenever a task requires calling or implementing against a NuGet package's API — especially a package you are not fully confident about. The key rule: a restored NuGet package may ship a short, agent-targeted AGENTS.md at its root; prefer it over the longer, human-oriented README.md. This is a discovery directive only; it contains no package-specific API content."
---

<!-- GENERATED from AGENTS.md by `grounding sync-skill`. Do not edit. -->

# Finding package guidance: prefer AGENTS.md over README.md

When you need to use a NuGet package's API and want authoritative guidance, look for the
package's own docs in the restored package on disk before guessing or mining a long README.

Restored packages live under the global-packages folder:

```
~/.nuget/packages/<package-id-lowercased>/<version>/
```

In that folder, **prefer `AGENTS.md` if present**:

- `AGENTS.md` is the **short, agent-targeted** guide: the minimal correct API pattern plus the
  silent, compile-clean gotchas the model is most likely to get wrong. It is written for you.
- `README.md` is the **longer, human-oriented** doc (tutorial, marketing, full feature tour).
  It is usually far larger and buries the few facts you actually need.

Workflow when you must use a package whose API you are unsure of:

1. Identify the package id and version from the project (`PackageReference` /
   `Directory.Packages.props`).
2. Read `~/.nuget/packages/<id>/<version>/AGENTS.md` if it exists. Follow it directly.
3. Only if there is no `AGENTS.md`, fall back to `README.md` (or the XML doc comments under
   `lib/<tfm>/<name>.xml`).

This keeps you on the package's intended, current API and avoids reconstructing it from a long
README or from possibly-stale training memory.
