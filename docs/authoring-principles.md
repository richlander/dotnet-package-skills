# Authoring principles for grounding docs

Grounding docs (`AGENTS.md`) are **not** READMEs. A README explains a package to a
human from scratch; a grounding doc supplies *only* what an AI agent provably lacks.
These principles keep the content tight, measurable, and worth its place in the
`AGENTS.md` line budget.

## 1. Record only what the model is proven to need (skip model-resident knowledge)

Only include information that an agent has been **demonstrated** (by eval signal) to
need and lack. If a web-blocked baseline already produces the correct answer, that
material is *model-resident* and must **not** go in the grounding doc — it adds length,
dilutes RAG matches, and buys no metric movement.

Concretely: write the content, then run the eval. If the grounded arm doesn't beat the
baseline on a scenario, the model already knew it — cut it.

### Evidence (System.CommandLine unit)

| Scenario | Baseline → Grounded | Improvement | Lesson |
| --- | --- | --- | --- |
| S1: `AcceptOnlyFromAmong` comparer overload | 5.0 → 5.0 | **−2.2%** | A "new in 3.x" member with a guessable name is model-resident. No signal. |
| G1: greenfield CLI on 3.x | 5.0 → 5.0 | **+1.1%** | The model writes correct current-API CLIs from scratch (2.0 GA API == 3.x). Greenfield authoring is model-resident. No signal. |
| M1: migrate a real 2.0.0-beta4 CLI to 3.x (compile-error gates) | 5.0 → 5.0 | **+6.4%** | After behavior gates gave the judge ground truth, the baseline migrates **correctly** (compile errors + reflection recover the member mapping). Residual signal is efficiency-only — below the 10% bar. |
| M1 + silent-break trap (`new Option<T>("--n","desc")`: 2nd arg = alias in 3.x) | 5.0 → 5.0 | **+9.6%** (runs=5; isolated arm **+20.6%**) | A migration that *compiles but behaves wrong* defeats the compile-error safety net, and the **isolated** arm shows the grounding is genuinely valuable (+20.6%). But over 5 runs the strong baseline self-recovers the single trap (quality ties 5.0), and the gating **plugin** (discover-and-read) arm sits at +9.6% — just under the bar. Two levers remain: widen the gap (multiple traps) and close the isolated-vs-plugin delivery gap. |

**Takeaway:** signal comes from *transforming code written against an API the model can
no longer rely on* (migration) — and specifically from the parts a model **cannot recover
locally**. A version-bump or removed-API migration is largely recoverable via compile
errors + reflection (model-resident, efficiency-only signal). The durable signal is a
**silent behavioral break**: code that compiles but is wrong, where only the grounding's
gotcha prevents the defect. The improvement metric is quality-dominated (Quality 0.40 +
OverallJudgment 0.30 = 70%; all efficiency dimensions only 10%), so clearing the bar
requires moving *correctness*, not tokens.

## 2. Optimize for RAG retrieval, not prose coherence

The alternative to a single complete grounding doc is **section-based RAG with similarity
matching**: an agent retrieves the *section* whose text is most similar to its task, not
the whole document. So:

- Spend **minimum** effort on narrative flow, transitions, and "completeness." A grounding
  doc may read as a disjoint set of targeted sections — that is fine, even preferred.
- Make each section **self-describing and keyword-dense** for the task it serves (e.g. a
  migration section should name the old and new member identifiers verbatim, because those
  are what an agent's query will match on).
- Focus every section on *describing the missing model knowledge* needed to move the metric
  to (at least) the success threshold — nothing more.

This is the opposite of how we write `README.md` files, which are authored to be read
top-to-bottom by a human.

## Practical consequences

- **Measure before you keep.** New grounding content is a hypothesis; the eval is the test.
  Keep only sections that move a scenario above the improvement threshold.
- **Prefer migration/transformation scenarios** when probing whether a knowledge gap is
  real; greenfield tasks tend to be model-resident for strong models.
- **Stay within the line budget** (`eng/agents-line-limit.txt`). Cutting model-resident
  content is the first lever when you're over budget.

## Conclusion: does System.CommandLine 3.x need grounding?

**Verdict: it needs grounding for a few specific, medium-to-high-value topics — not as
a general rule.**

Across every scenario we measured against a strong frontier model (Opus 4.6):

- **General API usage is model-resident.** Greenfield CLI authoring (G1, +1.1%) and
  "new in 3.x" member discovery (S1, −2.2%) showed no signal — the model already writes
  correct current-API code and guesses well-named members.
- **Even removed-API migration is largely model-resident.** Migrating a beta4 CLI whose
  API was deleted before GA (M1, +6.4% with behavior gates) is recoverable through the
  normal dev loop: compile errors point at the removed members and reflection reveals the
  replacements. Grounding bought efficiency, not correctness.
- **The one durable gap is silent breaking changes** — code that *compiles but behaves
  wrong*, where neither the compiler nor reflection can catch the defect. The
  alias-vs-description gotcha is the proof: with the grounding guaranteed in context the
  isolated arm reached **+20.6%**. (It only landed at +9.6% effective because a single
  trap is something this model often self-recovers, and the discover-and-read delivery
  path lags the in-context arm — both addressable.)

So the grounding doc's value is **not** "how to use System.CommandLine." It is the short
list of **non-discoverable hazards**: silent behavioral breaks and gotchas that compile
fine and look correct but aren't. Author those; let the model handle the rest.
