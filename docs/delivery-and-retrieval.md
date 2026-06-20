# How agents retrieve grounding: delivery, gates, and the retrieval pre-screen

**Date:** 2026-06-20
**Status:** Findings complete. Companion to [`authoring-principles.md`](./authoring-principles.md)
(which covers *what* to write); this doc covers *how the content reaches the agent* and
*whether it costs anything when it shouldn't*.

## TL;DR

A grounding `AGENTS.md` is only valuable if the agent reads it **when it helps** and skips it
**when it doesn't**. We built a controlled MCP server that serves our grounding through a
single tool and varied only the tool's *retrieval gate*, then measured the agent's **call
behavior** across packages and model tiers. Findings:

- **The model self-gates on its own uncertainty, and the gate is correct at both ends.** On a
  task the model knows cold it declines the content (and declining is the cheaper, correct
  call); on a task it doesn't, it retrieves the content and is rewarded — up to flipping a
  failing run into a passing one.
- **The gate *wording* barely matters.** A permissive "call whenever the user asks about a
  package" gate and a strict "call only when unsure / the version may post-date your training"
  gate produced **the same retrieval decisions** at both extremes. Model confidence dominates
  the description.
- **Progressive disclosure (a cheap summary tool + a full-body tool) is the right shape.** It
  mirrors how skills work (always-available frontmatter, body on demand), gives the agent a
  cheap way to *confirm relevance before paying for the body*, and yields an observable,
  graded retrieval signal.
- **"Call behavior" is a free pre-screen.** Whether (and which) tool the agent calls predicts
  where grounding pays — without running an expensive graded evaluation.

**Design implication for the NuGet MCP:** expose grounding as **two tools** — a cheap
`summarize_package_context` (returns the `AGENTS.md` YAML frontmatter) and a
`get_package_context` (returns the body) — and let the model self-select. Don't try to
engineer the perfect WHEN-TO-CALL string; expose a cheap summary and the model does the
gating for you. Because the model only pays for the body when it pays off, auto-delivered
grounding is **Pareto-safe**.

## Why measure delivery, not just content

[`authoring-principles.md`](./authoring-principles.md) establishes that grounding should
contain *only what the model provably lacks*. But even perfect content has a delivery cost:
if every agent pulls a package's full `AGENTS.md` on every task, confident models pay a
"frontier tax" (extra input/thinking tokens) for content they didn't need. The question this
doc answers is whether the *retrieval mechanism* can make grounding **cost-free when it's
useless and available when it's needed** — i.e. behave like retrieval-augmented generation
(RAG) rather than an always-on prompt prefix.

## The three environments

We frame delivery as a ladder, each rung isolating one variable:

1. **Tight harness** — the [`dotnet/skills`](https://github.com/dotnet/skills) skill-validator
   *force-injects* the content as a skill. Measures the **content-value ceiling**; cannot
   model an agent *declining* to read.
2. **Raw NuGet MCP** (`NuGet.Mcp.Server`) — the real server. Its `get_package_context` prefers
   the installed package's `AGENTS.md`, returns the **full body verbatim**, all-or-nothing,
   behind a fixed **task-type** gate ("call when the user asks about a package").
3. **Our controlled MCP** (`grounding/_mcp/grounding_mcp.py`) — a faithful stand-in for #2 with
   one knob: the tool description (the *gate*) is the experimental variable, and we can switch
   to a two-tool **progressive** shape. This is the instrument used below.

## The instrument

`grounding/_mcp/grounding_mcp.py` is a dependency-free stdio MCP server. It serves a
package's grounding by resolving `packageName` to that unit's `AGENTS.md`. The retrieval
**gate** is selected by the `GROUNDING_GATE` environment variable:

| Gate | Tools | WHEN-TO-CALL |
| --- | --- | --- |
| `task_type` | 1 (`get_package_context`) | "call whenever the user asks about a package" — mimics today's NuGet MCP |
| `uncertainty_version` | 1 (`get_package_context`) | "call when not confident of the current API for the installed version, or the version may post-date your training" |
| `progressive` | 2 (`summarize_package_context` + `get_package_context`) | a cheap summary tool and a full-body tool; the agent decides after reading the summary |

The unit that attaches the server ships **no inline grounding** (an inert `SKILL.md`), so the
content arrives *only* if the agent chooses to call a tool. Every call is logged (tool name,
package, version) to `.tools/mcp-calls.log`, giving an independent **P(call)** / P(summary) /
P(full) signal alongside the harness's own tool events.

### Progressive disclosure = the skills mechanic

The `progressive` gate ports skills' progressive disclosure to MCP:

| Skill mechanic | Progressive MCP |
| --- | --- |
| `SKILL.md` frontmatter (name + description) — always loaded, free | `summarize_package_context` — returns the `AGENTS.md` YAML frontmatter |
| `SKILL.md` body — loaded only on activation | `get_package_context` — returns the body (frontmatter stripped) |

This is why `AGENTS.md` now carries **YAML frontmatter** (`name` + `description`): it is the
shipped artifact, and that frontmatter *is* the cheap summary channel. The decision moves out
of the tool description and into the agent's judgement after a cheap peek.

## Metric: "call behavior" as the pre-screen

The headline metric here is **not** tokens — it is **what the agent chose to call**:
`P(call)`, which tool (summary vs full), the order (`summarize → get_full` vs
`summarize → ∅`), and the per-arm distribution.

It is the right metric because it is **near-binary, intentional, and low-variance**: the
agent either retrieves or it doesn't. Input-Equivalent Tokens
(IET = `fresh_input + 0.1·cacheRead + 1.25·cacheWrite + 5·output`) is a downstream, noisy
proxy — on a small task, fixed overhead and cache warmup swamp the ~600-token cost of the
retrieval decision (we observed identical-config baselines swing 19k–34k IET). So we read
**call behavior + pass/fail** on small/resident tasks, and reserve **IET** for large tasks
where the body of the cost is real.

> **Call behavior is a free pre-screen.** Register the tool and watch: *declines* → resident,
> grounding is dead-weight here; *peeks but declines full* → bounded cost, no value; *pulls
> full* → the model judged itself uncertain — now it is worth running the expensive IET/quality
> eval. The same signal a production NuGet MCP could log to learn, per package and per tier,
> when its content actually earns its cost.

## Findings

All runs use the skill-validator harness (pinned commit
`5d717dbdd1998cdf88e7542eef52c5517cbefdb9`), agent = the tier under test, judge =
`claude-opus-4.6`. Each scenario runs three arms: **baseline** (no grounding, no MCP),
**skilledIsolated**, and **skilledPlugin** (both attach the MCP; isolated = push, plugin =
discover-and-read). Two scenarios anchor the work:

- **M1** — migrate a real `System.CommandLine` `2.0.0-beta4` CLI to `3.x` (a *version-delta*
  task: the beta API was removed before GA, so it post-dates the model's confident knowledge).
- **R1** — greenfield `System.Text.Json` serialize/round-trip with **default options** on a
  current, in-training-window version (a *resident* task: the migration/case-insensitivity
  grounding is irrelevant, so declining is correct).

### 1. The gate wording barely matters; the model self-gates

`task_type` vs `uncertainty_version` on **M1 / Opus 4.6 / runs=5**: both gates fired
**P(call) = 100%** (every skilled arm pulled the full body). The grounded plugin arm converged
to **~67.7k IET under both gates** (67.9k / 67.5k). M1 is an *unambiguous* version-delta, so
both wordings — permissive and strict — correctly fire. Conversely on **R1** (below) every
gate avoids the full body. The model's intrinsic confidence, not the description, drives the
decision.

### 2. Cross-tier matrix (progressive gate, runs=3)

**R1 — resident greenfield STJ:**

| Tier | Baseline | Retrieval |
| --- | --- | --- |
| Opus | pass | peek only (`summarize`×1, **full×0**) |
| Sonnet | pass | **none (0 calls)** |
| Haiku | pass | **none (0 calls)** |

Every tier solves it unaided; **no tier pulls the full body.** Grounding is correctly inert.
(This *corrects* an initial prediction that weaker tiers would retrieve *more* here — they
retrieved *less* than Opus, because basic STJ is resident for everyone. The tier effect lives
on hard tasks, not easy ones.)

**M1 — version-delta migration:**

| Tier | Baseline | Grounded (best arm) | Pulls full? |
| --- | --- | --- | --- |
| Opus | pass · ~78k IET · 10–13 tools | pass · 68k (**−13%**) | yes |
| Sonnet | pass · ~216k IET · 40 tools | pass · 149k (**−31%**) | yes |
| Haiku | **FAIL** · ~386k IET · **49 tools** | **PASS** (plugin) | yes |

Three things move together as capability falls: baseline cost **explodes** (78k → 216k →
386k IET), weaker tiers **flail** (Haiku: 49 tool calls and still fails), and weaker tiers
**retrieve the full body** (lower confidence trips the same self-gate that kept them quiet on
R1). Grounding's payoff **escalates down-tier**: efficiency for Opus, big efficiency for
Sonnet, a **quality rescue** for Haiku.

### 3. The two poles (progressive gate, runs=5)

The diagonal of the matrix gives the canonical extremes, firmed at runs=5:

**Pole A — guidance SKIPPED, justifiably (Opus / R1):**

| Arm | Pass | Full pulls | Δ completion | Δ quality | Δ tokens |
| --- | --- | --- | --- | --- | --- |
| baseline | ✅ | — | — | — | — |
| isolated | ✅ | **0** (peeked, declined) | 0 | 0 | **−14%** |
| plugin | ✅ | **0** | 0 | 0 | **−15%** |

All 5 runs pass; zero full-body pulls. The token delta is **negative** — taking the guidance
would have *added* cost for *zero* benefit. This is a **judged** skip (isolated read the cheap
summary, then declined), and the counterfactual proves the skip was right.

**Pole B — guidance TAKEN, and rewarded (Haiku / M1):**

| Arm | Pass | Full pulls | Δ completion | Δ quality | Δ tokens |
| --- | --- | --- | --- | --- | --- |
| baseline | ❌ (68 tools, 471k IET) | — | — | — | — |
| isolated | ✅ | 1 | **+1 (fail→pass)** | +0.17 | +7% |
| plugin | ✅ | 2 | **+1 (fail→pass)** | +0.07 | +14% |

The weak model pulled the full body and **flipped fail → pass in both delivery arms** (runs=3
showed only the plugin arm rescuing; runs=5 confirms the isolated miss was noise). Quality up,
tokens down, correctness fixed. The correctness flip is the headline; the token cut is a
bonus on an already-huge bill.

### The causal tie

Aggregate but clean — "taken" = full-calls > 0; "rewarded" = task-completion delta > 0:

- **Pole A:** full = 0, improvement = 0, tokens *worse* → skip justified.
- **Pole B:** full > 0, completion = **+1**, quality & tokens up → take rewarded.

The model self-gates on its own uncertainty, and the gate is correct at **both** ends.

## Implications for NuGet MCP design

1. **Ship grounding as two tools (progressive disclosure), not one all-or-nothing tool.** A
   cheap `summarize_package_context` (the `AGENTS.md` frontmatter) + a `get_package_context`
   (the body). The summary lets a model confirm relevance before paying for the body, and caps
   the cost of a wrong guess at the summary.
2. **Make `AGENTS.md` self-contained with YAML frontmatter** (`name` + `description`). It is
   the shipped artifact and the summary channel; it should not depend on harness-only metadata.
3. **Don't engineer the WHEN-TO-CALL string.** Across permissive and strict gates the
   retrieval decision was the same — the model gates on its own confidence. Keep the
   description honest and mechanical; invest in the *summary*, not the gate prose.
4. **Keep agent instructions clean.** No "use the NuGet MCP" prompt. Register the tools; the
   model self-selects from normal tool context.
5. **Auto-delivered grounding is Pareto-safe** *because* the model only pays for the body when
   it pays off: declines on resident tasks (where taking it would only cost), retrieves on
   non-resident tasks (where it rescues correctness). This is the strongest argument for
   shipping `AGENTS.md` in core packages by default.
6. **Log call behavior in production.** P(summary)/P(full) per package and per model tier is a
   cheap, continuous read on whether a package's grounding is earning its cost.

## Threats to validity

- **Small samples** (runs = 3–5). Headlines are robust (e.g. the Haiku fail→pass rescue holds
  in both arms at runs=5), but per-arm magnitudes are soft and variance is high (CV up to
  ~1.6 on the thrashing Haiku M1 runs).
- **IET is noise-dominated on small/resident tasks.** We deliberately read call-behavior +
  pass/fail there, and IET only on the large M1 task.
- **Two scenarios, two packages.** M1 (`System.CommandLine`, version-delta) and R1
  (`System.Text.Json`, resident) are chosen as clean poles; the spectrum between them is
  interpolated, not densely sampled.
- **Single judge** (`claude-opus-4.6`) and **Copilot-SDK-reported usage** (reasoning tokens
  are folded into output, not itemized for Claude; the SDK `cost` field is a premium-request
  multiplier, not token cost — see the session notes on the reasoning-token audit).

## Reproduce

```sh
BIN=.tools/skill-validator-5d717dbdd1998cdf88e7542eef52c5517cbefdb9/skill-validator

# Pick a gate, then run a scenario. The MCP attaches to the skilled arms only.
export GROUNDING_GATE=progressive          # or task_type | uncertainty_version
: > .tools/mcp-calls.log                    # clear the call log first

# Pole A — skip justified (resident):
"$BIN" evaluate --tests-dir tests --model claude-opus-4.6 \
  --judge-model claude-opus-4.6 --runs 5 grounding/system-text-json-mcp

# Pole B — take & reward (version-delta, weak tier):
"$BIN" evaluate --tests-dir tests --model claude-haiku-4.5 \
  --judge-model claude-opus-4.6 --runs 5 grounding/system-commandline-mcp

# Read P(summary)/P(full):
cat .tools/mcp-calls.log
```

The MCP server, the inert attach units (`grounding/system-commandline-mcp`,
`grounding/system-text-json-mcp`) and the scenarios (`tests/system-commandline-mcp`,
`tests/system-text-json-mcp`) carry the full setup. `.skill-validator-results/` and
`.tools/` are gitignored; results live locally per run.
