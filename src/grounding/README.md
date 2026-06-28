# grounding — eval orchestration & analysis (C#)

A single Native-AOT CLI that ports the repo's Python/shell tooling. It drives the
external `skill-validator` harness and renders the grounding metric cards.

Build / run:

```bash
dotnet build src/grounding -c Release
dotnet src/grounding/bin/Release/net11.0/grounding.dll --help
# or publish a native binary:
dotnet publish src/grounding -c Release -r osx-arm64   # -> .../publish/grounding
```

## analyze — metric cards from results.json

```bash
grounding analyze <results.json...>                 # raw per-scenario table
grounding analyze -v card        a.json b.json      # primary cards + legend
grounding analyze -v model-diff  haiku.json opus.json
grounding analyze -v source-diff agents.json readme.json   # 'readme' in the filename = README arm
grounding analyze -v card --no-title a.json         # omit ### heading (for embedding)
```

Output is byte-for-byte identical to the legacy `eng/analyze-6q.py`.

## run — test a unit with README / AGENTS / nothing

`--source` is the first-class toggle for *what fills the grounded arm*:

```bash
grounding run nugetfetch --source agents --model "claude-haiku-4.5 claude-opus-4.8" --runs 3
grounding run nugetfetch --source readme --readme-file path/to/README.md   # README arm
grounding run nugetfetch --source none                                     # empty grounding
grounding run nugetfetch --source agents --dry-run                         # print the plan only
grounding run nugetfetch --source agents --emit-skill /tmp/SKILL.md        # inspect generated SKILL.md
```

`run` reversibly swaps `grounding/<unit>/SKILL.md` to the chosen source, invokes
`skill-validator`, copies `results.json` into `data/<unit>-6q/<tag>.json`
(`<unit>` / `<unit>-readme` / `<unit>-none`), restores `SKILL.md`, then prints the
table. The `agents` SKILL.md it generates matches `eng/sync-skill.sh` exactly.

The eval engine (`skill-validator`) stays external; build it once via
`eng/run-evals.sh`.
