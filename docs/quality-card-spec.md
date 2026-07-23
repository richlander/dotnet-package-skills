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
- `Kᵢˣ ∈ {0..k}` = runs that produced a **full-price unit** (clean pass: all functional assertions,
  ends *and* means). Yield `pᵢˣ = Kᵢˣ / k`. **Productive** = `Kᵢˣ ≥ 1`; **failed** = `Kᵢˣ = 0` only.
- Per-run cost in three currencies — **IET** (levelized tokens, primary; see `iet-model.md`),
  **turns**, **sec** — written `cᵢˣ,ᵣ` (i.e. `IETᵢˣ,ᵣ`, `turnsᵢˣ,ᵣ`, `secᵢˣ,ᵣ`).
- **Levelized cost per unit** `Lᵢˣ = (Σᵣ cᵢˣ,ᵣ) / Kᵢˣ` — a task's whole batch cost (the failed runs of a
  *yielding* task are the retry tax in the numerator) ÷ its full-price units; undefined when `Kᵢˣ = 0`.
  A displayed arm **level** is the arithmetic mean of `Lᵢˣ` over `S` (context only); the **ratio** is
  the geometric mean of `rᵢ = Lᵢᵍ/Lᵢᵇ` (the verdict quantity).
- **Shared productive set** `S = { i : Kᵢᵇ ≥ 1 and Kᵢᵍ ≥ 1 }` — where cost is compared, per-task
  paired and equal-weighted. Difficulty is controlled *within* `S` (the task is identically hard for
  both arms); the cost verdict is therefore **conditional on joint productivity** (both arms produced),
  not a suite-wide claim.
- **Two means.** *Totals* (whole-run spend) share a unit, so they **add** — arithmetic sum. *Per-unit
  cost ratios* combine by multiplying, not adding, so an arithmetic mean gives the **wrong answer**
  (a `2×` and a `0.5×` should wash to `1×`, but average to `1.25×`) — summarize with the **geometric
  mean** of `rᵢ = costᵢᵍ/costᵢᵇ`. See the model doc for the derivation.

## ① Outcome — the coverage scoreboard

*Example values are the current **binary `k=1` lens** — a single run's pass bit, so `pᵢˣ ∈ {0,1}` and
the `τ = 3/5` bar collapses to "passed" (reliably-solved and productive coincide). The graded `τ = 3/5`
threshold takes effect once per-run capture lands.*

| Label | Equation | Example (b→g) | Description |
| --- | --- | --- | --- |
| `tasks correct` | `#{ i : pᵢˣ ≥ 3/5 }` (i.e. `Kᵢˣ ≥ 3`) | `9 → 21` *(binary lens)* | **Reliably-solved** view (*can it produce dependably?*). The headline. A separate lens from the productive scoreboard below — not decomposed by it. Higher better. |
| `↳ both / grounded-only / baseline-only / neither` | four-way paired split of `(Kᵢᵇ≥1, Kᵢᵍ≥1)` | `9 / 12 / 0 / 3` | **Productive** view (*can it ever produce?*, `K≥1`). **`baseline-only` = regressions (a gate)**; `grounded-only` = new capability wins; shown as counts, never netted. |
| `↳ coverage gained / lost` | `#grounded-only / #baseline-only` | `+12 / −0` | New wins vs lost coverage. *(graded: unlock/loss yield mass `Σ₍grounded-only₎ pᵢᵍ` / `Σ₍baseline-only₎ pᵢᵇ` — restricted to the crossing tasks, not all-task yield movement.)* |
| `↳ func passed` | `Σᵢ fpᵢ / Σᵢ ftᵢ` | `103/126 → 123/126` | Functional assertions passed / total, summed. The assertion-level proof behind `tasks correct`. |
| `↳ reliability` | yield spread over the `k` runs | *(graded)* | How trustworthy each result is on `n=5` — the noise ruler for the verdict. Binary today; graded when per-run capture lands. |

## ② Mechanism — skills vs. archaeology

Context for the **assumed** mechanism (a skill read replacing library archaeology); not a causal claim.

| Label | Equation | Example (b→g) | Description |
| --- | --- | --- | --- |
| `skills activated` | `Σᵢ actᵢ` | `0 → 13` | Tasks where a skill was **activated**. Baseline = 0 by construction. |
| `↳ expected skill detected` | `H / T_t` | `18/24` | Of the `T_t` tasks targeting a specific skill, how many **detected** it (`H`). Detection is looser than activation, so this can exceed `skills activated`. Discovery signal. |
| `↳ unique skills: target / other` | `#(detected ∩ target) / #(detected ∖ target)` | `— → 5 / 0` | Distinct skills pulled, split into the package-under-test's own shelf vs. foreign skills mixed on (e.g. STJ during a markout run). |
| `relied on archaeology: cache / nuget.org` | `Σᵢ cacheᵢ / Σᵢ nugetᵢ` | `20 / 3 → 6 / 0` | Totals: NuGet-cache decompiles and nuget.org fetches — the digging grounding should delete. Lower better. |
| `↳ tool calls: web / bash / other` | `Σᵢ webᵢ / bashᵢ / otherᵢ` | `4/172/217 → 1/103/197` | Raw, **unfiltered** tool-call totals by class (every web fetch, bash call, etc.). Context, not a judgment. |

## ③ Turns — symmetric with ④, ⑤

| Label | Equation | Example (b→g) | Description |
| --- | --- | --- | --- |
| `Total turns` | `Σᵢ,ᵣ turnsᵢˣ,ᵣ` | `343 → 246` | Total turns across the whole run. Lower better. |
| `↳ tool-call turns (% of turns)` | `Σ tturns` ; share | `319 → 222 (93% → 90%)` | Tool-firing turns and their share. |
| `Turns per unit (shared set)` | geo-mean of `rᵢ = Lᵢᵍ/Lᵢᵇ` (turns) over `S` | `8.8 → 7.4` levels; ratio *(recompute)* | Levelized turns per full-price unit on the shared set. Summarize the per-task **ratio** by **geometric mean** (ratios multiply, so an arithmetic mean is wrong — see model doc); arm levels (arithmetic mean of `Lᵢˣ` over `S`) shown for context. |

## ④ Wall-clock (duration) — symmetric with ⑤

| Label | Equation | Example (b→g) | Description |
| --- | --- | --- | --- |
| `Total duration` | `Σᵢ,ᵣ secᵢˣ,ᵣ` ; `%Δ` | `1719s → 1214s (−29%)` | Total wall-clock across the run (incl. failures). Lower better. |
| `↳ tool-call turn secs (% of turn time)` | `Σ toolSec` ; share of `Total duration` | `1606s → 1100s (93% → 91%)` | Seconds in tool-firing turns + share. |
| `Duration per unit (shared set)` | geo-mean of `rᵢ = Lᵢᵍ/Lᵢᵇ` (sec) over `S` | `40.0s → 34.1s` levels; ratio *(recompute)* | Levelized wall-clock per full-price unit on the shared set; geometric mean of the per-task ratio. |

## ⑤ Token cost (IET) — the punchline

| Label | Equation | Example (b→g) | Description |
| --- | --- | --- | --- |
| `Total IET` | `Σᵢ,ᵣ IETᵢˣ,ᵣ` ; `%Δ` | `2.05M → 1.59M (−22%)` | Total levelized IET across the run (incl. failures). Lower better. |
| `↳ Skill IET (doc)` | `SkillIET = Σᵢ DocTokᵢ·(w_write + w_read·(t̄ᵢ−1))` | `0 → 54.1k` | Carrying cost of the skill doc: written once, cache-read each later turn (`t̄ᵢ` = mean turns). The **toll** (reuse regime: fresh session per task). |
| `↳ Work IET (agent)` | `Total − Skill IET` | `2.05M → 1.54M` | Everything else (thinking / output / tools). `Total = Skill + Work`. |
| `↳ skill load (tok)` | `DocTok = g_tok · Σᵢ actᵢ` | `0 → 24.9k` | Total skill-doc tokens loaded (doc size × activations). Context. |
| `↳ output (tok · IET share)` | tokens `Σᵢ outᵢ` ; IET share = output-IET / Total IET | `142k tok · 34% IET → 98.3k tok · 30% IET` | Output **tokens** (raw count) and output's **IET share** (weighted — output IET is priced above input, so its share of Total IET far exceeds its raw-token share). Two bases by design: the `142k` is tokens, the `34%` is weighted IET. |
| `↳ tool-call turn IET (% of turn IET)` | share | `92% → 92%` | % of turn IET in tool-firing turns. |
| `IET per unit (shared set)` | geo-mean of `rᵢ = Lᵢᵍ/Lᵢᵇ` (IET) over `S` | `46.9k → 44.0k` levels; ratio *(recompute)* | Levelized IET per full-price unit on the shared set — the economic punchline. Geometric mean of the per-task **ratio** (ratios multiply — an arithmetic mean is the wrong answer); carries its own task-level band. |

## Verdict

| Label | Equation | Example (b→g) | Description |
| --- | --- | --- | --- |
| `verdict` | tally of per-task grades + gate, **per model class** | `<strong·half·mixed·wash·regression·capability>` *(schematic — needs graded data)* | Each **both-productive** task is graded **strong win** (both axes better), **half win** (one axis better, other held), **mixed** (one better, other worse — a genuine trade), **wash** (both held), or **regression** (an axis worse with no compensating better). Grounded-only unlocks are **capability** wins (Axis 1 only — no cost axis), tallied separately. The aggregate *is* the tally (rows, no synthetic score). **Gate:** any material `baseline-only` loss disqualifies regardless of wins. **Bar is model-scoped:** frontier → cost-led wins; mini → capability unlocks; never pool classes. Primary currency IET; each margin = `max(practical floor, noise floor)`, predeclared ex ante per model class; thin `S` → "not estimable". |

## Invariants

1. **Only comparable values** — every row supports a like-for-like comparison. Never pool **cost levels**
   across tasks with different bases (no `Σcost/ΣK` blend over heterogeneous productive sets — the
   Simpson guard). A 0–1 rate like yield *does* average across tasks legitimately.
2. **Failed means `K = 0` only** — a `2/5` task is low-yield productive, not a failure.
3. **Cost is paired on the shared set `S`**, equal-weighted — never a mix-weighted pool across
   differing productive sets (the Simpson guard).
4. **Coverage is rows, not a net** — `baseline-only` (regressions) stays a visible gate.
5. **Lead with totals**; the only per-unit rate is the shared-set paired cost.
6. **Card ⊇ chart** — every value on the LIET SVG appears on the card; the card may add more.
