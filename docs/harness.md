# The measurement harness

How this repo **builds and runs** the [`dotnet/skills`](https://github.com/dotnet/skills)
`skill-validator` to measure whether grounding helps. The root [`README`](../README.md) covers
*what* grounding is and the findings; this file covers *how* the evals run.

> **None of this is part of the shipped grounding content.** The `SKILL.md` files, the slug
> rules, and the runner scripts are **test scaffolding** for the harness — not something a
> package author ships, and not a marketplace plugin. The only artifact under test is the
> `AGENTS.md` in each `grounding/<slug>/`.

## How it relates to dotnet/skills

We follow the same pattern `dotnet/skills` uses for its own evals: **build** the
`skill-validator` binary from source (`dotnet publish eng/skill-validator/src/SkillValidator.csproj`)
and run it. skill-validator is **not published to any NuGet feed** (not nuget.org, not GitHub
Packages) — `dotnet/skills` only builds it in-repo and publishes a rolling `--prerelease`
nightly to a GitHub Release. So we pin a `dotnet/skills` commit in
[`eng/skill-validator.sha`](../eng/skill-validator.sha) and build the validator from it.
"Taking updates" = bump that SHA — automated by
[`.github/workflows/update-harness.yml`](../.github/workflows/update-harness.yml), which opens a
PR pointing at the latest `dotnet/skills` main commit.

## Source of truth: `AGENTS.md` → `SKILL.md`

`AGENTS.md` is the human-authored artifact under test (the file that ships in the package root).
`SKILL.md` is **generated** from `AGENTS.md` + `meta.yaml` by
[`eng/sync-skill.sh`](../eng/sync-skill.sh) purely so the harness has a *togglable* skill to
add/remove between arms. It is an implementation detail of the harness, **not** a marketplace
skill and **not** something the package ships. Never hand-edit `SKILL.md`. Edit `AGENTS.md`, then
run `eng/sync-skill.sh`.

Grounding `AGENTS.md` files must stay **concise**: `eng/sync-skill.sh` fails if any exceeds the
budget in `eng/agents-line-limit.txt` (currently **60** lines). Keep content tight and prefer a
short "see also" link over inlining depth. Raise the limit deliberately, not casually.

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

The grounding folder name must match the tests folder name and the skill `name` (e.g.
`system-commandline`); the harness resolves `tests/<name>/eval.yaml`. Fixtures live under
`tests/` (never beside `AGENTS.md`) so the baseline arm cannot accidentally read the grounding.

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

## Channel-matrix runs

The delivery-channel study (raw package → NuGet MCP → shipped `AGENTS.md` → resident-index MCP)
is driven by [`eng/run-channel-matrix.sh`](../eng/run-channel-matrix.sh) and summarized by
[`eng/extract-channels.py`](../eng/extract-channels.py). See
[`docs/recommendation.md`](recommendation.md) for the results and
[`data/README.md`](../data/README.md) for the channel definitions.
