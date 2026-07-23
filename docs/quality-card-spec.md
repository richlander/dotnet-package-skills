# Quality-card row spec

The **row-level reference** for the quality card (`analyze --view card`): every row as
**Label · Equation · Example · Description**. It *derives from* the measurement model in
`quality-card-model.md` — see that doc for the rationale (the two axes, graded yield, the difficulty
/ pairing arguments, the deliberate exclusions). This doc only pins down what each row *is*.

Example values are the markout CT-24 holistic run (`N = 24`, `claude-haiku-4.5`, `baseline → grounded`).
They are the current **binary (last-run) measurement**; rows marked *(graded)* become richer once the
harness persists per-run yield (`Kᵢˣ / k`) and per-run cost into the results JSON — a change that needs
a re-run, since neither is recoverable from existing artifacts (see the model doc's *Capture* note).

## Notation

- `N` tasks, `i = 1..N` (markout: `N = 24`); each `(task, arm)` run `k = 5` times (fixed batch).
- Arms: `b` = baseline (ungrounded), `g` = grounded (arm under test, default `skilledPlugin`).
- `Kᵢˣ ∈ {0..k}` = runs that **Deliver** — the **full-price unit** on the `Fails → Satisfies →
  Delivers` ladder (clears both gates: **satisfies** = all functional assertions pass, **delivers** =
  did it as asked / taught API, not a hand-rolled equivalent). Yield `pᵢˣ = Kᵢˣ / k` counts `Delivers`
  only. **Productive** = `Kᵢˣ ≥ 1`; **failed** = `Kᵢˣ = 0` only. A run that *satisfies* but does not
  *deliver* is a workable "second," reported (C4) but **not** a full-price unit.
- Per-run cost in three currencies — **IET** (levelized tokens, primary; see `iet-model.md`),
  **turns**, **sec** — written `cᵢˣ,ᵣ` (i.e. `IETᵢˣ,ᵣ`, `turnsᵢˣ,ᵣ`, `secᵢˣ,ᵣ`).
- **Levelized cost per unit** `Lᵢˣ = (Σᵣ cᵢˣ,ᵣ) / Kᵢˣ` — a task's whole batch cost (the non-delivered
  runs — **Fails or Satisfies** — of a *yielding* task are the retry tax in the numerator) ÷ its
  full-price units; undefined when `Kᵢˣ = 0`.
  A displayed arm **level** is the **geometric** mean of `Lᵢˣ` over `S`; the level ratio then equals the
  ratio row exactly (`GM(Lᵍ)/GM(Lᵇ) = GM(rᵢ)`), so levels and ratio compose. The **ratio** is the
  geometric mean of `rᵢ = Lᵢᵍ/Lᵢᵇ` (the verdict quantity).
- **Shared productive set** `S = { i : Kᵢᵇ ≥ 1 and Kᵢᵍ ≥ 1 }` — where cost is compared, per-task
  paired and equal-weighted. Difficulty is controlled *within* `S` (the task is identically hard for
  both arms); the cost verdict is therefore **conditional on joint productivity** (both arms produced),
  not a suite-wide claim.
- **Two means.** *Totals* (whole-run spend) share a unit, so they **add** — arithmetic sum. A *typical
  per-unit cost multiplier* is summarized by the **geometric mean** of `rᵢ = costᵢᵍ/costᵢᵇ` (symmetric
  under reciprocal: a `2×` and a `0.5×` wash to `1×`, where an arithmetic mean would report `1.25×`).
  See the model doc for the derivation.

## ① Outcome — the coverage scoreboard

*Example values are the current **binary `k=1` lens** — a single run's **functional-pass bit** (the
**satisfies** gate only: assertions pass), used as a **proxy** for `Delivers` because the harness does
not yet persist the `delivers` bit. So today `pᵢˣ ∈ {0,1}` and the `τ = 3/5` bar collapses to
"functionally passed" (reliably-delivered and productive coincide **only under this proxy**). The
graded `τ = 3/5` threshold — and the true `Delivers`-counted `Kᵢˣ` — take effect once per-run capture
lands; until then, read every `Delivers`/`Kᵢˣ` figure below as its functional-pass stand-in, not a
confirmed fidelity signal.*

| Label | Equation | Example (b→g) | Description |
| --- | --- | --- | --- |
| `tasks reliably delivered` | `#{ i : pᵢˣ ≥ 3/5 }` (k=5: `Kᵢˣ ≥ 3`) ; print **yield-mass** `Σᵢ pᵢˣ` beside it | `9 → 21` *(binary lens)* ; mass `… → …` | **Reliably-delivered** view (*can it produce dependably?*), counting `Delivers` runs. The headline. A separate lens from the productive scoreboard below — not decomposed by it. **Print the yield-mass movement `Σᵢ pᵢˣ` (sum of posterior yields) beside the thresholded count**: the count is a hard threshold on noisy binomials, so a single `2/5 ↔ 3/5` boundary cell can move it ±1 with almost no change in the underlying quantity — the mass shows the real movement. Higher better. |
| `↳ both / grounded-only / baseline-only / neither` | four-way paired split of `(Kᵢᵇ≥1, Kᵢᵍ≥1)` | `9 / 12 / 0 / 3` | **Productive** view (*can it ever produce?*, `K≥1`). **`baseline-only` = capability loss (the hard gate)**; `grounded-only` = new capability wins; shown as counts, never netted. |
| `↳ coverage gained / lost` | `#grounded-only / #baseline-only` | `+12 / −0` | New wins vs lost coverage. *(graded: unlock/loss yield mass `Σ₍grounded-only₎ pᵢᵍ` / `Σ₍baseline-only₎ pᵢᵇ` — restricted to the crossing tasks, not all-task yield movement.)* |
| `↳ func passed` | `Σᵢ fpᵢ / Σᵢ ftᵢ` | `103/126 → 123/126` | Functional assertions passed / total, summed. The assertion-level proof behind the **satisfies** gate (function works) — *not* the `Delivers` bit (did it as asked), which the fidelity row below tests. |
| `↳ fidelity (Delivers \| works)` | mean over tasks with ≥1 working run of `Kᵢᵈᵉˡ / #(Satisfies ∪ Delivers)ᵢ` ; run-pooled = memo | *(graded — needs `delivers` bit)* | **C4**: among runs that *work*, the share that also did it **as asked** (taught API, not hand-rolled). **Per-task rate, equal-weighted across working tasks** — *not* pooled over all runs (pooling mix-weights tasks by working-run count → Simpson, violating Invariant 1); the run-pooled value is a memo. Isolates fidelity from mere function; the complement is the workable-but-off-spec "second" rate. Higher better. |
| `↳ reliability` | posterior/CI uncertainty of the yield `pᵢˣ` | *(graded)* | How trustworthy each result is on `n=5` — the noise ruler for the verdict. Binary today; graded when per-run capture lands. |

## ② Mechanism — skills vs. archaeology

Context for the **assumed** mechanism (a skill read replacing library archaeology); not a causal claim.

| Label | Equation | Example (b→g) | Description |
| --- | --- | --- | --- |
| `skills activated` | `Σᵢ actᵢ` (`actᵢ = 1` if a skill activated in **any** of the `k` runs) | `0 → 13` | Tasks where a skill was **activated**. Baseline = 0 by construction. |
| `↳ expected skill detected` | `H / T_t` | `18/24` | Of the `T_t` tasks targeting a specific skill, how many **detected** it (`H`). Detection is looser than activation, so this can exceed `skills activated`. Discovery signal. |
| `↳ unique skills: target / other` | `#(detected ∩ target) / #(detected ∖ target)` | `— → 5 / 0` | Distinct skills pulled, split into the package-under-test's own shelf vs. foreign skills mixed on (e.g. STJ during a markout run). |
| `relied on archaeology: cache / nuget.org` | `Σᵢ cacheᵢ / Σᵢ nugetᵢ` | `20 / 3 → 6 / 0` | Totals: NuGet-cache decompiles and nuget.org fetches — the digging grounding should delete. Lower better. |
| `↳ tool calls: web / bash / other` | `Σᵢ webᵢ / bashᵢ / otherᵢ` | `4/172/217 → 1/103/197` | Raw, **unfiltered** tool-call totals by class (every web fetch, bash call, etc.). Context, not a judgment. |

## ③ Turns — symmetric with ④, ⑤

| Label | Equation | Example (b→g) | Description |
| --- | --- | --- | --- |
| `Total turns (shared set)` | `Σ_{i∈S} median_delivered turnsᵢˣ` | `… → …` | Total turns on the **shared set `S`**, per arm — median-Delivered run per cell (see Total IET). Failure not re-charged. Lower better. |
| `↳ tool-call turns (% of turns)` | `Σ_{i∈S} tturns` (rep. run) ; share | `… (… → …)` | Tool-firing turns of the representative runs on `S` and their share of `Total turns (shared set)`. |
| `Turns per unit (shared set)` | geo-mean of `rᵢ = Lᵢᵍ/Lᵢᵇ` (turns) over `S` | `8.8 → 7.4` levels; ratio *(recompute)* | Levelized turns per full-price unit on the shared set. Summarize the per-task **ratio** by **geometric mean** (the right summary for a typical multiplier — see model doc); arm levels (geometric mean of `Lᵢˣ`, which compose with the ratio) shown for context. |

## ④ Wall-clock (duration) — symmetric with ⑤

| Label | Equation | Example (b→g) | Description |
| --- | --- | --- | --- |
| `Total duration (shared set)` | `Σ_{i∈S} median_delivered secᵢˣ` ; `%Δ` | `… → … (−…%)` | Total wall-clock on the **shared set `S`**, per arm — median-Delivered run per cell (see Total IET). Failure not re-charged. Lower better. |
| `↳ tool-call turn secs (% of turn time)` | `Σ_{i∈S} toolSec` (rep. run) ; share of `Total duration (shared set)` | `… (… → …)` | Seconds in tool-firing turns of the representative runs on `S` + share. |
| `Duration per unit (shared set)` | geo-mean of `rᵢ = Lᵢᵍ/Lᵢᵇ` (sec) over `S` | `40.0s → 34.1s` levels; ratio *(recompute)* | Levelized wall-clock per full-price unit on the shared set; geometric mean of the per-task ratio. |

## ⑤ Token cost (IET) — the punchline

| Label | Equation | Example (b→g) | Description |
| --- | --- | --- | --- |
| `Total IET (shared set)` | `Σ_{i∈S} med_delivered IETᵢˣ` ; `%Δ` | `… → … (−…%)` | Total IET (weighted tokens) on the **shared set `S`**, per arm — apples to apples. Per task/arm we take the **interpolated median IET among that cell's Delivered runs** (even count → mean of the two middle runs, which **removes the even-`K` lower-median bias** — it targets the central-50% location consistently — so a flaky low-`K` arm is not made to look cheaper). "What a full-price unit costs," robust to the tail. Failure is **not** re-charged here (Axis 1 owns it). Lower better. **Reconciliation note:** this `Totalᵍ/Totalᵇ` (arithmetic, median-of-Delivered) will **not** equal the `IET per unit` ratio below (geometric, levelized over `S`) — don't divide the totals to recover it. Each answers its own question: the total is "what the delivered work cost in aggregate," the ratio is "the typical per-task multiplier." Only the *levels* row composes geometrically. |
| `↳ grounded-only / baseline-only (memo)` | `Σ median_delivered IET` on each off-`S` partition | `… ; …` | Off-shared-set spend as **memo lines**, never compared: grounded-only = capability *investment*; baseline-only = lost coverage. |
| `↳ Skill IET (doc)` | `SkillIET = Σ_{i∈S} SkillIET̃ᵢ`, `SkillIET̃ᵢ = mean over the central run(s) of DocTokᵢ,ᵣ·(w_write + w_read·(tᵢ,ᵣ−1))` | `0 → …` | Carrying cost of the skill doc on `S`: written once, cache-read each later turn. Computed **per representative run then averaged** — for an even-`K` central pair, take each run's own Skill IET (its own activation `actᵢ,ᵣ` and turns `tᵢ,ᵣ`) and average the two, **never** recombine separately-averaged activation × turns (the product of averages ≠ average of products, and would break `Total = Skill + Work`). Uses the **representative run's** activation, not any-of-`k`. The **toll** (reuse regime: fresh session per task). |
| `↳ Work IET (agent)` | `Total(S) − Skill IET`, i.e. `Σ_{i∈S}` mean over the central run(s) of `(Totalᵢ,ᵣ − SkillIETᵢ,ᵣ)` | `… → …` | Everything else (thinking / output / tools) on `S`. Also computed **per run then averaged**, so `Total = Skill + Work` holds exactly for the even-`K` central pair. |
| `↳ skill load (tok)` | `DocTok = g_tok · Σ_{i∈S} act̃ᵢ` | `0 → …` | Total skill-doc tokens loaded on `S` (doc size × representative-run activations). Context. |
| `↳ output (tok · IET share)` | tokens `Σ_{i∈S} outᵢ` (rep. run) ; IET share = output-IET / `Total IET (shared set)` | `… tok · …% IET → … tok · …% IET` | Output **tokens** (raw count) and output's **IET share** (weighted — output IET is priced above input, so its share of Total IET exceeds its raw-token share), both on the representative runs of `S`. Two bases by design: the token count is raw, the `%` is weighted IET. |
| `↳ tool-call turn IET (% of turn IET)` | share of `Total IET (shared set)` (rep. run) | `… → …` | % of turn IET in tool-firing turns of the representative runs on `S`. |
| `IET per unit (shared set)` | geo-mean of `rᵢ = Lᵢᵍ/Lᵢᵇ` (IET) over `S` | `46.9k → 44.0k` levels; ratio *(recompute)* | Levelized IET per full-price unit on the shared set — the economic punchline. Geometric mean of the per-task **ratio** (symmetric under reciprocal — the typical multiplier); carries the **suite-level paired bootstrap band** (task-paired resampling, aggregated over `S`). |
| `↳ cost predictability (σ_g/σ_b)` | ratio of arm-specific log-cost SDs (`σ_within` per arm, **Delivered runs only**) ; band | `… (memo)` | Run-to-run cost steadiness — the **C5** memo. `σ_g/σ_b < 1` (band excluding 1) = grounding is steadier, not just cheaper. Estimated on **Delivered runs only** (non-delivered cost inflates the flakier arm's σ — usually baseline — and would credit C5 with C2's unreliability). Reported, not gated. |

## Verdict

| Label | Equation | Example (b→g) | Description |
| --- | --- | --- | --- |
| `verdict` | tally of per-task grades + gate, **per model class** | `<strong·half·mixed·wash·regression·capability>` *(schematic — needs graded data)* | Each **both-productive** task is graded **strong win** (both axes better), **half win** (one axis better, other held), **mixed** (one better, other worse — a genuine trade), **wash** (both held), or **regression** (an axis worse with no compensating better). Grounded-only unlocks are **capability** wins (Axis 1 only — no cost axis), tallied separately. The aggregate *is* the tally (rows, no synthetic score). **Gate:** a *material* `baseline-only` loss (`Δpᵢ` past the margin **and** band excludes zero) disqualifies regardless of wins — **or** a suite-level **loss-mass band** `Σ max(−Δpᵢ,0)` over baseline-only/regressed tasks, calibrated against a **null-bootstrap** (not literal zero, since the truncated sum is `≥0` under noise) with a **predeclared** materiality threshold, so diffuse flaky losses no single task can prove still trip it. **Bar is model-scoped:** frontier → cost-led wins; mini → capability unlocks; never pool classes. Primary currency IET; each margin is a **practical floor**, predeclared ex ante per model class (no separate noise floor — the suite band carries sampling uncertainty; noise sized from pooled log-scale `σ_within`, currently ~5% IET frontier / ~13% mini, only to keep the floor above harness resolution). **Yield inference:** beta-binomial with a **predeclared uniform `Beta(1,1)`** prior (primary) + a **Jeffreys `Beta(½,½)`** sensitivity read; independent per-cell priors (no hierarchical pooling — tasks are heterogeneous by design, and pooling would dilute C1 unlocks). Gate each axis in its **benefit direction**: yield (higher-better) on the band's lower bound; cost ratio `R` (lower-better) on `−ln R` or `R`'s upper bound — never a lower-bound rule on `R` itself. Thin `S` (`|S| < 8`) → "not estimable". |

## Invariants

1. **Only comparable values** — every row supports a like-for-like comparison. Never pool **cost levels**
   across tasks with different bases (no `Σcost/ΣK` blend over heterogeneous productive sets — the
   Simpson guard). A 0–1 rate like yield *does* average across tasks legitimately.
2. **`K = 0` (delivered no unit) is the only task-level failure** — a `2/5` task is low-yield
   productive, not a failure. `Kᵢˣ` counts **Delivers** runs only.
3. **Ladder ordering: `delivers ⇒ satisfies`** — a run cannot Deliver (did it as asked) without first
   Satisfying (it works); the grade is the highest gate cleared. **Fidelity** (`Delivers | works`) is
   **not estimable** for a task with zero working runs (empty `Satisfies ∪ Delivers`).
4. **Cost is paired on the shared set `S`**, equal-weighted — never a mix-weighted pool across
   differing productive sets (the Simpson guard).
5. **Coverage is rows, not a net** — `baseline-only` (regressions) stays a visible gate.
6. **Lead with totals**; the only per-unit rate is the shared-set paired cost.
7. **Card ⊇ chart** — every value on the LIET SVG appears on the card; the card may add more.
8. **Closed grading contract** — everything a run is graded on is inferable from the **prompt alone**;
   the skill teaches *how*, never *what*. Each assertion pairs a **mini-prompt** (the prompt-subset it
   gates) with an executable **test**; a design-time `assertion→prompt` audit classifies each as a
   **requirement** (entailed → gate at `Delivers`) or a **convention** (consistent-only → `Satisfies`
   accepts any working reading, win reported at C4). **Gradable scope is verifiable requirements only**
   (API/approach choice, technical constraints, functional correctness); subjective quality (idiom,
   elegance) is out of scope — so every gating assertion is a deterministic test and the certified path
   carries no judge noise.
