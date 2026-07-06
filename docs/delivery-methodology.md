# Push vs. pull grounding delivery — evaluation methodology

How to measure, honestly, whether delivering package grounding **push** (always-on, at t=0) beats
delivering it **pull** (a model-invoked skill the agent must discover). This is the "good process"
we follow and ask others to follow; it is written to be *defensible*, because the conclusions are
advocacy.

## Where this sits: two levers, two outcomes

Grounding is judged on two **outcomes** — does it let the agent answer **more** questions
(*capability / reach*) and answer them **more cheaply** (*efficiency / cost*) — moved by two
**levers**: the **content** (what the document says) and the **delivery** (how that content reaches
the agent). Delivery is **not a third outcome**; it *modulates* both — better delivery raises reach
and lowers cost at fixed content (the position effect below). In practice the two outcomes move
*together* (grounding that eliminates archaeology both cheapens and unlocks in one stroke), so they
are separable to reason about but correlated in the data. This doc is the **delivery** lever; the
content lever's reach/cost-vs-difficulty curve is the LIET axis (see [Composition](#composition-with-the-content-axis-liet)).

**Delivery is a spectrum of layers, not a binary.** Push (always-on at t=0) and pull (model-invoked)
are the endpoints, and **MCP is a pull-class channel** — a model-invoked tool, so it carries the same
**activation lottery** and **mid-session position penalty** as skill-pull, plus a query-formulation
step. MCP is the *easy* channel to ship (a standard protocol a package author can just serve) but not
the most effective. The mechanisms **compose**: the highest-value design is usually a **hybrid** — a
lean, always-on **push pointer** at t=0 ("authoritative grounding for X / Y / Z exists — reach for it
before using them") that closes pull/MCP's *discovery* gap, with the *bulk* pulled on demand (MCP) to
defer token cost to the sessions that need it. So "which delivery" is really "which layer each
mechanism is best at" — and it is an experiment, not an opinion.

## The two delivery modes

| | Pull | Push |
|---|---|---|
| Artifact | `SKILL.md` (model-invoked skill) | `<unit>.agent.md` (always-on agent) |
| When the body enters context | only if the agent invokes the skill | turn zero, by construction |
| `read grounding %` (activation) | a real, measured rate (0–100%) | ~100% by construction |
| Harness arm | `with-skill-isolated` | `with-agent-isolated` (agent selected as primary persona) |

Content is identical (both carry the same `AGENTS.md` body). **Only delivery differs.**

## The claim under test

> Push compresses the session (fewer turns, less archaeology, lower cost) and raises success by
> putting the grounding in front of the agent *before* it acts — instead of after it has explored,
> guessed, and burned turns.

## The central identity (read this before running anything)

The push *advantage* is bounded by how often pull fails to deliver:

```
push_advantage  ≈  content_trajectory_effect  ×  (1 − pull_activation_rate)
```

- **Where pull already activates** (e.g. a frontier model that reliably invokes the skill), push and
  pull deliver the *same content* → push ≈ pull on trajectory. Push's value there is **insurance**:
  it removes the discovery risk and cannot regress, but it wins nothing on trajectory.
- **The trajectory win lives entirely in the sessions pull misses.** So the empirical magnitude of
  push's advantage is *regime-dependent* — it is large only where pull activation is low.

Consequences for the experiment:
1. **`read grounding %` (activation) is the pivot metric** — always report it.
2. **To demonstrate a large advantage, choose a low-pull-activation regime** (measure activation
   first; a harder/broader set, or a smaller model, where the agent skips the skill).
3. **Never infer push > pull from a regime where pull already activated ~100%** — there is no room
   for it to, and claiming otherwise is indefensible.

## The four instruments — decompose pull into activation and position

The single "pull" arm blends two questions the identity keeps apart. Measuring **four** instruments
separates them — and all four are a **filter on existing data** (shared baseline + push arm + pull
arm + per-scenario activation flags), **no new run**:

1. **Baseline** — the model betting on itself; the **difficulty ruler**. Every other number is a
   delta from it.
2. **Push** — content present at ~100%: the pure **content effect** at each difficulty, with
   activation held constant.
3. **Does pull activate** — a pure **delivery probability** (a rate, not a cost curve): did the model
   decide to reach for the doc. A property of the trigger, the harness, and the model's disposition —
   not of whether the content is good.
4. **Pull given activation** — content effect **conditioned on** delivery having happened; directly
   comparable to push, because now both had the content present.

The factorization `push_advantage ≈ content_effect × (1 − pull_activation)` reads off these: (2) is
`content_effect`, (3) is `pull_activation`, and **(4) vs (2) is the decisive test**:

- **(2) ≈ (4)** → pull's only disadvantage is the activation lottery. The decision collapses to
  "what is the activation rate, and can you raise it" — a **delivery** fix (better trigger, a push
  pointer), *not* a content fix.
- **(2) ≠ (4)** → the delivery mechanism affects the content's value **even after both delivered** —
  a **position effect** (push lands the doc up front; pull/MCP inject it mid-session, into the tail,
  after the model has already reasoned). That is an argument for push *independent of activation*,
  and routes to a positional fix. The two-arm "pull vs push" comparison blends these into one loss
  and cannot tell you which lever to pull.

**The position effect is falsifiable — measure it, don't assert it.** The (2) − (4) gap should equal
the **pre-activation cost**: in a pull-given-activation session the doc lands only when invoked, so
every turn before that ran effectively un-grounded (baseline), and push is precisely what avoids
those turns. The harness records *when* the skill fired, so sum the turns/IET spent before it; if the
gap ≈ that pre-activation cost, the position mechanism is **confirmed**. If the gap is *larger*, the
model reasons worse over a tail-injected doc than a front-loaded one even once both are present — a
stronger, separate argument for push.

**Selection-bias trap — compare (2) vs (4) at matched rungs, per run.** (4) is conditioned on the
model *choosing* to activate, and it chooses when it senses it is out of its depth — the harder rungs
— so (4)'s correct-set skews hard, and comparing it to push's full curve is apples-to-oranges. At low
`n` the conditioning is per-**(rung, run)** (a rung can activate on some runs and not others).
Compare (4) to (2) **only on rungs that activated on all `n` runs** (or weight by per-run activation);
never as aggregate averages — otherwise the activation lottery leaks back in through the composition
of the correct-set, the exact contamination the split exists to kill.

## Confounds and the controls that remove them

| Confound | Symptom | Control |
|---|---|---|
| **Per-run baseline variance** | each delivery run computes its own baseline; at low `runs` the baselines diverge (we observed 4/6 vs 6/6 tasks and 74k vs 96k IET for the *same* ungrounded model) | **Shared pinned baseline** — `--baseline-out` once, `--baseline-from` for the other arm, so push and pull grounded arms compare against the **same** baseline |
| **Low statistical power** | one run is dominated by session noise | **`runs ≥ 3`**; report variance (skill-validator's CV / high-variance flag) |
| **Judge noise** | the pairwise judge wobbles near the floor and mis-scores truncated transcripts (we saw a −66% "judge" score on a run that solved 6/6 tasks) | **The functional gate governs** — `tasks correct` and `func passed (assertions)` decide; the judge score is advisory |
| **Content drift** | comparing different docs | same `AGENTS.md`, same scenario set; delivery is the only variable |
| **Turn-budget artifact** | the doc tax is `≈ 0.1 × doc_tokens × turns`, so a per-rung/session IET edge can move with the harness turn budget or the model's reasoning effort rather than the content | **fix turn budget + reasoning effort** across the compared arms; **log turns-per-rung** and check any IET crossing against it (see [eval-protocol.md](./eval-protocol.md) rule 9) |
| **Regime blindness** | comparing where pull didn't fail | measure pull activation first; select the regime deliberately (see the identity above) |

## Metrics to report

- **`read grounding %`** — activation; the delivery-discriminating metric.
- **`tasks correct` / `func passed`** — the functional gate (the outcome that governs the verdict).
- **Trajectory** — `Session turns`, `tool calls (web/bash/other)`, `nuget archaeology`, `Session IET`.
- **Verdict** — `BETTER / WORSE / NEUTRAL / FAIL` (goal-aware; the functional gate is the only hard gate).

## The reproducible procedure

```bash
# 0. Select the scenario set. Measure pull activation per model first (a pilot run);
#    a low activation rate is where push is expected to pull ahead.

# 1. PULL arm — runs the baseline AND the pull-grounded arm, and PINS the baseline.
grounding run <unit> --delivery pull  --model <M> --runs 3 \
  --tests-dir <set> --out <data-dir> --baseline-out <dir>/bl-{model}.json

# 2. PUSH arm — reuses the SAME pinned baseline (skips the baseline arm).
grounding run <unit> --delivery push  --model <M> --runs 3 \
  --tests-dir <set> --out <data-dir> --baseline-from <dir>/bl-{model}.json

# 3. Render and compare. Both datasets now share one baseline, so the push-grounded vs
#    pull-grounded contrast is clean.
grounding analyze <data-dir>/<unit>.<M>.json       --view doc-card   # pull (baseline -> pull-grounded)
grounding analyze <data-dir>/<unit>-push.<M>.json  --view doc-card   # push (same baseline -> push-grounded)
```

Run every model you care about, including at least one **frontier control** (expected to activate
under pull ~100%, so push ≈ pull) alongside the **needs-it tier** (where pull activation is low).
The control is what proves push does not *harm* the easy case.

## Reading — and defending — the result

- **Pull activation ≈ 100% (frontier control):** expect push ≈ pull on trajectory. Report it that
  way. Push's contribution is *removing discovery risk*, not compressing an already-grounded session.
  Claiming a trajectory win here is not defensible.
- **Pull activation low (needs-it tier):** push should recover the trajectory win in exactly the
  fraction pull missed. This is the advocacy case — and it is only credible with the shared baseline
  and `runs ≥ 3` in place.
- **The verdict is the functional gate**, goal-aware; the judge score is a signal, not a gate.

## Composition with the content axis (LIET)

This doc is the **delivery** axis. It is orthogonal to the **content** axis — the Levelized-IET
(LIET) curve, which plots per-rung IET vs difficulty for baseline / `AGENTS.md` / `SKILL.md` and is
computed **push-only** (delivery held constant at always-delivered, so the activation lottery can't
smear the content curve). The two compose only at the **ship call**:

- **LIET (content)** answers *which document reaches each difficulty rung cheapest when always
  delivered* — a pure content-reach hurdle (its "ship `SKILL.md` instead" envelope is a **content**
  comparison, not a delivery one).
- **This doc (delivery)** supplies the missing economics: `SKILL.md`'s doc tax is pull-amortized
  (paid only in the `activation` fraction of sessions), `AGENTS.md`'s is always-on (paid every
  session). The ship decision discounts LIET's content hurdle by these — **not** through the LIET
  envelope. Keep them separate; each number then means one thing.

One coupling worth stating because it affects both axes: the doc tax is **turn-coupled and
endogenous**. Under push the doc is ambient — a cache-read on *every* turn — so its cost is
`≈ 0.1 × doc_tokens × turns`, and grounding *removes* turns (fewer exploration turns), so a document
that works **shrinks its own tax**. It is not a flat offset. Definition, stated so it's checkable in
the logged data rather than inferred from a shape:

> **The harm region is exactly the rungs where the doc adds tax without removing exploration turns**
> — i.e. rungs where baseline already had ~0 archaeology (nothing to remove) so the always-on doc
> tax is pure overhead.

Diagnosing it therefore requires **turns split by kind** — exploration/archaeology vs irreducible —
per rung, not a single turn count ([eval-protocol.md](./eval-protocol.md) rule 9). We already count
archaeology per scenario, so this is a reporting change.

## The content ledger — attributing content to assertions (LIET's dual)

LIET measures the per-rung benefit *magnitude*; the **content ledger** attributes that benefit to
specific `AGENTS.md` blocks. It is a filter over the *same* per-scenario data (no re-run):
`grounding ledger <datasets>`.

The unit is the **functional assertion**, and the evidence is the per-question **baseline↔grounded
diff** (assertions align by index across arms). Assertions are the right unit because they are
*curated and declared* — unlike what the agent happened to dig for, which is noise:

- `file_contains` (type 2) → the required **API id** (`Metric`, `MarkoutSerializer.Serialize`). The
  gold signal: it tests "did the code use API X", and X maps to the block documenting X.
- `run_command` (type 9) → the **correctness oracle** (build + expected-output regex); sets task pass.
- `reject_tools` (type 11) → the **archaeology guard**; a fail→pass flip = grounding removed the dig.

**Four rung cases** (the three real outcomes + a harm flag), from the diff plus the resource divide:

| case | assertion diff | resource divide | meaning |
|---|---|---|---|
| **correctness** | flip fail→pass | — | grounding enabled the task |
| **efficiency** | none | big IET/archaeology | same correctness, cheaper |
| **redundant** | none | small | grounding didn't matter for this model |
| **regressed** | pass→fail | — | grounding harmed (or n=1 noise) |

The assertion-diff is the **correctness** instrument; the IET/archaeology divide is the **efficiency**
instrument — the diff is *blind* to efficiency wins (they have no flip), so the two channels are read
together.

**Per assertion, a 2×2** of {covered by a block?} × {diff outcome} gives the actionable reads:

- covered + flip → **load-bearing** (content earned the win).
- covered + still-fails → **present but ineffective** (salience/quality, not coverage).
- uncovered + still-fails → **missing content** (author it).
- uncovered + flip → **uncredited** (win came from reasoning, not the doc — don't credit it).
- covered + both-pass → **redundant for this model** (may still serve a weaker one — read per-model).

A block that covers *no* assertion is an **orphan** — nothing tests it, so cut it or grow a rung.
Generalization falls out of the tiers: a block whose flips land on **held-out CT** rungs (not just its
authoring MM rungs) is generalizing; flips only on authoring rungs are a mild overfit signal.

### One system: assertions grade *and* attribute

Because attribution rides on the assertions, **closing a ledger gap = adding a functional assertion**,
and that same assertion **hardens the eval gate**. One artifact, two payoffs, no separate instrument,
and the ledger stays a filter (no re-run). The discipline: add an assertion only when it encodes a
*genuine* task requirement — not to boost resolution — or you contaminate the correctness gate to
serve attribution.

The corollary is a **diagnostic**. An apparent "content-quality problem" partitions into four causes —
a real **content gap**, a **missing/imprecise assertion**, a **matcher** miss, or **n=1 noise**. An
imprecise or stale assertion produces a *phantom* content problem (the doc looks bad when the
measurement is bad). The ledger's own categories are the triage.

### Worked example — emergent content on `markout` CT8 / CT12

The ledger flagged two CT-tier gaps: `CT8` *missing content* (`ShowWhenProperty` undocumented,
grounded failing) and `CT12` *uncredited flip* (`MarkoutUnwrap` undocumented). The loop: author the
**minimal** content for both → re-run *only* those two rungs (haiku, `n=3`) → re-ledger.

| | CT8 | CT12 | still-failing | missing | uncredited | load-bearing flips |
|---|---|---|---:|---|---|---:|
| **before** | regressed (grounded fails) | uncredited | 1 | CT8:ShowWhen | CT12:Unwrap | 1 |
| **after** | **correctness (grounded passes)** | **efficiency (credited)** | **0** | none | none | **2** |

Content added *because measured* — and the **line budget forced compression** (the additions busted
the limit, so both attributes were folded onto one bullet: emergent ≠ accumulation). The same run also
surfaced a real ledger bug — the assertion value is a lenient substring (`ShowWhen`) while the doc
writes the full identifier (`ShowWhenProperty`), so matching is by **identifier family**, not exact
token.

**Caveats.** Attribution is a correlational lexical (identifier-family) join at section granularity.
The assertion diff is **`run[last]`** in the dataset, so flip/regressed *counts* are `n=1` noisy — but
structural findings (`missing`, `orphan`) are robust across runs. Correctness that lives in the fixture
*output regex* rather than an API `file_contains` (a strong model producing the right output without a
distinctive API call) is **unattributable** until an assertion pins the API — the loop's own next move.

## Reading the CT / low-activation run (a trap to avoid)

The two model-relative reads must come off **different arms**, or the activation lottery leaks back
into the content story the push-only rule just removed:

- **Presence premise** (does pull *fail to deliver* for the weaker model?) → read `read grounding %`
  on the **pull** arms. If haiku's pull activation on the hard tier is low, pull is failing and push's
  guaranteed delivery should win big.
- **Decay-migration** (does grounding's gap open at *earlier* rungs for the weaker model — the
  popularity/recency decay made visible?) → read **push arms only**, per rung, haiku-push vs
  opus-push. This is a *content*-axis result and is only clean where activation is pinned at 100%.

The trap: a **low-activation pull run convolves activation and content in one number**
(`push_advantage ≈ content_effect × (1 − pull_activation)`), so an "earlier gap" seen on pull data
could just be haiku under-invoking the skill — a delivery effect masquerading as content. The
low-activation run may *suggest* decay-migration; only the push arms *confirm* it.

## Honesty guardrails (the anti-overclaim checklist)

- [ ] Shared, pinned baseline across push and pull (not two independent baselines).
- [ ] `runs ≥ 3`; variance reported.
- [ ] Pull activation stated; the push advantage is read against `(1 − activation)`, not asserted.
- [ ] Functional gate (tasks/assertions) drives the verdict; judge score labeled advisory.
- [ ] A frontier control included, to show push does not regress the easy case.
- [ ] No push > pull claim from a `runs = 1` or unshared-baseline run. *(We made this mistake once —
      a lucky low push-baseline made push look better than it was. The shared baseline exists to
      prevent exactly that.)*

Push's *structural* properties — presence (it always fires), timing (turn zero), position (stable
front of the prompt) — are true **by construction** and need no experiment. The only *empirical*
question is the **magnitude** of the trajectory win, and that is what this methodology measures
without fooling itself.

## Worked example — `markout`, runs=3, shared pinned baseline

Mini-6 scenario set, `runs=3`, one baseline pinned per model and reused across both delivery arms
(`--baseline-out` on the pull run, `--baseline-from` on the push run). Baseline is byte-identical
across arms (same turns, archaeology, IET), so the grounded arms are directly comparable.

| | pull activation | shared baseline | pull-grounded | push-grounded |
|---|---:|---:|---:|---:|
| **haiku** | 100% | 13 turns / 70902 IET | 9 turns / 55516 (−22%) | **5 turns / 33516 (−53%)** |
| **opus** | 100% | 12 turns / 97535 IET | 7 turns / 51788 (−47%) | **5 turns / 44596 (−54%)** |

**Reading it honestly:**
- **This is a *high*-activation regime, by measurement.** Both models activated the pull skill 100%
  of the time on mini-6 — so per the identity, pull is *not* failing to deliver here, and the
  presence-driven push advantage `(1 − activation)` is ~0. This set cannot demonstrate that case;
  the low-activation regime (the harder CT tier, where a mini model skips the skill) is the separate
  experiment for it.
- **Push is nonetheless cheaper than pull at equal (100%) activation** — for both models, against the
  *same* baseline. That isolates a **timing/position** effect distinct from presence: even when pull
  eventually loads the skill, it loads it *mid-session* after the agent has explored, while push has
  it at t=0. This is defensible here only because the baseline is shared and the trajectory counts are
  consistently sourced (an earlier `runs=1`, unshared-baseline pass showed a spurious edge from a lucky
  low push-baseline — the exact trap the shared baseline exists to remove).
- **Scope of the claim:** one package, one 6-scenario set, `n=3`. Enough to *observe* the timing
  effect cleanly; not enough to *size* it. Widen the set and raise `n` before quoting a magnitude.
