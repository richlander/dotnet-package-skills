<!--
Comments provide context and instructions; they are not included in the final PR.
Start with a terse list of changes; some example changes may not apply, or apply differently.
The metric data below is an example (NuGetFetch).
-->

- Adds/Improves `AGENTS.md` grounding information
- Improves `README.md` per grounding evaluation ([usability gaps](<readme-issue>))
- Grounding test tasks — the questions this change is gated on: [`tests/<unit>/eval.yaml`](<eval-link>)
- Updated version `<x>` -> `<y>`
- Evaluated content with [richlander/dotnet-package-grounding@`<commit>`](https://github.com/richlander/dotnet-package-grounding/commit/<commit>)

## Metrics

> Should we accept this change?

Grounding effectiveness is read off **three single-variable cards**, which were used to gate this change.

- Test iterations: `n=3`
- Test scenarios: `6`
- Models tested: `claude-haiku-4.5`, `claude-opus-4.8`

### Effectiveness

> Does grounding help *this* model?

Run: `claude-haiku-4.5`; baseline (no grounding) vs `AGENTS.md` (`~<tok>`); judge `claude-haiku-4.5`

| Metric | Baseline | AGENTS.md |
| --- | ---: | ---: |
| success (scenarios) | 5/6 | 6/6 |
| func passed (assertions) | 17/18 | 18/18 |
| resourcefulness (archaeology) | 35 | 0 |
| IET | 31276 | 17558 |
| output tok | 5782 | 1716 |
| cost | 7.75 | 2.28 |

> **Conclusion:** **BETTER** — success 6/6 vs 5/6, resourcefulness 35→0, IET -44%, cost -71%.

Run: `claude-opus-4.8`; baseline (no grounding) vs `AGENTS.md` (`~<tok>`); judge `claude-haiku-4.5`

| Metric | Baseline | AGENTS.md |
| --- | ---: | ---: |
| success (scenarios) | 6/6 | 6/6 |
| func passed (assertions) | 18/18 | 18/18 |
| resourcefulness (archaeology) | 19 | 0 |
| IET | 29052 | 21967 |
| output tok | 4825 | 1488 |
| cost | 14.35 | 5.45 |

> **Conclusion:** **BETTER** — success 6/6 vs 6/6, resourcefulness 19→0, IET -24%, cost -62%.

Note: rows 1–2 are **correctness** (higher is better); row 3 is **resourcefulness** — the out-of-sandbox archaeology grounding eliminates (drive to 0); rows 4–6 are **cost** (lower is better).

### Model difference

> Does grounding improve performance (Pareto improvement)?

Run: `claude-haiku-4.5` and `claude-opus-4.8`; each grounded (`AGENTS.md`, `~<tok>`) vs its own baseline; judge `claude-haiku-4.5`

| Metric | `claude-haiku-4.5` | `claude-opus-4.8` |
| --- | ---: | ---: |
| success (scenarios) | +1 (6/6) | +0 (6/6) |
| func passed (assertions) | +1 (18/18) | +0 (18/18) |
| resourcefulness (archaeology) | 35→0 | 19→0 |
| IET | -44% | -24% |
| output tok | -70% | -69% |
| cost | -71% | -62% |
| **→ verdict** | **BETTER** — success 6/6 vs 5/6, resourcefulness 35→0, IET -44%, cost -71% | **BETTER** — success 6/6 vs 6/6, resourcefulness 19→0, IET -24%, cost -62% |

Note: each cell is the change vs that model's own baseline — correctness up, resourcefulness and cost down.

### Source-diff

> Is `AGENTS.md` effective relative to the existing `README.md`; also, should `README.md` be improved?

Run: `claude-haiku-4.5`; `AGENTS.md` (`~<tok>`) vs the package `README.md` (`~<tok>`); both via the grounding tool, baseline removed; judge `claude-haiku-4.5`

| Metric | AGENTS.md − README.md |
| --- | ---: |
| success (scenarios) | +2 (6/6) |
| func passed (assertions) | +1 (18/18) |
| resourcefulness (archaeology) | 0→0 |
| IET | -4% |
| output tok | +1% |
| cost | +7% |

> **Conclusion:** **BETTER** — success 6/6 vs 4/6, resourcefulness 0→0, IET -4%, cost +7%.

Note: the single column is **AGENTS.md − README.md**, same higher/lower targets as above. If `AGENTS.md` can't beat the package `README.md` here, the curated file isn't earning its remit; conversely, any question the README arm fails is a README usability bug to fix in the same PR. Full reading in the linked methodology/lifecycle docs.

## Analysis

<!-- One line: what grounding actually changes. State it from the transcripts, not a guess. -->
Grounding eliminates the resourcefulness (cache/web archaeology) the agent otherwise spends to reach the *same* correct API; on the weak tier it also rescues scenarios the ungrounded model fails.

## Caveats

The baseline self-grounds from the restored NuGet cache (README/AGENTS ship in the nupkg), so its resourcefulness is a **lower bound** — grounding's advantage is understated. Cache state is not a variable.

## Validation

```bash
dotnet pack src/<Package>/<Package>.csproj -c Release
unzip -l src/artifacts/package/release/<Package>.<y>.nupkg | grep -E 'AGENTS|README'   # both at root
```
Eval (in richlander/dotnet-package-grounding), on a clean box:
```bash
RUNS=3 MODELS=claude-haiku-4.5  eng/run-<unit>-6q.sh     # mini: expect BETTER
RUNS=3 MODELS=claude-opus-4.8   eng/run-<unit>-6q.sh     # frontier: expect not-WORSE
grounding analyze --card        data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>.n3.opus.json
grounding analyze --model-diff   data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>.n3.opus.json
grounding analyze --source-diff  data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>-readme.n3.haiku.json
```

## Grounding resources

- Test questions: [`tests/<unit>/eval.yaml`](<eval-link>); Datasets: `data/<unit>-6q/` (committed for `--baseline-from` reuse)
- Methodology: [grounding-eval-methodology.md](https://github.com/richlander/dotnet-package-grounding/blob/main/docs/grounding-eval-methodology.md)
- Lifecycle playbook: [grounding-lifecycle.md](https://github.com/richlander/dotnet-package-grounding/blob/main/docs/grounding-lifecycle.md)
