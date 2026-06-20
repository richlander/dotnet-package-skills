# Recommendation: should we author package grounding, and should the NuGet MCP change?

**Audience:** NuGet v-team. **Date:** 2026-06-20. **Status:** Findings complete (2 tasks × 5
channels × 2 tiers, runs=3).

This is the executive summary of the package-grounding study. It answers **two team decisions**
— *(1) do we write grounding content for packages?* and *(2) does the NuGet MCP need to change?*
— then backs each with one measured progression from the status-quo baseline to a recommended
design, across **four delivery channels** and **two model tiers**. Per-package findings live in
[`docs/reports/`](reports/); the *what to write* guidance is in
[`docs/authoring-principles.md`](authoring-principles.md); the delivery/retrieval mechanics are
in [`docs/delivery-and-retrieval.md`](delivery-and-retrieval.md). Raw `results.json` for every
cell is in [`data/`](../data/).

---

## Two questions for the team

This study exists to answer two decisions. Both answers are **yes**, and the data says *why*.

### Q1 — Should we author and ship grounding content (`AGENTS.md`) in NuGet packages? **Yes.**

A small, *complete* `AGENTS.md` (~3.5 KB) is **size-invariant** and, delivered through the MCP,
beats serving the README on **both** model tiers — and flips the weak tier from a failing run to
a passing one (Markout Haiku: README path passes but costs more; `AGENTS.md` path is leaner; the
README *without* MCP **fails**). Conversely, the README is a **liability**: high-variance and
high-ceiling for weak models, an efficiency tax for strong ones.

We have **worked examples for four real packages**, each with a backing report:

| Package | Authored grounding | Why it was written that way (report) |
|---------|--------------------|--------------------------------------|
| Markout | [`grounding/markout/AGENTS.md`](../grounding/markout/AGENTS.md) | [`reports/markout.md`](reports/markout.md) — non-resident; a no-reflection-fallback trap |
| System.CommandLine | [`grounding/system-commandline/AGENTS.md`](../grounding/system-commandline/AGENTS.md) | [`reports/system-commandline.md`](reports/system-commandline.md) — beta4→3.x migration breakage |
| System.Text.Json | [`grounding/system-text-json/AGENTS.md`](../grounding/system-text-json/AGENTS.md) | [`reports/system-text-json.md`](reports/system-text-json.md) — model-resident; what the model *lacks* |
| Microsoft.Extensions.AI | [`grounding/microsoft-extensions-ai/AGENTS.md`](../grounding/microsoft-extensions-ai/AGENTS.md) | [`reports/microsoft-extensions-ai.md`](reports/microsoft-extensions-ai.md) — function-invocation surface |

The authoring rule (only write what the model *provably* lacks) is in
[`authoring-principles.md`](authoring-principles.md). **Caveat — content alone is not enough:**
an `AGENTS.md` shipped without a delivery channel is **invisible** (raw lookup reads the README
anyway, and Haiku fails — Channel A′, Step 2). Writing it only pays off when the MCP delivers it.

### Q2 — Should the NuGet MCP change? **Yes — one addition, and one non-feature to avoid.**

The current `NuGet.Mcp.Server` is already most of the way there: we **verified by direct call**
that `get_package_context` prefers `AGENTS.md` over the README when present
(`nuget-context://…/AGENTS.md` vs `nuget-readme://…/README.md`). **Keep that.** What should change:

- **Add a resident, per-direct-dependency index to the `get_package_context` tool description,
  built from the project file the host already knows.** This is Channel **D** — the cheapest
  channel on the weak tier and on the harder multi-package task (multi-package Opus **92k IET**
  vs 188k raw-lookup, **−51%**; Haiku **286k** vs 939k, **−70%**), statistically tied with serving
  `AGENTS.md` on the easy strong-tier cell, and the **only** channel that surfaces silent,
  compile-clean gotchas. The agent self-gates: it declines when it already knows the package and
  retrieves when it doesn't, at **zero** extra tool calls. Treat discovery as an input; **abstain
  to on-demand when no project file is given** — one narrow rule, never a heuristic stack.
- **Do *not* add a separate `summarize_package_context` tool.** Gating the index behind an agent
  call makes peeking either an on-ramp (frontier pulls everything) or dead weight (weak models
  ignore it). The resident index gives costless selection and decline-by-default for free.

---

## The setup: four delivery channels

We hold the *task* and the *content* fixed and vary only **how the grounding reaches the agent**.

| Ch | Delivery mechanism | `AGENTS.md` in package | What the agent sees |
|----|--------------------|------------------------|---------------------|
| **A**  | raw package on disk (no MCP) | absent | finds + reads the **README** |
| **A′** | raw package on disk (no MCP) | **present** | **still reads the README** (AGENTS.md is *invisible*) |
| **B**  | real `NuGet.Mcp.Server` `get_package_context` | absent | server returns the **README** |
| **C**  | real `NuGet.Mcp.Server` `get_package_context` | **present** | server returns the **`AGENTS.md`** |
| **D**  | our controlled MCP (`get_package_context` + **resident index**) | served on demand | curated grounding, self-gated |

Two tasks: **Markout M1** (a genuinely non-resident single package — a source-generated
serializer with a no-reflection-fallback trap) and **multi-package triage** (a `System.CommandLine`
beta4→3.x migration with two benign distractor packages).

Two tiers: **Opus** (frontier — measures *efficiency*) and **Haiku** (weak — measures
*correctness rescue*).

**Metric — IET (Input-Equivalent Tokens).** All costs below are reported in weighted IET, a
cost-equivalent measure that normalizes each token class to fresh-input units:
`IET = fresh + 0.1·cacheRead + 1.25·cacheWrite + 5·output` (recomputed from the raw token classes
in each `results.json`). This is the honest cost signal: it stops cheap prompt-cache reads from
inflating the exploration-heavy raw baseline. Tables also show the harness's unweighted
`tokenEstimate` (`tEst` = `inputTokens + outputTokens`, cache reads at full price) in parentheses
for traceability.

---

## Step 1 — Baseline: raw package and NuGet MCP, with no `AGENTS.md` (A, B)

> *Claim: with no curated grounding, both the raw package and the NuGet MCP fall back to the
> README. The package is "self-teaching" only to the extent its README is — which is expensive
> and unreliable.*

<!-- DATA: Markout A vs B (Opus, Haiku); multipackage A vs B -->
**Markout M1 (runs=3, averaged).** "raw" = the agent finds and reads the package's README from
the restored package on disk; "NuGet MCP" = one `get_package_context` call returns that same README.

| tier | A — raw pkg → README | B — NuGet MCP → README |
|------|---:|---:|
| Opus  | 78k IET (397k tEst) / 23 tools | **38k IET (118k) / 8 tools** (1 MCP call) |
| Haiku | 49k IET (172k) / 12 tools | **40k IET (147k) / 8 tools** (1 MCP call) |

The headline is the **delivery channel, not the content**: serving the *same* README through one
MCP call instead of an open-ended filesystem hunt cuts Opus cost ~2× (78k→38k IET) and roughly
halves the tool count. The README is where the agent goes by default, and it is a **liability**: in a controlled size sweep ([`readme-liability.md`](reports/readme-liability.md)),
baseline cost rose with README size (and a too-small/truncated README was *also* expensive:
incompleteness → the agent spelunks the nupkg and the XML doc). The lesson is not "smaller
README" — it is "stop making the README the interface."

## Step 2 — A shipped `AGENTS.md` is invisible to raw lookup (A′)

> *Claim: simply putting `AGENTS.md` in the package is not enough. Without a delivery channel
> that points at it, the agent doesn't discover it — it reads the README anyway.*

<!-- DATA: Markout A' (AGENTS present in cache, baseline arm still reads README) -->
**Markout M1, raw lookup with `AGENTS.md` present in the package (runs=3).**

| tier | A — README only | A′ — `AGENTS.md` present, raw lookup | outcome |
|------|---:|---:|---|
| Opus  | 78k IET (397k tEst) / 23 tools (✓) | 124k IET (633k) / 26 tools (✓) | AGENTS.md **0 reads**; cost *up* |
| Haiku | 49k (172k) / 12 tools (✓) | 62k (239k) / 14 tools (**✗ failed**) | AGENTS.md **0 reads**; task **not completed** |

The agent never opened the curated doc sitting right next to the README, and the weak tier
actually **failed** the task (`taskCompleted=false`) while drowning in the README.

With `AGENTS.md` sitting right next to the README in the restored package, the raw-lookup agent
**ignored it** and mined the README regardless. So "ship `AGENTS.md`" is necessary but not
sufficient: the package needs a **delivery channel** that surfaces the curated doc. That is the
job of the MCP.

## Step 3 — A well-crafted `AGENTS.md`, delivered by the NuGet MCP (C)

> *Claim: once `AGENTS.md` ships AND the NuGet MCP serves it, the agent gets the targeted doc in
> one call — cheaper for strong models, and a correctness rescue for weak ones.*

<!-- DATA: Markout C vs A/B; Haiku rescue -->
**Markout M1, `AGENTS.md` shipped and served by the real NuGet MCP (runs=3).**

| tier | A′ — shipped but undelivered | B — MCP → README | C — MCP → `AGENTS.md` |
|------|---:|---:|---:|
| Opus  | 124k IET (633k tEst) / 26 (✓) | 38k (118k) / 8 (✓) | **28k (105k) / 7 (✓)** |
| Haiku | 62k (239k) / 14 (**✗**) | 40k (147k) / 8 (✓) | **39k (122k) / 9 (✓)** |

Delivering the `AGENTS.md` flips the weak-tier outcome from **fail → pass** and beats serving the
README (C < B on both tiers: Opus 28k<38k, Haiku 39k<40k IET). The concise pattern is legible
where the 488-line README is not. The selection is the upstream server's own behavior, verified
by a direct call ([`data/markout/nuget-mcp-delivery-proof.md`](../data/markout/nuget-mcp-delivery-proof.md)):
it returns `nuget-context://Markout/0.13.6/AGENTS.md` when the package ships `AGENTS.md`, and
falls back to `nuget-readme://.../README.md` otherwise.

## Step 4 — Our custom MCP, with a skill-oriented (resident-index) mode (D)

> *Claim: a faithful "skills" design — a resident per-package index pushed into context for
> free, plus one body tool — is the strongest channel where it matters most (hard tasks, weak
> models). The agent self-gates correctly, and the resident index is the only mechanism that
> catches silent, compile-clean gotchas.*

<!-- DATA: Markout D; multipackage D (resident_index) — triage + silent STJ gotcha -->
**Markout M1, custom MCP with the resident-index gate (runs=3).** On this easy single-package
task the two curated channels are close; D wins the weak tier outright and ties C on the strong
tier:

| tier | C — NuGet MCP → AGENTS | D — custom MCP (resident index) |
|------|---:|---:|
| Opus  | **28k IET (105k tEst) / 7** | 31k (91k) / 8 |
| Haiku | 39k (122k) / 9 | **31k (106k) / 8** |

On the strong tier the two are within noise (Opus C 28k vs D 31k IET); on the weak tier D is
clearly cheaper (Haiku 31k vs 39k). D's decisive advantage shows on the **multi-package
triage** task (quantified in the matrix below), where agents triage with cheap local signals
(csproj, compiler errors) and pull grounding for **only the load-bearing package**. A bare
on-demand tool misses a **silent**, compile-clean case-sensitivity gotcha in a distractor (no
compiler error to localize it); the **resident index** — whose one-line-per-dependency manifest
is in context for free — is the only channel under which the weak model retrieves that package's
guidance and avoids the latent bug. See [`delivery-and-retrieval.md`](delivery-and-retrieval.md)
for the full retrieval-gate analysis.

---

## The full channel matrix

<!-- DATA: consolidated A/A'/B/C/D x task x tier -->
**Markout M1 — all five channels, runs=3 averaged.** Primary = weighted IET; (`tEst`) = unweighted
harness estimate; ✓/✗ = `taskCompleted`; *save* = weighted-IET reduction vs. Channel A:

| Ch | delivery | Opus IET (tEst) / tools | save | Haiku IET (tEst) / tools | done |
|----|----------|---:|---:|---:|:--:|
| **A**  | raw pkg → README | 78k (397k) / 23 | — | 49k (172k) / 12 | ✓ |
| **A′** | raw pkg, AGENTS present (invisible) | 124k (633k) / 26 | −60% | 62k (239k) / 14 | **✗** |
| **B**  | real NuGet MCP → README | 38k (118k) / 8 | 51% | 40k (147k) / 8 | ✓ |
| **C**  | real NuGet MCP → `AGENTS.md` | **28k (105k) / 7** | **64%** | 39k (122k) / 9 | ✓ |
| **D**  | custom MCP (resident index) | 31k (91k) / 8 | 60% | **31k (106k) / 8** | ✓ |

Two effects compound, in order of magnitude: **(1) channel** — moving from raw filesystem lookup
to *any* one-shot MCP retrieval is the big win (~2× on Opus, even on weighted IET that discounts
the baseline's cheap cache reads); **(2) content** — serving the curated `AGENTS.md` instead of
the README, and adding the resident index, refines it further and rescues the weak tier. On the
strong tier C and D are within noise; on the weak tier D is cheapest. Channel A′ is the cautionary
cell: shipping `AGENTS.md` **without** a delivery channel costs more than doing nothing and still
fails Haiku.

> Multi-package triage channel data (A/B/D) is captured in
> [`data/multipackage/`](../data/multipackage/); the qualitative resident-index advantage
> (silent-gotcha capture, package-level self-triage) is detailed in
> [`delivery-and-retrieval.md`](delivery-and-retrieval.md).

**Multi-package triage (`System.CommandLine` beta4→3.x migration + 2 distractors), runs=3.**
A harder task (the agent must build, localize breakage, and migrate) — so absolute costs are
higher, but the channel ordering is identical and the resident-index gap *widens*:

| Ch | delivery | Opus IET (tEst) / tools | save | Haiku IET (tEst) / tools | save |
|----|----------|---:|---:|---:|---:|
| **A** | raw pkg → README | 188k (723k) / 27 | — | 939k (5,204k) / 99 | — |
| **B** | real NuGet MCP → README | 138k (520k) / 22 | 27% | 665k (3,634k) / 71 | 29% |
| **D** | custom MCP (resident index) | **92k (204k) / 13** | **51%** | **286k (1,236k) / 50** | **70%** |

The raw-lookup baseline *thrashes* on this task (Haiku: 99 tool calls, 5.2M unweighted tokens —
939k IET), and the resident-index channel cuts that ~3.3×. The harder the task, the larger the
channel gap. (Channels A′/C are omitted on this task by design — see *Method & caveats*.)

## Recommendation & design implications for the NuGet MCP

- **Author** a small, complete `AGENTS.md` per package (see
  [`authoring-principles.md`](authoring-principles.md): only what the model provably lacks).
- **Ship** it in the `.nupkg` (root). It is size-invariant value; the README stays for humans.
- **Deliver** it via one `get_package_context` **body tool** whose description carries a
  **resident, per-direct-dependency index** built from the **project file** the host already
  knows. Treat discovery as an input; **abstain to on-demand when no project is given** — one
  narrow rule, never a heuristic stack.
- **Do not** add a separate `summarize_package_context` *tool*: gating the index behind an agent
  call makes peeking an on-ramp (frontier pulls everything) or dead weight (weak models ignore
  it). The resident index gives costless selection + decline-by-default.

## Method & caveats

- Harness: `skill-validator` (pinned `eng/skill-validator.sha`), built from source. Runner:
  [`eng/run-channel-matrix.sh`](../eng/run-channel-matrix.sh); extractor:
  [`eng/extract-channels.py`](../eng/extract-channels.py).
- Channels A/A′/B/C are the baseline+plugin arms of the `*-realmcp` evals under two cache states;
  D is the plugin arm of the `*-custommcp` eval (`GROUNDING_GATE=resident_index`).
- **Metric.** Primary cost is **weighted IET** = `fresh + 0.1·cacheRead + 1.25·cacheWrite +
  5·output`, where `fresh = inputTokens − cacheReadTokens` (Copilot/OpenAI report `inputTokens`
  as the *total* prompt, with `cacheReadTokens` a subset). It is recomputed from the raw token
  classes in each `results.json` by [`eng/extract-channels.py`](../eng/extract-channels.py); it
  discounts cheap prompt-cache reads so the exploration-heavy raw baseline is not over-counted.
  Tables also cite the harness's unweighted `tokenEstimate` (`tEst` = `inputTokens + outputTokens`)
  for traceability. Switching to weighted IET preserves every channel ordering but shrinks the
  raw-vs-MCP multiplier (Opus ~3.4×→~2×) and tightens C-vs-D to a tie on the strong tier.
- runs=3, judge=Haiku (quality scores used only for parity checks). Baseline arms are
  high-variance; the **robust signals** are call behavior (which doc), `taskCompleted`, and the
  cross-channel gap — not single-cell IET.
- Channel C on the multi-package task is omitted by design (fragile 3-cache injection at
  migration versions; the Markout anchor demonstrates C cleanly).
- The Markout package cache was mutated for the experiment (`AGENTS.md` injected/removed,
  README restored); reproducible via the runner.
