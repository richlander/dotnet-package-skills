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

| Command | Replaces | Notes |
| --- | --- | --- |
| `analyze <results.json...>` | `eng/analyze-6q.py` | default = raw table |
| `analyze --card / --model-diff / --source-diff / --tools-card / --web-card` | `analyze-6q.py --card …` | also `-v <view>`; `--no-title` supported |
| `run <unit> --source agents\|readme\|none` | `eng/run-nugetfetch-6q.sh` | README/AGENTS/nothing toggle; `--dry-run`, `--emit-skill` |
| `sync-skill [--check]` | `eng/sync-skill.sh` | regenerate `grounding/<unit>/SKILL.md` |
| `gen-plugins` | `eng/gen-plugins.sh` | expand `plugin.json.in` |
| `rescore <model=path>… [--w N]` | `eng/rescore.py` | IET rubric, Pareto gate |
| `rescore --all` | `eng/rescore_all.py` | batch over `.skill-validator-results/` |
| `channels extract [dir]` | `eng/extract-channels.py` | default dir `data/markout` |
| `channels compare` | `eng/compare-channels.py` | cross-channel IET (data/markout) |
| `mcp` | `grounding/_mcp/grounding_mcp.py` | stdio JSON-RPC server (`GROUNDING_GATE`) |

Every command's output is verified byte-for-byte (or, for `mcp`, semantically
JSON-identical) against its Python/bash original.

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

## Migration

The legacy `eng/*.py` / `eng/*.sh` tools are now thin shims that delegate to this
CLI via `eng/grounding`, so existing callers and docs keep working unchanged. The
implementations live here. (`grounding/_mcp/grounding_mcp.py` is retained while the
MCP eval units still reference it directly; `grounding mcp` is the C# equivalent.)
