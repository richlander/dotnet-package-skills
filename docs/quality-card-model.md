# Quality-card measurement model

The **ratified conceptual model** behind the grounding eval — the head-to-head (H2H) comparison of an
**ungrounded** agent (baseline) against a **grounded** agent (skill docs on the shelf) over a fixed
suite of coding tasks. This document defines *what we measure and why*. It is **model-only**: no code.

The model has a **reference visualization** — the Levelized-IET (LIET) chart (`analyze --view liet`,
`docs/liet.md`) — which already embodies the sound methodology (per-task pairing, difficulty on the
x-axis, coverage tiers). **The card is the tabular *superset* of that chart.** Canonicality is
**scoped**: where the two disagree on the **methodology of comparison** (pairing, the difficulty axis,
coverage tiers, the competitor-envelope construction), the chart is canonical and the card is wrong;
but on **verdict and regression rules** *this model is canonical* — `liet.md` predates the ladder and
still frames regression as a brittle per-rung veto, which the materiality-gated gate here supersedes.
`liet.md` also draws a **third (oracle) series** the two-arm card omits; see spec Invariant 7 for that
carve-out.

Its **row-level companion** is `quality-card-spec.md` — the approachable Label·Equation·Example·Description
table for each card row, which *derives from* this model.

## What grounding claims to deliver

Grounding — putting the right skill docs on the shelf — is asserted to change agent behavior in five
specific, **falsifiable** ways. The model exists to adjudicate each claim; every axis and row below
traces back to one of them. Claims differ in **how strong a verdict the data can carry**: **C2** and
**C3** are the two **margin-certified** claims — "delivered" only if the effect clears its
**predeclared margin** at the **suite level**, per model class. **C1** is reported **descriptively**
(a capability win has no competitor to ratio against), **C4** is an **independently reported** rate
(no certified band), and **C5** is a **memo** signal read alongside Axis 2 — each stated at its own
evidential strength, never over-claimed as a certified band.

| # | Claim | What it means | Where the model tests it |
| --- | --- | --- | --- |
| **C1** | **Capability** — grounding unlocks work the baseline did not produce | Tasks the ungrounded agent did not produce this batch (`Kᵇ = 0`) become productive under grounding (`Kᵍ ≥ 1`) | **Axis 1**, grounded-only partition — a capability win with **no competitor** (no cost ratio); reported as descriptive evidence, not a margin-certified band |
| **C2** | **Reliability** — grounding wins more *consistently* | Higher yield `pˣ = Kˣ/k`: flaky `2/5` wins become dependable `5/5` | **Axis 1**, `ΔP = Pᵍ − Pᵇ` judged against its risk band — **margin-certified** |
| **C3** | **Efficiency** — a **Delivered** unit costs less to produce | Lower **levelized** cost per full-price unit — retry tax and entry fee included | **Axis 2**, geometric-mean cost ratio on the shared set `S` — **margin-certified** |
| **C4** | **Fidelity** — grounding uses the *taught* approach, not a hand-rolled equivalent | On the `Fails → Satisfies → Delivers` ladder, more working runs reach **Delivers** (did it as asked), not just **Satisfies** (workable but hand-rolled) | **Independently reported:** the **per-task** `Delivers`-among-working rate — `Kᵢᵈᵉˡ / #{Satisfies ∪ Delivers}ᵢ` — **averaged equal-weight over tasks with ≥ 1 working run** (a *run-pooled* rate mix-weights tasks by working-run count and breaks Invariant 1 — the same Simpson trap the cost axis avoids; the pooled figure is kept only as a memo). Isolates fidelity from function; `Delivers` is also the full-price gate feeding every yield. Not estimable for a task with zero working runs |
| **C5** | **Predictability** — grounding makes cost *steadier*, not just lower | Lower run-to-run cost variance under grounding: `σ_g < σ_b` (arm-specific log-cost SD) | **Memo** read alongside Axis 2 — the variance ratio `σ_g/σ_b` with its band; a reported-not-gated signal (pooled `σ_within` sizes the margin; the *arm-specific* pair tests C5) |

Two guardrails ride alongside the claims, because "delivers value" is not the same as "does no harm":

- **No capability loss (hard gate).** Any task the baseline solved that grounding *breaks*
  (`Kᵇ ≥ 1`, `Kᵍ = 0`) is a **capability regression** — it disqualifies regardless of wins elsewhere.
  The gate trips on a **per-task** regression *or* on a **suite-level loss-mass band** (Axis 1), so
  diffuse flaky losses that no single task can prove still count.
- **No material cost regression.** Efficiency claims are **conditional on joint productivity**; a cost
  win bought by solving fewer things is not a win.

Everything downstream — the two axes, the coverage scoreboard, the graded verdict — is the apparatus
for turning these five claims and two guardrails into a defensible yes/no per model class.

## What this measures — the gradable scope

The claims above are only as trustworthy as the requirements they grade. This model measures one
**specific class** of requirement, and deliberately excludes another.

**In scope — objectively verifiable requirements.** Everything the ladder gates must be checkable from
the produced artifact by a deterministic test:

- **Approach / API selection** — *use the markout serializer*, *use `System.Text.Json`* (verifiable:
  the graded behavior is produced **via** the named surface — on the execution path, not merely
  referenced in source — and the hand-rolled equivalent is absent).
- **Technical constraints** — *NAOT-compatible*, trim-safe, target framework (verifiable at the
  strongest level: publish, run, treat trim/reflection warnings as errors).
- **Functional correctness** — the output is right (the existing assertions).

**Out of scope — subjective quality.** Idiom, elegance, "best practice," readability. *"Use the
serializer **in the idiomatic way**"* is a value judgment — not verifiable, judge-dependent, and the
most contestable grade a card could carry. It is **not gated and not reported**.

**The two are one rule seen twice.** A requirement is **in scope iff it can be both *stated in the
prompt* and *reduced to a test*.** "Use the markout serializer" clears both; "be idiomatic" clears
neither. This is the closed-contract invariant (next section) viewed from the requirement side — which
is why the scope boundary and the grading contract reinforce each other.

**A consequence, adopted deliberately: ugly-but-compliant *Delivers*.** A run that uses the serializer
correctly but gracelessly still earns `Delivers` — we grade *approach and constraints honored*, never
taste. This keeps every grade inspectable and un-attackable, at the cost of not rewarding elegance.
Elegance is a non-goal here, by design.

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
  that given only `k` runs? ("You say n=5 — will I see this again?") — adjudicates **C1 capability**
  and **C2 reliability**.
- **Axis 2 — Levelized cost / yield.** What does it cost to bring *one sellable win* to market —
  retry tax and entry fee included — versus the alternative? — adjudicates **C3 efficiency** (with
  **C5 predictability** as a memo alongside).

(**C4 fidelity** is not a separate axis — it lives in the **unit definition**: the `Fails → Satisfies →
Delivers` ladder gates yield on `Delivers` and reports the `Satisfies`-vs-`Delivers` split as the
fidelity signal.)

## Setup, the unit, and graded yield

- `N` tasks, indexed `i`. Two arms: `b` = **baseline** (ungrounded), `g` = **grounded**.
- Each `(task, arm)` is run `k` times as a **fixed batch** (here `k = 5`) — *not* "retry until first
  success then stop." Fixed-batch is what the harness does and makes reliability order-independent
  (only *how many* runs passed matters, not which).
- **The unit is graded on an ascending ladder — `Fails → Satisfies → Delivers`.** Each run is scored
  against **two gates**: does it *satisfy* (the output works — every functional-correctness assertion
  passes) and does it *deliver* (it did what was asked — used the taught API, not a hand-rolled
  equivalent). A run's grade is the **highest gate it clears**:
  - **Fails** — clears neither; the output does not work. Scrap.
  - **Satisfies** — clears the *satisfy* gate only: a **workable** solution, but not built the way the
    task asked. Credit for a working answer, but not the ask — a "second."
  - **Delivers** — clears both gates: works **and** as asked. This is the **full-price unit.**
- **Grading is graded yield, not binary.** For `(task i, arm x)`:
  - `Kᵢˣ ∈ {0 … k}` = number of runs that **Deliver** (the full-price unit); **yield** `pᵢˣ = Kᵢˣ / k`.
    Yield counts **Delivers only** — a `Satisfies` run is workable but is *not* a full-price unit.
  - **"Failed" means `Kᵢˣ = 0` only.** Any `Kᵢˣ ≥ 1` is a **productive** task at reliability `pᵢˣ`.
    A `2/5` task is *not* a failure — it produced two real units with real, measurable cost; it is a
    low-yield productive task. Collapsing `3/5` and `5/5` to one "passed" bit, or dropping `2/5` as
    "failed," throws away signal — so a task's outcome is its **yield**, not a bit.
  - Run cost `cᵢˣ,ᵣ` in the chosen currency. **IET (tokens) is primary**; turns and wall-clock are
    secondary views of the same shape.
- **This ladder *is* two-tier binning — now activated (claim C4).** Graded yield grades a unit across
  its `k` reruns; the `Satisfies`/`Delivers` gates grade **within a single run** by bucketing its
  assertions into two **value tiers** (works vs. as-asked). A run that clears both tiers is full-price;
  one that clears only *satisfy* is a `Satisfies` "second" (right answer, wrong approach); zero is
  scrap. The **fidelity claim C4** reads off exactly this split — the **per-task `Delivers` rate among
  {`Satisfies` ∪ `Delivers`} runs, averaged equal-weight over tasks with ≥ 1 working run** ("when it
  worked, did it work as asked?") — isolating fidelity from mere function. (Averaging *per task*, not
  pooling *over all runs*: a run-pooled rate would weight tasks by their working-run count and reopen
  the Simpson trap Invariant 1 forbids; the pooled number is a memo only.) The two axes compose
  cleanly: bin each run into tiers **and** take yield over the reruns. (We hold to **these two** tiers;
  further tier proliferation stays deferred.)

> **Capture (what the harness must persist).** The graded ladder needs, per `(task, arm, run)`, **two
> outcome bits** — `satisfies` (output works) and `delivers` (works as asked / taught API) — **and**
> the per-run cost. None is recoverable from the current artifacts: the results JSON aggregates each
> arm to one figure and keeps only the **last run's** assertions (`n = 1`), and while `sessions.db`
> holds per-run cost it does **not** persist per-run assertions. So the ladder requires (1) a harness
> change that writes per-run `{satisfies, delivers, iet, turns, sec}` arrays into the **results JSON**
> (self-contained, not host-local db state), then (2) a re-run. The example values below are therefore
> still the `n = 1` binary measurement — a single **functional-pass** bit (the `satisfies` gate),
> standing in as a **proxy** for `Delivers` until the `delivers` bit is captured — marked *(graded)*
> where they become richer once that capture lands.


## The grading contract — a closed prompt↔assertion loop

The two ladder gates (`satisfies`, `delivers`) are only fair if **everything a run is graded on is
inferable from the task prompt alone**. The skill's legitimate job is teaching *how* to satisfy the
contract efficiently — never *discovering what the contract is*. If a grade can be met only by content
the skill supplies, the baseline loses on **information asymmetry about the grading**, not on capability
— a non-comparable comparison the guiding principle forbids.

**The contract, made structural.** An **assertion** is a pair: a **mini-prompt** (a re-statement of the
prompt-subset it gates) plus an executable **test**. The suite is then audited by two judge passes,
**at design time, once** — never per run:

- `prompt → assertion` — **coverage**: is every **prompt clause gated by an assertion**? A gap does
  *not* distort yield — neither arm is graded on the ungated clause — it leaks through the **cost
  axis**: the grounded arm can shed the ungraded scope the skill deprioritizes while the baseline
  dutifully attempts it, buying a cost advantage with **no grade penalty**. That is the real fairness
  exposure of an ungated clause. Fix by gating the clause, or by narrowing the prompt so it no longer
  asks for it.
- `assertion → prompt` — **entailment**: is every assertion actually backed by the prompt? This pass
  returns a **three-way verdict** that *is* the conventions-vs-requirements adjudication:
  - **entailed** → a **requirement** → gate it at `Delivers`; a baseline that hand-rolls has
    *truthfully* failed to deliver (a legitimate model-gap finding).
  - **consistent-with but not entailed** → a **convention** → do **not** gate it at `Satisfies`; the
    `Satisfies` assertion must accept *any* working reading (hand-rolled included), and the idiom win is
    reported at `Delivers`/C4 — honest grounding credit, not a rigged gate.
  - **contradicted / unrelated** → a **task bug** → fix the task.

**The tier rule.** Whether "use the markout serializer" gates `Delivers` or only informs C4 is decided
by the prompt's wording, not by taste: name it in the prompt and it is a **requirement** (gated at
`Delivers`); leave it to the skill and it is a **convention** (`Satisfies` accepts hand-rolling, win
reported at C4). Both are in scope — both verifiable — the prompt just sets the tier. *(We say* tier
*here, not* rung*: `Satisfies`/`Delivers` are grade **tiers**; the LIET chart's difficulty **rungs**
are a different ladder.)*

**Why grading stays noise-free.** Because subjective quality is out of scope, every *gating* assertion
is a **deterministic test** — so the certified path (`K`, `S`, `ΔP`, cost ratio) is graded without judge
error, and the bootstrap's assumption that `delivers` is *observed without error* holds exactly. The
judges do real work, but only on the **design-time contract audit** (admitting or flagging tasks); that
audit's noise never enters `K`/`S`/`ΔP`. This is the deliberate resolution of the "judge noise outside
the bootstrap" risk — we removed the noise from the certified path rather than modeling it in.

**Two cheap standing audits** back the automated passes: a *competent-developer* read — holding only the
prompt, is the assertion-checked behavior entailed? — and a *judge-symmetry* read — a reviewer holding
only the prompt and two transcripts (no skill doc) must make the same `Delivers` calls, arm-symmetrically.

**Return = reliable winning.** With `k` runs, "solved" is a *rate* (yield `pᵢˣ`), not a bit.
Throughout Axis 1 the binomial **success** of a run is a **Delivers** (the full-price unit); a binomial
**failure** is any **non-delivery** — a run that only **Satisfies** *or* **Fails**. So "0 failures in
5" below means five Delivered runs, not merely five that ran.

```
Pₓ = (1/N) · Σᵢ pᵢˣ          # arm's expected win rate on a random run
ΔP = Pᵍ − Pᵇ                 # the raw return grounding delivers, all N tasks
ΔP|both = mean over both-productive tasks of (pᵢᵍ − pᵢᵇ)   # the certified C2 quantity
```

**Two ΔPs, because C1 and C2 are different claims.** The raw `ΔP` over all `N` gives a grounded-only
unlock (`pᵢᵇ = 0`) its full `pᵢᵍ`, so a **capability** win (C1, descriptive-only) would flow straight
into the **margin-certified reliability band** — letting C1 silently carry C2. To keep the claims
separate we **certify C2 on `ΔP|both`** — the paired yield gain restricted to the **both-productive
set** (`Kᵢᵇ ≥ 1 & Kᵢᵍ ≥ 1`), i.e. "wins more consistently *on work both arms can already do*." The raw
all-`N` `ΔP` is reported too, but as a **combined return** that mixes capability and reliability, not
as the C2 certificate. (Consistent with the rows-not-nets rule: unlocks are counted as C1 coverage,
not folded into the reliability margin.)

**Band ΔP|both from the same bootstrap, both-productive set recomputed per iteration.** Membership in
the both-productive set is itself a **noisy five-run outcome** (a `1/5` arm can draw `0/5` and drop the
task), so `ΔP|both` must not be banded as if the set were fixed. It reads from the **one nested
task→run bootstrap** below, with the both-productive set **recomputed each iteration** — exactly as the
cost axis recomputes `S*` — so a task that draws `Kᵇ* = 0` or `Kᵍ* = 0` leaves the set that draw. Like
Axis 2, `ΔP|both` is therefore **conditional on joint productivity**, and its two-sided exclusion is
**load-bearing**: dropping *both* grounded-only unlocks *and* baseline-only losses is precisely what
isolates the C2 quantity — the excluded tasks are owned by the C1 coverage row (unlocks) and the hard
gate (losses), never double-counted here.

**Risk = how much to trust it on only `k` runs** — the "n=5, will I see this again?" question. This
"risk" is the **within-batch sampling uncertainty** of the yield (how firmly `k` runs pin `pᵢˣ`); a
premium that *swings batch to batch* is a different, **cross-batch** volatility that a single `k`-run
batch cannot identify — detecting that would need repeated batches (a stated limitation). The
risk-adjustment is operational: the verdict judges `ΔP` against its **band lower bound**, not its point
estimate, so a wide-band win is discounted toward its downside.

- *Per task*, five runs is a tiny poll: `5/5` does **not** prove `p = 1` (0 failures in 5 is still
  consistent with a true failure rate near ½). So a single task's dot carries a **wide** band that
  never collapses to "certain," and `3/5` vs `5/5` are distinguished — not merged.
- *For the suite*, state the **estimand** — the **finite-suite** result is the one we certify; the
  task-population figure is a **sensitivity** read, not an external claim:
  - **Finite-suite** (*these* 24 tasks) — the confirmatory estimand: uncertainty comes only from the
    reruns, so the suite estimate is comparatively tight. This is what the verdict rests on.
  - **Task-population** (does grounding help *in general*?): the 5 reruns of a task are correlated, so
    the honest independent unit is the **task** — cluster at `n ≈ 24`, not `120`. This band is wider.
    But the 24 tasks are a **curated** suite, not a random sample of any population, so it generalizes
    only to *this* suite's task mix — report it as a sensitivity bound, never as "grounding helps
    everywhere." A genuine population claim would need a defined task-sampling frame we do not have.
  Either way the per-task band does **not** cap the suite conclusion.
- Inference is **paired at the task level**: compute the per-task change `Δpᵢ = pᵢᵍ − pᵢᵇ` (same task,
  same run budget) and aggregate those with **one** pre-specified small-sample procedure — a
  **beta-binomial posterior** (primary; boundary-safe), cross-checked by a **nested task→run bootstrap**
  (below) — **not** endpoint plug-in Wald variance. Pairing buys **precision** *when the two arms'
  per-task outcomes are positively correlated* (a hard task depresses both), removing that task's
  intrinsic difficulty from the *variance*; it never moves the point estimate. Runs are **not** matched
  across arms (no shared seed), so we pair whole tasks, never replicate indices.

  *Why each choice matters:*
  - **Pair, don't compare two suite averages.** Each task's difficulty hits both arms; subtracting
    *within* a task (`Δpᵢ`) removes it from the error bar, the way a crossover trial uses each patient
    as their own control. (In this balanced design pairing does **not** move the point estimate —
    `mean(pᵍ) − mean(pᵇ) = mean(Δpᵢ)` exactly — it only tightens the *variance*. The difficulty-mix
    *reversal* risk lives on **Axis 2**, where costs are pooled with unequal weights; see there.)
  - **Never plug-in Wald.** The textbook SE `√(p̂(1−p̂)/k)` **collapses to 0 at `5/5` or `0/5`** — it
    claims certainty exactly where five runs prove least (0 failures in 5 is still consistent with a
    true failure rate near ½). The honest band there is *wide*; Wald reports a point.
  - **Beta-binomial (primary) fixes the boundary:** a `5/5` under a uniform prior is `Beta(6,1)`
    (mean ≈ 0.86, still spread), so the band never collapses; propagate each task's posterior into
    `Δpᵢ` and aggregate across tasks. **Predeclare the prior — uniform `Beta(1,1)` — *before* the
    scoring run**, and report a **Jeffreys `Beta(½,½)` sensitivity** read alongside; if the verdict
    flips between the two, the verdict is about the prior, not the data. We keep **48 independent
    per-cell uniforms** rather than a **hierarchical partial-pooling** prior *on purpose*: the suite is
    *deliberately* heterogeneous in difficulty, so tasks are **not exchangeable within an arm** (the
    assumption pooling needs), and pooling would shrink a genuine grounded-only unlock (`pᵇ = 0`)
    toward the arm's mean — **diluting the C1 capability signal**, the sharpest finding on the card.
    The honest cure for small-`k` variance is **more data** (a higher-`k` pilot), not a stronger prior.
    *Bank the prior's own conservative bias:* the uniform prior shrinks a `5/5` toward `0.5` **harder**
    than a `3/5`, so when grounded is the high arm it **attenuates `ΔP`** — a bias that runs *against*
    grounding, alongside the `K ≥ 1` winner's curse below. Any positive result has cleared it.
  - **Nested task→run bootstrap (cross-check).** Draw the 24 **tasks** with replacement (the dominant
    uncertainty for a general claim), then *within* each drawn task redraw its runs as **joint
    `(delivered, cost)` draws** from that task's fitted per-arm model — resampling from each task's
    **posterior/parametric** yield paired with its log-cost distribution, **not** its 5 raw outcomes,
    so a `5/5` or `0/5` still carries spread (an empirical redraw of five identical outcomes is
    degenerate). This single pass regenerates `K*`, then `L* = Σcost*/K*`, then the shared set `S*` and
    the geometric ratio, so **both axes read from one resampling procedure**. The two arms stay tied
    inside each drawn task (task-level pairing); the shared set `S*` is **recomputed each iteration**,
    so a task that draws `K* = 0` drops out — its uncertainty correctly surfaces as an Axis-1 capability
    gap rather than an undefined cost. For the **confirmatory finite-suite** band, hold the 24 tasks
    fixed (redraw only runs); add the outer task redraw **only** for the task-population sensitivity
    read.

> **What the risk band is *for*.** It is the **noise ruler** for the verdict: it sets how large a
> `ΔP` (or cost gap) must clear to count as a result rather than luck. Two wins with the *same*
> average premium are **not** the same buy. A tight-band win — say a steady `2/5 → 5/5` that lands the
> same every batch — is **TIPS**: the premium is real and it shows up on every run. A win with the
> *same average* premium but a yield that swings run to run is **MSFT** — the average looks great, but
> the next five runs might not repeat it. Same headline return, different risk: the volatile win is
> discounted toward its downside, not celebrated at face value.

**Regression is cross-arm, not temporal.** We never compare to "yesterday"; we compare **baseline vs
grounded on the same task, both at n=5**. A regression is a materially negative `Δpᵢ` (e.g. baseline
4/5 → grounded 0/5) — measured by yield movement past a noninferiority margin, **not** a brittle
"was-5/5-now-isn't" veto (which, when many tasks sit near the ceiling, fires on almost every suite from
pure noise yet still misses real `4/5→0/5` collapses). Report positive/negative yield mass over **all**
tasks: `Σ max(±Δpᵢ, 0)` — the regression/gain signal, distinct from the *coverage* crossing counts.

**Aggregate loss-mass can also trip the hard gate.** Per-task `Δpᵢ` bands rarely exclude zero at
`k = 5`, so three individually-inconclusive `2/5 → 0/5` losses — collectively strong evidence of harm —
would each land in "flagged," and the hard gate never sees them. So put a **suite-level band on the
loss mass** `Σ max(−Δpᵢ, 0)` **over all `N` tasks** (the same all-tasks quantity Axis 1 already
reports — so a both-productive `5/5 → 2/5` slide counts, not only cell-crossing regressions; a diffuse
harm gate should see slides too) and let the gate **trip on it too** (`OR` with the per-task veto).
Two cautions keep it honest: (1) `Σ max(−Δpᵢ, 0)` is **truncation-biased** — it is `≥ 0` and positive
even under the null from noise alone, so calibrate the band against a **null-bootstrap** reference
(resample **both** arms of task `i` from a single pooled rate `p̃ᵢ = (Kᵢᵇ + Kᵢᵍ) / 2k`, giving `Δpᵢ = 0`
in expectation), **not** against literal zero, or it over-trips; (2) **predeclare the materiality
threshold** like every other margin. Erring toward disqualification is the safe way for a "do no harm"
guard to be wrong.

## Axis 2 — Levelized cost / yield

The cost of a win, done like a **levelized cost of electricity (LCOE)** / manufacturing
**cost-per-good-unit**: count the scrap from a *yielding* run as part of that unit's price, amortize
the fixed entry cost, and benchmark difficulty-for-difficulty against the alternative.

**Per task, per arm — realized fixed-batch levelized cost:**

```
Lᵢˣ = ( Σᵣ cᵢˣ,ᵣ ) / Kᵢˣ            # all k runs' cost ÷ full-price units;  undefined when Kᵢˣ = 0
```

The **non-delivered** runs of a *yielding* task sit in the numerator — the **retry tax** — so
unreliability raises the price automatically (`Lᵢˣ = c̄ / p`). A non-delivered run is any run that did
**not** produce a full-price unit — whether it **Failed** (didn't work) *or* only **Satisfied** (worked
but not as asked); both spent budget toward the eventual Delivered unit. (Statistically this is the
*observed* batch cost, not an unbiased forecast of the deployment `c/p`; treat it as measured, not
predicted.)

**Selection into `S` runs *against* grounding (bank it).** A task enters the shared set on `Kᵢˣ ≥ 1`,
so for a low-`p` arm the batches that qualify are the *lucky* ones: `K | K ≥ 1` is biased high versus
`k·p`, which biases `Lᵢˣ = Σc/K` **low** for a flaky arm. Baseline is usually the flakier arm, so its
levelized cost is the more downward-biased, making `rᵢ = Lᵢᵍ/Lᵢᵇ` biased **upward — against grounding**.
This "winner's curse" is therefore **conservative**: any efficiency win we certify has cleared a
selection effect tilted against it. (It also argues, again, for reporting cost only on `S` and never
imputing a price to `K = 0`.)

**The task is the boundary** (the line that keeps the metric honest):

- **Retries *within* a yielding task are IN** — every non-delivered run from a batch that produced good
  units is part of that unit's cost (`K = 3/5` → the 2 non-delivered runs count).
- **A task that delivered no unit (`Kᵢˣ = 0`) is OUT of cost** — no full-price unit exists, so it is not
  "an expensive unit." It is a **capability miss** and belongs to Axis 1 / the coverage scoreboard,
  never the cost numerator. This is what "you don't count items you didn't buy" means, and why we
  **reject
  per-all-tasks blending** (dividing a suite's whole cost by all `N`).

**Difficulty control — compare on the same shopping list (this is the Simpson guard).** A raw pooled
`Σcost/ΣK` over each arm's *own* productive set is **unsound**: the arms make different mixes of easy
(cheap) and hard (expensive) units, and the mix — not the prices — can flip the average. (Numeric:
grounded pricier on *both* of two tasks, yet pooled `Lᵍ=$18.5 < Lᵇ=$83.5` because the arms' unit
mixes differ.) The verdict cost comparison must therefore be **per-task paired on the shared
productive set, equal-weighted**:

```
shared set S = { i : Kᵢᵇ ≥ 1 and Kᵢᵍ ≥ 1 }
per-unit ratio   rᵢ = Lᵢᵍ / Lᵢᵇ   for i ∈ S
summary          = geometric mean of rᵢ  =  exp( (1/|S|) · Σᵢ ln rᵢ )   # equal weight per task
```

This is exactly **the gap between the grounded and baseline curves where both plot** on the LIET
chart — difficulty already controlled because the same task is identically hard for both arms.
Own-set pooled `Lˣ` is kept only as a descriptive fleet statistic.

**Why the geometric mean — the right tool for a typical multiplier.** We want the **typical
proportional effect**: if grounding makes one task `2×` more expensive and another `2×` cheaper
(`rᵢ = 2.0` and `0.5`), that is a **wash**, and the summary should say so. The geometric mean does:
`√(2.0·0.5) = 1.0`. The arithmetic mean reports `1.25` — a 25% loss — because it answers a *different*
question (the average ratio if you picked a task at random), which **misrepresents the break-even** and
lets one extreme task swing the number (the SPEC-benchmark result, *Fleming & Wallace, CACM 29(3),
1986*). For a "how much cheaper, typically?" multiplier the geometric mean is the correct summary: it is
**symmetric under reciprocal** (halving and doubling cancel), each task's arbitrary baseline cancels,
and every task carries equal weight in log-ratio space — consistent with our equal-weighting of tasks.

**Two questions, two means.** *What did the comparable work cost?* — costs share a unit (tokens), so
they **add**: the **Total IET**, summed over the **shared set `S`** where both arms produced. To keep
that total honest we take, per task/arm, the **median IET among that cell's Delivered runs** — one
representative "what a full-price unit costs" number, robust to the run-to-run tail and to
non-delivered cost (which the retry-tax `L` and Axis 1 already carry; we do not double-charge it here).
*How much cheaper is a unit, typically?* — that asks for a typical multiplier, whose right summary is
the **geometric mean** of the levelized ratios `rᵢ = Lᵢᵍ/Lᵢᵇ` (an arithmetic average answers a
different question — and note `rᵢ` is the ratio of the *levelized* `Lᵢˣ = Σcost/Kᵢˣ`, not a ratio of
the Total-IET median runs). Totals are arithmetic **sums of representative runs on `S`**; ratios are
geometric.

**Total IET is partitioned, never a black box.** Median-of-**Delivered** makes the total truthful but
only *comparable* where both arms produced, so Total IET is reported by productivity partition: the
head-to-head **Total IET lives on the shared set `S`** (Σ median-Delivered IET per arm — apples to
apples), while **grounded-only** spend (capability *investment*) and **baseline-only** spend appear as
**memo lines**, never blended into the comparison. Everything comparable sits on `S`; everything off-`S`
is capability-only. Even count of Delivered runs → **interpolated median** (mean of the two middle
runs): this **removes the even-`K` lower-median bias** (it targets the central-50% location
consistently) so a flaky low-`K` arm is not made to look artificially cheaper than a reliable high-`K`
arm (a "lower median" would bias toward whichever arm has fewer Delivered runs — usually baseline — and
quietly work against the efficiency claim). For the two central runs, average each run's **additive
components** (Skill, Work, output, tool IET) so `Total = Skill + Work` is preserved exactly — do not
recombine separately-averaged activation and turns.

Axis 2 carries its **own** paired uncertainty band from the **same** nested bootstrap as Axis 1 (draw
tasks, redraw each task's runs as joint `(delivered, cost)` draws, recompute `K*`, `L*`, and the shared
set `S*` each iteration). The shared set is **recomputed per iteration**, not frozen on the observed
`S`: a task that draws `K* = 0` for either arm simply leaves `S*` that iteration — its uncertainty
surfaces as an Axis-1 capability gap, never an undefined `L`. If `S*` is empty in an iteration (no
shared productive task), that iteration produces **no** Axis-2 ratio: exclude it from the ratio
quantiles but count it toward an **estimability rate**; if the non-estimable fraction exceeds a
predeclared threshold (e.g. 5% of iterations), report Axis 2 as **not estimable** rather than banding a
biased subset. The Axis-2 band
cannot borrow the Axis-1 band. *(Binning by difficulty tier would later summarize this gap as a
difficulty→cost curve — deferred.)*

**Entry fee (the membership) — toll vs. membership regime.** Grounding's fixed skill-load cost is
incurred inside each grounded run, so it is already in the numerator. **The regime must be stated:**
the current harness runs a **fresh session per task**, so the fee is paid every task — a **per-trip
toll**, not an amortized membership. The card reports the measured **toll** primarily; the membership
economics (one load, many wins) appear only as a **modeled reuse curve + break-even reuse count**,
never blended into the empirical verdict.

**Benchmark vs the alternative.** A unit price is meaningful only against the alternative's price for
the *same* unit — here the alternative is **baseline**, the other arm in this head-to-head. A
**grounded-only** unit (`Kᵢᵇ = 0 < Kᵢᵍ`) has **no competitor by definition**, so there is no price to
divide by: it is a **capability win** on Axis 1, and its cost is not a ratio at all. That cost is not
hidden, though — it lands in the **grounded-only memo line** of Total IET (its own partition), so an
expensive unlock is visible as capability *investment* without contaminating the shared-set comparison.
(A `0/5` baseline is *observed nonproduction this batch*, not proven impossibility.)

## The coverage scoreboard

Aggregate outcomes are shown as the **four-way paired decomposition — as rows, never a single netted
number** (so `+14 wins, −2 losses` cannot masquerade as `+12`):

| Cell | Meaning |
| --- | --- |
| **both productive** | `Kᵢᵇ ≥ 1` and `Kᵢᵍ ≥ 1` — the shared set (where cost is compared) |
| **grounded-only** | `Kᵢᵇ = 0 < Kᵢᵍ` — new capability wins |
| **baseline-only** | `Kᵢᵍ = 0 < Kᵢᵇ` — **regressions / lost coverage (a gate)** |
| **neither** | both `0` — out of reach for both |

These rows are the four cells of the LIET chart's **difficulty ordering, counted**. The ordering runs
**both-productive → grounded-only unlock → baseline-only regression → neither**; the `baseline-only`
group (grounded gapped where baseline produced) is a **visible regression tier**, drawn even when — as
in the example run — its count is `0`. Each cell may carry average yield (e.g. "grounded-only: 5 tasks,
avg 4.2/5").

Note the scoreboard and the headline are **two different lenses on the same runs, at two thresholds**,
not a decomposition of one by the other:
- **Productive coverage** (`K ≥ 1` — *can it ever produce the unit?*) drives the four-way scoreboard.
- **Reliably delivered** (`K ≥ τ`, bar `τ = 3/5` — *can it produce dependably?*) drives the headline
  `tasks reliably delivered`.
A `2/5 → 3/5` task moves the *reliably-delivered* count but is "both productive" on the scoreboard — the
two views legitimately disagree, and both are reported. Because the headline is a **hard threshold on
noisy binomials**, always print the **yield-mass movement `Σᵢ pᵢˣ`** (sum of posterior yields) beside
the thresholded count: a lone boundary cell flipping `2/5 ↔ 3/5` can move the count ±1 with essentially
no change in the certified quantity, and the mass keeps that from reading as real progress.

## The verdict (to be litigated)

The verdict is **not** a suite-level either/or to adjudicate — it is a **tally of per-task grades**,
read straight off the coverage scoreboard. Each **both-productive** task is classified by two moves —
the **yield move** (Axis 1) and the **cost move** (Axis 2) — each read against a **predeclared margin**
as *better*, *held*, or *worse*. The 3×3 grid is exhaustive and unambiguous:

| yield \ cost | cost better | cost held | cost worse |
| --- | --- | --- | --- |
| **yield better** | **strong** | **half** | **mixed** |
| **yield held** | **half** | **wash** | **regression** |
| **yield worse** | **mixed** | **regression** | **regression** |

A **grounded-only** unlock has no cost axis → **capability win** (Axis 1 only). A **baseline-only**
loss → the hard capability gate. **Mixed** is a genuine trade — one axis better, the other worse —
surfaced as its own grade, never buried inside a "win."

The aggregate is the tally itself — `<strong> · <half> · <mixed> · <wash> · <regression>` counts —
scoped to **runtime economics** (authoring/maintenance is a separate deployment scenario, not folded
in). No synthetic combined score: the rows speak. *(A real graded tally awaits per-run capture; the
current card's `n=1` binary example cannot populate it — and note that only the `both-productive`
tasks can earn a two-axis grade, since a grounded-only unlock has no cost axis.)*

Two rules keep the tally honest:

1. **The capability gate is hard.** A material baseline-only regression disqualifies regardless of how
   many wins offset it — and because the scoreboard is rows, never a net, a lost task cannot hide
   behind the wins. *Predeclare "material":* a baseline-only loss counts when the paired `Δpᵢ` is
   negative beyond the Axis-1 margin **and** its band excludes zero — a single `1/5 → 0/5` blip that
   the band cannot separate from noise is flagged for inspection, not an automatic gate trip.
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
4. **Grades are descriptive; trust lives at the suite level.** A per-task grade classifies *point
   estimates* (the **beta-binomial posterior means** for yield — the uniform-prior `Beta` posterior
   introduced above, which shrinks each cell toward `0.5`), it is **not** an independent significance
   test — grading 24 tasks each
   against a 95% band would manufacture roughly one false movement per suite from noise alone. The
   verdict's confidence comes from the **suite-level** bands: the paired `ΔP` (Axis 1) and the
   geometric-mean cost ratio (Axis 2). The tally *describes*; the suite bands *certify*.

Predeclare, **before the scoring run**, the **primary currency** (IET), the per-axis **margins** that
separate *better / held / worse*, and the label for an **empty/thin shared set** ("economics not
estimable" — predeclare *thin* as `|S| < 8` tasks, i.e. fewer than a third of the suite produce jointly,
so the geometric ratio's band is too wide to certify).

Each margin is a **practical floor** — the smallest move worth reporting — fixed ex ante per model
class and never fit to the observed outcome (a `−1%` IET change is not a result even if it is
"significant"). We deliberately do **not** add a separate "noise floor" into the margin: the
**suite-level band already carries the sampling uncertainty**, so inflating the margin by a noise term
would double-count it. The decision rule is *the suite band must clear the practical floor*, applied on
each axis in its **benefit direction** so the band bound faces the same way regardless of whether the
raw statistic is "higher-better" or "lower-better":
- **Axis 1 — yield (`ΔP`, higher-better):** superiority requires **lower bound `> +margin`**;
  noninferiority requires **lower bound `> −margin`**.
- **Axis 2 — cost ratio (`R = GM(rᵢ)`, lower-better):** work on the benefit scale `E = −ln R` (a
  *cost reduction*, higher-better) and apply the **same** lower-bound rule; equivalently, keep `R` and
  gate its **upper** bound — superiority `upper(R) < e^(−margin)`, noninferiority `upper(R) < e^(+margin)`.

A naïve "lower bound `> +margin`" applied to `R` itself would certify cost *increases* — the benefit
direction fixes that.

**How noise informs the floor (the formalism).** Run-to-run noise is a **single model-level
parameter**, not a per-task property — a per-cell CV from `k` reruns has only `k−1` degrees of freedom
and is untrustworthy. Estimate it *pooled* on the **log scale** (cost is positive and multiplicative):
decompose `log(cost)_{task,arm,run} = μ + task + arm + (task×arm) + ε`, and take the pooled
within-cell residual **`σ_within`** across all cells — one number per model, well-identified (the mined
`k = 3`, 3-arm snapshot gives `72 × (k−1) ≈ 144` df; the shipped 2-arm `k = 5` scoring run gives
`48 × 4 = 192` df). This `σ` lives on the same log scale as the geometric cost axis, so it drops
straight into the cost band. Its only job is a **sanity check**: the practical floor must sit *above*
the resolution the harness can achieve — roughly `σ_within · √(2/k) / √|S|` at the suite level, which
isolates the *continuous* compute variance and so acts as a **strict lower bound** (flaky-yield tasks
inject extra binomial variance into `L = Σcost/K` via the retry tax, widening the true suite CI
further). The floor must clear this **empirical suite band**, not the closed-form alone.

**Ad-hoc anchor (current numbers).** Mining the existing `sessions.db` (24 tasks, `k = 3`, all arms)
gives pooled `σ_within ≈ 0.28` (Opus / frontier) and `≈ 0.72` (Haiku / mini) on the log scale — a
~2.5× gap that is itself the evidence for **per-model-class margins**. Through
`σ_within · √(2/k) / √|S|` at `k = 5`, `|S| ≈ 15`, that is a suite-level cost resolution of ≈ **4.7%
(frontier)** and ≈ **12.5% (mini)**. The floor must sit *above* that, so we set the ex-ante practical
floors at **~5% IET (frontier)** and **~13% IET (mini)** — the mini floor deliberately rounded up past
its resolution, not down. The mined `σ` is itself **biased high** (it mixes non-delivered-run cost into
the tail), so these are conservative anchors. A **targeted variance study** — higher `k` (~12),
**Delivered-runs only**, log-variance-components with a CI — would replace them with a tighter, unbiased
anchor; the ad-hoc method above is the reproducible way to re-anchor if the harness changes.

**Pooled vs. arm-specific — two different jobs.** The *pooled* `σ_within` above sizes the **margin**
(one harness-noise number). It cannot test **C5 predictability**, which asks whether *grounded* cost is
steadier than *baseline* cost — that needs the **arm-specific** `σ_b` and `σ_g` (same log-scale
within-cell residual, estimated per arm) and their **ratio `σ_g/σ_b` with a band**. `σ_g/σ_b < 1`
(band excluding 1) is the C5 win. It is a **memo** read — reported, never gated. **Estimate both σ on
`Delivered` runs only.** Non-delivered cost contaminates the residual *asymmetrically*: the flakier arm
(usually baseline) carries more non-delivered runs, inflating `σ_b` and pushing `σ_g/σ_b < 1`
spuriously — which would credit C5 with steadiness that is really just C2's unreliability showing up in
the cost tail. Restricting to Delivered runs keeps C5 measuring *cost* dispersion, not *yield*
dispersion in disguise. **Band the ratio *inside* the bootstrap, not analytically.** On Delivered runs
each cell contributes only `Kᵢ − 1` degrees of freedom, so the flakier arm's `σ` is estimated on
**fewer df** — `σ_b` is noisier exactly when baseline is flaky. Computing `σ_g/σ_b` within the same
resampling procedure carries that unequal, `K`-dependent df automatically; an analytic band with
symmetric-df assumptions would understate the ratio's uncertainty.

At `k = 5` the yield band is coarse (steps of `1/5 = 0.2`, smallest detectable move ±1 run), so most
near-margin tasks are carried by the finer **cost** axis.

## Visualization — the reference (LIET chart)

The card derives from this chart; documenting it fixes the model's meaning in one picture.

- **x-axis = difficulty** (authored rung order); one **rung = one task**. Both arms plot at the same
  x — head-to-head on the same course. This *is* the per-task pairing and the difficulty control.
- **y-axis = IET** (cost); one **series per arm** (baseline, grounded, and optional oracle/README).
- A rung plots a value for an arm **only where that arm is productive**; the line **breaks over a gap**
  so no segment implies a win the arm never produced. Wall-clock is overlaid as a thin dashed
  companion on *every* rung (time is spent pass or fail); a twin **archaeology** chart shares the x.
- **Competitor envelope / ceiling** = min cost of the **other arms that also produced this unit** —
  the ceiling grounded must stay under to "pay its way." Absent on a **grounded-only** rung (no
  competitor there — the capability-win case).
- **Four-way ordering** (both-productive → grounded-only unlocks → baseline-only regressions → neither)
  makes unlocks and regressions visually obvious — the coverage scoreboard, drawn. A `baseline-only`
  rung shows the baseline series with the grounded series **gapped** (the visible regression).
- **Graded-yield encoding:** encode reliability on each dot — fill/opacity/size ∝ `Kᵢˣ/k`
  (5/5 solid, 2/5 faint) with an error band from run-to-run agreement — and plot **low-yield
  productive tasks** at their Delivered-run cost. Only `K = 0` is absent.

## Deliberate exclusions

- **No per-all-tasks blend** (`Σcost/N`) — mixes unsolved tasks into the denominator; meaningless.
- **No binary "pass/fail" collapse** — a task's outcome is its yield `Kᵢˣ/k` on the
  `Fails → Satisfies → Delivers` ladder; `K = 0` (delivered no unit) is the only task-level failure.
- **No shared subtracted floor.** Difficulty is controlled by *pairing* — each task against its own
  baseline — not by subtracting a suite-wide reference level. A shared floor fails two ways: it is one
  constant subtracted from both arms, so it cancels in every `Δ` (`ΔE ≡ ΔR`) and adjusts nothing; and a
  single task's distance to it is dominated by that task's own difficulty relative to the reference
  set — directionally right on aggregate, but never sharp per task.
- **No pharma-style failure attribution.** Successes and failures are **independent** tasks; failures
  are not precursors that fund the wins, so a task that delivered no unit is not charged against wins.
- **No causal "grounding replaced archaeology" claim.** We report the **outcomes** (IET ↓, duration ↓,
  correctness ↑, in some combination) and **assume** archaeology-replacement is the mechanism, showing
  the archaeology counts as supporting **context**, not proof.
- **No tiers beyond Satisfies/Delivers yet** — the two-tier ladder (works vs. as-asked) is active; a
  richer grade would bucket assertions into *more* value tiers and score a run by which tiers are
  completely vs. partially true. Deferred. A different axis from graded yield.

## Analogy glossary

| Concept | Everyday analogy |
| --- | --- |
| Risk-adjusted return (Axis 1) | **TIPS vs MSFT:** an equal *headline* return isn't an equal buy — a banked, low-variance premium (TIPS = a steady `2/5 → 5/5` that repeats every batch) beats an equal-average but volatile one (MSFT = a win you might not see on the next five runs). |
| Trust on n=5 / "will I see it again?" | A poll of 5 people has a huge margin of error; 5/5 is not "always." |
| Reliability of the win | The kid driving to college: the reliable car vs. the one that might strand them halfway. |
| Graded yield (not binary) | A 2/5 batch still made 2 good parts — that's low yield, not "failure." |
| Expected cost per win (Axis 2) | Print shop / factory: run a batch of 5, some smudge — real cost per *usable* print = whole batch ÷ the good ones. |
| Difficulty control / mix guard | Mix-adjusted ASP: compare price item-by-item on the same shopping list, not blended receipts. |
| Entry fee + unit price vs alternatives | Costco: pay the membership, then compare per-item price to Safeway/QFC — amortizes only if you keep shopping there. |
| Retries as yield loss | First-pass yield / scrap & rework; `1/p` ≈ Number Needed to Treat; geometric expected trials to a success. |
| Capability win (baseline can't) | A store that doesn't stock the item this batch. |
