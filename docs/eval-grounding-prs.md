# Eval-based grounding content PRs — methodology, terms, and the ship gate

This is the standalone reference for proposing and reviewing a change to package **grounding
content** (an `AGENTS.md` that ships in a package root). It covers (1) the methodology, (2) the terms
we use or redefine, (3) the **threshold gate** that decides whether a change ships, and (4) the
copy-paste eval dump every PR carries.

Core rule: **a grounding change is a claim, and a claim ships with its evidence.** An `AGENTS.md` edit
without a reproducible eval behind it is not reviewable. This matches the rigor we hold code PRs to —
see [dotnet-inspect#1209](https://github.com/richlander/dotnet-inspect/pull/1209) for the shape we are
matching: a *Changes* summary, a `Baseline | … | Delta` table, a representative check, and an explicit
*Validation* block. Broader context:
[aspire#18437 (comment)](https://github.com/microsoft/aspire/pull/18437#issuecomment-4782880736).

---

## 1. Methodology

We reuse the [`dotnet/skills`](https://github.com/dotnet/skills) **skill-validator** harness to run a
**baseline vs. grounded** evaluation over a fixed set of scenarios (a "6-question unit", e.g. N1–N6
for NuGetFetch, M1–M6 for Markout). Each scenario runs three **arms**:

- **baseline** — no grounding; model knowledge only, web tools rejected.
- **skilled-isolated** — grounding delivered inline.
- **skilled-plugin** — grounding delivered as an auto-loaded plugin skill.

A pairwise LLM judge scores rubric quality; the harness also records tokens, cost, tool calls, and
assertion pass/fail. Mechanics live in [`harness.md`](./harness.md). We read results with
`eng/analyze-6q.py` (full table) or `eng/analyze-6q.py --card` (the PR dump, §4).

**Why grounded must be compared carefully.** The baseline is *not* a clean "model ignorance" control:
a package's `README.md`/`AGENTS.md` are packed inside its nupkg, so any `dotnet build` restores them
to `~/.nuget/packages`, where the web-blocked baseline can read them. So **the baseline is partly
self-grounded and the measured gap understates grounding's value** (see [harness.md](./harness.md) for
the per-arm read attribution and the empty-cache analysis). Treat every quality delta as a *lower
bound*.

---

## 2. Terms we use or redefine

| Term | Meaning here |
| --- | --- |
| **Grounding** | A compact, package-specific `AGENTS.md` that ships in the package root and makes the package self-teaching for an agent. Records only what the model is *proven to lack* — not model-resident knowledge. |
| **`AGENTS.md` vs `SKILL.md`** | `AGENTS.md` is the source of truth (it ships in the package). `SKILL.md` is generated from it by `eng/sync-skill.sh` purely so the harness can toggle grounding on/off. Never hand-edit `SKILL.md`. |
| **isolated / plugin** | The two grounded delivery channels the harness simulates. **plugin** (auto-loaded) is closest to a packed `AGENTS.md` that is always present, so the ship gate is evaluated on the **plugin** arm. |
| **qual** | Judge quality, rubric-weighted `overallScore` 1–5. A **value** metric. |
| **func** | Functional assertions passed (build + file + run-output regex). A **value** metric. The correctness/recovery analog. |
| **tok** | Gross tokens (`input + output`); `input` *includes* cache reads. Inflated by cache re-reads, so not the harm by itself. |
| **IET** | **Input-Equivalent Tokens** — cache-excluded effective tokens, `(input − cacheRead) + output`. Our headline cost stick (see `README.md`). `tok` and `iet` bracket the real spend; `cost` sits between. |
| **cost** | Premium-request multiplier (cache-discounted). The truest single harm proxy. |
| **output tok** | Output/thinking tokens. The most expensive, most variable component and the key **frontier-harm** signal (§3). |
| **Normative metric** | A quantity we *claim* as value or harm: `qual`, `func`, `tok`, `iet`, `cost`, `secs`. A conclusion may rest only on these. |
| **Informative signal** | Corroborating behavioral data that *explains* a metric move but is never the claim: `web`, `tools`, `turns`, `cache` (bash rummaging `~/.nuget/packages`), `di`, `mcp`, `bash`. A tool call adds nothing to the bill on its own; many signals together trace the narrative arc (archaeology, cache-reflection, retry loops). |
| **warm / cold cache** | Whether the package is restored on disk. For build-based scenarios the agent restores it within its first few tool calls, so **starting cache state is not a variable** — treat it as warm (see harness.md). |
| **verify-close** | A package-specific grounding line that makes the agent surface the final code/API calls, fixing a *verifiability artifact* where the judge underscores efficient grounded runs it can't see (see [nugetfetch report](./reports/nugetfetch.md)). |
| **Pareto gate / tier** | The ship rule (§3). **mini tier** (λ low — weaker/cheaper model that *needs* grounding): quality is the binding constraint, tokens are cheap → seek the **win** here. **frontier tier** (λ high — strong model that doesn't need it): quality is near ceiling → require **zero harm** here. |

---

## 3. The ship gate — win on the tier that needs it, zero harm on the tier that doesn't

Grounding is **auto-installed and un-removable**, so the verdict is a **Pareto gate**, not an average
(authoring-principles §4). Modeled directly on the decompiler quality-diff card — which **requires a
real win** (recovery up, malformed down) while holding harm rows (semantic defects, fidelity, pass
bugs) at **zero tolerance** — the grounding gate is two-sided:

> **Ship iff** the change **materially helps the tier that needs grounding (mini)** **AND does no
> meaningful harm to the tier that doesn't (frontier).** Rip it out iff it helps neither.

A complete decision therefore needs **two runs**: a mini-tier run (default `claude-haiku-4.5`) for the
win, and a frontier-tier run (e.g. `claude-opus-*`) for the no-harm check. Each is n ≥ 3, model and
judge named. The gate is evaluated on the **plugin** arm vs **baseline**, on means across the unit's
scenarios.

### Mini tier — the WIN (thresholds)

Correctness may never regress; then at least one win axis must clear:

| Axis | Type | Threshold |
| --- | --- | --- |
| `func` | guard (hard) | Δ ≥ 0 — no functional regression |
| `qual` | guard (hard) | Δ ≥ −0.1 — no regression beyond n=3 noise |
| `web` | guard (hard) | grounded web calls = 0 (no `reject_tools` breach) |
| `iet` | **win** | ≥ 25% reduction vs baseline, **or** |
| `cost` | **win** | ≥ 25% reduction vs baseline, **or** |
| `qual` | **win** | Δ ≥ +0.3 lift vs baseline |

On the mini tier a large cost/IET reduction with quality flat (within the −0.1 band) **is** a
legitimate ship — tokens are cheap, the binding constraint was the model otherwise flailing or
failing.

### Frontier tier — the HARM (zero tolerance)

The strong model rarely *needs* grounding, so this run does not need a win — it must prove grounding
**does no damage**. This is the direct analog of "no drop in recovery, no increase in malformed":

| Axis | Threshold |
| --- | --- |
| `func` | Δ ≥ 0 — no correctness/recovery drop |
| `qual` | Δ ≥ −0.1 — no quality regression |
| **`output tok`** | Δ ≤ +5% — **no output/thinking-token inflation** (the frontier spend harm) |
| `web` | grounded web calls = 0 |

The case the gate exists to catch: grounding that *adds* output/thinking tokens for only a *modest*
quality change on the frontier — pure harm to the tier that didn't need it. A small quality gain
bought with significant output tokens is a **fail** on the frontier.

> These thresholds are the team's starting line (haiku/opus tiers, n=3). They are tunable in one place
> — `GATE` in `eng/analyze-6q.py` — and the analyzer applies them automatically per `--card`.

---

## 4. The eval dump (copy-paste into the PR)

Generate the dump from the committed dataset:

```bash
python3 eng/analyze-6q.py --card data/<unit>-6q/<unit>.n3.haiku.json     # mini-tier WIN card
python3 eng/analyze-6q.py --card data/<unit>-6q/<unit>.n3.opus.json      # frontier-tier HARM card
```

`--card` prints a self-contained Markdown block — the metrics table, the tier-appropriate gate with
each threshold PASS/FAIL, and the lower-bound caveat — **paste it verbatim** into the PR's *Metrics*
section. It detects the tier from the model name and evaluates the correct gate. Example (NuGetFetch,
mini tier):

```
| Metric | Baseline | Isolated | Plugin |
| --- | ---: | ---: | ---: |
| quality (1–5) | 4.40 | 4.38 | 4.67 |
| func passed | 17/18 | 18/18 | 18/18 |
| IET (mean) | 30189 | 20980 | 17220 |
| output tok (mean) | 6246 | 1518 | 1604 |
| cost (mean) | 7.77 | 1.83 | 2.08 |
| web calls | 6 | 0 | 0 |

**Mini WIN gate (plugin vs baseline): ✅ PASS**
- PASS  func no regression (Δ +1)
- PASS  quality no regression (Δ +0.27; floor −0.1)
- WIN   IET reduction 43% (bar 25%)
- WIN   cost reduction 73% (bar 25%)
```

---

## 5. What a grounding PR contains

| Artifact | Path |
| --- | --- |
| The grounding edit (body ≤ `eng/agents-line-limit.txt`, currently 60) | `grounding/<unit>/AGENTS.md` |
| Regenerated wrapper, in sync (`sync-skill.sh --check`) | `grounding/<unit>/SKILL.md` |
| The matched n≥3 mini-tier dataset | `data/<unit>-6q/<unit>.n3.haiku.json` |
| The matched n≥3 frontier-tier dataset (no-harm check) | `data/<unit>-6q/<unit>.n3.opus.json` |
| The report | `docs/reports/<unit>.md` |

### PR description format
Use `.github/PULL_REQUEST_TEMPLATE.md`. Required sections: **Changes** (what changed and *why it is
package-specific knowledge*, pointing at the package's actual trap), **Metrics** (paste both `--card`
dumps), **Representative check** (one concrete before/after: the wrong API the ungrounded agent reaches
for vs. what grounding makes it do), **Validation** (the exact commands), **Caveats** (the cache
self-grounding lower-bound + cache-state-not-a-variable).

### Validation (reproducible)

```bash
eng/sync-skill.sh --check
RUNS=3 eng/run-<unit>-6q.sh                                    # -> data/<unit>-6q/<unit>.haiku.json
RUNS=3 MODELS=claude-opus-4.8 eng/run-<unit>-6q.sh            # frontier no-harm run
python3 eng/analyze-6q.py --card data/<unit>-6q/<unit>.haiku.json
cp data/<unit>-6q/<unit>.haiku.json data/<unit>-6q/<unit>.n3.haiku.json   # commit the matched run
```

---

## 6. Reviewer checklist

- [ ] `AGENTS.md` within the line limit; `eng/sync-skill.sh --check` passes.
- [ ] Datasets committed under `data/<unit>-6q/`; both `--card` dumps in the PR match them.
- [ ] n ≥ 3; model and judge named, for **both** tiers.
- [ ] **mini WIN** gate passes (a real cost/IET or quality win; no func/quality/web regression).
- [ ] **frontier NO-HARM** gate passes (zero output-token inflation; no quality/func regression).
- [ ] Claims cite normative metrics; signals only explain.
- [ ] Grounding text is package-specific, justified by the package's actual trap.
- [ ] Required caveats present; cache reads attributed per arm (not grepped).
- [ ] `docs/reports/<unit>.md` updated.
