<!-- Canonical grounding-PR template. Replace <unit>/numbers; example data is illustrative.
     Omit the "Complete Textbook (SKILL.md)" section for packages that ship no SKILL.md.
     See docs/overview.md for the model this template renders. -->

Grounding for `<package>`, eval-validated and shipped with its tasks.

**What ships:** `README.md` (**Brochure**) + `AGENTS.md` (**Missing Manual**), packed at the nupkg
root. `SKILL.md` (**Complete Textbook**) is a repo asset used as the eval **ceiling** — *not*
packed. The `README.md` was improved with the same methodology so it satisfies every eval task.
Supersedes the prior PR.

Results:

- `AGENTS.md` is **BETTER than no grounding** on both the mini and frontier model.
- vs the cared-for **Brochure**: **NEUTRAL on tasks, BETTER on cost/archaeology** (its always-on
  slot pays for itself in efficiency, not in a different answer).
- _[Complete Textbook only]_ `SKILL.md` clears **CT-24** where `AGENTS.md` falls off — the
  measured price of completeness.

## Effectiveness by tier

Nested task tiers — **BR-6 ⊂ MM-12 ⊂ CT-24** (day-1 common → day-100 niche). Each cell is
baseline (no grounding) → `AGENTS.md` (~`<tok>` tok): **tasks correct** and the **cost** cut.
Judge `<judge>`, n=`<n>`. (Cost = IET × input price; IET weights output 5x, cache read 0.1x — see
[docs/overview.md](../overview.md).)

| Tier | `claude-haiku-4.5` (mini) | `claude-opus-4.8` (frontier) | Verdict |
| --- | --- | --- | --- |
| **BR-6** (smoke) | 5/6 -> 6/6 - cost -68% | 6/6 -> 6/6 - -69% | **BETTER** |
| **MM-12** (remit + overfit guard) | 11/12 -> 12/12 - -61% | 12/12 -> 12/12 - -40% | **BETTER** |

_**FAIL** = fewer tasks correct; **BETTER** = more tasks correct / archaeology->0 / cost cut >=20%;
**WORSE** = cost/output inflated >=20%; **NEUTRAL** = held. Archaeology, web, judge are signals, not
gates._

### MM-12 detail (the remit)

_baseline -> `AGENTS.md` (~`<tok>` tok). Columns are models. Means across scenarios._

| Metric (goal) | `claude-haiku-4.5` | `claude-opus-4.8` |
| --- | ---: | ---: |
| tasks correct (+) | 11/12 -> 12/12 | 12/12 -> 12/12 |
| func passed (assertions) (+) | 35/36 -> 36/36 | 36/36 -> 36/36 |
| nuget-cache reads (archaeology) (-) | 38 -> 0 | 17 -> 0 |
| tool calls: web / bash / other (context) | 4/61/74 -> 0/24/66 | 2/48/70 -> 0/21/64 |
| grounding load (tok) (context) | 0 -> `<tok>` | 0 -> `<tok>` |
| read grounding (%) | 0% -> 100% | 0% -> 100% |
| output tok (% of IET) (-) | 4602 (27%) -> 1661 (25%) | ... |
| tool-call turns (% of total) (-) | 22 (95%) -> 9 (88%) | ... |
| Session turns (-) | 23 -> 10 | ... |
| Session IET (-) | 24977 -> 15905 | ... |
| Session Cost (-) | 5.90 -> 1.98 | ... |
| **verdict** | **BETTER** | **BETTER** |

## Comparison to README.md (Brochure)

_`AGENTS.md` - `README.md`, both grounded, baseline removed (- = AGENTS cheaper; + on tasks/func;
lower archaeology = AGENTS more self-sufficient). At MM-12._

| Metric (goal) | `claude-haiku-4.5` | `claude-opus-4.8` |
| --- | ---: | ---: |
| tasks correct (+) | +0 (12/12) | +0 (12/12) |
| func passed (assertions) (+) | +0 (36/36) | +0 (36/36) |
| nuget-cache reads (archaeology) (-) | 2 -> 1 | 4 -> 0 |
| Session IET (-) | +2% | +7% |
| Session Cost (-) | +7% | -23% |
| **verdict** | **NEUTRAL** | **BETTER** |

Both reach the ceiling — the README is a real usability test and it passes. `AGENTS.md`'s edge is
efficiency (fewer tokens, less archaeology), which shows most on the frontier.

## Complete Textbook (SKILL.md) — optional, if the surface warrants

_Include only if the package's surface is broad enough to leave a **CT-24** tail the compact
`AGENTS.md` can't reach. `SKILL.md` is a repo asset (the eval ceiling), not packed. At CT-24:
`AGENTS.md` vs `SKILL.md`, on the steadier frontier model per the protocol._

| Tier / arm | tasks correct | archaeology | cost |
| --- | --- | --- | --- |
| CT-24 - `AGENTS.md` | `<a>`/24 | `<a>` | `<a>` |
| CT-24 - `SKILL.md` (ceiling) | 24/24 | 0 | `<s>` |

The `SKILL.md` - `AGENTS.md` gap at CT-24 is the **measured price of completeness** — the
justification for maintaining a Textbook at all. If the gap is small, the compact `AGENTS.md`
suffices and no Textbook is warranted (record that finding and omit this section).

## Bundle

- `grounding/<unit>/` — `AGENTS.md`, `TASKS.md`, `eval.yaml` + fixtures, n>=3 datasets, `run.sh`.
- `SKILL.md` (Complete Textbook), if present, lives at the **repo root** as the eval ceiling — not
  in the packed grounding.

## Validation

```bash
./grounding/<unit>/run.sh
```
