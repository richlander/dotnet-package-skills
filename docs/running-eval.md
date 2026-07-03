# Running eval

This repo is the **generic eval harness**. It holds no package grounding of its own — the grounding
(`AGENTS.md`, optional `SKILL.md`) and its eval (`eval.yaml` + `fixtures/`) live in the **package's own
repo**, under `grounding/<unit>/`. You run eval by pointing the harness at that repo. Nothing is packed
or published to iterate: the harness reads the grounding file **in place** from the target tree, so a
typo fix is an edit and a re-run.

## Prerequisites

- The `grounding` CLI, built from this repo — see [`getting-started.md`](./getting-started.md#build--install-the-grounding-cli-from-source).
- The `skill-validator` harness, built once into `.tools/skill-validator-<sha>/` (see [`harness.md`](./harness.md)).
  This is the only machine-specific artifact; `.tools/` is git-ignored, so build it for the platform you
  run on (e.g. `-r osx-arm64` or `-r linux-x64`).
- A checkout of the **target package repo** whose grounding you want to evaluate.
- `gh auth login` — `skill-validator`'s Copilot SDK rides your `gh` credentials.

## The bundle a target repo ships

A package repo carries a self-contained grounding bundle (inputs only — datasets are **not** committed):

```text
<target-repo>/grounding/<unit>/
  AGENTS.md          # the Missing Manual (source of truth; also packed into the nupkg)
  SKILL.md           # optional Complete Textbook (repo asset; not packed)
  eval.yaml          # scenarios: prompt + setup fixtures + assertions
  fixtures/...        # starting project(s), gated by `dotnet build`/`run`
  README.md          # the durable prose eval summary (the card lives in the PR)
  run.sh / run.ps1   # regenerate the datasets
```

## Point the harness at it

```bash
# Reads <target-repo>/grounding/<unit>/AGENTS.md in place. No packing, no publish.
grounding run <unit> --root <target-repo> --source agents  --runs 5 -m "claude-haiku-4.5 claude-opus-4.8"
grounding run <unit> --root <target-repo> --source readme  --readme-file <target-repo>/README.md --runs 5 -m "..."
```

- **`--root`** (or the `GROUNDING_ROOT` env var) is the grounding root — the target repo. The
  `skill-validator` binary is still found in *this* repo's `.tools/`; only the grounding unit + eval are
  read from the target.
- **`--tests-dir` is auto-detected:** a co-located bundle (`grounding/<unit>/eval.yaml`) resolves to
  `grounding`; the classic split layout (`tests/<unit>/eval.yaml`) resolves to `tests`. Override with
  `--tests-dir` if needed.
- The harness synthesizes the `plugin.json` and `SKILL.md` wrapper the validator needs and cleans them
  up on exit, so the target repo keeps only its grounding inputs — no harness scaffolding, no artifacts
  left behind.
- **`--source`** selects the force-fed content arm: `agents` (Missing Manual), `readme` (Brochure, with
  `--readme-file`), or `none` (a no-grounding control). The baseline arm always runs alongside.

If the target repo's bundle includes `run.sh` / `run.ps1`, those already wrap the above — run them from
the package repo.

## Clean-content hygiene

For a content measurement, scrub `~/.dotnet/tools` from the agent's PATH so `dotnet-inspect` can't
substitute for the document (tool availability is a separate lever). Verify `di == 0` on the content
arms in the table.

## Read the result

Datasets are regenerable outputs and are **not** written to any repo. They land in the grounding cache —
`$GROUNDING_DATA_DIR`, else `$XDG_CACHE_HOME/grounding`, else `~/.cache/grounding/<unit>-6q/` (override
per run with `--out`). The cache is per-machine; delete it freely and re-run.

```bash
DATA="${GROUNDING_DATA_DIR:-${XDG_CACHE_HOME:-$HOME/.cache}/grounding}/<unit>-6q"
grounding analyze          "$DATA/<unit>.<model>.json"   # full table (baseline + content arms)
grounding analyze --card   "$DATA/<unit>.haiku.json" "$DATA/<unit>.opus.json"   # the PR dump (BETTER / NEUTRAL / WORSE)
```

The distilled card goes in the PR body; the prose summary lives in the bundle `README.md`. See
[`scoring.md`](./scoring.md) for the metrics and [`grounding-lifecycle.md`](./grounding-lifecycle.md)
for the ship gate.

## Running elsewhere

There is no special "other machine" path — running eval is always "clone this harness, build
`skill-validator` for the platform, point `--root` at the target repo." Datasets are machine-local and
regenerable, so each machine produces its own cache; only the inputs (in the target repo) and the
distilled card (in the PR) are shared.
