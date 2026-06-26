// Comments provide context and instructions; they are not included in the final PR
// Start with a terse list of changes; Some example changes may not apply at all or as worded
// All the data is examples.

- Adds/Improves `AGENTS.md` grounding information
- Improves `README.md` per grounding evaluation
- Grounding test tasks
- Updated version `x` -> `y`
- Evaluated content with [richlander/dotnet-package-grounding@3bd9749](https://github.com/richlander/dotnet-package-grounding/commit/3bd97493174063c38fd87937642cf17f05c1b09c)

## Metrics

> should we accept this change?

Grounding effectiveness is read off **three single-variable cards**, which were used to gate this change.

- Test iterations: `n=3`
- Test scenarios: `6`
- Models tested: `2`

### Effectiveness

> does grounding help *this* model?

- Baseline: no grounding
- AGENTS.md: `AGENTS.md` (~<tok>, via grounding tool)
- Evaluation model: `claude-haiku-4.5`
- Judge model: `claude-haiku-4.5`

| Metric | Baseline | AGENTS.md |
| --- | ---: | ---: |
| success (scenarios) | 5/6 | 6/6 |
| func passed (assertions) | 17/18 | 18/18 |
| resourcefulness (archaeology) | 35 | 0 |
| IET | 31276 | 17558 |
| output tok | 5782 | 1716 |
| cost | 7.75 | 2.28 |

> **Conclusion:** **BETTER** — success 6/6 vs 5/6, resourcefulness 35→0, IET -44%, cost -71%.

- Baseline: no grounding
- AGENTS.md: `AGENTS.md` (~<tok>, via grounding tool)
- Evaluation model: `claude-opus-4.8`
- Judge model: `claude-haiku-4.5`

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

- README.md: the package `README.md` (via grounding tool — typically far larger than `AGENTS.md`)
- AGENTS.md: `AGENTS.md` (~<tok>, via grounding tool)
- Evaluation model: `claude-haiku-4.5`
- Judge model: `claude-haiku-4.5`

_`claude-haiku-4.5` · Both surfaced via the grounding tool; baseline removed. Single column = AGENTS.md change vs README.md (− = AGENTS cheaper on cost metrics, + on success/func, lower resourcefulness = AGENTS more self-sufficient). The README is co-tested here as a usability artifact._

| Metric | AGENTS.md − README.md |
| --- | ---: |
| success (scenarios) | +2 (6/6) |
| func passed (assertions) | +1 (18/18) |
| resourcefulness (archaeology) | 0→0 |
| IET | -4% |
| output tok | +1% |
| cost | +7% |

> **Conclusion:** **BETTER** — success 6/6 vs 4/6, resourcefulness 0→0, IET -4%, cost +7% _(README arm is co-tested for usability, not a floor to beat)._

Note: `README.md` acts as the baseline; rows show the difference and the end state (same higher/lower targets apply). A `README.md` that cannot be used to answer all test questions is a signal to improve that file. When a strong `README.md` exists, `AGENTS.md` should win on efficiency, not correctness.

## Validation

```bash
dotnet pack src/<Package>/<Package>.csproj -c Release
unzip -l src/artifacts/package/release/<Package>.<y>.nupkg | grep -E 'AGENTS|README'   # both at root
```
Eval (in richlander/dotnet-package-grounding), on a clean box:
```bash
RUNS=3 MODELS=claude-haiku-4.5  eng/run-<unit>-6q.sh     # mini: expect BETTER
RUNS=3 MODELS=claude-opus-4.8   eng/run-<unit>-6q.sh     # frontier: expect not-WORSE
python3 eng/analyze-6q.py --card        data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>.n3.opus.json
python3 eng/analyze-6q.py --model-diff   data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>.n3.opus.json
python3 eng/analyze-6q.py --source-diff  data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>-readme.n3.haiku.json
```

## Grounding resources

- Methodology: [grounding-eval-methodology.md](https://github.com/richlander/dotnet-package-grounding/blob/main/docs/grounding-eval-methodology.md)
- Lifecycle playbook: [grounding-lifecycle.md](https://github.com/richlander/dotnet-package-grounding/blob/main/docs/grounding-lifecycle.md)
