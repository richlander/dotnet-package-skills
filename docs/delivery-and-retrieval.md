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
- **Agents self-gate at *package granularity*, not just per-task.** On a multi-package project
  where only one package is load-bearing and the others are benign distractors, agents retrieve
  grounding for **only the load-bearing package** — the compiler localizes the breakage, and
  they don't over-pull the rest to rule them out. They triage with cheap local signals (csproj,
  prompt, compiler errors) *before* touching the grounding tool.
- **A progressive "summary" *tool* does NOT act as a filter — and can backfire.** We expected a
  cheap summary tool to let agents *peek and decline* the distractors. It didn't. Opus treats it
  as an **on-ramp** (peeks *all* packages, then pulls *all* bodies, incl. the benign one); Haiku
  ignores it and stays myopic on the build blocker. Across every scenario **progressive never
  beat the single tool.** The fix is *not* a summary tool — it is a **resident index** (below).
- **The faithful skills design — a *resident* index + one body tool — is the good end.** Skills
  work because every skill's description is **pushed into context for free** (a resident index);
  only the body is gated. We ported that as a single `get_package_context` whose description
  carries a free per-package manifest. For weak models this is the **only** delivery that
  surfaces a **silent, compile-clean** gotcha the agent would otherwise miss (Haiku retrieved
  `System.Text.Json` *only* under the resident index; under the bare tool and progressive it
  triaged solely on the compiler error and never looked). It strictly dominates the two-tool
  progressive.
- **"Call behavior" is a free pre-screen.** Whether (and which) tool the agent calls predicts
  where grounding pays — without running an expensive graded evaluation. It is also far less
  noisy than pass/fail deltas, which flip run-to-run at small n.

**Design implication for the NuGet MCP:** expose grounding as **one `get_package_context` body
tool**, and pair it with a **resident index built from a project file** — a
`get_project_grounding_index(projectPath)` the *host* calls and pushes into context (a
one-line-per-direct-dependency manifest from each `AGENTS.md` frontmatter). Treat discovery as an
**input**: build the index only from a project the host already knows, and **abstain to on-demand
when none is given** — one narrow rule, never a heuristic stack. **Ship `AGENTS.md` in the
package.** Do *not* add a separate `summarize_package_context` *tool*: gating the index behind an
agent call makes peeking an on-ramp (frontier models pull everything) or dead weight (weak models
ignore it). The resident index gives costless selection + decline-by-default, and is the only
channel that surfaces silent gotchas the compiler can't flag.

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
| `resident_index` | 1 (`get_package_context`) | the body tool's description carries a free, always-in-context manifest (one line per package, from `AGENTS.md` frontmatter); the agent reads the index for free and calls only for a relevant package — the faithful skills mechanic (§8) |

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

> **We tested this hypothesis and it did not hold.** The summary channel does *not* function as
> a selective filter — agents either pull everything after peeking (Opus) or ignore the summary
> (Haiku). See [§4](#4-agents-self-triage-at-package-granularity-multi-package) and
> [§5](#5-the-progressive-summary-is-an-on-ramp-not-a-filter) below. The mechanic is described
> here because it is the design under evaluation, not because it won.

## Metric: "call behavior" as the pre-screen

The headline metric here is **not** tokens — it is **what the agent chose to call**:
`P(call)`, which tool (summary vs full), the order (`summarize → get_full` vs
`summarize → ∅`), and the per-arm distribution.

It is the right metric because it is **near-binary, intentional, and low-variance**: the
agent either retrieves or it doesn't. Weighted Input-Equivalent Tokens
(IET = `fresh + 0.1·cacheRead + 1.25·cacheWrite + 5·output`, where
`fresh = inputTokens − cacheReadTokens`; the repo-wide primary cost metric, recomputed from the
raw token classes by [`eng/extract-channels.py`](../eng/extract-channels.py)) is a downstream,
noisy proxy — on a small task, fixed overhead and cache warmup swamp the ~600-token cost of the
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

### 4. Agents self-triage at package granularity (multi-package)

Poles A/B used single-package scenarios. The decisive test of whether a *summary* channel adds
value is a **multi-package** project: a realistic solution referencing several packages where
only one is load-bearing. We built **T1** — a `System.CommandLine` `beta4→3.x` migration
(load-bearing: the removed API won't compile) shipped alongside `System.Text.Json` and
`Microsoft.Extensions.AI` as **benign distractors** (referenced, but their documented gotcha is
never exercised, so their grounding bodies are dead weight). The prompt is **symmetric** — all
three packages are listed with target versions, with no hint that `System.CommandLine` is
special.

**Finding:** both Opus and Haiku, under both gates, retrieved grounding for
**`System.CommandLine` only** — never the distractors. The compiler localizes the breakage to
`System.CommandLine`, and the agent triages on that local signal *before* reaching for the
grounding tool. **Agents self-gate at package granularity already**, using cheap local signals
(csproj, prompt, compiler errors). The hoped-for progressive benefit — "peek all, decline the
irrelevant ones" — never had to fire, because the agent never considered the distractors at
all.

### 5. The progressive summary is an on-ramp, not a filter

To give the summary channel a real job, we built **T2b** — an *ambiguous* multi-package
scenario where a distractor is *not* benign: a naive `System.Text.Json` deserialization
silently mis-binds camelCase JSON into a PascalCase record (case-sensitive by default →
silently empty), **fixable from the one-line summary alone**. Now a smart progressive agent
*should* peek `System.Text.Json`, recognize the fix from the summary, and **decline** the full
body. `Microsoft.Extensions.AI` remains a benign skip.

**Opus, runs=3:**

| Gate | Retrieval behavior |
| --- | --- |
| `task_type` (single tool) | pulled `System.CommandLine` ×7 full, STJ ×1 full, M.E.AI ×0 — mostly self-solved |
| `progressive` (two tools) | **summarized all 3 AND pulled the full body for all 3** (M.E.AI 0% → ~50% of arm-runs) |

The summary tool behaved as an **on-ramp, not a filter**: Opus peeked everything and then
pulled everything — *including* the benign `Microsoft.Extensions.AI` distractor it had ignored
under the single tool. It did **not** decline after peeking. So the second tool *increased*
retrieval breadth rather than enabling selective decline.

**Haiku, runs=3:** under **both** gates Haiku retrieved **`System.CommandLine` only** (4
summarize + 4 get on the progressive gate; never peeked the distractors). The weak model stays
myopic on the build blocker and ignores the summary channel entirely. So the peek-all/pull-all
enumeration is **Opus-specific**; Haiku derives no triage benefit from the summary either.

**Why it was harmless here (and why that won't generalize):** the grounding bodies are small
(~40–60 lines), so Opus's extra pulls cost little and `taskCompletionImprovement` was 0 (Opus
self-solves T2b regardless). With *large* bodies, peek-all/pull-all would be **actively worse**
than the single tool — paying full-body cost for packages the agent would otherwise have
skipped. Across M1, R1, T1, and T2b, **progressive never beat the single tool.**

### 6. Validation against the real NuGet MCP server

We ran T1 against the **real `NuGet.Mcp.Server`** (`dnx NuGet.Mcp.Server --yes`; 6 tools incl.
`get_package_context(solutionDirectory, packageName, packageVersion)`) to validate the
instrument. Three confirmations:

- **Content:** `get_package_context` for `System.CommandLine` returns a `nuget-readme://…`
  resource — the **package README**, *not* an `AGENTS.md` (these packages ship none, so the
  server falls back to the README). The README shows the 3.x API shape but **none of the
  curated gotcha** — i.e. the real server under-delivers exactly the silent/obscure content our
  authoring principles target.
- **Self-triage holds:** when Opus called it, it queried **`System.CommandLine` only** — no
  distractor over-pull, matching §4.
- **Under-firing on coding tasks:** Opus's `skilledPlugin` arm called the server **0 times**;
  Haiku called it **0 times in both arms** (it used `web_fetch` + its own knowledge, and the
  harness logged "Skill NOT activated"). The harness wiring works (Opus's isolated arm did call
  it once), so this is genuine **call-avoidance**: the real server's **task-type gate
  under-fires on coding tasks**, the dominant grounding use case. This is the strongest single
  piece of feedback for the NuGet team: the gate wording targets "the user asks about a
  package," which a coding TODO is not.

### 7. Why §5 backfired: we gated the *index*, not just the body

The progressive scheme was meant to *be* the skills mechanic. It isn't — and the gap explains
the §5 failure. Skills push **every** skill's frontmatter (`name` + `description`) into context
**up front**: an always-on, zero-cost, *resident* index. The agent reads the whole index for
free and *acts* (activates the body) only on the relevant entry. Two properties fall out:

- **Selection is a free read, not an action.** Skimming a candidate costs nothing and commits
  to nothing.
- **Decline is the default.** You skip a skill by simply *not acting*; skipping is free and is
  never externalized as a decision.

Our `progressive` gate exposed the summaries as a **tool** (`summarize_package_context`,
grounding_mcp.py:223–232): the per-package descriptions are **not** in context — the agent must
*call* a tool to see one. So we gated **both** the index *and* the body behind calls, destroying
both properties:

- **Peeking became an action.** Once Opus spends a call to summarize a package, it is already
  "investigating" it — so it continues to `get_full`. The summary is an **on-ramp**, not a
  glance. (Hence peek-all → pull-all.)
- **Decline stopped being free.** You only learn a package is irrelevant *after* paying to
  summarize it. Haiku's response is the opposite failure: it won't pay the peek toll, so it
  ignores the channel entirely.

So §5's two failure modes are **rational responses to gating the index behind a call**, not
model error. The faithful port keeps the index **resident** and gates only the body:

| Layer | Skills | Broken `progressive` MCP | Faithful port (`resident_index`) |
| --- | --- | --- | --- |
| Index (per-package summary) | **pushed, resident, free** | behind `summarize` tool call | **pushed, resident, free** |
| Body | activated on demand | behind `get` tool call | behind `get_package_context` call |

The MCP host already knows the installed set (csproj / lockfile), so it can push a
one-line-per-installed-package manifest into context with **no agent action**, and expose a
single `get_package_context` for bodies. That restores costless passive selection +
decline-by-default + gated bodies — the real skills mechanic — *without* a summarize tool.

**The spectrum collapses to two viable ends, with a broken middle:**

| Design | Index | Body | Verdict |
| --- | --- | --- | --- |
| **All-or-nothing single tool** | none | one tool | works; agent self-triages on the package name — but only as well as its triage *signal* (the compiler), so it misses **silent** non-compiling gotchas (§8) |
| Two-tool `progressive` | gated (tool) | gated (tool) | **broken middle** — on-ramp (Opus) / ignored (Haiku) |
| **Resident index + body tool** (skills) | pushed, free | one tool | **best for weak models** — the manifest surfaces silent gotchas the bare tool misses (§8) |

The all-or-nothing tool works because the agent self-triages on the *package name* it already
has (§4) — but its triage is only as good as the **compiler**, so it misses gotchas that
compile clean and fail silently. The resident-index design is the only one that surfaces those
**without** a peek toll. §8 tests it directly.

### 8. The faithful skills design: resident index + one body tool

We implemented the faithful port as the `resident_index` gate: **one** `get_package_context`
tool, with the per-package manifest (each `AGENTS.md`'s frontmatter `description`) baked into
the tool's description — so it is always in context, free, no agent action. We re-ran **T2b**
(`System.CommandLine` build break + `System.Text.Json` *silent* case bug + benign
`Microsoft.Extensions.AI`) at runs=3. Call behavior (the robust signal) is decisive:

| Tier · gate | SCL (build break) | STJ (**silent** bug) | M.E.AI (benign) |
| --- | --- | --- | --- |
| Opus · bare single tool (`task_type`) | 7 | 1 | **0** |
| Opus · `progressive` | 6 (+6 peek) | 6 (+6 peek) | 3 (+4 peek) |
| **Opus · `resident_index`** | 6 | 6 | **6** |
| Haiku · bare single tool | 7 | **0** | 0 |
| Haiku · `progressive` | 4 (+4 peek) | **0** | 0 |
| **Haiku · `resident_index`** | 11 | **10** | **2** |

Two findings, split by tier:

- **Weak model (Haiku): the resident index is the win.** Under the bare tool *and* under
  progressive, Haiku triaged only to the **compile error** (`System.CommandLine`) and **never
  retrieved `System.Text.Json`** — so it could not have known about the silent case bug. Under
  `resident_index` the free manifest **surfaced STJ** (SCL ×11, STJ ×10) while Haiku **declined
  the benign M.E.AI** (×2). The resident manifest is the *only* channel that put the silent,
  compile-clean gotcha in front of the model that needed it. Both skilled arms completed the
  task; baseline thrashed (74 tools).
- **Frontier model (Opus): the manifest broadens retrieval.** Opus pulled **all three** bodies
  every run (incl. benign M.E.AI), where the *bare* tool had it triage surgically to SCL
  (×7, STJ ×1, MEAI ×0). A confident, thorough model treats the advertised manifest as a
  checklist and pulls everything referenced. Harmless here — small bodies, all runs complete,
  tokens still below the noisy baseline, and Opus self-solves regardless
  (`taskCompletionImprovement` = 0) — but **less surgical** than the bare tool.

**Why call behavior, not the completion delta.** Haiku's `resident_index` arms show
`taskCompletionImprovement` = +1 (fail→pass), but the `progressive` baseline *also* passed
(delta 0): the Haiku baseline flips pass/fail run-to-run at n=3 (`varianceCV` 1.2–6.9). The
completion delta is baseline-noise; the **retrieval targeting** (which packages the model
pulled) is the stable, intentional signal — and it cleanly separates the designs.

**Net.** `resident_index` **strictly dominates** the two-tool `progressive`: same-or-better
targeting, no peek toll, none of progressive's wasted summarize round-trips. Versus the bare
single tool it is a **wash for confident models** (slightly broader pulls) and a **clear win
for weak models on silent bugs** (it surfaces compile-clean gotchas the compiler can't flag).
The good end of the spectrum is the **resident index + one body tool**; the two-tool
progressive is the broken middle.

## Discovery & feasibility: how a resident index ships

The lab result (§8) assumes the host *already knows* the installed package set. In production
that is the hard part — and the trap is solving it with a pile of fragile heuristics (parse CI
YAML, detect `OutputType=Exe`, walk the project graph, exclude test projects…). Such a stack is
untestable and behaves wildly across repos. The discipline is the opposite:

> **Discovery is an *input*, not an *inference*. Use exactly one narrow rule, and make it
> *abstain* under ambiguity** — fall back to the on-demand tool rather than guess.

Every real host already *holds* an authoritative target: an IDE knows the active/startup
project (or the project owning the open file); a CLI/agent runs `dotnet build|run|test <target>`
which *names* it. Consume that. The only rule is:

```
host provides a target project?
  ├─ yes → read its direct PackageReferences → build the resident manifest → push it
  └─ no  → abstain → on-demand get_package_context (today's NuGet MCP)
```

This is trivially testable (`project path → manifest` is deterministic; absence → on-demand) and
cannot sprawl, because ambiguity routes to the design that needs no discovery. It also matches
reality: the contexts where you'd *want* the proactive manifest (editing in an IDE, an agent
told to build `src/dotnet-inspect`) are exactly the ones where the target is already known.

**A concrete tool shape — "a skill system, given a project file."** Expose two tools:

1. `get_project_grounding_index(projectPath)` — the **host** calls this once when a project is
   targeted (project open / build command), and **pushes the returned manifest into context**.
   The manifest is one line per direct dependency that ships grounding (its `AGENTS.md`
   frontmatter). Because the *host* drives this call deterministically — not the agent on a
   judgement call — it sidesteps the under-firing we measured against the real server (§6): the
   index becomes resident the same way skills' frontmatter is, without relying on the agent to
   decide to fetch it.
2. `get_package_context(packageName, version)` — the body tool the **agent** calls on demand for
   a package the manifest (or the code) flags. Unchanged from today.

That is the faithful skills mechanic delivered over MCP: tool 1 is the resident index (pushed by
the host), tool 2 is the gated body. Abstention is built in — if the host has no project to pass
to tool 1, you are simply back to on-demand.

**The index read is restore-free.** For a target project, both halves are plain text:
`PackageReference Include=…` says *which* packages; `Directory.Packages.props` (Central Package
Management) says the versions — no `dotnet restore` required. Concretely, `dotnet-inspect`'s
`src/dotnet-inspect/dotnet-inspect.csproj` yields five direct packages
(`MarkdownTable.Formatting`, `Markout`, `NuGet.Versioning`, `ShellComplete`,
`System.CommandLine`), versioned centrally (`System.CommandLine` pinned to a **3.x preview**) —
exactly the kind of post-training version the agent should be warned about *before* it writes
beta-style code. (The authoritative MSBuild query, `msbuild -getItem:PackageReference`, also
works but forces a restore of the project graph and is slow — prefer the text read.)

**The one residual NuGet-side ask.** Text tells you *which* packages and versions, but not
*whether* a package ships an `AGENTS.md` or *what* its summary line is. That needs either the
package restored into the global-packages folder (read the frontmatter locally) or — the clean
enabler — **NuGet exposing `AGENTS.md` frontmatter as service-side metadata** (registration leaf
or a dedicated grounding resource), so `get_project_grounding_index` can build the manifest from
the csproj alone, pre-restore. This is the same restore boundary that already exists today:
NuGet's own `get_package_context` serves `AGENTS.md` only for locally-installed packages and
falls back to a remote README otherwise (§6). So the resident index adds **no new restore
dependency for content** — only a desire to read frontmatter cheaply, which a small metadata
addition solves.

## Implications for NuGet MCP design

1. **Ship grounding as one `get_package_context` body tool — *not* a `summarize` + `get`
   pair.** A separate summary *tool* gates the index behind a call, which makes peeking an
   on-ramp (frontier models pull everything) or dead weight (weak models ignore it). It never
   beat the single tool across M1/R1/T1/T2b.
2. **Pair the body tool with a *resident* index — push it, don't gate it.** Add a
   `get_project_grounding_index(projectPath)` tool the **host** calls once per targeted project
   and pushes into context: a one-line-per-direct-dependency manifest from each `AGENTS.md`
   frontmatter. The agent then calls the single `get_package_context` for bodies. This is the
   actual skills mechanic — "a skill system, given a project file." Validated in §8: it strictly
   dominates the two-tool progressive and is the **only** delivery that surfaces a silent,
   compile-clean gotcha to a weak model (which otherwise triages on the compiler alone and never
   looks at the offending package). Host-driven (not agent-judged) calling sidesteps the §6
   under-firing.
3. **Make discovery an input, not an inference.** Build the manifest only from a project the
   host already knows (IDE active project, or a `dotnet build|run` target); **abstain to
   on-demand when none is provided.** One narrow, testable rule — never a heuristic stack
   (CI-YAML parsing, exe-root detection, graph walking). See *Discovery & feasibility*.
4. **Make `AGENTS.md` self-contained with YAML frontmatter** (`name` + `description`). It is the
   shipped artifact and the resident-summary channel; it should not depend on harness-only
   metadata. To let the index be built pre-restore, expose that frontmatter as **service-side
   package metadata**.
5. **Don't engineer the WHEN-TO-CALL string.** Across permissive and strict gates the retrieval
   decision was the same — the model gates on its own confidence and the package name. The one
   place wording demonstrably *hurts* is the real server's **task-type** gate ("call when the
   user asks about a package"), which **under-fires on coding tasks** (§6) — the dominant
   grounding case. Prefer an uncertainty/version-delta framing.
6. **Keep agent instructions clean.** No "use the NuGet MCP" prompt. Register the tool / push
   the manifest; the model self-selects from normal context.
7. **Auto-delivered grounding is Pareto-safe** *because* the model only pays for the body when
   it pays off: it declines on resident tasks (where taking it would only cost) and retrieves on
   non-resident tasks (where it rescues correctness). This is the strongest argument for
   shipping `AGENTS.md` in core packages by default.
8. **Log call behavior in production.** P(full) per package and per model tier is a cheap,
   continuous read on whether a package's grounding is earning its cost.

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

# One-time after clone: generate each MCP unit's plugin.json (absolute paths) from the
# committed plugin.json.in templates. The harness drops cwd, so the MCP path must be absolute;
# the generated plugin.json files are gitignored.
./eng/gen-plugins.sh

# Pick a gate, then run a scenario. The MCP attaches to the skilled arms only.
export GROUNDING_GATE=resident_index       # or task_type | uncertainty_version | progressive
: > .tools/mcp-calls.log                    # clear the call log first

# Pole A — skip justified (resident):
"$BIN" evaluate --tests-dir tests --model claude-opus-4.6 \
  --judge-model claude-opus-4.6 --runs 5 grounding/system-text-json-mcp

# Pole B — take & reward (version-delta, weak tier):
"$BIN" evaluate --tests-dir tests --model claude-haiku-4.5 \
  --judge-model claude-opus-4.6 --runs 5 grounding/system-commandline-mcp

# §8 — resident index surfaces a silent gotcha to a weak model (multi-package, ambiguous):
"$BIN" evaluate --tests-dir tests --model claude-haiku-4.5 \
  --judge-model claude-opus-4.6 --runs 3 grounding/multi-package-ambiguous-mcp

# Read which packages were pulled (the robust signal):
cat .tools/mcp-calls.log
```

The MCP server (`grounding/_mcp/grounding_mcp.py`), the inert attach units
(`grounding/*-mcp/`, generated `plugin.json` from `plugin.json.in`) and the scenarios
(`tests/*-mcp/`) carry the full setup. `.skill-validator-results/` and `.tools/` are
gitignored; results live locally per run. Run gates **sequentially** — concurrent runs share
`.tools/mcp-calls.log`.
