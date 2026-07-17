# Eval protocol — how to measure a grounding unit without fooling yourself

This is the **pre-registered measurement discipline** for every grounding eval. It exists
because results are easy to misread: contaminated arms, single-draw noise, brittle
assertions, and spliced datasets each produced a wrong conclusion in practice. Decide the
arm, the runs, the metric, and the thresholds **before** you look at numbers, then follow
the rules below.

`scoring.md` defines *grades and ship gates*; `harness.md` covers *confounds*. This doc is
the *operating procedure* that makes those numbers trustworthy.

---

## The rules

### 1. Arm hygiene — grade the clean isolated arm only
Every scenario runs three arms: `baseline` (no grounding), `skilledIsolated` (**only** the
target grounding loaded), and `skilledPlugin` (the target **plus every other skill on the
shelf**). **Claims about a document use `skilledIsolated`.** Never `skilledPlugin`.

- **Mistake it prevents:** markout's `skilledPlugin` also loads the broadskill *Textbook* +
  prefer-dotnet-inspect, so the card read **23/24** when the clean compact AGENTS.md was
  **15/24**. We reported the shelf's score, not the document's.
- **How:** the analyze card grades `skilledIsolated` by default. Before trusting a unit,
  enumerate what else is in `grounding/` (shared dir = shared shelf) and confirm the
  isolated arm is what you think it is.

### 2. Variance pre-flight — a per-scenario verdict is invalid under high variance
Before claiming any per-scenario pass/fail, check `highVariance` / `varianceCV` across
scenarios.

- **Mistake it prevents:** for markout **23/24 scenarios were `highVariance`** (CV median
  3.4, max 27). The "Textbook regressed CT6/CT8 vs baseline" — *a document cannot make a
  model worse at boolformat* — proved the per-scenario numbers were pure draw noise.
- **How:** if a meaningful share of scenarios are high-variance at the current `n`, the
  per-scenario verdict **does not hold**. Either raise `n` until rates stabilize, or report
  pass **rates with intervals**, not single-draw pass/fail. Contrast: SCL was 9/24
  high-variance (CV 1.2) and its numbers held.

### 3. Pass-rate metric, not a single representative run
A scenario's functional result is its **k/n pass rate**, computed identically for baseline
and grounded. "Solved" = rate ≥ a stated threshold (declare the threshold up front).

- **Mistake it prevents:** quoting one representative run as "12/12" or "23/24" that then
  failed to reproduce.
- **How:** at low `n` the single-draw `Fp == Ft` success is only an estimate. Treat
  `success` as an estimate of the rate; under high variance (rule 2) raise `n`.

### 4. Assertions must accept every correct solution
Gate on **semantics**, never on incidental formatting — whitespace, separator style, case,
ordering, key wording. When authoring, confirm the assertion passes the reference **and**
at least one reasonable valid variant.

- **Mistake it prevents:** SCL **M8** failed `items: a,b,c` because the model emitted the
  equally-correct `items: a, b, c`. A brittle assertion manufactured a "gap." Same class, JSON
  edition: STJ **J4** emitted a perfectly correct pretty-printed object (`"Name": "Ada"`) but the
  matcher demanded compact `"Name":"Ada"` — a manufactured content failure that the doc could never
  fix, because the code was already right.
- **How:** prefer tolerant patterns (`a,\s*b,\s*c`, `[Tt]rue`, `\":\s*\"` for JSON), assert on
  stable anchors (a deterministic value, an exit code, a substring), and avoid pinning exact
  whitespace or punctuation a competent solution may vary.
- **Diagnostic tell:** if **every arm — including `baseline` — produces the same wrong output**, the
  fault is almost never the model or the doc; it is the *test* (assertion, prompt, or fixture). A real
  content gap shows up as *variation* across arms (the doc moves the needle); a phantom shows up as
  *invariance*. When all arms agree and all fail, fix the instrument, not the document.

### 5. No splicing — one dataset = one clean condition
When the document changes, **re-run the whole suite** under identical harness conditions.
Never fuse runs from different timestamps or doc versions into one dataset.

- **Mistake it prevents:** the nuget-fetch "12/12" splice (overnight 0.7.0 results + targeted
  0.7.1 re-runs of two scenarios) was unreproducible and confusing. A clean full re-run
  showed the same result and removed all ambiguity.
- **How:** before/after = **two fresh full runs**. If you reuse unchanged scenarios, you are
  splicing — don't.

### 6. Separate stable metrics from noisy ones
- **Stable across runs (safe to lead with at low n):** IET, output tokens, cost, archaeology
  (web / cache / dotnet-inspect calls).
- **Noisy (needs adequate n, rule 2/3):** per-scenario functional pass.
- **Signal, never a gate:** judge quality score (it saturates and wobbles near the floor).

Lead the story with the stable metrics; gate on functional pass only when `n` is adequate.

### 7. Model-for-the-claim
A **completeness** claim ("does the document let the model do every task?") needs a model
steady enough to give a stable pass rate. If a unit is too noisy on the mini-tier model
(haiku), measure completeness on the frontier model (opus) and use haiku for
cost/efficiency. Cost/archaeology claims hold on both tiers.

### 8. Pre-register, then look
Fix the arm (1), the runs and variance handling (2/3), the success threshold (3), and the
assertion review (4) **before** rendering numbers. Choosing them after seeing results is
p-hacking — it manufactures whichever conclusion you were hoping for.

### 9. Log turns-per-rung — the doc tax is turn-coupled, so a settings artifact can pass as a content result
Per-turn IET carries a **doc tax**: the grounding block is re-read at the cache rate every turn, so
its cost ≈ `0.1 × doc_tokens × turns`. That scales with **turn count**, and turn count is set by the
**harness turn budget** and the **model's reasoning effort** — not by the content alone.
- **Mistake it prevents:** a per-rung IET crossover, harm-region width, or push-vs-pull edge that
  actually moved with the *turn budget* or *thinking level* being read as a content/delivery result.
  And the deeper coupling: grounding *removes* exploration turns, so a doc that works **shrinks its
  own tax** — the tax is endogenous to the doc's effectiveness, not a flat offset. The harm region is
  therefore precisely the rungs where the doc adds *reading* turns without removing *exploration*
  turns; you cannot see that without the turn counts.
- **How:** record **turns per rung, split by kind** — *exploration* turns (web / nuget-cache /
  dotnet-inspect: our archaeology metric) vs *irreducible* turns (output/reasoning) — alongside IET;
  hold **turn budget and reasoning effort fixed** across every compared arm; and before calling any
  IET crossing a content (or delivery) finding, check it against the split. A total turn count shows
  the tax *move* but can't *attribute* it; only the split makes "adds reading-tax without removing
  exploration turns" a measurement rather than a sentence. (We already count exploration per scenario
  in `toolStats` — surfacing it per rung is a reporting change, not new instrumentation.)

### 10. Prompts must pin a unique correct answer — no ambiguous values, no unpinned round-trips
An assertion can only be as precise as the task. If the prompt leaves the required answer
under-determined, competent models diverge to *different* valid outputs and the assertion fails them
all — a phantom that looks like a content gap. Two recurring shapes, both from STJ:
- **Value collides with a domain term.** STJ **J5** said *"serialize `` `System.Text.Json` ``"* as a
  data value — but `System.Text.Json` is also *the library under test*, so every arm (baseline
  included) resolved it to a canonical sample package (`Newtonsoft.Json`) and failed the exact-string
  match, despite mapping the custom property names flawlessly. Pick sample values that cannot be read
  as the API, the library, or any term in the surrounding prose.
- **Unpinned round-trip input.** STJ **J7** asked to *"deserialize string-valued status JSON"* without
  saying **which** value; models picked `Running`, the assertion wanted `Complete`. The skill being
  tested (enum-as-string) round-tripped correctly — only the unspecified input value differed. Pin the
  exact input the round-trip must consume (e.g. *"deserialize `{"Status":"Complete"}`"*).
- **How:** for every prompt, ask "is there exactly one output a correct solution can produce?" If a
  value, order, or intermediate is free, either pin it in the prompt or widen the assertion to accept
  the whole valid set — never both under-specify *and* match exactly.

---

## Pre-run checklist

- [ ] Which arm is the claim about? (`skilledIsolated` for a document.)
- [ ] What else is on the shelf for this unit? (shared `grounding/` dir ⇒ contamination risk)
- [ ] Expected variance? Is `n` adequate, or do I report rates + intervals?
- [ ] Success threshold stated.
- [ ] Assertions reviewed for brittleness (pass the reference **and** a valid variant).
- [ ] One clean condition per dataset — no splice.
- [ ] Turn budget + reasoning effort fixed across the arms being compared.

## Reporting checklist

- [ ] Numbers are from `skilledIsolated`.
- [ ] High-variance scenarios flagged; per-scenario verdicts only where variance permits.
- [ ] Stable metrics (cost/archaeology) lead; functional pass qualified by `n`.
- [ ] Turns-per-rung reported with IET; any IET crossing checked against turn counts (not a
      turn-budget/thinking-level artifact).
- [ ] "Impossible" results (a doc making the model *worse* at an unrelated task) are treated
      as a noise/contamination/assertion flag, not a finding.
- [ ] Datasets + harness commit + package versions pinned for reproduction.
