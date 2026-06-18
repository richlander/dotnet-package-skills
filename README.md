# dotnet-package-grounding

A repo dedicated to **NuGet package grounding content** — `AGENTS.md` files that ship in a
package's root and make the package *self-teaching* for an AI agent — and to **measuring**
whether that content actually helps.

It reuses the [`dotnet/skills`](https://github.com/dotnet/skills) **skill-validator** harness
to run a **baseline vs enriched** evaluation: the agent attempts each task once *without* the
grounding and once *with* it, and the harness compares accuracy, token usage, tool calls, and
time using pairwise LLM judging with confidence intervals.

> This repo is for **package grounding content**, not `plugin.json` marketplace skills. It has
> no marketplace/plugin distribution machinery — only the grounding files and their evals.

## How it relates to dotnet/skills

We **consume** the published `Microsoft.DotNet.SkillValidator` tool (via `dnx` or the AOT
tarball from the `skill-validator-nightly` release). We do **not** fork or vendor its source.
"Taking updates" is just bumping [`eng/tool-version.txt`](eng/tool-version.txt) — automated by
[`.github/workflows/update-harness.yml`](.github/workflows/update-harness.yml).

## Source of truth: `AGENTS.md`

`AGENTS.md` is the human-authored artifact under test (the file that ships in the package root).
`SKILL.md` is **generated** from `AGENTS.md` + `meta.yaml` by [`eng/sync-skill.sh`](eng/sync-skill.sh)
purely so the harness has a togglable skill to add/remove between arms. Never hand-edit
`SKILL.md`. Edit `AGENTS.md`, then run `eng/sync-skill.sh`.

## Layout

```
grounding/<Package>/
  AGENTS.md     # SOURCE OF TRUTH — ships in the package root
  meta.yaml     # name + description for the generated SKILL.md
  SKILL.md      # GENERATED (eng/sync-skill.sh) — do not edit
tests/<Package>/
  eval.yaml     # scenarios: prompt + setup.copy_test_files + assertions
  <fixtures>    # sample project(s) copied into the agent workdir; gated by `dotnet test`
eng/
  tool-version.txt   # pinned skill-validator version (the update knob)
  sync-skill.sh      # AGENTS.md (+ meta.yaml) -> SKILL.md   (--check for CI)
  run-evals.sh       # wraps `dnx Microsoft.DotNet.SkillValidator evaluate ...`
```

The grounding folder name must match the tests folder name (e.g. `System.CommandLine`); the
harness resolves `tests/<name>/eval.yaml`. Fixtures live under `tests/` (never beside
`AGENTS.md`) so the baseline arm cannot accidentally read the grounding.

## Run locally

```bash
# Prereq: dotnet 10+, and `gh auth login` (skill-validator's Copilot SDK uses gh creds).
eng/sync-skill.sh                  # regenerate SKILL.md from AGENTS.md
eng/run-evals.sh System.CommandLine
```

To use the nightly nupkg directly, download it from the
[`skill-validator-nightly` release](https://github.com/dotnet/skills/releases/tag/skill-validator-nightly)
and point the feed at the folder: `FEED=./.tools eng/run-evals.sh`.

## Adding a package

1. `grounding/<Package>/AGENTS.md` — the grounding content.
2. `grounding/<Package>/meta.yaml` — `name` + `description`.
3. `tests/<Package>/eval.yaml` — one or more scenarios.
4. `tests/<Package>/<fixture project(s)>` — the task, with a `dotnet test` correctness gate.
5. `eng/sync-skill.sh` then `eng/run-evals.sh <Package>`.
