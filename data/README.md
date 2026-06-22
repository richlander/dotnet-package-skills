# Raw evaluation data

Canonical raw `results.json` files behind [`docs/recommendation.md`](../docs/recommendation.md)
and the per-package reports in [`docs/reports/`](../docs/reports/).

Each file is the unmodified skill-validator `results.json` for one (task × delivery-channel ×
model-tier) cell. Naming: `<task>/<channel>.<model>.json`.

## Delivery channels

| Ch | id | Delivery mechanism | AGENTS.md in package |
|----|----|--------------------|----------------------|
| A  | `raw-readme`     | raw package on disk (no MCP) | absent (reads README) |
| A′ | `raw-invisible`  | raw package on disk (no MCP) | **present** (still reads README — invisible) |
| B  | `nugetmcp-readme`| real NuGet.Mcp.Server `get_package_context` | absent (serves README) |
| C  | `nugetmcp-agents`| real NuGet.Mcp.Server `get_package_context` | **present** (serves AGENTS.md) |
| D  | `custommcp`      | our controlled grounding MCP (resident-index gate) | served on demand from curated grounding |
| E  | `inspect-readme` | `dotnet-inspect package <id>@<ver> --readme` CLI ([#960]) | absent (CLI serves README) |
| E′ | `inspect-agents` | `dotnet-inspect package <id>@<ver> --readme` CLI ([#960]) | **present** (CLI serves AGENTS.md) |

[#960]: https://github.com/richlander/dotnet-inspect/pull/960

Channels A and A′ are the **baseline** arm of the `*-realmcp` evals (cache AGENTS absent vs
present); B and C are the **plugin** arm of those same evals; D is the **plugin** arm of the
`*-custommcp` eval. So each `*-realmcp` run captures two channels at once. Channels E and E′ are
the **isolated** arm of the `prefer-dotnet-inspect` directive unit, captured the same way (cache
AGENTS absent vs present) — they are the CLI analog of B and C: the agent fetches the package's
shipped doc with the `dotnet-inspect` CLI instead of the NuGet MCP. They require a `dotnet-inspect`
with [#960] (>= 0.11.0) on PATH. Which doc the CLI actually served (E vs E′) is confirmable from
the tool's own provenance ([#965]): `dotnet-inspect package <id>@<ver> --readme --info` reports
`Readme | <path> (<bytes> B)` (e.g. `AGENTS.md (3390 B)` vs `README.md (18843 B)`) on **stderr**,
while stdout stays the raw document.

[#965]: https://github.com/richlander/dotnet-inspect/pull/965

The canonical `inspect-*.{opus,haiku}.json` reflect the **shipped (no-peek) directive** —
`--readme` fetches the whole doc in one call. An earlier variant that told the agent to peek
`--frontmatter` before pulling `--body` is archived as `inspect-*-peek.*.json`; it was measured out
because it never helped and inflated weak-tier README thrash (see the report's *Frontmatter peek*
section).

## How to regenerate

See [`eng/run-channel-matrix.sh`](../eng/run-channel-matrix.sh). Harness build is pinned by
`eng/skill-validator.sha`. For the cross-channel **IET** comparison — plus **HIET**
(Haiku-Equivalent IET: IET × input-price-vs-Haiku, Opus 15× / Sonnet 3× / Haiku 1×, the
dollar-comparable cross-tier view) and the cross-tier table — run
[`eng/compare-channels.py`](../eng/compare-channels.py); the writeup is
[`docs/reports/dotnet-inspect-channel.md`](../docs/reports/dotnet-inspect-channel.md).
