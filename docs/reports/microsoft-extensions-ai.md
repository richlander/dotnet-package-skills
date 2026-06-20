# Does Microsoft.Extensions.AI need agent grounding? A measured answer

**Package:** `Microsoft.Extensions.AI`
**Date:** 2026-06-19
**Status:** Findings complete. Recorded as a third cross-package validation of the methodology
developed for the System.CommandLine and System.Text.Json units.

## TL;DR

Microsoft.Extensions.AI (M.E.AI) **does not need general agent grounding _for a frontier-class
agent_** — but it emphatically **does for a weaker, more commonly deployed agent.** We probed
the package's single most prominent silent footgun — tools registered in `ChatOptions.Tools`
never run unless the client pipeline includes `.UseFunctionInvocation()` — and the verdict
**depends entirely on which agent model runs the task**:

- **Agent = Opus 4.6** (judge = Opus 4.6): **−1.0%** (runs=5, CI [−1.6%, −1.0%], significant).
  The strong agent diagnoses the missing `UseFunctionInvocation` and fixes it in one edit every
  run. *Silent but resident → no signal.*
- **Agent = Haiku 4.5** (judge = Opus 4.6): **+63.3%** (runs=5, CI [+39.7%, +74.0%],
  significant, g=+100%). The weaker agent's baseline picks the wrong fix (a hand-written tool
  loop), fails to compile, and never produces a working app (quality 1.6/5); with grounding it
  adds `.UseFunctionInvocation()`, builds, runs, and finishes correctly — **and ~3× cheaper**
  (281k→87k tokens, 20→7 tool calls). *Silent and—for this model—obscure → large signal.*

**The gotcha did not change; the agent did.** "Model-resident" is a property of the
*model*, not the *package*. A cheap closed-book residency pre-probe predicted the split: asked
the gotcha cold, Haiku 4.5 said *"I don't know,"* while Opus knows it cold.

**Recommendation:** ship M.E.AI grounding for the function-calling footgun **if the target is a
mid/low-tier agent** (the common production case) — it converts task failure into success and
cuts cost ~3×. For a frontier agent it is redundant. Always state the target agent model.

## Why measure instead of assert

M.E.AI looked like a *promising* grounding target going in: it is a **new** package (preview
through 2024–2025, GA `9.5.0` ~May 2025), it churned its core API hard during preview with
**no `[Obsolete]` shims**, and its builder/middleware pipeline is unusual. The hypothesis was
that much of it would be post-training or under-represented and therefore non-resident. The
discipline of the methodology is to *test* that hypothesis rather than assume it: ship grounding
only for content that **measurably changes agent behavior** on a representative task by a
meaningful margin. M.E.AI is a useful counter-case because the intuition ("new package → needs
grounding") turned out to be wrong for its most prominent gotcha.

## Methodology

### Harness

We used the [`dotnet/skills`](https://github.com/dotnet/skills) **skill-validator** harness
(pinned at commit `5d717dbdd1998cdf88e7542eef52c5517cbefdb9`). For each scenario it runs an
agent on the same task three ways and has an LLM judge score the results:

- **baseline** — no grounding.
- **skilled (isolated)** — the grounding content injected directly into context.
- **skilled (plugin)** — the grounding offered as a discoverable skill the agent must
  *choose to load and read* (the realistic delivery path).

A scenario "passes" when the **effective score = min(isolated, plugin)** ≥ the threshold
(default **10%**). The `improvementScore` is a weighted blend dominated by quality:
Quality 0.40 + OverallJudgment 0.30 = **70%**; all efficiency dimensions (tokens, tool-calls,
time) total only **10%**; task completion is **0.15**. Clearing the bar requires moving
**correctness**, not saving tokens.

### Model and target

- Agent and judge model: **Claude Opus 4.6** (single model; see [Threats to validity](#threats-to-validity)).
- Target: **Microsoft.Extensions.AI 9.7.0** on `net10.0`.
- **Fully offline and deterministic.** The fixture supplies a stub `IChatClient`
  (`StubChatClient`) that simulates a model's tool-calling protocol with no network: on the
  first call it returns a `FunctionCallContent` asking for `GetWeather`; once it sees a
  `FunctionResultContent` in the history it returns the final answer echoing that result. This
  lets the behavior gate (`Sunny, 72F` must appear in stdout) give the judge objective ground
  truth: a correctly wired pipeline invokes the tool and prints the weather; the unwired
  version prints an empty `Assistant:` line and fails the gate.

The prompt includes the **"build and run to confirm"** instruction so both arms verify their
own work, leveling the verification-visibility confound observed in the System.Text.Json unit.

## Scenario and result

| # | Scenario | Agent model | Improvement | Interpretation |
| --- | --- | --- | --- | --- |
| A1 | Fix a weather assistant that prints an empty answer because its `IChatClient` is used directly, without `.UseFunctionInvocation()` in the pipeline (silent break = tools in `ChatOptions.Tools` are never invoked; `response.Text` is empty, no exception) | **Opus 4.6** | **−1.0%** (runs=5; iso −0.6%, plugin −1.0%; CI [−1.6%, −1.0%], significant) | Baseline (quality 5.0/5) identifies the missing `UseFunctionInvocation` and adds it in one edit. Judge winner: **tie**. Gotcha is **resident** for this model. No signal. |
| A1′ | *Same scenario, same grounding, same Opus judge — only the agent model changed* | **Haiku 4.5** | **+63.3%** (runs=5; iso +63.3%, plugin +65.1%; CI [+39.7%, +74.0%], significant, g=+100%) | Baseline (quality **1.6/5**) chooses the wrong fix (manual tool loop), hits 4 compile errors, never builds a working app. Grounded → adds `.UseFunctionInvocation()`, builds, runs, **5.0/5**, in 7 tool calls. Judge: **MuchBetter**. Gotcha is **non-resident** for this model → large signal. |

(A1 effective score `min(iso=−0.6%, plugin=−1.0%) = −1.0%`; A1′ `min(iso=+63.3%, plugin=+65.1%)
= +63.3%`. Under Opus the grounded arms spent **more** tokens (+29%/+35%) restating known
content; under Haiku the grounded arms spent **far fewer** (−69%/−64% tokens, −65%/−60% tool
calls, −61%/−62% time) by going straight to the fix instead of thrashing. Same content, opposite
economics — because residency differs. Full artifacts in the appendix.)

## Analysis

### What the model already knows (skip it)

Automatic tool invocation requiring `UseFunctionInvocation()` *feels* like a trap — it is a
genuinely **silent** break (compiles, runs, returns success, but the tool never fires and the
text is empty), and Microsoft's docs do not flag it as a "pitfall." Yet it produced **negative**
signal. The reason is the same one that sank System.Text.Json's case-insensitivity scenario:
the pattern is **everywhere**. Every M.E.AI function-calling sample, blog post, and quickstart
builds the client with `.UseFunctionInvocation()`. The model has seen the correct shape
thousands of times, so when it reads code that puts tools in `ChatOptions.Tools` and calls a
raw client, it immediately recognizes the omission and fixes it — every run, with low variance.

> *Silent is necessary but not sufficient; the gap must also be **obscure** (rarely
> demonstrated). A heavily-exampled pattern is model-resident even when it is undocumented as
> a pitfall.*

### Low variance makes the resident verdict robust

Unlike most scenarios in this repo (where run-to-run variance is endemic and large), A1 was
**tight**: CI [−1.6%, −1.0%], reported "significant," quality tied at 5.0/5 on both rubric and
overall axes, judge winner "tie" on the narrative. There is little ambiguity here — this is a
clean "the model already knows this" result, not a noisy null.

### Where signal might still hide (not yet tested)

A1 is the *canonical* M.E.AI footgun, and it is resident. That does not rule out grounding value
in M.E.AI's genuinely **obscure** corners — behaviors that are both silent and rarely
demonstrated, and therefore plausibly non-resident:

- **`ChatOptions.Tools` is `[JsonIgnore]`.** When a `DistributedCachingChatClient` serializes a
  request for its cache key, tools are dropped — two requests that differ only in their tools
  can collide on one cache entry, silently returning the wrong cached response.
- **`response.Text` concatenates *all* assistant messages.** After multi-turn tool calls
  `response.Messages` holds several entries, so `Text` may include intermediate content — but
  this surfaces as *visibly* wrong output, so an agent that runs the app would likely
  self-correct (failing the "silent" test, like the STJ × AOT case).
- **Middleware ordering / shared `ChatOptions` mutation** across concurrent calls.

These are left as future probes. Based on the methodology's track record, only the *first*
(silent **and** obscure **and** not self-correcting) is a strong candidate to clear the bar; we
did not invest in it because the package's headline behavior is already shown to be resident and
the marginal value of one more probe is low.

## Conclusion

**Verdict: whether Microsoft.Extensions.AI needs grounding for its headline gotcha depends on
the agent model — and that dependence is the finding.** With a frontier agent (Opus 4.6) the
function-invocation footgun is resident (**−1.0%**); with a weaker, more commonly deployed agent
(Haiku 4.5) the *identical* grounding clears the bar by a wide margin (**+63.3%**), turning task
failure into success and cutting cost ~3×.

This reframes the earlier "third resident gotcha" observation. The *obscurity* axis of the
authoring rule is **model-relative**: a gotcha that is famous to Opus is genuinely obscure to
Haiku. So the rule still holds — ground a gotcha only if it is **silent**, **obscure (for the
target model)**, and **not self-correcting** — but "obscure" must be evaluated against the agent
you are actually shipping to, not the strongest model available:

> Ground a gotcha if it is **silent** (compiles *and* runs without error but behaves wrong),
> **obscure for the target agent model** (that model can't recall the fix cold — check with a
> closed-book residency pre-probe), **and** not reliably self-correcting at run time. M.E.AI's
> function-invocation footgun fails "obscure" for Opus 4.6 but passes it decisively for Haiku
> 4.5 — hence the −1.0% vs +63.3% split on identical content.

The "new package" intuition is not a reliable proxy for "needs grounding"; neither is a single
frontier-model null result a reliable proxy for "no package needs grounding." What matters is
whether a *specific behavior* is silent and unrecoverable **and non-resident for the agent that
will consume the grounding** — which for most production agents is not the frontier model.

## Threats to validity

- **Single scenario.** We tested one (canonical) behavior. The verdict is about that gotcha's
  residency per agent model, not an exhaustive package audit; the obscure candidates above are
  untested.
- **Two agent models, one judge.** We ran the agent as both Opus 4.6 and Haiku 4.5, with the
  judge fixed at Opus 4.6 (so the +63.3% is not a judge-leniency artifact — the same strict
  judge scored the Opus agent −1.0%). Other agent models will land between these poles; the
  *direction* (weaker agent → more grounding value) is the robust result.
- **Judge is still a single model.** Using Opus 4.6 as the judge for a Haiku agent could carry
  some same-family bias; a third-party judge would harden the magnitude.
- **Threshold is a convention.** The 10% bar is the harness default. The qualitative ranking —
  resident vs. loud vs. silent-and-obscure — is the durable result.
- **Offline stub, not a live model.** The fixture uses a deterministic `StubChatClient` to
  simulate the tool-call protocol. This isolates the *wiring* behavior under test (whether the
  pipeline invokes tools) from model nondeterminism; it does not exercise a real provider, which
  is intentional — the gotcha is in client composition, not the model.

## Appendix: reproduction

- Harness: `dotnet/skills` skill-validator @ `5d717dbdd1998cdf88e7542eef52c5517cbefdb9`,
  built from source by `eng/run-evals.sh`.
- Run: `eng/run-evals.sh Microsoft.Extensions.AI` (or
  `skill-validator evaluate --tests-dir ./tests --runs 5 grounding/microsoft-extensions-ai`).
- Eval spec + fixtures: `tests/microsoft-extensions-ai/`.
- Versions: Microsoft.Extensions.AI `9.7.0` on `net10.0`.
- Result artifacts (`.skill-validator-results/`):
  - `20260619-185226` — A1 function invocation, agent=Opus 4.6, `--runs 5` (−1.0%, CI [−1.6%, −1.0%]).
  - `20260619-193348` — A1′ function invocation, agent=Haiku 4.5, judge=Opus 4.6, `--runs 5` (+63.3%, CI [+39.7%, +74.0%]).
- Asymmetric run command:
  `skill-validator evaluate --tests-dir ./tests --model claude-haiku-4.5 --judge-model claude-opus-4.6 --runs 5 grounding/microsoft-extensions-ai`
- Authoring principles distilled from this work:
  [`docs/authoring-principles.md`](../authoring-principles.md).
