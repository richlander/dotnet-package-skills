# Scoring, grading, and the grounding PR

How an eval run becomes a **ship / no-ship decision** and a reviewable PR: the grade model
(**BETTER / NEUTRAL / WORSE**), the tier-aware Pareto gate, the copy-paste cards, the PR contents and
checklist, and why quality is a floor not a score. The *approach* these grade — the arms, the two
regimes, and the three-rung ladder — is in
[`grounding-eval-methodology.md`](./grounding-eval-methodology.md).

> **Arm naming.** This doc and the `grounding analyze` cards still speak of a single grounded
> "**AGENTS.md** arm" vs **baseline**, and a "**source-diff** (AGENTS.md vs README.md)" comparison.
> Under the current content-arm naming (methodology §1), read those as the **Missing Manual** arm and
> the **Front Door** comparison. The `analyze`/card tooling relabel is a tracked follow-up; the grade
> logic is unchanged.

---

## The ship decision — require BETTER on the tier that needs it, not WORSE on the tier that doesn't

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
> (the cards below) — the *same* rubric for haiku and opus. It deliberately does **not** encode shipping. The
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
> — `GATE` in `grounding analyze` — and the analyzer applies them automatically per `--card`.

---

## The eval dump (copy-paste into the PR)

`grounding analyze` emits **three single-variable cards**, each isolating exactly one comparison so the
data is trivial to read. Every card shows the same metric rows — `success (scenarios)`, `func passed`,
`resourcefulness (archaeology)`, `IET`, `output tok`, `cost` — and a **Conclusion**: a single **uniform,
model-independent grade** of grounding's effect vs baseline, **BETTER / NEUTRAL / WORSE** (the same rubric
for every model — the card grades, it does not decide shipping; that is the ship decision above). Grading keys off **objective
axes only** (success, web archaeology, cost/IET); there is **no judge-quality diff** (the judge-floor section below). A dataset whose
filename contains `readme` is read as the **README arm**.

- **BETTER** — success held and a real win: solves more scenarios, resourcefulness eliminated, or IET/cost down ≥ 25%; no regression.
- **NEUTRAL** — success held, no material efficiency win.
- **WORSE** — success dropped, grounded web archaeology, or cost/IET/output inflated past the cap.

| Card | Flag | Holds fixed | Varies | Answers |
| --- | --- | --- | --- | --- |
| **Primary** | `--card` | one model | baseline → AGENTS.md | Does grounding help *this* model? (one card per model, graded BETTER/NEUTRAL/WORSE) |
| **Model-diff** | `--model-diff` | AGENTS.md vs baseline | the model | Does the grade hold across tiers — side by side. |
| **Source-diff** | `--source-diff` | one model, grounding-tool delivery | AGENTS.md vs README.md | A **usability test of the README** (not a floor to beat): does the README also answer every question with 0 archaeology? README failures are bugs to **fix in the same PR**. Once the README is complete, AGENTS's edge narrows to efficiency/retrieval. |

```bash
# primary, one card per model
grounding analyze --card data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>.n3.opus.json
# model-diff (AGENTS lift, models side by side)
grounding analyze --model-diff data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>.n3.opus.json
# source-diff (AGENTS − README, one model — usually the mini tier)
grounding analyze --source-diff data/<unit>-6q/<unit>.n3.haiku.json data/<unit>-6q/<unit>-readme.n3.haiku.json
```

**Paste the cards verbatim** into the PR's *Metrics* section. The PR carries four: primary (mini), primary
(frontier), model-diff, source-diff (mini). Example (NuGetFetch, mini primary):

```text
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

## What a grounding PR contains

| Artifact | Path |
| --- | --- |
| The grounding edit (body ≤ `eng/agents-line-limit.txt`, currently 60) | `grounding/<unit>/AGENTS.md` |
| Regenerated wrapper, in sync (`grounding sync-skill --check`) | `grounding/<unit>/SKILL.md` |
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
grounding sync-skill --check
RUNS=3 eng/run-<unit>-6q.sh                                    # -> data/<unit>-6q/<unit>.haiku.json
RUNS=3 MODELS=claude-opus-4.8 eng/run-<unit>-6q.sh            # frontier no-harm run
grounding analyze --card data/<unit>-6q/<unit>.haiku.json
cp data/<unit>-6q/<unit>.haiku.json data/<unit>-6q/<unit>.n3.haiku.json   # commit the matched run
```

---

## Reviewer checklist

- [ ] `AGENTS.md` within the line limit; `grounding sync-skill --check` passes.
- [ ] Datasets committed under `data/<unit>-6q/`; both `--card` dumps in the PR match them.
- [ ] n ≥ 3; model and judge named, for **both** tiers.
- [ ] Mini tier graded **BETTER** (a success gain, eliminated resourcefulness, or ≥25% cost/IET cut; no success/func/web regression).
- [ ] Frontier tier graded **not WORSE** (BETTER or NEUTRAL — IET/cost/output under the cap; no success/func regression).
- [ ] Claims cite normative metrics; signals only explain.
- [ ] Grounding text is package-specific, justified by the package's actual trap.
- [ ] Required caveats present; cache reads attributed per arm (not grepped).
- [ ] `docs/reports/<unit>.md` updated.

---

## Why quality is a floor, not a score (the judge-subjectivity finding)

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
