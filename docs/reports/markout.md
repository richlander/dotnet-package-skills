# Does Markout need agent grounding? A measured answer — and what the README competes with

**Package:** `Markout` (0.13.6)
**Date:** 2026-06-20
**Status:** First measurement on a *genuinely non-resident* package (unlike System.CommandLine,
System.Text.Json, and Microsoft.Extensions.AI, whose headline gotchas turned out to be largely
model-resident). The result is the cleanest tier-conditioned signal yet — and it reframes what
package grounding actually competes with.

## Why Markout is the right test

Markout is a niche, source-generated .NET serializer ("created for dotnet-inspect", v0.13.x) with
effectively **zero presence in model training data**. It is also a **System.Text.Json look-alike**
— `[MarkoutSerializable]` ≈ `[JsonSerializable]`, `MarkoutSerializerContext` ≈ `JsonSerializerContext`,
a `[MarkoutContext(typeof(T))]` partial context — which actively *misleads*: every one of the 12
`MarkoutSerializer.Serialize` overloads requires a context, and there is **no reflection fallback**
(no `Serialize(obj)`). A model running on intuition writes a context-less or `Json*`-style API that
does not compile. This is the package an agent *cannot* fake.

## What we measured

Scenario **M1** (`tests/markout`): a console project that references Markout and holds scan data in
plain objects. The task: render a Markdown report with an H1 title (via `TitleProperty`), a field
table, and a `## Advisories` section table — produced **through the serializer**, not hand-written.
Web tools rejected; the run must build and print the exact report (deterministic behavior gate).
Agent arms: baseline (no grounding), skilled-isolated, skilled-plugin. Judge: Opus 4.6.

## Result (runs=3; directional)

| Tier | Baseline | Grounded (best arm) | Reading |
| --- | --- | --- | --- |
| **Opus 4.6** (frontier) | **PASS** — 15 tools, ~274k tok | **PASS** — 7–8 tools, ~89–99k tok | do-no-harm + **~3× token / ~½ tool-call efficiency** |
| **Haiku 4.5** (weak) | **FAIL** (`taskCompleted=false`) — tried blocked `web_fetch`, ~163k tok | **PASS** — plugin 7 tools, ~101k tok | **correctness rescue** |

Quality was judged a **tie** at both tiers among produced outputs (parity → do-no-harm holds). The
harness's quality-weighted improvement score therefore reads ~0/slightly-negative (Opus iso −0.09 /
plugin +0.07; Haiku iso −0.06 / plugin +0.21, varianceCV 1.7). Per the methodology lesson
([metrics vs. signals](../harness.md#metrics-vs-signals-what-a-claim-may-rest-on)), the robust
**normative metric** here is `taskCompleted` (correctness) plus token/cost spend; **call behavior**
is the corroborating *signal* that explains the spend, not a claim in itself — and either is more
robust than the run-to-run quality delta at n=3.

> **Caveat — the baseline is partly self-grounded from the NuGet cache.** `Markout 0.13.8` packs both
> `README.md` and `AGENTS.md` **inside its nupkg**, so `dotnet build` restores them into
> `~/.nuget/packages`. The web-blocked baseline **read them from disk** — attributing sessions to arms
> via `sessions.db`, the baseline arm made **28 successful reads** of the cached `README.md`/`AGENTS.md`
> across its 18 sessions, while the grounded arms read the cache **0×** (they get `AGENTS.md` from the
> skill). So the baseline is not truly ungrounded, and the gap reported here **understates** grounding's
> value. See
> [the cache-contamination confound](../harness.md#a-confound-the-baseline-can-read-the-package-from-the-nuget-cache).
>
> Because the docs ride **inside the nupkg**, every restore extracts them (verified: a `dotnet restore`
> into an empty cache re-materializes both files). And every Markout scenario asserts `dotnet build`. So
> a build-based scenario can **never** give a truly cold/ungrounded baseline — the agent's own build
> always warms the cache. A truly cold baseline is only possible for advisory tasks that never build.
>
> **Doc-strip probe (n=3, matched, upper bound).** Relocating the `0.13.8` docs out of the cache (lib
> kept) and re-running the baseline dropped its successful cache-doc reads **28 → 1**; the lone survivor
> was the agent falling back to a **sibling cached version** (`… 0.13.8/README.md || … 0.13.7/README.md`),
> proving stripping one version is not a cold cache. Measured effect on the baseline arm:
>
> | baseline (mean / 6 scenarios, n=3) | quality | cost | iet |
> | --- | --- | --- | --- |
> | WARM (docs cached) | 4.23 | 11.75 | 51,951 |
> | doc-stripped (0.13.8 only) | 4.05 | 11.24 | 47,937 |
> | **delta (lower bound)** | **+0.18** | +0.51 | +4,014 |
>
> So denying the `0.13.8` docs cost the baseline **~0.18 quality** — a *lower bound*, since sibling
> versions still leaked; cost/iet moved within noise. (NuGetFetch 0.6.2 packs no docs in its nupkg — only
> the DLL — so it has no equivalent strippable leak.)

## The reframing: grounding competes with the package's README, not with model ignorance

The decisive observation came from the Opus baseline: it **succeeded by reading Markout's shipped
488-line `README.md`** (the judge: *"A read the README, B used a skill"*). Markout is unknown to the
model, but the package is **self-teaching via its README** — and a frontier model mines it well.

So the value of an `AGENTS.md` is **not** "rescue an unknown package" at the frontier; it is:

1. **Efficiency at the frontier.** The concise `AGENTS.md` (56 lines) replaces ~274k tokens / 15
   tool calls of README-spelunking with ~90k tokens / 7–8 calls at identical quality (~3×).
2. **Correctness at the weak tier.** Haiku does **not** reliably self-serve from a 488-line README
   (it reached for blocked web tools and failed); the short, targeted `AGENTS.md` rescues it
   (fail → pass). The brevity is the point — a weak model uses a 56-line skill but drowns in a
   488-line README.

This is the tier-conditioned thesis (frontier → optimize cost; weak tier → optimize task
completion), now demonstrated on a real dependency: **grounding pays where the model can't, or won't,
afford to extract the same knowledge from the README.**

## Delivery gap found in the wild

Markout's README tells agents *"see SKILL.md for integration instructions"*, but **`SKILL.md` does
not ship in the nupkg** — only `README.md` does. So NuGet MCP's `get_package_context` (which prefers
`AGENTS.md`, else falls back to README) would serve the long tutorial README and never the
agent-targeted guide. We closed this by authoring `grounding/markout/AGENTS.md` and **shipping it
into the package**: added `AGENTS.md` at the Markout repo root and packed it to the nupkg root
(verified: `Markout.0.13.6.nupkg` now contains `AGENTS.md`). NuGet MCP will now serve the concise
grounding instead of the 488-line README.

## Recommendation

- **Ship the Markout `AGENTS.md`.** It is do-no-harm at the frontier (and ~3× cheaper) and a
  correctness rescue at the weak tier — the Pareto gate passes.
- **Brevity is a feature, not just a forcing function.** The win over the existing README is
  precisely its size: targeted enough for a weak model to consume, cheap enough that a frontier model
  prefers it over README-mining.
- **General lesson for the NuGet team:** for a package that already ships a thorough README, measure
  `AGENTS.md` value as *efficiency at the frontier + completion at the weak tier*, not as a frontier
  pass/fail rescue. The README is the baseline's lifeline; `AGENTS.md` should beat it on cost and on
  weak-tier reliability.

## Reproduce

```bash
# frontier (do-no-harm + efficiency)
eng/run-evals.sh markout   # or:
.tools/skill-validator-*/skill-validator evaluate --tests-dir tests \
  --model claude-opus-4.6 --runs 5 grounding/markout
# weak tier (correctness rescue)
.tools/skill-validator-*/skill-validator evaluate --tests-dir tests \
  --model claude-haiku-4.5 --judge-model claude-opus-4.6 --runs 5 grounding/markout
```

> Caveat: runs=3 here (varianceCV up to 1.7). The two robust claims — Haiku baseline
> `taskCompleted=false` vs grounded `true`, and the frontier efficiency gap — should be firmed at
> runs=5. The quality tie (do-no-harm) is already stable.

## Related: delivery via the `dotnet-inspect` CLI

The same Markout content delivered through `dotnet-inspect package <id> --readme` (instead of the
NuGet MCP) lands in the MCP's cost regime when it serves `AGENTS.md`, with the same README liability
when it falls back — see [`dotnet-inspect-channel.md`](./dotnet-inspect-channel.md).
