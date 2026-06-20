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
| M1 + silent-break, **doc refocused** to dense RAG-style migration/gotchas + sharper discovery description | 4.6 → 5.0 | **+15.1%** (runs=5; isolated **+22.6%**, plugin **+15.1%**) | Scoping the doc to migration/gotchas and tightening the discovery description **lifted the gating plugin arm from +9.6% → +15.1%** — delivery, not content, was the blocker. Now clears the bar, but variance is very high (CV=91%, one +21.8% run carries the mean): the signal is real and above threshold, magnitude is soft. |

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

### Cross-package data point (System.Text.Json unit)

| Scenario | Baseline → Grounded | Improvement | Lesson |
| --- | --- | --- | --- |
| N1: migrate a Newtonsoft.Json CLI to System.Text.Json; silent break = STJ is case-sensitive by default (Newtonsoft is case-insensitive) | 5.0 → 4.8 (isolated) / 4.0 (plugin) | **−12.5%** (runs=5; isolated −6.9%) | The baseline reliably adds `PropertyNameCaseInsensitive = true` on its own — STJ case-insensitivity is **the most-cited STJ gotcha, so it is model-resident**. Grounding adds no value (slightly negative). A *silent* break is necessary but **not sufficient**: it must also be *obscure* (under-documented) to be non-resident. |
| N2: make a reflection-based STJ tool Native AOT compatible (`PublishAot=true` → reflection serialization disabled → throws at run time; fix = `JsonSerializerContext` source-gen) | 5.0 → 5.0 (quality tied) | **+7.9%** (runs=5; isolated +9.4%, plugin +7.9%) — under the bar | **Baseline objectively *fails* the task** (task-completion ✗ → ✓ with grounding): this content has real value. But the break is **LOUD** — it throws `InvalidOperationException` whose message literally says *"use the source generator APIs"* — so the model recovers to **equal final quality** (judge tie). The only durable, bar-relevant win is the 0.15-weighted task-completion delta, which nets +7.9% after discovery overhead. **A loud, self-describing break caps below the bar even when it genuinely improves task completion.** |

The contrast with the SCL silent-break win is the key lesson: SCL's alias-vs-description
shift earned signal because it is **both silent and obscure** (a rarely-discussed beta-era
constructor change). STJ case-insensitivity is silent but **famous**, so the model already
guards against it. And STJ × Native AOT is **non-resident but loud**: it announces its own
fix at run time, so even though the baseline fails the task, it recovers to equal quality and
the measured gain stays under threshold. When probing a package, target gotchas that are
**silent (compile- and run-clean but wrong), obscure (rarely written about), AND not
self-correcting at run time.** Drop any one and the signal collapses:

| Property | SCL alias-vs-description | STJ case-insensitivity | STJ × Native AOT | M.E.AI function-invocation |
| --- | --- | --- | --- | --- |
| Silent (no error) | ✅ | ✅ | ❌ throws | ✅ empty output |
| Obscure (non-resident) | ✅ | ❌ famous | ✅ post-training | ❌ famous |
| Result | **+15.1% (clears bar)** | −12.5% | +7.9% (real value, under bar) | −1.0% |

### Cross-package data point (Microsoft.Extensions.AI unit)

| Scenario | Baseline → Grounded | Improvement | Lesson |
| --- | --- | --- | --- |
| A1: wire up tool calling in an `IChatClient` assistant; silent break = tools in `ChatOptions.Tools` are never invoked unless the client is built with `.UseFunctionInvocation()` (`FunctionInvokingChatClient`). Without it the call succeeds, `response.Text` is empty, the tool never runs, no exception. | 5.0 → 5.0 (quality tied) | **−1.0%** (runs=5; CI [−1.6%, −1.0%], low variance) | The baseline agent **immediately** diagnoses the missing `UseFunctionInvocation` and fixes it in one edit, every run. Despite *not* being labeled a "pitfall" in Microsoft docs, the pattern is so heavily represented in examples that it is **model-resident**. Same profile as STJ case-insensitivity: **silent but famous → no signal.** A reproducible, low-variance −1.0% is a clean "the model already knows this" result. |

This is the **third** package where the most prominent silent gotcha turned out to be
model-resident (STJ case-insensitivity, M.E.AI function invocation). The pattern is now
robust: a gotcha being *silent* and even *undocumented-as-a-pitfall* is not enough — if it is
**demonstrated frequently** (every tutorial wires `UseFunctionInvocation`; every STJ migration
guide mentions case-insensitivity), strong models absorb it. The winning SCL case was obscure
precisely because it is a **transient beta-era detail** that mostly washed out of the corpus.

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
  isolated arm reached **+20.6%**. (It only landed at +9.6% effective at first because a
  single trap is something this model often self-recovers, and the discover-and-read
  delivery path lagged the in-context arm. Refocusing the doc to dense, RAG-style
  migration/gotcha content with a sharper discovery description lifted the gating plugin
  arm to **+15.1%**, clearing the bar — though run-to-run variance stays high.)

So the grounding doc's value is **not** "how to use System.CommandLine." It is the short
list of **non-discoverable hazards**: silent behavioral breaks and gotchas that compile
fine and look correct but aren't. Author those; let the model handle the rest.

## Methodological limitations (and how to harden the method)

The three "model-resident" verdicts above are real but come with two structural caveats
that anyone reading these numbers should weigh.

### 1. The discovery–measurement circularity

Our pipeline has two phases: **discovery** (enumerate candidate footguns) and
**measurement** (baseline-vs-grounded eval, same model family). When candidates are sourced
from the model's own memory (introspection plus a research agent), discovery can only surface
gaps the model can already recall — and is blind to exactly the gaps that would move the
metric. The measurement then "confirms" residency for content we obtained *from* the model.
This biases the method toward null results: **it is good at falsifying grounding need, weak at
discovering genuine gaps.**

The System.CommandLine win is the proof the loop is escapable: the alias-vs-description gotcha
was not recalled from memory, it was a **transient beta-era detail that had washed out of the
training corpus**. The fixes all amount to *decoupling discovery from model memory*:

- **Source candidates externally**, not introspectively: post-cutoff release notes, GitHub
  issues labeled "surprising/gotcha," high-upvote Stack Overflow questions, the package's own
  edge-case tests. Anything dated after the model's training cutoff is non-resident by
  construction (this is why a zero-day upgrade is the strongest scenario class).
- **Run a cheap residency pre-probe** before spending a 5-run eval: ask the bare model "what
  are the gotchas of X?" If it names the candidate, it is resident — skip it. This turns
  implicit circularity into an explicit, fast gate.
- **Test an agent weaker than the judge.** `--model` (agent) and `--judge-model` are
  independent. Running Opus-as-both is the *hardest* residency bar and the *least*
  representative of the weaker agents most teams actually deploy. "Does package X need
  grounding?" is underspecified without "...for which agent model?" Grounding value is
  **model-relative**; our null results are strictly "the strongest model doesn't need this."

### 2. The scoring weights are tuned for skills, not grounding

The harness improvement score (documented in `eng/skill-validator/src/README.md` and
`src/docs/InvestigatingResults.md`; weights in `DefaultWeights`, `Models.cs`) is:
Quality 0.40 + OverallJudgment 0.30 + TaskCompletion 0.15 + (Token 0.05 + Error 0.05 +
ToolCall 0.025 + Time 0.025). Quality dominates by design — *"a skill that improves output
quality will pass even if it uses more tokens"* — which is correct for a **skill** validator
(loading a skill always costs tokens; don't punish that).

But grounding's value is frequently **efficiency**: not letting the agent flail through many
tool calls to rediscover a hazard. That value lands entirely in tokens + tool-calls + time =
**0.10 combined, clamped** — so the quality-tuned yardstick structurally hides the win
grounding is meant to deliver. A grounding-specific weight profile that elevates the
efficiency bucket may surface signal the default masks (weights are not flag-overridable
today, so this means a local re-score).

### 3. "Tokens" is one number but should be a cost

`tokenEstimate = inputTokens + outputTokens`, summed 1:1 (`MetricsCollector.cs`), clamped so
≥2× usage maps to only −0.05 final. This is too coarse: output tokens cost ~4–5× input on
every frontier pricing sheet, and reasoning/thinking tokens (billed as output, currently
folded invisibly into `outputTokens`) are the most expensive and most variable component. A
doubling of *output* tokens should be a material signal; today it is nearly free in the score.
The per-arm input/output counts are already collected separately — only the score collapses
them. The clean fix is a single **cost-weighted scalar** (`input·pᵢ + output·pₒ + reasoning·pᵣ`)
scored on its reduction, rather than a 1:1 token sum or two parallel token metrics.
