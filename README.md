# dotnet-package-grounding

A repo dedicated to **NuGet package grounding** — `AGENTS.md` files that ship in a package's
root and make the package *self-teaching* for an AI agent — and to **measuring** whether that
content actually helps.

Use it two ways:

- **As a how-to.** Practical instruction for authoring package grounding — what to write, what to
  leave out, and how to validate it — grounded in worked examples for real packages.
- **As a record of our approach.** These patterns form our approach to **context engineering** —
  which we mean concretely as *what to add to an agent's context, and how to limit it*: which
  delivery channel surfaces grounding, how agents retrieve it, when it helps versus hurts, and the
  evidence behind each call.

It reuses the [`dotnet/skills`](https://github.com/dotnet/skills) **skill-validator** harness to
run a **baseline vs enriched** evaluation: the agent attempts each task once *without* the
grounding and once *with* it, and the harness compares accuracy, token usage, and tool calls
using pairwise LLM judging. The harness mechanics live in [`docs/harness.md`](docs/harness.md);
this page is about the *concept* and the *findings*.

## What "grounding" is — and what it is not

Several different files get called `AGENTS.md` or `SKILL.md`. They live in different places,
serve different audiences, and should not be confused. This repo is about exactly one of them —
the first row.

These definitions are up for debate and may differ by domain or community. We define them a
particular way here for the purposes of measurement and guidance for the package-grounding
feature.

| Artifact | Where it lives | Who consumes it | Purpose | In this repo? |
|----------|----------------|-----------------|---------|---------------|
| **Package grounding** — `AGENTS.md` in the package | the **NuGet package** root (`.nupkg`), shipped by the package author | an AI agent working in a *consumer's* project that depends on the package, **surfaced on demand by the NuGet MCP** (`get_package_context`) | the model may already know the package's common, everyday usage — we *measure* what's resident rather than assume it; we **target the footguns** — the non-obvious gotchas it is *proven to lack* — as the highest-value focus (anti-flailing), so it avoids latent bugs against *that dependency* | **Yes — the artifact under test** (`grounding/<slug>/AGENTS.md`) |
| **Repo `AGENTS.md`** | the root of a **source repository** | an AI coding agent working *inside that repo* | repo-wide conventions, build/test commands, house style for *this codebase* | No — different concern (a repo may have one for its own contributors) |
| **Harness `SKILL.md`** | `grounding/<slug>/SKILL.md`, **generated** | the `skill-validator` harness, locally | a *togglable* skill the harness adds/removes between the baseline and enriched arms — an **implementation detail of measurement** | Yes, but generated/internal — see [`docs/harness.md`](docs/harness.md) |
| **Marketplace `SKILL.md`** | published as a `plugin.json` plugin in a **skills marketplace** (the `dotnet/skills` distribution model) | an agent *host* that installs marketplace plugins globally | a distributable, installable capability/instruction set | **No — explicitly out of scope.** This repo has no `plugin.json` marketplace machinery |

The distinction that matters: **package grounding travels *with the dependency* and is surfaced
per-package, on demand**, when an agent touches a project that references it. A repo `AGENTS.md`
is scoped to one working repository; a marketplace skill is installed globally into the host; the
harness `SKILL.md` here is only a test toggle. Everything below is about package grounding.

## Grounding vs. skills: our policy

Skills and grounding both reduce to *context* for an agent, so they are easy to conflate. Our
policy is that the **axis that separates them is installation, not content**:

- **A skill is *pulled*.** It is **opt-in and user-visible**: the user installs it (the
  `marketplace.json` + `plugin.json` dance, or makes it repo-resident) and can **remove it** if it
  misbehaves. Pull works because the *need is visible* — the agent recognizes "this is a
  NuGet-publishing task" and loads the publishing skill. Skills are typically *procedures* /
  multi-component workflows ("how *we* do CI here").
- **Grounding is *pushed*.** It rides along **with the package** the consumer already depends on,
  is surfaced automatically by the NuGet MCP, and is **invisible to the user** — it never appears
  in any install list or toggle. Its highest-value content is
  the *silent* gaps the agent doesn't know it has — which is exactly why pull is self-defeating
  for it (the agent has no trigger to go fetch it) and why it must be pushed.

The consequence — and the reason grounding gets a **stricter, differently-shaped discipline** than
a skill: **grounding can't be turned off the way a skill can.** A user opts into the *delivery
channel* (the NuGet MCP / `dotnet-inspect`), but not into any *individual package's* grounding —
it cannot be uninstalled short of dropping a dependency they need. Because it is pushed to everyone
and un-removable, two rules follow (full treatment in
[`docs/authoring-principles.md`](docs/authoring-principles.md)):

1. **Stay in your lane.** Assert only **first-party, package-local facts** — your overloads, your
   footguns, your beta→stable renames. The moment a doc describes a workflow *across* components,
   it has become a skill and left its lane. This bounds the blast radius: a doc that only ever
   names its own package cannot mislead about one it never mentions.
2. **Pass a Pareto gate, not an average.** Ship grounding iff it **materially helps the tier that
   needs it** (often a weaker model) **and does no meaningful harm to the tier that doesn't** (the
   frontier). What you can't opt out of must be judged on its worst case across the fleet, not its
   mean.

## Start here: the recommendation

**[`docs/recommendation.md`](docs/recommendation.md)** — the executive summary for the NuGet
v-team. It answers two team decisions — **(1) should we author grounding content for packages?**
and **(2) should the NuGet MCP change?** — backing each with one progression (raw package → NuGet
MCP → shipped `AGENTS.md` → resident-index MCP) measured across **2 real tasks × 5 delivery
channels × 2 model tiers**, with raw data in [`data/`](data/) and worked `AGENTS.md` examples for
four real packages. The supporting deep-dives are
[`docs/authoring-principles.md`](docs/authoring-principles.md) (*what* to write),
[`docs/delivery-and-retrieval.md`](docs/delivery-and-retrieval.md) (*how* it reaches the agent),
and the per-package reports in [`docs/reports/`](docs/reports/).

## How a grounding doc is written

A grounding doc records **only what an agent is proven to lack** (by eval signal) and is written
for **section-based RAG retrieval**, not top-to-bottom reading — unlike a README. It must stay
**concise** (the harness enforces a per-file line budget). See
[`docs/authoring-principles.md`](docs/authoring-principles.md) for the principles and the
empirical evidence behind them.

The per-package reports under [`docs/reports/`](docs/reports/) are writeups suitable for an
upstream PR:

- [System.CommandLine](docs/reports/system-commandline.md) — needs grounding for a narrow set of
  topics.
- [System.Text.Json](docs/reports/system-text-json.md) — does **not** need general grounding.
- [Microsoft.Extensions.AI](docs/reports/microsoft-extensions-ai.md) — grounding need is
  **model-relative**: its headline gotcha is resident for Opus 4.6 (−1.0%) but a +63.3% rescue
  for Haiku 4.5.
- [Markout](docs/reports/markout.md) — a genuinely non-resident package whose grounding competes
  with the package's own README: do-no-harm + ~3× token efficiency at the frontier, and a
  fail→pass correctness rescue at the weak tier.
- [README liability](docs/reports/readme-liability.md) — a README-size sweep showing a lean
  ~3.5 KB targeted doc is **size-invariant** and beats a README of any realistic size (3–74 KB)
  by **~2–3×** (weighted IET), while README reliance is a high-variance, high-ceiling regime — so
  the lever is **completeness + targeting**, not a size ratio.

## Running the evals

The harness mechanics — building `skill-validator` from a pinned `dotnet/skills` SHA, the
`grounding/` + `tests/` layout, the `AGENTS.md` → `SKILL.md` generation step, and how to add a
package — are documented in **[`docs/harness.md`](docs/harness.md)**. Quick start:

```bash
# Prereq: a .NET SDK matching dotnet/skills' global.json, git, and `gh auth login`.
eng/sync-skill.sh                  # regenerate SKILL.md from AGENTS.md
eng/run-evals.sh System.CommandLine
```
