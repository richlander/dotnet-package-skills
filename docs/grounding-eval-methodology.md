# Grounding eval methodology — measuring, deciding, and shipping package grounding

This is the standalone reference for how we evaluate package **grounding content** (an `AGENTS.md`
that ships in a package root) and decide whether it ships. It covers (1) the methodology, (2) the
terms we use or redefine, (3) the **threshold gate** that decides whether a change ships, and (4) the
copy-paste eval dump that carries the evidence (e.g. into a PR).

Core rule: **a grounding change is a claim, and a claim ships with its evidence.** An `AGENTS.md` edit
without a reproducible eval behind it is not reviewable. This matches the rigor we hold code PRs to —
see [dotnet-inspect#1209](https://github.com/richlander/dotnet-inspect/pull/1209) for the shape we are
matching: a *Changes* summary, a `Baseline | … | Delta` table, a representative check, and an explicit
*Validation* block. Broader context:
[aspire#18437 (comment)](https://github.com/microsoft/aspire/pull/18437#issuecomment-4782880736).

---

## 1. Methodology

We reuse the [`dotnet/skills`](https://github.com/dotnet/skills) **skill-validator** harness to run a
**baseline vs. grounded** evaluation over a fixed set of scenarios (a "6-question unit", e.g. N1–N6
for NuGetFetch, M1–M6 for Markout). Each scenario runs three **arms**:

- **baseline** — no grounding; model knowledge only. The agent falls back to **archaeology** — searching outside the sandbox (web fetch/search, rummaging the restored NuGet cache, etc.) to reconstruct what grounding would have told it.
- **skilled-isolated** — grounding delivered inline. A research-only diagnostic; **not shown on ship cards**.
- **skilled-plugin** — grounding delivered as an auto-loaded skill. Stands in for any **grounding tool** (a packed `AGENTS.md`, the NuGet MCP, `dotnet-inspect`, …); the ship gate is read off this arm.

The grounded arms can be run with **either** grounding source: `AGENTS.md` (the curated grounding) or
the package `README.md` (the **fallback** a grounding tool surfaces when no `AGENTS.md` ships). Running
both feeds the **source-diff card** (③ in §4), which isolates `AGENTS.md − README.md` as a **usability
test of the README**: if the README arm fails questions or forces archaeology, those are README bugs to
fix; and if a *complete* README already clears the gate as cheaply, the curated `AGENTS.md` isn't earning
its place.

A pairwise LLM judge scores rubric quality; the harness also records tokens, cost, tool calls, and
assertion pass/fail. Mechanics live in [`harness.md`](./harness.md). We read results with
`eng/analyze-6q.py` (full table) or `eng/analyze-6q.py --card` (the PR dump, §4).

**Why grounded must be compared carefully.** The baseline is *not* a clean "model ignorance" control:
a package's `README.md`/`AGENTS.md` are packed inside its nupkg, so any `dotnet build` restores them
to `~/.nuget/packages`, where the baseline can read them (one form of archaeology). So **the baseline
is partly self-grounded and the measured gap understates grounding's value** (see [harness.md](./harness.md)
for the per-arm read attribution and the empty-cache analysis). Treat every quality delta as a *lower
bound*.

---

## 2. Terms we use or redefine

| Term | Meaning here |
| --- | --- |
| **Grounding** | A compact, package-specific `AGENTS.md` that ships in the package root and makes the package self-teaching for an agent. Records only what the model is *proven to lack* — not model-resident knowledge. |
| **`AGENTS.md` vs `SKILL.md`** | `AGENTS.md` is the source of truth (it ships in the package). `SKILL.md` is generated from it by `eng/sync-skill.sh` purely so the harness can toggle grounding on/off. Never hand-edit `SKILL.md`. |
| **isolated / plugin → "grounding tool"** | The two grounded delivery channels the harness simulates. **plugin** (auto-loaded) is closest to a packed `AGENTS.md` that is always present — it stands in for any **grounding tool** (packed `AGENTS.md`, NuGet MCP, `dotnet-inspect`), so the ship gate is evaluated on it and ship cards label it **"Grounding tool"**. **isolated** is a research-only diagnostic, omitted from cards. |
| **resourcefulness (archaeology)** | Out-of-sandbox lookups the agent must make to recover API knowledge that grounding would supply inline — web fetch/search **plus** local NuGet-cache rummaging / decompiling DLLs. Measured **objectively** from the timeline (not the judge). **High = the agent had to be resourceful; grounding's job is to drive it to 0, so lower is the win, not a loss.** Cards show it as one **resourcefulness (archaeology)** row; the **web** portion alone is a hard gate guard (a grounded run must never resort to the internet). |
| **success** | A scenario is **solved** for an arm iff every functional assertion passes **and** the judge's overall quality clears the **≥4 floor** ("meets expectations"). Reported per arm as a rate (e.g. `6/6`). The headline **value** metric. The judge's 1–5 score enters **only** as this pass/fail floor — its subjective 4→5 top band is discarded (see §7). |
| **quality** (judge `overallScore` 1–5) | Used **only** as the ≥4 success floor above — never as a reported metric or a baseline diff. Its top ~1 point is subjective and instruction-sensitive (§7), so it cannot grade harm. |
| **func** | Functional assertions passed (build + file + run-output regex). A **value** metric; the objective correctness signal that, with the ≥4 floor, defines `success`. |
| **tok** | Gross tokens (`input + output`); `input` *includes* cache reads. Inflated by cache re-reads, so not the harm by itself. |
| **IET** | **Input-Equivalent Tokens** — cache-excluded effective tokens, `(input − cacheRead) + output`. Our headline cost stick (see `README.md`) and the **frontier harm number** (§3). Empirically **input-dominated**: output is only ~7–21% of IET, non-cached input ~79–93%, so IET mainly tracks context/read bloat (the likeliest grounding harm). `tok` and `iet` bracket the real spend; `cost` sits between. |
| **cost** | Premium-request multiplier (cache-discounted). The truest single harm proxy. |
| **output tok** | Output/thinking tokens. The most expensive *per-token* and most variable component. A small share of IET, so kept as its **own visible guard row** (§3) lest an output-only blow-up be masked when input nets down. |
| **Normative metric** | A quantity we *claim* as value or harm: `success`, `func`, `resourcefulness`, `tok`, `iet`, `cost`, `secs`. A conclusion may rest only on these. |
| **Informative signal** | Corroborating behavioral data that *explains* a metric move but is never the claim: `web`, `tools`, `turns`, `cache` (bash rummaging `~/.nuget/packages`), `di`, `mcp`, `bash`. A tool call adds nothing to the bill on its own; many signals together trace the narrative arc (archaeology, cache-reflection, retry loops). |
| **warm / cold cache** | Whether the package is restored on disk. For build-based scenarios the agent restores it within its first few tool calls, so **starting cache state is not a variable** — treat it as warm (see harness.md). |
| **verify-close** | A package-specific grounding line that makes the agent surface the final code/API calls, fixing a *verifiability artifact* where the judge underscores efficient grounded runs it can't see (see [nugetfetch report](./reports/nugetfetch.md)). |
| **Pareto gate / tier** | The ship rule (§3). **mini tier** (λ low — weaker/cheaper model that *needs* grounding): **success** is the binding constraint, tokens are cheap → require the card to grade **BETTER** here. **frontier tier** (λ high — strong model that doesn't need it): success is near ceiling → require **not WORSE** (BETTER or NEUTRAL) here. |

---

## 3. The ship decision — require BETTER on the tier that needs it, not WORSE on the tier that doesn't

Grounding is **auto-installed and un-removable**, so the verdict is a **Pareto gate**, not an average
(authoring-principles §4). Modeled directly on the decompiler quality-diff card — which **requires a
real win** (recovery up, malformed down) while holding harm rows (semantic defects, fidelity, pass
bugs) at **zero tolerance** — the grounding gate is two-sided:

> **Ship iff** the change **materially helps the tier that needs grounding (mini)** **AND does no
> meaningful harm to the tier that doesn't (frontier).** Rip it out iff it helps neither.

A complete decision therefore needs **two runs**: a mini-tier run (default `claude-haiku-4.5`) for the
win, and a frontier-tier run (e.g. `claude-opus-*`) for the no-harm check. Each is n ≥ 3, model and
judge named. The gate is evaluated on the **grounding-tool** arm (the `plugin` channel) vs **baseline**, on means across the unit's
scenarios.

> **The card grades; this section decides.** The `--card` conclusion is a single **uniform,
> model-independent grade** of grounding's measured effect vs baseline — **BETTER / NEUTRAL / WORSE**
> (§4) — the *same* rubric for haiku and opus. It deliberately does **not** encode shipping. The
> tier-aware ship decision below is the **higher-level analysis** layered on top of those grades: we
> *require* **BETTER** on the mini tier (it needs grounding) and merely *not* **WORSE** on the frontier
> tier (it doesn't). A frontier **BETTER** is a welcome bonus, never a requirement. Reading the grade is
> the card's job; deciding what the grade means for shipping is yours.

### Mini tier — require BETTER (the win thresholds)

Correctness may never regress; then at least one win axis must clear:

| Axis | Type | Threshold |
| --- | --- | --- |
| `success` | guard (hard) | Δ ≥ 0 — grounding must not solve fewer scenarios |
| `func` | guard (hard) | Δ ≥ 0 — no functional-assertion regression |
| `web` | guard (hard) | grounded **web** calls = 0 (never resort to the internet; local cache peeks are allowed) |
| `success` | **win** | Δ > 0 — solves scenarios the baseline failed, **or** |
| `iet` | **win** | ≥ 25% reduction vs baseline, **or** |
| `cost` | **win** | ≥ 25% reduction vs baseline, **or** |
| `resourcefulness` | **win** | eliminated (baseline > 0 → grounded 0) |

On the mini tier a large cost/IET reduction (or eliminated resourcefulness) with **success held** **is**
a legitimate ship — tokens are cheap, the binding constraint was the model otherwise flailing or
failing. A success *gain* (solving scenarios the baseline failed) is the strongest mini win.

### Frontier tier — require *not WORSE* (a BETTER is a welcome bonus)

The strong model rarely *needs* grounding, so this run is not **required** to be **BETTER** — it must
only prove grounding does **no damage** (the card grades it **not WORSE**: BETTER or NEUTRAL). This is the
direct analog of "no drop in recovery, no increase in malformed". But "not required to be better" is not
"can't be better": if grounding makes even the frontier model **materially cheaper** (clears the same
IET/cost win bar) with no regression, the card grades it **BETTER** on its own — same rubric as every
model. Don't read a frontier NEUTRAL as failure and don't withhold a deserved BETTER; we cornered
ourselves into only ever saying "no harm" on opus once, and the uniform grade fixes that.

So, reading the **uniform card grade** for the frontier ship decision:
- **BETTER** — grounding pays off even here (cheaper, or eliminates resourcefulness, with success held). Ships; a bonus.
- **NEUTRAL** — success held, no material efficiency win. **Ships** — this is all the frontier tier is required to show.
- **WORSE** — success dropped, grounded web archaeology, or IET/cost/output inflated past the cap. **Blocks the ship.**

**WORSE is bounded by a number, not a bool.** The headline harm metric is the **IET diff from baseline**
(`IET_grounded − IET_baseline`, signed). It need not be zero — it carries a **hard cap** (a budget):
grounding may cost a little more on a model that didn't need it, but not a lot (past the cap ⇒ WORSE). In
practice the diff is usually *negative* (grounding makes even the frontier cheaper — which is what tips a
NEUTRAL into a **BETTER**), so the cap rarely binds; it exists to catch a bloated grounding doc. We report
the number so the effect is *tracked as a quantity*, not collapsed to a pass/fail.

| Axis | Threshold |
| --- | --- |
| `success` | Δ ≥ 0 — grounding must not solve fewer scenarios |
| `func` | Δ ≥ 0 — no functional-assertion drop |
| **`IET diff`** | **≤ hard cap** — `IET_grounded − IET_baseline` may not inflate past the budget (**the headline harm number**) |
| `cost` | ≤ hard cap — grounded cost may not inflate past the budget |
| `output tok` | shown as a guard row (see below) — output overspend must be visible even when IET nets out |
| `web` | grounded **web** calls = 0 (cache peeks allowed) |

**Why IET, not output-tokens-only.** A natural objection: the harm we most fear is output/thinking
overspend, so why not gate on output tokens alone? Because in our data **output is *not* the dominant
share of IET** — it is only ~7–21%; non-cached input is ~79–93% (`IET = (input − cacheRead) + output`,
and nothing guarantees output dominates). The **most likely grounding-induced harm on a strong model is
input bloat** — a fat `AGENTS.md`, or one that induces large file reads / extra tool results — and it
lands on *every* request. An output-only gate would be **blind to exactly that failure mode**. IET
catches input bloat *and* output overspend, while correctly discounting cache reads (cheap, and any
cache/input churn it hides reliably shows up in the informative signals — `turns`, `archaeology`,
flailing). The one tradeoff: because IET is input-dominated, a pure "reasons-in-circles" output blow-up
could be partly masked if input nets down — so we keep **`output tok` as its own visible guard row**, and
a small efficiency gain bought with a large output-token increase is still a **fail**.

> These thresholds are the team's starting line (haiku/opus tiers, n=3). They are tunable in one place
> — `GATE` in `eng/analyze-6q.py` — and the analyzer applies them automatically per `--card`.

---

## 4. The eval dump (copy-paste into the PR)

`eng/analyze-6q.py` emits **three single-variable cards**, each isolating exactly one comparison so the
data is trivial to read. Every card shows the same metric rows — `success (scenarios)`, `func passed`,
`resourcefulness (archaeology)`, `IET`, `output tok`, `cost` — and a **Conclusion**: a single **uniform,
model-independent grade** of grounding's effect vs baseline, **BETTER / NEUTRAL / WORSE** (the same rubric
for every model — the card grades, it does not decide shipping; that is §3). Grading keys off **objective
axes only** (success, web archaeology, cost/IET); there is **no judge-quality diff** (§7). A dataset whose
filename contains `readme` is read as the **README arm**.

- **BETTER** — success held and a real win: solves more scenarios, resourcefulness eliminated, or IET/cost down ≥ 25%; no regression.
- **NEUTRAL** — success held, no material efficiency win.
- **WORSE** — success dropped, grounded web archaeology, or cost/IET/output inflated past the cap.

| Card | Flag | Holds fixed | Varies | Answers |
| --- | --- | --- | --- | --- |
| ① **Primary** | `--card` | one model | baseline → AGENTS.md | Does grounding help *this* model? (one card per model, graded BETTER/NEUTRAL/WORSE) |
| ② **Model-diff** | `--model-diff` | AGENTS.md vs baseline | the model | Does the grade hold across tiers — side by side. |
| ③ **Source-diff** | `--source-diff` | one model, grounding-tool delivery | AGENTS.md vs README.md | A **usability test of the README** (not a floor to beat): does the README also answer every question with 0 archaeology? README failures are bugs to **fix in the same PR**. Once the README is complete, AGENTS's edge narrows to efficiency/retrieval. |

```bash
# ① primary, one card per model
python3 eng/analyze-6q.py --card data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>.n3.opus.json
# ② model-diff (AGENTS lift, models side by side)
python3 eng/analyze-6q.py --model-diff data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>.n3.opus.json
# ③ source-diff (AGENTS − README, one model — usually the mini tier)
python3 eng/analyze-6q.py --source-diff data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>-readme.n3.haiku.json
```

**Paste the cards verbatim** into the PR's *Metrics* section. The PR carries four: ① primary (mini), ①
primary (frontier), ② model-diff, ③ source-diff (mini). Example (NuGetFetch, mini primary):

```
### Grounding eval — nugetfetch · `claude-haiku-4.5`

| Metric | Baseline | AGENTS.md |
| --- | ---: | ---: |
| success (scenarios) | 5/6 | 6/6 |
| func passed (assertions) | 17/18 | 18/18 |
| resourcefulness (archaeology) | 35 | 0 |
| IET | 31276 | 17558 |
| output tok | 5782 | 1716 |
| cost | 7.75 | 2.28 |

> **Conclusion:** **BETTER** — success 6/6 vs 5/6, resourcefulness 35→0, IET -44%, cost -71%.
```

The card emits a shared **Legend** explaining each row and the BETTER / NEUTRAL / WORSE grade. For the
operational "which card for which lifecycle operation" guide, see
[`grounding-lifecycle.md`](./grounding-lifecycle.md).

---

## 5. What a grounding PR contains

| Artifact | Path |
| --- | --- |
| The grounding edit (body ≤ `eng/agents-line-limit.txt`, currently 60) | `grounding/<unit>/AGENTS.md` |
| Regenerated wrapper, in sync (`sync-skill.sh --check`) | `grounding/<unit>/SKILL.md` |
| The matched n≥3 mini-tier dataset | `data/<unit>-6q/<unit>.n3.haiku.json` |
| The matched n≥3 frontier-tier dataset (no-harm check) | `data/<unit>-6q/<unit>.n3.opus.json` |
| The report | `docs/reports/<unit>.md` |

### PR description format
Use `.github/PULL_REQUEST_TEMPLATE.md`. Required sections: **Changes** (what changed, the scenario it
supports, and that it is motivated by the metrics), **Metrics** (paste the `--card` dumps), **Analysis**
(what grounding actually changes — usually eliminating the *resourcefulness* the agent otherwise spends to
reach the **same** correct API, not preventing a wrong-API hallucination; verify the claim against the
transcripts), **Validation** (the exact commands), **Caveats** (the cache self-grounding lower-bound +
cache-state-not-a-variable).

### Validation (reproducible)

```bash
eng/sync-skill.sh --check
RUNS=3 eng/run-<unit>-6q.sh                                    # -> data/<unit>-6q/<unit>.haiku.json
RUNS=3 MODELS=claude-opus-4.8 eng/run-<unit>-6q.sh            # frontier no-harm run
python3 eng/analyze-6q.py --card data/<unit>-6q/<unit>.haiku.json
cp data/<unit>-6q/<unit>.haiku.json data/<unit>-6q/<unit>.n3.haiku.json   # commit the matched run
```

---

## 6. Reviewer checklist

- [ ] `AGENTS.md` within the line limit; `eng/sync-skill.sh --check` passes.
- [ ] Datasets committed under `data/<unit>-6q/`; both `--card` dumps in the PR match them.
- [ ] n ≥ 3; model and judge named, for **both** tiers.
- [ ] Mini tier graded **BETTER** (a success gain, eliminated resourcefulness, or ≥25% cost/IET cut; no success/func/web regression).
- [ ] Frontier tier graded **not WORSE** (BETTER or NEUTRAL — IET/cost/output under the cap; no success/func regression).
- [ ] Claims cite normative metrics; signals only explain.
- [ ] Grounding text is package-specific, justified by the package's actual trap.
- [ ] Required caveats present; cache reads attributed per arm (not grepped).
- [ ] `docs/reports/<unit>.md` updated.
---

## 7. Why quality is a floor, not a score (the judge-subjectivity finding)

Earlier versions of these cards graded grounding on a **quality diff**
(`overallScore_grounded − overallScore_baseline`). We retired that. A correct solution
(build + run + assertions pass) lands at **4–5 by construction**, so the judge's 1–5 score has
only **~1 point of usable range** for correct work — and that top band is **subjective and
instruction-sensitive**, not a stable basis for a harm verdict.

**Evidence.** We re-judged the *identical* set of opus-4.8 nugetfetch sessions (not one agent
token changed) under four judge framings. The mean quality Δ (grounded − baseline) swung across a
~0.45 range on judge wording alone:

| Judge framing | mean quality Δ |
| --- | ---: |
| original (inline) | −0.15 → "WORSE" |
| re-judge, same prompt | −0.017 (tie) |
| + efficiency clause | +0.28 |
| + path-neutrality clause | +0.10 |

A metric that swings from "WORSE" to a clear win on wording alone cannot gate shipping.

**The bias.** Reading the per-criterion reasoning, the judge **rewarded visible effort**: an
ungrounded agent that reverse-engineers the API via reflection reads as "rigorous," while an agent
that trusts the package's own shipped `AGENTS.md` was docked for "relying on an unverified external
skill" — even on outcome criteria both arms satisfied. That is exactly backwards for grounding,
whose entire value is to make that effort unnecessary.

**The fix — two parts:**

1. **Decompose quality into `success` + `resourcefulness`.** `success` uses the judge's score
   *only* as a coarse ≥4 pass/fail floor (a robust judgment). `resourcefulness` — the "how hard did
   it have to work" signal the top band was groping at — is measured **objectively** from the
   timeline (archaeology), never from the judge. Grounding's job is to make resourcefulness
   *unnecessary*, so lower-for-grounded is the win, never a quality penalty. Harm is then keyed on
   objective axes only (success, web archaeology, cost/IET).

2. **De-bias the judge's floor.** Even the ≥4 floor can be nudged by the effort bias (a grounded
   scenario sat at 3.7 under the original judge, 4.3 under a de-biased one). Two judge-prompt clauses
   correct it (proposed upstream to the skill-validator):
   - **Source provenance:** package-shipped grounding (`AGENTS.md`/`SKILL.md` surfaced via a
     tool/skill call) is a *trusted, first-class* source — equal to reflection or reading source. Do
     not credit independent rediscovery, nor penalize relying on the package's own grounding.
   - **Path neutrality:** judge the *result* given the constraints, not the difficulty of the path.
     Equal correct-and-complete results are equal quality; effort is not a bonus and an easy path is
     not a deficit.

See [`authoring-principles.md`](./authoring-principles.md) for how this connects to *generating*
grounding (eval-driven from a zero-grounding baseline, never a model-written draft).
