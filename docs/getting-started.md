# Getting started

This repo is **generic infrastructure** for evaluating NuGet package *grounding* — it ships no
grounding content of its own. You point it at a package (or a repo with candidate grounding) and
measure whether the grounding helps an AI agent use the package correctly. Concepts live in
[`grounding-eval-methodology.md`](./grounding-eval-methodology.md); how the harness runs is in
[`harness.md`](./harness.md).

## Prerequisites

- A **.NET SDK** matching `dotnet/skills`' `global.json` (the harness builds `skill-validator` from a
  pinned commit — see [`harness.md`](./harness.md)).
- `git`, and **`gh auth login`** — `skill-validator`'s Copilot SDK uses your `gh` credentials.
- *(optional)* `dotnet-inspect` for library inspection — but **not** for clean content runs (see below).

## Build & install the `grounding` CLI from source

The CLI is in `src/grounding/` (`System.CommandLine`, net11.0). It is **not yet on a public feed**, so
build it from this repo. Pick the path that suits you:

```bash
# A. Run without installing (dev inner loop) — build once, forward args:
eng/grounding --help                       # bash;  eng/grounding.ps1 for PowerShell
# or run the built dll directly (any OS, no WSL):
dotnet build src/grounding -c Release && dotnet src/grounding/bin/Release/net11.0/grounding.dll --help

# B. Install as a global tool (framework-dependent; easiest, fully cross-platform):
dotnet pack src/grounding -c Release
dotnet tool install -g --add-source src/grounding/nupkg dotnet-package-grounding
grounding --help                           # runs via the dotnet host

# C. Install as a Native AOT binary (self-contained ~5 MB, no dotnet host needed):
eng/install-grounding.sh                   # bash;  eng/install-grounding.ps1 for PowerShell
#   (wraps `dotnet publish -c Release -r <rid>` + copy to ~/.dotnet/tools)
grounding --help
```

> **FDD vs AOT:** option **B** packs a conventional framework-dependent global tool (run via `dotnet`);
> option **C** produces a single native executable with no managed-host dependency. Both install a
> `grounding` command on PATH — use one. AOT is gated to the `-r <rid>` publish, so plain `build`/`pack`
> stay fast and framework-dependent. Once published to a feed,
> `dotnet tool install -g dotnet-package-grounding` will work directly.

## The grounding loop

1. **Author a unit.** Add the grounding doc and a small eval:

   ```text
   grounding/<slug>/AGENTS.md   # the Missing Manual (source of truth; ships in the package)
   grounding/<slug>/meta.yaml   # name (== <slug>), package, description
   tests/<slug>/eval.yaml       # scenarios: prompt + setup fixtures + assertions
   tests/<slug>/fixtures/...     # sample project(s) gated by `dotnet build`/`run`
   ```

   Write `AGENTS.md` additively from an **empty baseline** — only what an agent is *proven* to lack (see
   [`authoring-principles.md`](./authoring-principles.md)). Keep it under the line budget
   (`eng/agents-line-limit.txt`).

2. **Sync the skill wrapper** (regenerates the harness's `SKILL.md` from `AGENTS.md`):

   ```bash
   grounding sync-skill
   ```

3. **Run the ladder** (arms are named by *content*, all force-fed — see methodology §1):

   ```bash
   # Rung 0/1 — Missing Manual scheme (the ~6-question everyday floor).
   grounding run <slug> --source agents                                   # baseline vs Missing Manual
   grounding run <slug> --source readme --readme-file path/to/README.md   # adds Front Door
   ```

   For a **clean content measurement**, runs scrub `~/.dotnet/tools` from the agent's PATH so
   `dotnet-inspect` can't substitute for the document (tool availability is a separate lever). Verify with
   `di == 0` on the content arms.

4. **Read the result:**

   ```bash
   grounding analyze data/<slug>-6q/<slug>.<model>.json     # full table (baseline + content arms)
   grounding analyze --card data/<slug>-6q                  # the PR dump (BETTER / NEUTRAL / WORSE)
   ```

Iterate **eval-driven**: run, find where the grounding falls short, patch the doc *generally* (not to the
specific prompts), repeat. Then ship per [`grounding-lifecycle.md`](./grounding-lifecycle.md) with the
evidence dump and the [PR template](./templates/canonical-grounding-pr.md).

## Where to go next

- [`grounding-eval-methodology.md`](./grounding-eval-methodology.md) — the arms, the two regimes
  (Mini 6 / MM 12 / CT 24, nested), the cost-tiered ladder, and the ship gate.
- [`authoring-principles.md`](./authoring-principles.md) — how to write the three documents
  (Front Door / Missing Manual / Complete Textbook).
- [`harness.md`](./harness.md) — how `skill-validator` is built and run, and the confounds.
