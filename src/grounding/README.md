# grounding — eval orchestration & analysis (C#)

A single Native-AOT CLI that ports this repo's Python/shell tooling. It drives the
external `skill-validator` harness and renders the grounding metric cards. The eval
engine (`skill-validator`) stays external; build it once via `eng/run-evals.sh`.

Build / run:

```bash
dotnet build src/grounding -c Release
dotnet src/grounding/bin/Release/net11.0/grounding.dll --help
eng/grounding --help            # launcher: builds once, then forwards args
```

## Install as a native tool on PATH

The CLI is **Native AOT**. Publish the native binary and drop it into the
dotnet tools dir (already on PATH) so `grounding` runs without `dotnet run`:

```bash
eng/install-grounding.sh        # publish AOT + copy to ~/.dotnet/tools/grounding
grounding --help                # now a bare command, anywhere
```

Conventional (framework-dependent) global-tool route, if preferred:

```bash
dotnet pack src/grounding -c Release
dotnet tool install --global --add-source src/grounding/nupkg dotnet-package-grounding
```

## Commands

| Command | Notes |
| --- | --- |
| `analyze <results.json...>` | default = raw per-scenario table |
| `analyze --card / --doc-card / --model-diff / --source-diff / --skill-diff / --tools-card / --web-card` | also `-v <view>`; `--no-title` supported |
| `run <unit> --source agents\|readme\|none` | README/AGENTS/nothing toggle; `--dry-run`, `--emit-skill` |
| `check-agents` | validate every `grounding/<unit>/AGENTS.md` is within the line budget |
| `gen-plugins` | expand `grounding/**/plugin.json.in` |
| `rescore <model=path>… [--w N]` | IET rubric, Pareto gate |
| `rescore --all` | batch over `.skill-validator-results/` |
| `channels extract [dir]` | per-model channel matrix (default dir `data/markout`) |
| `channels compare` | cross-channel IET (data/markout) |
| `mcp [--root <repo>]` | stdio JSON-RPC server (`GROUNDING_GATE`) |

This CLI is the single implementation of the repo's eval tooling.

## Source toggle (README / AGENTS / nothing)

`run --source` is the first-class toggle for *what fills the grounded arm*:

```bash
grounding run nugetfetch --source agents --model "claude-haiku-4.5 claude-opus-4.8" --runs 3
grounding run nugetfetch --source readme --readme-file path/to/README.md
grounding run nugetfetch --source none
grounding run nugetfetch --source agents --dry-run      # print the plan only
grounding run nugetfetch --source agents --emit-skill /tmp/SKILL.md
```

`run` reversibly swaps `grounding/<unit>/SKILL.md` to the chosen source, invokes
`skill-validator`, copies `results.json` into `data/<unit>-6q/<tag>.json`
(`<unit>` / `<unit>-readme` / `<unit>-none`), restores `SKILL.md`, then prints the
table.

## Eval scripts

The harness scripts in `eng/` (`run-evals.sh`, `run-*-6q.sh`,
`run-channel-matrix.sh`) call this CLI through the `eng/grounding` launcher,
which builds the project once and forwards arguments. The MCP eval units spawn
the server via `dotnet <grounding.dll> mcp --root <repo>` (skill-validator's
command allowlist permits `dotnet`, not arbitrary binaries).
