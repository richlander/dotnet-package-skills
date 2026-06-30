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
**baseline vs. grounded** evaluation over a fixed set of scenarios. We name the arms by the **grounding
content** supplied — and force-feed it — *not* by skill-validator's internal delivery mechanism:

| Our arm | Content (force-fed) | What it answers |
| --- | --- | --- |
| **baseline** | none | the control — model knowledge only (web blocked) |
| **Missing Manual** | `AGENTS.md` | does the compact, co-located doc fill the gap? |
| **Front Door** | `README.md` | is the human README usable by an agent? |
| **Textbook** | `SKILL.md` | (rung 2) does the full guide recover what the compact doc can't? |

**Why these names (and not skill-validator's).** skill-validator's own arms are named by *loader scope* —
`baseline` (nothing loaded), `isolated` (only the target skill loaded), `plugin` (everything loaded, the
agent self-selects). Those are **internal-facing `dotnet/skills` mechanism names** that conflate *content*
with *delivery* (`plugin` moves both at once), so we replace them with **outward-facing, grounding-specific**
names keyed to the document under test. Mechanically each content arm *is* skill-validator's `isolated`
mode fed a different document (`grounding run --source agents|readme|…`); we read that arm and **relabel it
by content**. skill-validator's `plugin` arm (everything loaded, the agent self-selects — the "shelf") is a
**separate delivery axis**, not a content arm, and is omitted from content cards.

A pairwise LLM judge scores rubric quality; the harness also records tokens, cost, tool calls, and
assertion pass/fail. Mechanics live in [`harness.md`](./harness.md). We read results with
`grounding analyze` (full table) or `grounding analyze --card` (the PR dump — see [scoring.md](./scoring.md)).

Because the judge is **pairwise**, **baseline is the anchor**: each content arm's quality is rendered
*relative to* baseline (`baseline.judgeResult.overallScore`). That is why skill-validator's own
`summary.md` shows the grounded arms' quality as a **delta over baseline** rather than a standalone
"Quality (Baseline)" column — baseline is the zero-point, not omitted (its score is in `results.json` and
in the terminal table). `grounding analyze` prints **baseline quality as its own column** when you want the
absolute view.

**Two confounds keep the baseline from being a clean control.** First, a package's `README.md`/`AGENTS.md`
are packed inside its nupkg, so any `dotnet build` restores them to `~/.nuget/packages`, where the baseline
can read them (archaeology) — so **the baseline is partly self-grounded and the measured gap understates
grounding's value** (treat every delta as a *lower bound*; see [harness.md](./harness.md)). Second, to keep
**content** arms about the *document* and not a tool, eval runs **scrub `~/.dotnet/tools` from the agent's
PATH** (removing `dotnet-inspect`/`ildasm`/`ilspycmd`, keeping system `dotnet`/`dnx`). Tool availability is
a **separate lever**, layered in deliberately as its own arm — not part of the content comparison.

### Three question tiers, the arms that run them, and a cost-tiered ladder

Three **nested** question tiers, named for the doc that should clear each (`Mini ⊂ MM ⊂ CT`):

| Tier | Size | Role |
| --- | --- | --- |
| **Mini** | **6** | smoke test — fast sanity for any doc |
| **MM** (Missing Manual) | **12** (6 + 6) | what `AGENTS.md` should clear — and, being broader than the smoke 6, the **overfit guard** |
| **CT** (Complete Textbook) | **24** (12 + 12) | the full exam; the top 12 are **`SKILL.md` territory** |

A **cost-tiered, opt-in ladder** — run as much as the question warrants:

| Rung | Arms | Tier | Question |
| --- | --- | --- | --- |
| **0 — smoke** | baseline vs Missing Manual | Mini-6 | does grounding beat none, quickly? |
| **1 — remit** | baseline · Front Door · Missing Manual | MM-12 | does `AGENTS.md` clear its remit without overfitting; is the README usable? |
| **2 — full** (optional) | baseline · Missing Manual · Front Door · **Textbook** | CT-24 | where does `AGENTS.md` fall off, and **what do `SKILL.md`'s extra tokens buy** (`AGENTS.md`@24 vs `SKILL.md`@24)? |

**Mini** is the cheap smoke; **MM-12** is the overfit guard (a compact doc tuned to the smoke 6 must still
generalize to 12); **CT-24** runs the real `SKILL.md` (so it **doubles as a skill eval**) and, by also
running `AGENTS.md` across all 24, measures the Textbook's marginal value. The **Front Door** arm *is* the
README usability test (it replaces the older "source-diff card"): if the README-grounded agent fails a
question or is forced into archaeology, that is a **README bug to fix in the same PR** — *if an AI given
only the README can't answer it, an untrained human can't either.*

> **Sizing — double is the north star.** 6 / 12 / 24 is the **starting** size, chosen because eval is
> expensive. The aspirational target is **double** — 12 / 24 / 48 — for sharper overfit and falloff
> signal, but we **defer it until more packages, users, and feedback motivate the cost**. Start small;
> scale the tiers when the evidence demands it.

---

## 2. Terms we use or redefine

| Term | Meaning here |
| --- | --- |
| **Grounding** | A compact, package-specific `AGENTS.md` that ships in the package root and makes the package self-teaching for an agent. Records only what the model is *proven to lack* — not model-resident knowledge. |
| **The three docs** | A package may ship three grounding documents: `README.md` = **Front Door** (humans; may market/onboard), `AGENTS.md` = **Missing Manual** (co-located, always-on RAG gap-filler), `SKILL.md` = **Complete Textbook** (opt-in, narrative full guide). See [authoring-principles §2d](./authoring-principles.md). The harness *also* generates a per-unit `SKILL.md` **skill-wrapper** — test scaffolding holding whichever content an arm force-feeds (skill-validator just requires that filename) — which is **not** the shipped Textbook. Never hand-edit the generated wrapper; edit `AGENTS.md`, then `grounding sync-skill`. |
| **arm names (content, not mechanism)** | Arms are named by the document supplied — **baseline** / **Missing Manual** (`AGENTS.md`) / **Front Door** (`README.md`) / **Textbook** (`SKILL.md`), all force-fed. We do **not** use skill-validator's loader-scope names `isolated`/`plugin` (§1 explains why and gives the mapping). `plugin` (everything loaded, agent self-selects — the "shelf") is a separate **delivery** axis, omitted from content cards. |
| **resourcefulness (archaeology)** | Out-of-sandbox lookups the agent must make to recover API knowledge that grounding would supply inline — web fetch/search **plus** local NuGet-cache rummaging / decompiling DLLs. Measured **objectively** from the timeline (not the judge). **High = the agent had to be resourceful; grounding's job is to drive it to 0, so lower is the win, not a loss.** Cards show it as one **resourcefulness (archaeology)** row; the **web** portion alone is a hard gate guard (a grounded run must never resort to the internet). |
| **success** | A scenario is **solved** for an arm iff every functional assertion passes **and** the judge's overall quality clears the **≥4 floor** ("meets expectations"). Reported per arm as a rate (e.g. `6/6`). The headline **value** metric. The judge's 1–5 score enters **only** as this pass/fail floor — its subjective 4→5 top band is discarded (see [scoring.md](./scoring.md)). |
| **quality** (judge `overallScore` 1–5) | Used **only** as the ≥4 success floor above — never as a reported metric or a baseline diff. Its top ~1 point is subjective and instruction-sensitive (see [scoring.md](./scoring.md)), so it cannot grade harm. |
| **func** | Functional assertions passed (build + file + run-output regex). A **value** metric; the objective correctness signal that, with the ≥4 floor, defines `success`. |
| **tok** | Gross tokens (`input + output`); `input` *includes* cache reads. Inflated by cache re-reads, so not the harm by itself. |
| **IET** | **Input-Equivalent Tokens** — cache-excluded effective tokens, `(input − cacheRead) + output`. Our headline cost stick (see `README.md`) and the **frontier harm number** (see [scoring.md](./scoring.md)). Empirically **input-dominated**: output is only ~7–21% of IET, non-cached input ~79–93%, so IET mainly tracks context/read bloat (the likeliest grounding harm). `tok` and `iet` bracket the real spend; `cost` sits between. |
| **cost** | Premium-request multiplier (cache-discounted). The truest single harm proxy. |
| **output tok** | Output/thinking tokens. The most expensive *per-token* and most variable component. A small share of IET, so kept as its **own visible guard row** (see [scoring.md](./scoring.md)) lest an output-only blow-up be masked when input nets down. |
| **Normative metric** | A quantity we *claim* as value or harm: `success`, `func`, `resourcefulness`, `tok`, `iet`, `cost`, `secs`. A conclusion may rest only on these. |
| **Informative signal** | Corroborating behavioral data that *explains* a metric move but is never the claim: `web`, `tools`, `turns`, `cache` (bash rummaging `~/.nuget/packages`), `di`, `mcp`, `bash`. A tool call adds nothing to the bill on its own; many signals together trace the narrative arc (archaeology, cache-reflection, retry loops). |
| **warm / cold cache** | Whether the package is restored on disk. For build-based scenarios the agent restores it within its first few tool calls, so **starting cache state is not a variable** — treat it as warm (see harness.md). |
| **verify-close** | A package-specific grounding line that makes the agent surface the final code/API calls, fixing a *verifiability artifact* where the judge underscores efficient grounded runs it can't see (see [nugetfetch report](./reports/nugetfetch.md)). |
| **Pareto gate / tier** | The ship rule (see [scoring.md](./scoring.md)). **mini tier** (λ low — weaker/cheaper model that *needs* grounding): **success** is the binding constraint, tokens are cheap → require the card to grade **BETTER** here. **frontier tier** (λ high — strong model that doesn't need it): success is near ceiling → require **not WORSE** (BETTER or NEUTRAL) here. |

---

## Scoring, grading, and shipping

The grade model (**BETTER / NEUTRAL / WORSE**), the tier-aware **ship gate** (require BETTER on the mini
tier, *not WORSE* on the frontier tier), the copy-paste **cards**, the **PR contents + checklist**, and
the judge-floor finding live in **[`scoring.md`](./scoring.md)**.
