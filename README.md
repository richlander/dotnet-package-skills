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

We follow the same pattern `dotnet/skills` uses for its own evals: **build** the
`skill-validator` binary from source (`dotnet publish eng/skill-validator/src/SkillValidator.csproj`)
and run it. skill-validator is **not published to any NuGet feed** (not nuget.org, not GitHub
Packages) — `dotnet/skills` only builds it in-repo and publishes a rolling `--prerelease`
nightly to a GitHub Release. So we pin a `dotnet/skills` commit in
[`eng/skill-validator.sha`](eng/skill-validator.sha) and build the validator from it.
"Taking updates" = bump that SHA — automated by
[`.github/workflows/update-harness.yml`](.github/workflows/update-harness.yml), which opens a
PR pointing at the latest `dotnet/skills` main commit.

## Source of truth: `AGENTS.md`

`AGENTS.md` is the human-authored artifact under test (the file that ships in the package root).
`SKILL.md` is **generated** from `AGENTS.md` + `meta.yaml` by [`eng/sync-skill.sh`](eng/sync-skill.sh)
purely so the harness has a togglable skill to add/remove between arms. Never hand-edit
`SKILL.md`. Edit `AGENTS.md`, then run `eng/sync-skill.sh`.

## Layout

Each grounding unit lives in a folder named with a **lowercase-hyphen slug** (the
skill-validator skill name rule), e.g. `system-commandline` for the `System.CommandLine`
package. The real package id is recorded in `meta.yaml` (`package:`).

```
grounding/<slug>/
  AGENTS.md     # SOURCE OF TRUTH — ships in the package root
  meta.yaml     # name (== <slug>), package, description for the generated SKILL.md
  SKILL.md      # GENERATED (eng/sync-skill.sh) — do not edit
tests/<slug>/
  eval.yaml     # scenarios: prompt + setup.copy_test_files + assertions
  <fixtures>    # sample project(s) copied into the agent workdir; gated by `dotnet test`
eng/
  skill-validator.sha    # pinned dotnet/skills commit we build the validator from
  agents-line-limit.txt  # max lines allowed in any AGENTS.md (start: 60)
  sync-skill.sh          # AGENTS.md (+ meta.yaml) -> SKILL.md; enforces the line limit (--check for CI)
  run-evals.sh           # builds skill-validator from the pinned SHA, then runs evaluate
```

Grounding `AGENTS.md` files must stay **concise**: `eng/sync-skill.sh` fails if any exceeds the
budget in `eng/agents-line-limit.txt` (currently **60** lines). Keep content tight and prefer a
short "see also" link over inlining depth. Raise the limit deliberately, not casually.

A grounding doc records **only what an agent is proven to lack** (by eval signal) and is written
for **section-based RAG retrieval**, not top-to-bottom reading — unlike a README. See
[docs/authoring-principles.md](docs/authoring-principles.md) for the principles and the empirical
evidence behind them. For a per-package writeup suitable for an upstream PR, see the reports
under [docs/reports/](docs/reports/)
([System.CommandLine](docs/reports/system-commandline.md) — needs grounding for a narrow set
of topics; [System.Text.Json](docs/reports/system-text-json.md) — does not need general
grounding; [Microsoft.Extensions.AI](docs/reports/microsoft-extensions-ai.md) — grounding need
is **model-relative**: its headline gotcha is resident for Opus 4.6 (−1.0%) but a +63.3%
rescue for Haiku 4.5; [Markout](docs/reports/markout.md) — a genuinely non-resident package
whose grounding competes with the package's own README: do-no-harm + ~3× token efficiency at
the frontier, and a fail→pass correctness rescue at the weak tier;
[README liability](docs/reports/readme-liability.md) — a README-size sweep showing a lean
~3.5 KB targeted doc is **size-invariant** and beats a README of any realistic size (3–74 KB)
by **3–4×**, while README reliance is a high-variance, high-ceiling regime — so the lever is
**completeness + targeting**, not a size ratio).

The grounding folder name must match the tests folder name and the skill `name` (e.g.
`system-commandline`); the
harness resolves `tests/<name>/eval.yaml`. Fixtures live under `tests/` (never beside
`AGENTS.md`) so the baseline arm cannot accidentally read the grounding.

## Run locally

```bash
# Prereq: a .NET SDK matching dotnet/skills' global.json, git, and
# `gh auth login` (skill-validator's Copilot SDK uses gh creds).
eng/sync-skill.sh                  # regenerate SKILL.md from AGENTS.md
eng/run-evals.sh System.CommandLine
```

`run-evals.sh` clones `dotnet/skills` at the pinned SHA into `./.tools`, builds
`skill-validator`, and caches it per-SHA, so only the first run pays the build cost.

## Adding a package

1. `grounding/<slug>/AGENTS.md` — the grounding content.
2. `grounding/<slug>/meta.yaml` — `name` (== `<slug>`), `package`, `description`.
3. `tests/<slug>/eval.yaml` — one or more scenarios.
4. `tests/<slug>/<fixture project(s)>` — the task, with a `dotnet test` correctness gate.
5. `eng/sync-skill.sh` then `eng/run-evals.sh <slug>`.
