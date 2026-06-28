---
name: prefer-dotnet-inspect
description: "How to fetch a NuGet package's own authoritative docs with the dotnet-inspect CLI before writing or editing code against it. Use whenever a task requires calling or implementing against a NuGet package's API — especially a package you are not fully confident about. The key rule: run `dotnet-inspect package <id>@<version> --readme`, which returns the package's best shipped doc (AGENTS.md > README.md > PACKAGE.md), and prefer it over guessing or relying on training memory. This is a discovery directive only; it contains no package-specific API content."
---

<!-- GENERATED from AGENTS.md by `grounding sync-skill`. Do not edit. -->

# Finding package guidance: fetch it with dotnet-inspect

When you must use a NuGet package's API and want authoritative, version-specific guidance,
fetch the package's own shipped docs with the `dotnet-inspect` CLI instead of guessing or
relying on possibly-stale training memory.

`dotnet-inspect` is a global .NET tool already on your PATH. It reads the package (from the
restored NuGet cache or the feed) and prints its documentation. It does HTTP itself; you do
not need a web tool.

Workflow when you must use a package whose API you are unsure of:

1. Identify the package id and the **exact version** from the project (`PackageReference` /
   `Directory.Packages.props`) — e.g. `Markout` `0.13.6`. Always pass the referenced version
   so you read the docs for the version the project actually uses.
2. Fetch the package's best doc:

   ```
   dotnet-inspect package <id>@<version> --readme
   ```

   `--readme` returns the single best shipped doc, preferring `AGENTS.md` (short,
   agent-targeted) over `README.md` (long, human-oriented) over `PACKAGE.md`. Follow it
   directly — it is written for the version you are on.
This keeps you on the package's intended, current API for the exact version referenced, and
avoids reconstructing it from training memory or mining a long README.
