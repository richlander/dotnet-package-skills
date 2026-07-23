# Quality-card measurement model

The **ratified conceptual model** behind the grounding eval — the head-to-head (H2H) comparison of an
**ungrounded** agent (baseline) against a **grounded** agent (skill docs on the shelf) over a fixed
suite of coding tasks. This document defines *what we measure and why*. It is **model-only**: no code.

The model has a **reference visualization** — the Levelized-IET (LIET) chart (`analyze --view liet`,
`docs/liet.md`) — which already embodies the sound methodology (per-task pairing, difficulty on the
x-axis, coverage tiers). **The card is the tabular *superset* of that chart**; where the two ever
disagree, the chart's methodology is canonical and the card is wrong.

Its **row-level companion** is `quality-card-spec.md` — the approachable Label·Equation·Example·Description
table for each card row, which *derives from* this model.

## Guiding principle — only expose comparable values

Every number on the card must support a **meaningful, intuitive comparison**; we omit any figure that
invites a misleading one. The cautionary case is the flat per-task mean (`Σᵢ mᵢ / N`): the `N` tasks
span two very different classes (solved vs. unsolved, easy vs. hard), so averaging across all of them
compares nothing. Every metric below is chosen so that what a reader compares is **like-for-like** —
paired per task, on shared sets, difficulty controlled — and anything that cannot be compared cleanly
is either decomposed until it can be, or left off.

## The two axes

We assess grounding the way you assess any investment: **return** (adjusted for how much you can
trust it) and **cost** (understood at depth, per unit delivered). Neither alone decides it.

- **Axis 1 — Risk-adjusted return.** How much more does grounding *win*, and how much should I trust
  that given only `k` runs? ("You say n=5 — will I see this again?")
- **Axis 2 — Levelized cost / yield.** What does it cost to bring *one sellable win* to market —
  retry tax and entry fee included — versus the alternative?

## Setup, the unit, and graded yield

- `N` tasks, indexed `i`. Two arms: `b` = **baseline** (ungrounded), `g` = **grounded**.
- Each `(task, arm)` is run `k` times as a **fixed batch** (here `k = 5`) — *not* "retry until first
  success then stop." Fixed-batch is what the harness does and makes reliability order-independent
  (only *how many* runs passed matters, not which).
- **The unit = a full-price unit.** A single run yields a full-price unit iff it is a **clean pass**:
  every functional assertion passes, **ends *and* means** (correct output *and* used the taught API,
  not a hand-rolled equivalent). Anything else is scrap.
- **Grading is graded yield, not binary.** For `(task i, arm x)`:
  - `Kᵢˣ ∈ {0 … k}` = number of runs that produced a full-price unit; **yield** `pᵢˣ = Kᵢˣ / k`.
  - **"Failed" means `Kᵢˣ = 0` only.** Any `Kᵢˣ ≥ 1` is a **productive** task at reliability `pᵢˣ`.
    A `2/5` task is *not* a failure — it produced two real units with real, measurable cost; it is a
    low-yield productive task. Collapsing `3/5` and `5/5` to one "passed" bit, or dropping `2/5` as
    "failed," throws away signal — so a task's outcome is its **yield**, not a bit.
  - Run cost `cᵢˣ,ᵣ` in the chosen currency. **IET (tokens) is primary**; turns and wall-clock are
    secondary views of the same shape.
- **Binning is a *different* axis — the deferred richer grade.** Graded yield (above) grades a unit
  across its `k` reruns against **one** full-price gate. *Binning* instead grades **within a single run**
  by bucketing the functional assertions into **value tiers** (e.g. a "core-correctness" tier and a
  "preferred-approach" tier). A run that passes all tiers is full-price; one that passes the core tier
  but only partially satisfies a higher tier is a **second** (right answer, wrong approach); zero core
  is scrap. So the grade is a function of *which* tiers are completely vs. partially true — not of how
  many reruns passed. A single equal-weighted tier (all assertions count the same) is the one-tier case
  of binning, so a full-price gate composes cleanly with tiers whenever they are added. The two axes
  compose: you could bin each run into tiers **and** take yield over the reruns.

## Axis 1 — Risk-adjusted return

**Return = reliable winning.** With `k` runs, "solved" is a *rate* (yield `pᵢˣ`), not a bit.

```
Pₓ = (1/N) · Σᵢ pᵢˣ          # arm's expected win rate on a random run
ΔP = Pᵍ − Pᵇ                 # the return grounding delivers
```

**Risk = how much to trust it on only `k` runs** — the "n=5, will I see this again?" question.

- *Per task*, five runs is a tiny poll: `5/5` does **not** prove `p = 1` (0 failures in 5 is still
  consistent with a true failure rate near ½). So a single task's dot carries a **wide** band that
  never collapses to "certain," and `3/5` vs `5/5` are distinguished — not merged.
- *For the suite*, the aggregate pools `N·k` observations (24×5 = 120), so `ΔP` is **sharp** even
  though any single dot is fuzzy. The per-task band does **not** cap the suite conclusion.
- Inference is **paired**: compute the per-task change `Δpᵢ = pᵢᵍ − pᵢᵇ` (same task, same run budget,
  both arms) and aggregate those, using a pre-specified small-sample paired procedure (beta-binomial
  or paired bootstrap over `(task, replicate)` blocks) — **not** endpoint plug-in Wald variance.
  Pairing cancels each task's intrinsic difficulty, isolating grounding's effect.

  *Why each choice matters:*
  - **Pair, don't compare two suite averages.** Each task's difficulty hits both arms; subtracting
    *within* a task (`Δpᵢ`) cancels it, the way a crossover trial uses each patient as their own
    control. Comparing two pooled means instead lets difficulty leak into the error bar (and can even
    flip the sign — Simpson).
  - **Never plug-in Wald.** The textbook SE `√(p̂(1−p̂)/k)` **collapses to 0 at `5/5` or `0/5`** — it
    claims certainty exactly where five runs prove least (0 failures in 5 is still consistent with a
    true failure rate near ½). The honest band there is *wide*; Wald reports a point.
  - **Beta-binomial** fixes the boundary: a `5/5` under a uniform prior is `Beta(6,1)` (mean ≈ 0.86,
    still spread), so the band never collapses; propagate each task's posterior into `Δpᵢ`.
  - **Paired bootstrap over `(task, replicate)` blocks** captures *two* nested sources of luck —
    which **tasks** we happened to sample (redraw the 24 tasks; the dominant uncertainty for "does
    grounding help *in general*?") and which **reruns** we happened to get (redraw the 5 outcomes).
    Resampling whole task-blocks keeps the two arms tied together inside each drawn task, preserving
    the pairing.

> **What the risk band is *for*.** It is the **noise ruler** for the verdict: it sets how large a
> `ΔP` (or cost gap) must clear to count as a result rather than luck. Two wins with the *same*
> average premium are **not** the same buy. A tight-band `5/5 → 5/5` win is **TIPS** — the premium is
> banked, it shows up on every run. A wide-band, high-variance win is **MSFT** — the average looks
> great, but the next five runs might not repeat it. Same headline return, different risk: the
> volatile win is discounted toward its downside, not celebrated at face value.

**Regression is cross-arm, not temporal.** We never compare to "yesterday"; we compare **baseline vs
grounded on the same task, both at n=5**. A regression is a materially negative `Δpᵢ` (e.g. baseline
4/5 → grounded 0/5) — measured by yield movement past a noninferiority margin, **not** a brittle
"was-5/5-now-isn't" veto (which fires ~99.9% on pure noise across 24 tasks and misses real `4/5→0/5`
collapses). Report positive/negative yield mass: `Σ max(±Δpᵢ, 0)`.

## Axis 2 — Levelized cost / yield

The cost of a win, done like a **levelized cost of electricity (LCOE)** / manufacturing
**cost-per-good-unit**: count the scrap from a *yielding* run as part of that unit's price, amortize
the fixed entry cost, and benchmark difficulty-for-difficulty against the alternative.

**Per task, per arm — realized fixed-batch levelized cost:**

```
Lᵢˣ = ( Σᵣ cᵢˣ,ᵣ ) / Kᵢˣ            # all k runs' cost ÷ full-price units;  undefined when Kᵢˣ = 0
```

The failed runs of a *yielding* task sit in the numerator — the **retry tax** — so unreliability
raises the price automatically (`Lᵢˣ = c̄ / p`). (Statistically this is the *observed* batch cost, not
an unbiased forecast of the deployment `c/p`; treat it as measured, not predicted.)

**The task is the boundary** (the line that keeps the metric honest):

- **Retries *within* a yielding task are IN** — scrap from a batch that produced good units is part of
  that unit's cost (`K = 3/5` → the 2 failed runs count).
- **A wholly-failed task (`Kᵢˣ = 0`) is OUT of cost** — no unit exists, so it is not "an expensive
  unit." It is a **capability miss** and belongs to Axis 1 / the coverage scoreboard, never the cost
  numerator. This is what "you don't count items you didn't buy" means, and why we **reject
  per-all-tasks blending** (dividing a suite's whole cost by all `N`).

**Difficulty control — compare on the same shopping list (this is the Simpson guard).** A raw pooled
`Σcost/ΣK` over each arm's *own* productive set is **unsound**: the arms make different mixes of easy
(cheap) and hard (expensive) units, and the mix — not the prices — can flip the average. (Numeric:
grounded pricier on *both* of two tasks, yet pooled `Lᵍ=$18.5 < Lᵇ=$83.5` because the arms' unit
mixes differ.) The verdict cost comparison must therefore be **per-task paired on the shared
productive set, equal-weighted**:

```
shared set S = { i : Kᵢᵇ ≥ 1 and Kᵢᵍ ≥ 1 }
compare, per i∈S, Lᵢᵍ vs Lᵢᵇ  (equal weight 1/|S|, or the paired mean log-cost ratio)
```

This is exactly **the gap between the grounded and baseline curves where both plot** on the LIET
chart — difficulty already controlled because the same task is identically hard for both arms.
Own-set pooled `Lˣ` is kept only as a descriptive fleet statistic. Axis 2 carries its **own** paired
uncertainty band (resampling cost and outcome jointly); it cannot borrow the Axis-1 band. *(Binning
by difficulty tier would later summarize this gap as a difficulty→cost curve — deferred.)*

**Entry fee (the membership) — toll vs. membership regime.** Grounding's fixed skill-load cost is
incurred inside each grounded run, so it is already in the numerator. **The regime must be stated:**
the current harness runs a **fresh session per task**, so the fee is paid every task — a **per-trip
toll**, not an amortized membership. The card reports the measured **toll** primarily; the membership
economics (one load, many wins) appear only as a **modeled reuse curve + break-even reuse count**,
never blended into the empirical verdict.

**Benchmark vs the alternative.** A unit price is meaningful only against the alternative's price for
the *same* unit (baseline = the competing store; other models/configs are further alternatives). On
grounded-only units (`Kᵢᵇ = 0 < Kᵢᵍ`) there is no baseline price to compare — that is a **capability
win** (coverage), reported as such, not as a price ratio. (A `0/5` baseline is *observed
nonproduction this batch*, not proven impossibility.)

## The coverage scoreboard

Aggregate outcomes are shown as the **four-way paired decomposition — as rows, never a single netted
number** (so `+14 wins, −2 losses` cannot masquerade as `+12`):

| Cell | Meaning |
| --- | --- |
| **both productive** | `Kᵢᵇ ≥ 1` and `Kᵢᵍ ≥ 1` — the shared set (where cost is compared) |
| **grounded-only** | `Kᵢᵇ = 0 < Kᵢᵍ` — new capability wins |
| **baseline-only** | `Kᵢᵍ = 0 < Kᵢᵇ` — **regressions / lost coverage (a gate)** |
| **neither** | both `0` — out of reach for both |

These rows are the LIET chart's **three difficulty tiers, counted** (baseline-correct / grounded-only
unlock / neither). Each cell may carry average yield (e.g. "grounded-only: 5 tasks, avg 4.2/5"). The
familiar headline `correct 9→21` is the only synthetic top-line, and these rows explain it.

## The verdict (to be litigated)

The verdict is **not** a suite-level either/or to adjudicate — it is a **tally of per-task grades**,
read straight off the coverage scoreboard. Each task earns a grade from the two axes (each move judged
against its own band, so noise isn't counted as a win):

| Grade | Condition |
| --- | --- |
| **Strong win** | grounded better on *both* axes — more reliable **and** cheaper, each beyond its band |
| **Half win** | better on one axis beyond its band, holding (noninferior) on the other: cheaper-at-held-reliability, or more-reliable-at-held-cost |
| **Wash** | neither axis moves past its band |
| **Regression** | materially worse on an axis (a baseline-only coverage loss is the severe case) |

The aggregate is the tally itself — e.g. `22 strong · 2 half · 0 regression` — scoped to **runtime
economics** (authoring/maintenance is a separate deployment scenario, not folded in). No synthetic
combined score: the rows speak.

Two rules keep the tally honest:

1. **The capability gate is hard.** A material baseline-only regression disqualifies regardless of how
   many wins offset it — and because the scoreboard is rows, never a net, a lost task cannot hide
   behind the wins.
2. **Grade the axis that exists.** Reliability is graded on every task; cost only where both arms are
   productive (the shared set). A grounded-only unlock is graded on Axis 1 alone — no baseline price is
   fabricated for it.
3. **The bar is model-scoped.** Each card is one model, and the bar is declared per **model class**,
   because baseline capability differs. A **frontier** baseline may already sit near the ceiling, so
   grounding's win may be mostly **cost/reliability** — cost-led half/strong wins with few capability
   unlocks — and the verdict weighted accordingly. A **mini/weak** baseline may have real headroom, so
   grounding's win may be **capability** — grounded-only unlocks, with Axis 1 carrying the verdict.
   (These are expected tendencies, not laws — we have rigorous data on one library so far.) Never pool
   model classes into one tally; compare like-for-like (the model-diff view), and set each class's
   bands/margins to its own regime.

Predeclare the **primary currency** (IET), the per-axis **bands/margins** that separate a grade from
noise *(open — to be set from data + judgment, per model class)*, and the label for an **empty/thin
shared set** ("economics not estimable").

## Visualization — the reference (LIET chart)

The card derives from this chart; documenting it fixes the model's meaning in one picture.

- **x-axis = difficulty** (authored rung order); one **rung = one task**. Both arms plot at the same
  x — head-to-head on the same course. This *is* the per-task pairing and the difficulty control.
- **y-axis = IET** (cost); one **series per arm** (baseline, grounded, and optional oracle/README).
- A rung plots a value for an arm **only where that arm is productive**; the line **breaks over a gap**
  so no segment implies a win the arm never produced. Wall-clock is overlaid as a thin dashed
  companion on *every* rung (time is spent pass or fail); a twin **archaeology** chart shares the x.
- **Competitor envelope / ceiling** = min cost of the other productive arms — the ceiling grounded
  must stay under to "pay its way."
- **Three-tier ordering** (baseline-correct → grounded-only unlocks → neither) makes unlocks and
  regressions visually obvious — the coverage scoreboard, drawn.
- **Graded-yield encoding:** encode reliability on each dot — fill/opacity/size ∝ `Kᵢˣ/k`
  (5/5 solid, 2/5 faint) with an error band from run-to-run agreement — and plot **low-yield
  productive tasks** at their successful-run cost. Only `K = 0` is absent.

## Deliberate exclusions

- **No per-all-tasks blend** (`Σcost/N`) — mixes unsolved tasks into the denominator; meaningless.
- **No binary "pass/fail" collapse** — a task's outcome is its yield `Kᵢˣ/k`; `K = 0` is the only
  failure.
- **No shared subtracted floor.** Difficulty is controlled by *pairing* — each task against its own
  baseline — not by subtracting a suite-wide reference level. A shared floor fails two ways: it is one
  constant subtracted from both arms, so it cancels in every `Δ` (`ΔE ≡ ΔR`) and adjusts nothing; and a
  single task's distance to it is dominated by that task's own difficulty relative to the reference
  set — directionally right on aggregate, but never sharp per task.
- **No pharma-style failure attribution.** Successes and failures are **independent** tasks; failures
  are not precursors that fund the wins, so a wholly-failed task's cost is not charged against wins.
- **No causal "grounding replaced archaeology" claim.** We report the **outcomes** (IET ↓, duration ↓,
  correctness ↑, in some combination) and **assume** archaeology-replacement is the mechanism, showing
  the archaeology counts as supporting **context**, not proof.
- **No binning yet** — the deferred richer grade: bucketing assertions into value tiers and scoring a
  run by which tiers are completely vs. partially true (full-price / second / scrap). A different axis
  from graded yield; a single equal-weighted tier is its one-tier case.

## Analogy glossary

| Concept | Everyday analogy |
| --- | --- |
| Risk-adjusted return (Axis 1) | **TIPS vs MSFT:** an equal *headline* return isn't an equal buy — a banked, low-variance premium (TIPS = a `5/5→5/5` win) beats an equal-average but volatile one (MSFT = a win you might not see on the next five runs). |
| Trust on n=5 / "will I see it again?" | A poll of 5 people has a huge margin of error; 5/5 is not "always." |
| Reliability of the win | The kid driving to college: the reliable car vs. the one that might strand them halfway. |
| Graded yield (not binary) | A 2/5 batch still made 2 good parts — that's low yield, not "failure." |
| Expected cost per win (Axis 2) | Print shop / factory: run a batch of 5, some smudge — real cost per *usable* print = whole batch ÷ the good ones. |
| Difficulty control / mix guard | Mix-adjusted ASP: compare price item-by-item on the same shopping list, not blended receipts. |
| Entry fee + unit price vs alternatives | Costco: pay the membership, then compare per-item price to Safeway/QFC — amortizes only if you keep shopping there. |
| Retries as yield loss | First-pass yield / scrap & rework; `1/p` ≈ Number Needed to Treat; geometric expected trials to a success. |
| Capability win (baseline can't) | A store that doesn't stock the item this batch. |
