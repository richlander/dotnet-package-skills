# The Levelized IET Curve

*A shared cost axis for comparing grounding documents, and a name for the metric.*

## What this fixes

IET collapses many token kinds into one honest total per session. But a *single* IET number — or a mean IET across answered questions — throws away the one axis that carries the grounding story: **difficulty**. Worse, averaging across difficulty can invert the truth. Take a four-rung ladder:

| Rung | baseline correct? | baseline IET | `AGENTS.md` correct? | `AGENTS.md` IET |
|---|---|---:|---|---:|
| 1 (easy) | ✓ | 1000 | ✓ | 400 |
| 2 (easy) | ✓ | 1200 | ✓ | 500 |
| 3 (hard) | ✗ | — | ✓ | 2500 |
| 4 (hard) | ✗ | — | ✓ | 3000 |

Mean IET over each arm's own correct set: baseline = 1100, `AGENTS.md` = 1600. The scalar says grounding is *worse* — even though it costs less than half on every shared rung and unlocked two rungs baseline could not reach at any price. The mean inverted a total domination, because the two arms were averaged over different-difficulty populations. The better document is punished for the difficulty of the rungs it alone climbed.

The difficulty axis is not noise to average away. It is the signal. Keep it on the x-axis and the inversion disappears.

## What a per-rung IET number actually contains

Before plotting anything, it helps to name the three components inside each rung's IET, because the whole metric is really about how they trade off:

1. **Irreducible difficulty** — the output tokens required to actually answer, weighted 5×. Rises with rung. Present in *every* arm, baseline included. No knowledge, however perfect, drives this to zero.
2. **Knowledge-gap cost** — the exploration and archaeology a model does when it doesn't know: web searches, cache spelunking, reverse-engineering. Near zero on rungs the model already knows; explodes on rungs it doesn't. **This is the term grounding removes.**
3. **Doc tax** — the grounding load itself, once, wherever it is pulled in. A roughly constant offset for enriched arms; zero for baseline.

Grounding wins on a rung exactly when the knowledge-gap cost it *removes* exceeds the doc tax it *adds*. The subtlety this exposes — and the reason the four-rung example above wins on *every* shared rung — is that **difficulty is not the same as knowledge gap.** Difficulty is the x-axis; the gap is a separate curve, set by where the package sits on the popularity-and-recency decay from the main spec's Knowledge section. A rung can be low-difficulty yet *unknown* (a niche package the model never trained on) or low-difficulty and *known* (a popular one). Grounding's value turns on the gap, not the rung number — so the same ladder produces two different curve shapes, and the metric must handle both.

**Niche package — gap present from rung 1.** Baseline is already paying knowledge-gap cost on the easy rungs, so grounding wins there too, and baseline exits early as difficulty climbs. Grounding dominates wherever the arms coexist; there is no left-side harm region and no price crossover — baseline just drops out. *This is the four-rung example above,* chosen because a clean domination makes the mean-inversion starkest.

**Well-known package — gap ≈ 0 at low difficulty.** Baseline is cheap on the easy rungs because the model already knows them, so grounding there is pure doc tax: a genuine **harm region**. The gap opens only as difficulty exceeds the model's training, and that is where grounding starts winning — so here the curves genuinely **cross**:

| Rung | baseline | `AGENTS.md` | region |
|---|---:|---:|---|
| 1 (easy, known) | 300 | 550 | harm — pure tax, baseline wins |
| 2 (easy, known) | 350 | 580 | harm |
| 3 (hard, gap opens) | 2000 | 900 | win — crossover between rungs 2 and 3 |
| 4 (hard, unknown) | ✗ | 1200 | unlock |

Minimizing per-rung IET correctly prefers baseline on rungs 1–2 and grounding on 3–4. The crossover rung is where `AGENTS.md` begins to pay its way; everything left of it is the harm region your Pareto rule exists to catch — and which the niche example, by construction, cannot show.

This also corrects a tempting misread of `SKILL.md`. The oracle is a ceiling on **reach** — it should answer every rung — but it is *not* a floor on cost. It carries the largest doc tax and the smallest knowledge-gap cost, so on easy rungs it is often the *worst* value, not the best. `SKILL.md` pays IET; usually it pays the most.

## The idea: put every document on one cost axis

Plot IET against ladder rung, one curve per arm — baseline, `AGENTS.md`, and the `SKILL.md` oracle — on shared axes. This is the LCOE chart applied to grounding. In levelized-cost-of-energy, every generation technology is drawn on one \$/kWh axis so heterogeneous sources become comparable and you read the story from curve shapes and crossings, not from one figure. Here every document is drawn on one IET-per-answered-rung axis, and the same reading applies.

The property both charts share: **comparison happens at equal difficulty.** Two energy curves are compared at the same year; two grounding curves at the same rung. No curve is credited or penalized for operating in a different regime than another.

*Figure 1 — well-known-package regime. The line shapes are meaningful, not decorative: the oracle (`SKILL.md`) rises **gently** — that slope is irreducible difficulty, the cost even perfect knowledge must pay as questions harden. Baseline is **flat while the model knows the package, then spikes** as its knowledge gap opens, and fails after rung 3. `AGENTS.md` sits **above** the envelope on the easy rungs (open markers — the harm region, where it is pure doc tax and baseline wins), **crosses** as baseline spikes, then **rides under** the envelope with a slight upward knee of its own. The vertical marker is the handoff rung, where the cheapest competitor switches from baseline to the oracle. A niche package looks different — no harm region, no crossover, baseline exiting immediately — as described above.*

## Reading the curve

Each feature answers a question the spec currently asks qualitatively:

**Oracle slope = irreducible difficulty.** `SKILL.md`'s *slope* (not its height) tracks the cost that is the question's fault rather than the document's — a day-100 question costs more even with perfect knowledge. Its high *intercept* is the doc tax. Keep those two separate; conflating them is what made an earlier draft read as "SKILL is free."

**Value delivered = baseline − `AGENTS.md`** at each shared rung. What grounding is worth, in IET, at that difficulty.

**Oracle gap = `AGENTS.md` − `SKILL.md`.** Value remaining — how much a fuller document could still recover.

**Generalization = how closely `AGENTS.md` tracks the oracle as difficulty rises.** A document that generalizes stays a roughly constant distance above `SKILL.md`. One overfit to the easy rungs hugs the oracle at the bottom then peels upward — its **excess slope over the oracle is the generalization deficit**, measured in tokens rather than asserted.

**Shared-region slope = efficiency.** In the table, rungs 1→2 cost +100 for `AGENTS.md` vs +200 for baseline. Grounding is not just lower, it is *flatter* — it flattens the difficulty gradient, helping more as questions harden.

**The knee = the divergence rung.** Where a curve bends sharply upward is where grounding stops carrying the model and the model starts paying its own way in reasoning and exploration. The knee summarizes *reach* better than "reached rung 16," because two documents can reach rung 16 with completely different curves — cheap-then-cliff vs expensive-throughout — and only the knee tells them apart. "Which rungs did you stand on strongly" is everything left of the knee.

## What the per-rung number is measured on

The harness-reported per-turn IET already contains all three components at real weights — the grounding load, the exploration, the output. No share of grounding cost is artificially allocated to a rung; each rung counts only what was spent on its turn. Baseline's number is "model alone," the enriched number is "model plus grounding," and the difference is the true measured effect. Compare like-for-like, as reported.

(One measurement note: whether each rung runs in a fresh session or a shared one decides whether the doc tax lands at full price per rung or gets cached at 0.1× on later rungs. Either is fine, as long as it is the same for both arms being compared.)

## Regions and the comparability rule

A curve exists only where its arm answers correctly, so the ladder splits into three regions, each read differently:

**Shared region (all arms correct) — measured ΔIET.** Difficulty is held fixed, so slope and gap are clean, hard numbers. This is the only region where *efficiency* is defined.

**Unlocked region (some arms failed) — the maximum price of generalization.** See below. This is where capability, not efficiency, is the story.

**Regressions (baseline correct, `AGENTS.md` wrong) — harm.** Reported as its own outcome, never blended into an average that could hide it. This is the veto your Pareto rule exists to enforce.

## The maximum price of generalization

On a rung an arm failed, do **not** extrapolate where it *would* have landed. That fabricates data, and it is biased: an arm's IET is only observed on rungs it passed — which are the cheap ones — so projecting forward under-predicts the true cost precisely because the expensive attempts are the ones that failed and are missing from the sample.

Instead, compute where a failed arm *would have had to* land to remain worth choosing. That is a **threshold, not a prediction**, and it makes no claim about reality — it draws the line reality would have to beat. It is a hurdle rate: you don't forecast the return, you state the return that must be cleared, then compare.

Per rung, the hurdle is the **competitor envelope** — the lower boundary of every *other* participant's IET that answered correctly:

```
hurdle(rung) = min( IET of each other arm that answered correctly at rung )
```

`AGENTS.md` **pays its way on a rung iff its IET there is below this envelope.** Where it sits under the line, it is the document worth shipping at that difficulty; where it pokes above, it is dominated *at that difficulty* by something cheaper that also works, and you can read by how much and decide whether to train it down or concede the rung.

Two consequences make this the spine of the ship decision:

**The binding competitor switches identity as difficulty rises.** On easy rungs the envelope is set by *baseline* — grounding must beat "the model already knew it," a bar it often can't clear because the doc tax is pure overhead there. On hard rungs baseline has dropped out, so the envelope is set by `SKILL.md` — grounding must beat "just ship the textbook." The **handoff rung**, where the binding hurdle passes from baseline to oracle, is where `AGENTS.md`'s reason to exist changes from *cheaper than knowing nothing* to *cheaper than knowing everything*. It is arguably the single most important point on the chart for a ship/no-ship call.

**It defines a maximum, not a minimum, price.** If `SKILL.md` answers rung 6 at 4000 IET, then 4000 is the *most* `AGENTS.md` may spend to answer rung 6 and still justify existing — above that, ship the textbook and skip the missing manual. The band between `AGENTS.md` and the envelope is a training target with a built-in stop: close the gap until you are under the line, not to zero, because under the line is where "worth maintaining" already lives and further compression is effort past the point it changes the decision.

*Figure 2 — the same chart when `AGENTS.md` fails rung 6. It answers rungs 1–5 (last filled marker), then has no correct answer at rung 6, so **no point is plotted there** — the failed rung is not extrapolated, exactly as baseline was not. What *is* drawn is the ceiling: the oracle's measured cost at rung 6 is the **maximum price of generalization**, the most a future `AGENTS.md` may spend to answer that rung and still beat "just ship `SKILL.md`." Anything under the ceiling (green) pays its way; anything over it (red) means shipping the textbook is cheaper. The ceiling is a measured value from an arm that succeeded, never a guess about the arm that failed — which is the whole point.*

This retires the survivorship problem cleanly, which is the tell that it is the right instrument: the hurdle depends only on the *observed* IET of arms that *succeeded* at the rung. Nothing unmeasured enters. The part of the chart you couldn't trust is deleted; the part you could is kept.

## Total IET is a product, not a target

"Low per-rung IET and low total IET" is a coherent joint goal **only within a like-for-like comparison** — same rungs answered. The moment two arms answer different numbers of rungs, total IET stops being a fair target, because the higher-capability arm legitimately spends more by doing more work. On the table above, minimizing total IET picks *baseline* (2200 vs 6400) — the weaker document — the same inversion as the mean.

So treat total IET as the *product* of two independent objectives, not a third target:

- **Capability** — how many rungs an arm stands on. Maximize across the ladder.
- **Efficiency** — per-rung IET on the shared region. Minimize within like-for-like.

Total IET is an output of those two. As a standalone target it rewards doing less.

## What it answers in the spec

- **Pareto across models.** Plot Haiku-grounded and Opus-grounded IET(rung) on shared axes. A Pareto violation appears as *curves crossing* — a rung where an "improvement" made one model more expensive than before. A scalar buries this; the curve makes it impossible to miss and localizes it to a difficulty band to inspect.
- **Ladder self-audit.** If the difficulty ordering is real — and it is meant to be an emergent, observed property — then IET(rung) should be roughly monotonic on each arm's own passed rungs. A rung that consistently costs more than the rungs above it is evidence it is mis-placed or malformed. The metric audits the ladder it runs on; non-monotonicity is a bad-question detector, and it is also the gate for trusting any envelope or slope read.

## Recommended name

Reclaim your own coinage, corrected: **Levelized IET (LIET)** — IET per correctly-answered task *at a given difficulty*. That is a faithful LCOE analog (cost per unit of capability delivered) and sits in the same family as FTE, CO2-equivalent, and LCOE already in the spec. The earlier problem with LIET was never the concept; it was collapsing it to a single cross-difficulty average, which is exactly the inversion above. Per rung, LIET is right. The rule:

> **LIET is reported as a curve across difficulty, never as one number averaged across difficulty.**

The vocabulary that comes with it:

- **LIET curve** — the per-arm IET(rung) plot; the family (baseline / `AGENTS.md` / `SKILL.md`) is the deliverable.
- **Competitor envelope** — the per-rung lower boundary of the other arms; the **maximum price of generalization**.
- **Handoff rung** — where the binding hurdle passes from baseline to oracle; the ship-decision pivot.
- **Divergence rung** — the knee; the headline scalar for reach.
- **Oracle gap** — distance to `SKILL.md`; value remaining and the generalization measure.
- **Shared-region slope** — efficiency on mutually-correct ground.

These are the honest scalar reduction of the curve. Every one is difficulty-aware by construction, so unlike Mean IET none can invert a domination.

## Cautions

- **Do not fit an equation.** `a·e^(b·rung)` overclaims at 24 rungs and n=3. Report the curve by robust features — flat-region cost, shared-region slope, divergence rung, oracle gap, envelope crossings — not a fitted coefficient.
- **Do not extrapolate failed arms.** Use the envelope (a threshold on observed data), not a projection (a guess on censored data). If a projected point is ever wanted as a visual aid, draw it dotted and never feed it to a scalar.
- **The curve is only as good as the ordering.** Everything leans on monotonic, emergent-observed difficulty. Run the self-audit before trusting any shape.
- **Report dispersion.** The divergence rung and shared-region slope drive decisions and are exposed to stochasticity at n=3. Carry min/max or a band.
- **IET definition.** This metric inherits the corrected, netted IET from the main spec — `fresh input + 0.1·cache read + 1.25·cache write + 5·output`, where `fresh = input − cache read − cache write` — so cached tokens are priced once, at the cheap rate, not double-counted.

## One line for the spec

*IET tells you what a session cost. The LIET curve tells you how that cost scales with difficulty for each document — on one shared axis where baseline, `AGENTS.md`, and the `SKILL.md` oracle all compete — its divergence rung tells you how far each carries the model, and its competitor envelope tells you the most `AGENTS.md` may spend on a rung before you should ship the textbook instead.*
