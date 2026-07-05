# dotnet-package-grounding

A repo dedicated to **NuGet package grounding** — `AGENTS.md` files that ship in a package's
root and make the package *self-teaching* for an AI agent — and to **measuring** whether that
content actually helps.

**Why this is needed.** When an agent touches a package today, the best it gets is the package's
README — and **often not even that**: across the **top 1,000** Microsoft/Azure/System packages,
**62% ship no in-package README at all** (and ~50% of the top 1,000 community packages don't either);
where one exists it is small — a median of **~2–3 kB** — with a long tail to **44–94 kB**
([top-1,000 README survey](docs/reports/readme-size-survey-top1000.md); earlier
[top-40 study](docs/reports/readme-size-survey.md)).
That prose is
written to **onboard a human browsing nuget.org**: install steps, prerequisites, "key concepts,"
contributing boilerplate (2–4 kB on its own in the Azure-SDK template), and broad usage examples —
*most of which the model already knows*. Installation is the clearest case of waste: by the time an
agent is working in a project that *references* the package, installation is **solved by
definition** — the dependency is already there. Little of a README is the non-obvious, version-
specific gotcha an agent actually needs. Grounding inverts that ratio: a small, targeted doc
(~3.5 kB) carrying only what the model is proven to lack.

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
this page is about the *concept* and the *findings*. How we evaluate a grounding change and decide
whether it ships — the methodology, terms, threshold gate, and evidence dump — is the
[grounding eval methodology](docs/grounding-eval-methodology.md).

## The three documents and three tiers

A package can carry up to three grounding documents — each for a different reader and delivery —
and the eval runs three nested task tiers. See **[docs/overview.md](docs/overview.md)** for the full
model; the salient shape:

| Document | Nickname | Reader | Delivery | Job |
| --- | --- | --- | --- | --- |
| `README.md` / `PACKAGE.md` | **Brochure** | humans | pulled (you open it) | introduce, market, onboard |
| `AGENTS.md` | **Missing Manual** | models | co-located, always-on | fill the model's gaps; terse |
| `SKILL.md` | **Complete Textbook** | models (opt-in) | opted-in repo asset — *not* packed | complete instruction; the eval **ceiling** |

Three nested task tiers — **Core-6 ⊂ MM-12 ⊂ CT-24** — grow from day-1 common tasks to niche
day-100 ones. We run head-to-head per tier (baseline vs each document, on a mini *and* a frontier
model) and read the **eval diff across the documents**. That triangulation is the payoff: it shows
where each document earns its keep, whether `AGENTS.md` generalizes up the ladder, and whether
[Pareto](https://en.wikipedia.org/wiki/Pareto_efficiency) holds across model tiers — while paying
eval dividends for all three. `SKILL.md` isn't shipped in the package; it serves as the
completeness ceiling every question should clear.

## How we measure cost: IET

Our measuring stick is **IET — Input-Equivalent Tokens**, a single cost-equivalent number that
normalizes each token class to fresh-input units:

```
IET = fresh + 0.1·cacheRead + 1.25·cacheWrite + 5·output
```

where `fresh = inputTokens − cacheReadTokens` is Anthropic's **Base Input Tokens** at 1× (the
SDK's `inputTokens` is cache-read-inclusive, verified against every dataset — cacheRead never
exceeds it). The formula maps 1:1 onto Anthropic's four billed categories (Base / cache read /
cache write / output). This **diverges from the `dotnet/skills` harness
metric**, which reports an unweighted `tokenEstimate = inputTokens + outputTokens` (cache reads
counted at full price, output counted the same as input). We diverged because that estimate
inflates the exploration-heavy raw baseline — the channel that does the most cheap prompt-cache
reads looks the most expensive — and undercounts output, which is the dominant real cost. The
weights are **Anthropic's published pricing multipliers**
([price sheet](https://platform.claude.com/docs/en/about-claude/pricing)): cache read **0.1×**
base input, 5-min cache write **1.25×**, and **output 5×** — uniform and exact across current Claude
models (Opus 4.8 $5→$25, Sonnet $3→$15, Haiku 4.5 $1→$5; see also
[dotnet/sdk#54417](https://github.com/dotnet/sdk/issues/54417)). So the
weights also expose the **arbitrage** between classes — spending cheap cached input to avoid
expensive output is a win the unweighted metric can't see. Cross-channel comparisons then reflect
spend rather than cache-read volume. Tables still cite the harness's raw `tokenEstimate` (`tEst`)
in parentheses for traceability. Full derivation:
[`docs/recommendation.md`](docs/recommendation.md) (Metric) and
[`docs/delivery-and-retrieval.md`](docs/delivery-and-retrieval.md).

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

> *Pareto* here is the economist's sense: a change is a
> [**Pareto improvement**](https://en.wikipedia.org/wiki/Pareto_efficiency) only if it makes at
> least one party better off and **no party worse off**. Applied to grounding, the "parties" are the
> model tiers — a doc earns its place only if it lifts the tier that needs it without dragging down
> the tier that doesn't.

## What we found

We authored and measured grounding for four real packages — **System.CommandLine**,
**System.Text.Json**, **Microsoft.Extensions.AI**, and **Markout** — across two tasks. The
recognizable packages double as the readable examples; **Markout is a deliberate control** — an
obscure, source-generated serializer the models genuinely *don't* know, which is exactly why it
gives a clean grounding signal (a model-resident package like System.Text.Json would mask it). The
headline results are in weighted [IET](#how-we-measure-cost-iet) (`tEst` = unweighted harness
estimate, shown for traceability); full tables, method, and caveats are in
[`docs/recommendation.md`](docs/recommendation.md).

1. **On a real migration, grounding cuts cost the most.** The flagship task is a **System.CommandLine
   `beta4` → 3.x migration** (the agent must build, localize the breakage, and migrate) with two
   distractor packages. runs=3:

   | Channel | what the agent gets | Opus IET | Haiku IET |
   |---------|---------------------|---------:|----------:|
   | **A** raw package, no MCP | finds + reads the **README** | 188k | 939k |
   | **B** NuGet MCP, no `AGENTS.md` | server returns the **README** | 138k | 665k |
   | **D** MCP + resident index | curated grounding, self-gated | **92k** | **286k** |

   That's **−51% (Opus)** and **−70% (Haiku)**. The raw baseline *thrashes* — Haiku burns 99 tool
   calls — and the resident-index channel is also the **only** one that surfaces silent, compile-clean
   gotchas the agent wouldn't know to ask for.

   *Is this a one-off, or repeatable?* Repeatable — because of **where the value sits**. We measured
   five scenario shapes for System.CommandLine ([report](docs/reports/system-commandline.md)) and the
   pattern was consistent: general API shape, greenfield authoring, idiomatic usage, and the
   command-line-parser domain itself are **already in the model** — grounding moves the needle −2% to
   +1% there (i.e. not at all). The signal concentrates almost entirely in the **non-resident delta**:
   version-specific breakages and *silent* gotchas (the canonical one being the `Option<T>` constructor
   whose second argument flipped from *description* to *alias* between beta and GA — code that compiles
   and looks right but behaves wrong). That is the general shape of grounding's value: the model carries
   the bulk; grounding carries the **footguns the model can't recover by compiling**.

   This shapes *who* writes grounding and *how*. Because the payload is the non-obvious delta, you
   can't auto-generate it from the public API surface — it's a combination of **expert view** (a
   maintainer's judgment of what actually trips people up) and **hard-won experience** (the silent
   gotchas surfaced by real bug reports and migrations). The role of our harness is to **keep that
   instinct honest** — measuring each candidate fact so only the lines that change agent behavior
   ship, and the merely-nice-to-know ones don't.

   There is a **circularity** to watch for when *generating* grounding. You can't use Opus 4.8 to
   author `AGENTS.md` *for* Opus 4.8 — if Opus can produce the fact on request, it already knows it,
   so the act of writing it down only proves it's redundant. The productive direction is
   **asymmetric**: use the strong model to author grounding for the *weaker* ones (Sonnet, and
   especially Haiku). That is essentially **distillation** — the frontier model's resident knowledge
   becomes the weak model's shipped context — and it almost certainly helps the tier that needs it.
   The constraint is the other half of the asymmetry: **don't harm the strong tier in the process.**
   Opus tokens are far more expensive, so grounding that bloats or misleads the frontier to rescue
   the weak tier is a bad trade. Help the weak, no harm to the strong — the
   [Pareto gate](#grounding-vs-skills-our-policy), and the asymmetry, restated as a generation
   recipe.

2. **The clean mechanism, isolated.** On the controlled Markout probe we can run all five delivery
   channels (same task, same content, varying only delivery). runs=3:

   | Channel | what the agent gets | Opus IET | Haiku IET |
   |---------|---------------------|---------:|----------:|
   | **A** raw package, no MCP | finds + reads the **README** | 78k | 49k |
   | **A′** raw package, `AGENTS.md` present | *still reads the README* — `AGENTS.md` is **invisible** | 124k | 62k |
   | **B** NuGet MCP, no `AGENTS.md` | server returns the **README** | 38k | 40k |
   | **C** NuGet MCP, `AGENTS.md` present | server returns the **`AGENTS.md`** | **28k** | 39k |
   | **D** MCP + resident index | curated grounding, self-gated | 31k | **31k** |

3. **Content alone is worthless without a delivery channel.** Channel A′ — `AGENTS.md` shipped but
   no MCP — is the *most* expensive cell on both tiers: the agent never sees it and reads the README
   anyway. Writing grounding only pays off when the MCP delivers it.

4. **The README is a measurable liability, and targeted value is size-invariant.** Sweeping the
   shipped README from 3 KB → 74 KB (24×) while holding `AGENTS.md` at 3.5 KB: the README path
   tracks its own bloat (72k–117k IET, high-variance), while the `AGENTS.md` path stays **flat at
   ~36–42k IET / 9–11 tools** — a **48–69% saving** that *widens* as the README grows. Full sweep:
   [`docs/reports/readme-liability.md`](docs/reports/readme-liability.md). And the liability isn't
   hypothetical: a [download-ranked survey of popular packages](docs/reports/readme-size-survey.md)
   (top 40 Microsoft-owned + top 40 community) finds a median in-package README of ~4–5 kB with a
   real tail to ~26 kB (Azure.Identity, OpenTelemetry.Api) — and **~25–30% that ship no README at
   all**, net-new ground for targeted grounding.

5. **For weak models it's correctness, not just cost.** The README-without-MCP path *fails* the weak
   tier; the delivered `AGENTS.md` flips it to a pass. Grounding rescues the tier that needs it
   while costing the frontier tier nothing — the Pareto gate above, met in the data.

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
for the **section-based RAG retrieval** paradigm, not top-to-bottom reading — unlike a README. It must stay
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
`grounding/` + `tests/` layout, and how to add a package — are documented in
**[`docs/harness.md`](docs/harness.md)**. Quick start:

```bash
# Prereq: a .NET SDK matching dotnet/skills' global.json, git, and `gh auth login`.
grounding check-agents                # validate every AGENTS.md is within the line budget
eng/run-evals.sh System.CommandLine
```
