<!-- Canonical grounding-PR template. Replace <unit>/numbers; example data is NuGetFetch. -->

Grounding (`AGENTS.md`) for `<package>`, eval-validated and shipped with its tasks. `README.md` was improved with the same methodology (updated to satisfy all eval tasks). Supersedes the prior PR.

Results:

- `AGENTS.md` is **BETTER than no grounding on both tiers**
- vs an evaled README: **NEUTRAL on mini, BETTER on frontier** (cost + archaeology)

## Grounding effectiveness

_Each cell: baseline (no grounding) → `AGENTS.md` (~904 tok). Columns are models. Judge `claude-haiku-4.5`. Means across scenarios._

| Metric (goal) | `claude-haiku-4.5` | `claude-opus-4.8` |
| --- | ---: | ---: |
| success (scenarios) (+) | 5/6 → 6/6 | 6/6 → 6/6 |
| func passed (assertions) (+) | 17/18 → 18/18 | 18/18 → 18/18 |
| resourcefulness (archaeology) (-) | 23 → 1 | 21 → 0 |
| IET (-) | 29664 → 17702 | 29930 → 28515 |
| output tok (-) | 5543 → 1704 | 5226 → 1408 |
| cost (-) | 7.23 → 2.30 | 16.40 → 5.12 |
| **verdict** | **BETTER** | **BETTER** |

_**FAIL** = solved fewer (correctness regressed); **BETTER** = solved more / archaeology→0 / IET/cost cut ≥20%; **WORSE** = IET/cost/output inflated ≥20%; **NEUTRAL** = held. Archaeology, web, judge are signals, not gates._

## Model difference

_Each cell: `AGENTS.md` change vs that model's own baseline (count Δ; before→after for archaeology; % for IET/output/cost, − = cheaper). Columns are models. Judge `claude-haiku-4.5`. Means across scenarios._

| Metric (goal) | `claude-haiku-4.5` | `claude-opus-4.8` |
| --- | ---: | ---: |
| success (scenarios) (+) | +1 (6/6) | +0 (6/6) |
| func passed (assertions) (+) | +1 (18/18) | +0 (18/18) |
| resourcefulness (archaeology) (-) | 23→1 | 21→0 |
| IET (-) | -40% | -5% |
| output tok (-) | -69% | -73% |
| cost (-) | -68% | -69% |
| **verdict** | **BETTER** | **BETTER** |

## Comparison to README.md

_Each cell: `AGENTS.md` − `README.md`, both via the grounding tool, baseline removed (− = AGENTS cheaper; + on success/func; lower archaeology = AGENTS more self-sufficient). Columns are models. Judge `claude-haiku-4.5`. Means across scenarios._

| Metric (goal) | `claude-haiku-4.5` | `claude-opus-4.8` |
| --- | ---: | ---: |
| success (scenarios) (+) | +0 (6/6) | +0 (6/6) |
| func passed (assertions) (+) | +0 (18/18) | +0 (18/18) |
| resourcefulness (archaeology) (-) | 0→1 | 4→0 |
| IET (-) | -7% | +7% |
| output tok (-) | -11% | -26% |
| cost (-) | -10% | -23% |
| **verdict** | **NEUTRAL** | **BETTER** |

## Bundle

- `grounding/<unit>/` — AGENTS.md, TASKS.md, eval.yaml + fixtures, n=3 datasets, run.sh

## Validation

```bash
./grounding/<unit>/run.sh
```
