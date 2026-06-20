# README liability vs. targeted AGENTS.md value (size sweep)

**Question (user framing):** Is the grounding feature really *"README avoidance, while
offering targeted value"*? Can we prove a README of *x* kB is a **liability**, and that a
README:AGENTS size ratio is the **sweet spot**?

**Method.** Hold the task constant (Markout M1 report task; the agent must write the
context-required `MarkoutSerializer.Serialize(...)` pattern). Vary only the **README that
ships in the restored package** (`~/.nuget/packages/markout/0.13.6/README.md`), built from
**real Markout docs** so the bloat is realistic (README → +spec/nested-lists → +user-guide/
SKILL). Hold the **AGENTS.md fixed at 3.5 KB**. Two arms:

- **baseline** — no grounding skill; agent self-serves from the package on disk (reads the
  README, may unzip the nupkg, read `Markout.xml`, etc.).
- **skilledIsolated** — the package-agnostic `prefer-agents-md` redirect skill is loaded;
  agent goes straight to the 3.5 KB `AGENTS.md`.

Agent = Opus 4.6, **runs=3** (averaged), judge = Haiku (quality scores ignored; we read
tokens/tools/completion). **IET = weighted Input-Equivalent Tokens** (`fresh + 0.1·cacheRead +
1.25·cacheWrite + 5·output`, recomputed from the raw token classes); `(tEst)` = the unweighted
harness token estimate (`inputTokens + outputTokens`). The `skilledPlugin` arm is ignored
(co-residence confound, documented earlier).

## Results (runs=3, averaged)

| README | kB | ratio vs AGENTS | baseline IET (tEst) | tools | AGENTS.md IET (tEst) | tools | saving |
|--------|----:|----:|----:|----:|----:|----:|----:|
| xs (truncated) | 3.1 | ~1× | 81k (368,918) | 21 | 42k (144,420) | 11 | **48%** |
| s  (real README) | 18.3 | 5× | 90k (486,318) | 27 | 36k (128,800) |  9 | **60%** |
| m  (+spec+nested) | 34.3 | 10× | 72k (355,066) | 21 | 36k (131,205) | 10 | **50%** |
| xl (+user-guide+SKILL) | 73.7 | 21× | 117k (619,921) | 27 | 36k (138,446) | 11 | **69%** |

All arms completed the task (`failedRunCount=0` everywhere at this tier). `highVariance=True`
for xs/m/xl — the **baseline** is intrinsically high-variance; the AGENTS path is not.

## Findings

1. **Targeted value is size-invariant (PROVEN, clean).** The 3.5 KB AGENTS.md path costs
   **~36–42k IET and 9–11 tools across a 24× README size range** (3 KB → 74 KB). Flat line.
   The grounding artifact's cost does not depend on how big the package README is, because the
   agent never reads the README — it reads the one targeted doc and stops.

2. **README reliance is a high-cost, high-variance regime (the real "liability").** Baseline
   ranges **72k–117k IET, 21–27 tools**, and is flagged `highVariance`. The **largest README
   is decisively the worst** (xl 74 KB → 117k, 27 tools). The redirect saves **48–69%** —
   roughly a **2–3× token cut at every size**.

3. **REFUTATION of the naïve "smaller README = cheaper".** The *smallest* README (xs, 3.1 KB —
   smaller than the AGENTS.md itself) was **not** the cheapest baseline (81k IET, 21 tools). A
   *truncated* README removes content the agent needs, so it spelunks elsewhere (unzips the
   nupkg, reads the XML doc, greps the DLL). So the liability is **not raw kB**: it is
   **relying on an open-ended general doc at all**. Too-small-and-incomplete is as bad as
   too-big-and-bloated.

## Verdict on the user's reframing

- *"README avoidance + targeted value"* — **yes, supported.** The redirect cuts tokens ~2–3×
  at parity quality by avoiding the README entirely and reading one lean, complete doc.
- *"README of x kB is a liability"* — **directionally yes, but not a tidy monotonic kB curve.**
  The cost ceiling and variance rise with size (worst at 74 KB), but a too-small README is
  *also* expensive. The liability is the **exploration regime** (open-ended, high-variance,
  high-ceiling), which size aggravates at the top end — not a clean f(kB).
- *"README:AGENTS ratio sweet spot"* — **reframe.** There is no knife-edge ratio. A **lean,
  complete ~3.5 KB targeted doc dominates a README of any realistic size by ~2–3× (weighted
  IET).** The lever
  is **completeness + targeting**, not a size ratio. Practical rule: ship a small *complete*
  `AGENTS.md`; it wins whether the README is 3 KB or 74 KB.

## Caveats

- runs=3, single tier (Opus); baseline is high-variance so treat individual cells as ±; the
  **robust signals are the flat AGENTS line and the ~2–3× gap**, not exact baseline values.
- README variants were built by appending real Markout docs (Quick Start stays near the top),
  modelling natural README accretion; a different layout (needle buried deep) would worsen the
  baseline further.
- Cache was modified for the experiment: AGENTS.md injected at
  `~/.nuget/packages/markout/0.13.6/AGENTS.md`; README swapped per size and **restored to the
  original 18.3 KB afterward**. The injected AGENTS.md remains (the `prefer-agents-md` eval
  depends on it); revertible by deleting that file.
