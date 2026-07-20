# Skill-shelf methodology — the holistic benchmark and composition-axis LIET

*How we evaluate a **shelf** of skills as a whole, and how we attribute the shelf's score
back to the individual skills that earned it. Testing and scoring are one document because
they are one question asked against two reference lines.*

This is the skill-shelf counterpart to [`grounding-eval-methodology.md`](./grounding-eval-methodology.md)
(which measures a single `AGENTS.md` content arm). It reuses [LIET](./liet.md) wholesale — the
same difference-of-areas instrument — and applies it to a second axis. Read `liet.md` first;
this doc assumes its vocabulary (oracle gap, competitor envelope, harm region, divergence rung).

---

## 1. Two questions, two paradigms

Skill evaluation is really two different questions, and conflating them is what makes results
hard to read.

| | **Q1 — per-skill value** | **Q2 — holistic shelf** |
| --- | --- | --- |
| Asks | is skill *S* worth its context cost? | does the whole shelf, with the agent choosing what to load, serve the real distribution of developer questions? |
| Object of study | one skill, in isolation | the shelf + the agent's own retrieval/selection |
| Arm | `skilledIsolated` (only *S* loaded) | `skilledPlugin` (whole shelf, agent self-selects) |
| Paradigm | [`dotnet/skills`](https://github.com/dotnet/skills) **per-skill PR** — each skill tested alone, on its own tests | **CT-24 holistic benchmark** — a frozen question suite, ground-up "what will a .NET dev ask an agent," scored against the shelf |
| Reference line | **baseline** — beat it *from above* (undercut cost) | **oracle ceiling** — reach it *from below* (close the gap) |

**The two-rooms analogy.** Put two people in separate rooms who never speak. Person A develops
skills the `dotnet/skills` way: each skill is brought to PR independently, with its own tests,
graded in isolation. Person B writes CT-24 ground-up — a representation of what a .NET developer
will actually ask an agent — and scores the *shelf* holistically, letting the agent discover and
select. These are different instruments answering different questions. **Adding B's paradigm is
not compatible with how A tests today**, and that incompatibility is structural, not incidental
(§8).

**This does not overturn [eval-protocol.md rule 1](./eval-protocol.md)** ("grade the clean
`skilledIsolated` arm, never `skilledPlugin`"). Rule 1 governs **Q1**: a claim *about one document*
must not be contaminated by other shelf content. Q2 is a *different claim* — the shelf, plus the
agent's selection, **is** the object of study, so `skilledPlugin` is the subject, not a confound.
Same rule, different question: **grade the arm that matches the question you are asking.**

---

## 2. The holistic benchmark (Q2)

CT-24 run as a whole-shelf benchmark is HLE-shaped: a frozen task suite that scores the shelf
plus the agent's discovery and selection, not any one skill.

- **Three arms.** `baseline` (no grounding) · `skilledIsolated` (target skill only) ·
  `skilledPlugin` (whole shelf, agent self-selects). The holistic benchmark reads
  **`skilledPlugin`** (`GROUNDING_CARD_ARM=skilledPlugin`).
- **Organic discovery.** Prompts **never name the skill** — the harness rejects any prompt
  containing the target skill's whole-word name — so the benchmark tests whether the agent
  *finds* the right skill from a functional description, not whether it can follow a signpost.
- **Target-skill hit.** Because we backfill each task's `expected_skill`, we can score selection
  directly: on the locked n=5 markout shelf the agent self-selected the intended skill on
  **opus 23/24, haiku 24/24**. This is a first-class metric of the *shelf*, unavailable to any
  per-skill test.
- **n ≥ 5.** Per-scenario verdicts are invalid under high variance ([eval-protocol rule 2](./eval-protocol.md));
  the shelf's near-zero-effect rungs are the noisiest, so the holistic benchmark standardizes at
  **runs = 5** (n = 3 flipped both a haiku baseline and a per-rung "regression" that n = 5 cleared).

**The scored value stays LIET.** [`liet.md`](./liet.md) applied here is unchanged: correctness is
all-functional-pass (`Ft > 0 && Fp == Ft`, discipline/`reject_tools` tracked separately as the
archaeology axis), and value is the per-rung IET difference over the difficulty ladder with the
**floor** (six cheapest baseline-correct rungs) anchoring the worst offenders. This is the
**descend-below-baseline** reading: on the difficulty axis, grounding wins a rung when the
knowledge-gap cost it removes exceeds the doc tax it adds.

> **Verdict divergence, stated deliberately.** The harness's own per-run verdict is
> `min(isolatedScore, pluginScore)` (`EvaluateCommand.cs:1289`) — a conservative floor that
> penalizes any task the isolated arm can't solve alone. Our shipping chart reads the **plugin
> arm only**. For a holistic benchmark that is the *correct* lens: a task the shelf solves by
> composing two skills is a success of the shelf, not a failure to be floored. The `min` verdict
> belongs to Q1 (§1); the plugin arm belongs to Q2. See §7 for why the two even diverge.

---

## 3. The composition axis — LIET again, flipped

When a task activates more than one skill, "which skill earned the score?" is a **slope**, and it
is the same instrument as LIET — just measured against a different reference line.

- **Difficulty-axis LIET:** `value(rung) = IET_baseline − IET_grounded`, held at equal difficulty.
  Polarity: **descend below baseline.**
- **Composition-axis LIET:** `value(skill A) = outcome(full shelf) − outcome(shelf − A)`, held at
  equal task. Polarity: **ascend to the oracle ceiling.** This is the **oracle gap** from `liet.md`
  ("value remaining, how much a fuller document could recover") **decomposed per skill**: attribution
  asks *which skills close the oracle gap, and by how much*.

Both are finite differences of **observed successes at matched conditions**, never raw averages —
the defining property of LIET, and the reason the same no-extrapolate, report-dispersion,
never-hide-a-regression discipline transfers verbatim.

**The lattice draws a slope.** The composition axis is not an ordered ladder but a **subset lattice**
(the power set of the shelf), so the faithful object of "skill A's contribution" is a
[Shapley](https://en.wikipedia.org/wiki/Shapley_value) decomposition — the average marginal
contribution of A across coalitions — of which a leave-one-out is the single top edge
(grand-coalition-minus-A). But the outcome **induces an ordering**: sort coalitions weakest → strongest
by achieved score (or IET), from the empty coalition up to the shelf sitting on the ceiling. The
**step sizes between adjacent sorted coalitions are the per-skill marginals** — a steep step is a
load-bearing skill, a flat step is a free-rider. That is a readable slope from weakest to strongest
combination, exactly as on the difficulty axis.

Two cautions keep it honest:

1. **The monotonicity is imposed, not emergent.** On the difficulty axis, non-monotonicity *audits
   the ladder* (a rung that costs more than harder rungs is a bad question). Here the sorted list is
   monotone *because you sorted by the response*, so the shape audits nothing — the information is
   entirely in **where the big steps sit**, never in the monotone envelope.
2. **Read the frontier, not one path.** "Weakest → strongest" is one path through the lattice, and
   interacting skills are credited differently on different paths. But the question we actually ask —
   *reached collaboratively or separately?* — is a **frontier** question (which is the weakest
   coalition that reaches the ceiling), and the frontier collapses the path-dependence.

---

## 4. Three interference regimes

Because coalition value has **no free disposal**, adjacent lattice steps are **not monotone** —
adding a skill can move you *down*. That gives three regimes:

| Step when the skill is added | Interference | Reading | Action |
| --- | --- | --- | --- |
| flat (≈ 0) | none / additive | **free-rider** — present, but closes no gap | tighten its description so it isn't pulled here; or cut |
| positive, and only when paired | **constructive** (super-additive) | **collaboration** — no singleton reaches the ceiling, the pair does | keep both |
| **negative** — a subset outscores its own superset | **destructive** (sub-additive → negative) | **conflict** — the skill subtracts | scope / reconcile / cut |

**The destructive case is the harm veto, transplanted to the skill axis.** On the difficulty axis,
LIET vetoes a rung where grounded scores *worse* than baseline and reports it as its own outcome,
never blended into an average that hides it. The composition-axis analog is a **positive
leave-one-out** — removing the skill moves you *toward* the ceiling. Its fingerprint is unmistakable
in the induced ordering: **a coalition that outscores one of its own supersets.** A single skill can
beat the multi-skill combination we were trying to reach.

Three mechanisms produce it, with different fixes:

1. **Context tax degrades reasoning** — the skill is fine alone, but its tokens dilute and distract.
   *Fix:* shrink it, or tighten its description so retrieval doesn't pull it when irrelevant.
2. **Confounding instructions** — its guidance is wrong *for this task's context*. *Fix:* scope its
   applicability / narrow activation.
3. **Encoded disagreement between skills** — skill A says X, skill B says ¬X; each is individually
   correct, and the conflict exists **only when both load**. *Fix:* reconcile, establish precedence,
   or partition their domains.

The leave-one-out *sign* flags "net-negative here"; to separate mechanism 1 from 2/3 you read the
transcript for which instruction misfired (or hold token count fixed and vary only content).

---

## 5. The interaction matrix — relatedness by compression, and the distillation reading

§4 credits a skill by its **top edge** — the leave-one-out `v(full) − v(full ∖ {A})`. Read the
**bottom edges** instead and you get the rest of the matrix. Let `v(S)` be the improvement score
(over the ungrounded baseline) of the shelf restricted to subset `S`, so `v(∅) = 0`. The **diagonal**
is each skill's singleton value `v({A})` — a *keep-only-A* run, exactly the shelf we build by
excluding every other skill. The **off-diagonal** is the second-order interaction

```
c(A, B) = v({A, B}) − v({A}) − v({B})
```

`c` is basis-independent and symmetric; it is precisely the amount by which the leave-one-out top edge
and the keep-only bottom edge **disagree**. In a purely additive shelf every `c(A, B) = 0` and top and
bottom edges coincide — so the whole interaction story lives in the off-diagonal, and a nonzero `c` is
the fingerprint of composition. (The full lattice generalizes this to the
[Shapley](https://en.wikipedia.org/wiki/Shapley_value) decomposition of §3; the pairwise `c` is its
first interaction term.)

**Relatedness by compression.** The cross-term is a decompression of an old idea. Benedetto, Caglioti
& Loreto, [*Language Trees and Zipping*](https://arxiv.org/abs/cond-mat/0108530) (2002; video
walkthrough <https://www.youtube.com/watch?v=GlYgs6v2YfU>), measure the "remoteness of two bodies of
knowledge" by how much better one text compresses when seeded with a fragment of another — an
operational cross-entropy / relative-entropy (a [normalized compression
distance](https://en.wikipedia.org/wiki/Normalized_compression_distance)). Languages that share
structure compress together; unrelated ones do not. Our skills are bodies of knowledge and the CT-24
tasks are the queries against them, so `c(A, B)` is that same measurement in *outcome* space: **how
much does loading B change what A is worth on this task?** Sign and magnitude classify a pair exactly
as a compression distance classifies two texts — near-duplicate, complementary, or repellent.

This extends §4's three single-skill regimes to a **four-way pair classification** (§4 is the diagonal
view of the same facts):

| Cross-term `c(A, B)` | Singletons vs pair | Relation | Reading | Action |
| --- | --- | --- | --- | --- |
| ≈ 0 | both `v({·}) > 0`, `v({A,B}) ≈ v({A}) + v({B})` | **orthogonal** | independent, additive | safe to co-shelve |
| < 0, and `v({A}) ≈ v({B}) ≈ v({A,B})` | either alone already reaches the ceiling | **redundant** (compression distance ≈ 0, near-duplicate) | interchangeable substitutes | **merge** their guidance |
| > 0 (super-additive) | neither singleton reaches the ceiling, the pair does | **synergistic** | collaboration / integration | **keep or compose** |
| < 0, and `v({A,B}) < min(v({A}), v({B}))` | a subset outscores its superset | **interfering** | conflict (§4 destructive) | scope / reconcile / cut |

**Redundancy is not interference, and the distinction sets the lever.** A redundant pair looks like
mutual free-riding on the diagonal (removing either changes little, because the other covers it), but
the pair view reveals the fix is **merge-down**, not cut — the two skills are the same knowledge said
twice, paying shelf tokens and activation ambiguity for nothing. Interference is the opposite
pathology: co-loading actively subtracts. **Synergy is the dual of interference** and the one to hunt
for — a gap neither skill closes alone that the join distills; it is a **compose-up** signal, not a
trim.

**The breadth entropy tax.** Because activation is a retrieval over descriptions and every loaded
skill spends context, shelf value is **not monotone in cardinality**. The honest accounting for
adding skill *N+1* is

```
net(N+1) = gain on its target tasks  −  interference tax on every other task
```

where the tax is the sum of the negative cross-terms it introduces plus the diffuse cost of widening
the activation surface (more chances to mis-pull) and diluting attention (more tokens to reason
around). Left uncompensated by description tightening, breadth trends toward erosion — **quality
decays with each added skill unless each addition is re-priced on the whole shelf.** This is a
pressure, not a theorem, and it is exactly why the tax is visible *only* on the holistic self-select
arm (§7, §8): the isolated arm sets every cross-term to zero by construction. *(Worked example: on
markout CT18, the full five-skill shelf scored far below any focused two-skill shelf and with an
order-of-magnitude higher run-to-run variance — the tax made concrete as instability, not merely a
lower mean; the two section-gating skills proved **redundant**, not collaborative, so the lever was
merge-and-tighten, not narrow-or-broaden.)*

**The distillation reading of the score.** The improvement score
([scoring.md](./scoring.md): pairwise quality of the grounded answer vs the *ungrounded* baseline, in
[−1, +1]) is a **per-skill distillation yield**. The baseline run plus the judge together are an
**oracle**: the baseline exposes the model's own prior, and the score is the divergence between that
prior and the grounded answer. Then

- **positive marginal** — the skill distills a genuine model gap (the prior was wrong or absent);
- **≈ 0 marginal** — the skill teaches what the model already knew → free-rider, nothing to distill;
- **negative marginal** — **anti-distillation**: the skill overwrites a *better* prior the model (or a
  sibling skill) already held.

Under this lens the exercise is two dual searches at once: **maximize distillation yield per skill**
(sharp, gap-filling descriptions and bodies) while **minimizing interference** (drive the negative
cross-terms toward zero). The synergy cell is where the two goals coincide — a gap distillable only by
a coalition — and it is the positive object the matrix exists to find: the integration opportunity
that is the exact opposite of the conflict the holistic arm was built to catch.

---

## 6. Attribution protocol

Attribution is **on-demand**, triggered by the pull signal — not a mandatory arm on every run.

1. **Classify by pull-consistency (n ≥ 5).** For each task, over the plugin runs:
   - **consistent-1** — the same single skill pulls every run → clean attribution, done.
   - **consistent-same-2** — the same pair pulls every run → collaboration *candidate*; go to step 2.
   - **variable** — the pulled set changes run to run → a description-overlap smell; tighten
     descriptions before attributing.
2. **Leave-one-out, both ways.** For a co-pulled pair {A, B} whose shelf reaches the ceiling, remove
   each and re-score:

   | Remove A | Remove B | Conclusion |
   | --- | --- | --- |
   | falls | falls | both load-bearing → **genuine collaboration**, keep both |
   | falls | holds | A carries, B free-rides → tighten B's description |
   | holds | falls | symmetric → tighten A's description |
   | holds | holds | the rest of the shelf (or base knowledge) sufficed → tighten both |
   | **rises** | — | A is **destructive** (§4) → scope or cut A |

3. **Isolated as an on-demand precision probe.** Repurpose `skilledIsolated` from a mandatory arm to
   a targeted **absolute-sufficiency** test: run it *only* on multi-pull tasks to ask "does this one
   skill suffice alone?" — the complement of leave-one-out's "is this one skill necessary?"

This is the frontier read of §3 made operational: singleton-on-the-ceiling = solved **separately**;
pair-on-the-ceiling with both singletons short = solved **collaboratively**; superset-below-subset =
**conflict**.

---

## 7. Why the holistic arm is not optional — the defect it alone can see

Mechanism 3 (encoded disagreement) is the argument that the holistic benchmark catches a **defect
class the per-skill paradigm cannot see, by construction.** Inter-skill disagreement is invisible to
any arm that loads one skill at a time — that includes **both** `dotnet/skills`' per-skill-PR tests
*and our own `skilledIsolated` arm*. A conflict between A and B exists only when A and B are
**co-loaded**, so it appears only in `skilledPlugin`. "Skills that disagree when loaded together" is
therefore not merely another attribution reading — it is a **shelf-integration defect that only the
holistic, self-select arm surfaces**, and it is a first-class reason the plugin arm is the shipping
lens (§2) and isolated is demoted to on-demand (§6).

This also explains the deliberate verdict divergence of §2: the `min(isolated, plugin)` harness
verdict and the plugin-only chart diverge precisely on the tasks where composition matters — the
tasks the holistic benchmark exists to reward.

---

## 8. Demoting the mandatory isolated arm

Two independent reasons, one change:

- **Cost.** For CT-24, `skilledIsolated` is roughly a third of total eval cost. `dotnet/skills` pays
  nothing like it, because per-PR testing *is* the isolated arm — one skill at a time is its whole
  world. Running it on every holistic task is redundant with a signal we already get for free from
  the pull distribution (deletion candidates show up as pull × 0; discovery gaps as never-pulled).
- **Incompatibility with multi-skill tasks.** The point of a holistic benchmark is to include
  **intentional multi-skill tasks** that model sophisticated prompts. But a 2-skill task can never be
  satisfied by the isolated arm (only one skill is loaded), so under `min(isolated, plugin)` it is a
  guaranteed false failure that drags the effective score negative. **Adding multi-skill tasks and
  dropping the default isolated arm are therefore the same change** — you cannot do one without the
  other.

Isolated is not deleted; it is **repurposed** (§6, step 3) as an on-demand precision probe.

---

## 9. The cross-package gap

One limitation is worth naming because it is the same class as the isolated-vs-holistic gap.
`dotnet/skills` ships **multiple plugins**, for good reason (domain separation, independent
ownership). CT-24 testing can run **within** a plugin — that's an implementation detail. But there
is **no affordance for testing across packages / across plugins**: the composition axis of §3 stops
at the plugin boundary, so a conflict or a collaboration that spans two plugins is as invisible to a
single-plugin holistic benchmark as an intra-plugin conflict is to a per-skill test. Cross-package
composition is the next frontier of this methodology, not something it currently measures.

---

## One line

> LIET on the **difficulty** axis measures how far grounding descends below baseline; LIET on the
> **skill-composition** axis measures how the shelf ascends to the oracle ceiling — and the induced
> weakest-to-strongest ordering of coalitions makes attribution a slope you read off step sizes:
> a steep step is a load-bearing skill, a flat step a free-rider, and a subset outscoring its
> superset is a conflict only the co-loaded holistic arm can see.

> And one axis deeper: the leave-one-out marginals are the **diagonal** of a skill-interaction
> matrix whose off-diagonal cross-terms are a **compression distance** between skills — orthogonal,
> redundant (merge), synergistic (compose), or interfering (cut). Each marginal is a **distillation
> yield** against the baseline-plus-judge oracle, so the whole method is two dual searches:
> maximize per-skill yield while driving the interference cross-terms to zero — and the synergy
> cell, where both goals coincide, is the integration opportunity that is the exact opposite of the
> conflict the holistic arm was built to catch.
