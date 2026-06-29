# The measurement harness

How this repo **builds and runs** the [`dotnet/skills`](https://github.com/dotnet/skills)
`skill-validator` to measure whether grounding helps. The root [`README`](../README.md) covers
*what* grounding is and the findings; this file covers *how* the evals run.

> The harness scaffolding — the **generated** per-unit `SKILL.md` skill-wrappers, the slug
> rules, and the runner scripts — is **not** shipped grounding. The artifacts under test are the
> package's grounding **documents**: the **Missing Manual** (`AGENTS.md`, rungs 0–1) and, at rung 2,
> the **Complete Textbook** (a hand-authored, shipped `SKILL.md`). Don't confuse that shipped Textbook
> with the harness's generated `SKILL.md` wrapper — same filename, different thing (skill-validator just
> requires that name).

## Metrics vs. signals: what a claim may rest on

The study reads two epistemically different kinds of data, and we keep them strictly
separated — the analyzer (`grounding analyze`) even prints them in
two labeled column groups. Conflating them is the easiest way to overclaim.

**Normative metrics** are the quantities we are *allowed to draw conclusions from* — the
actual value delivered or harm incurred:

- **quality** (judge `overallScore`) and **functional pass** (`taskCompleted` + assertion
  gates) — the *value* axis (was the task done, and done well?).
- **tokens**, **cost** (premium-request multiplier), and **wall-clock** — the *harm* axis
  (what did it cost to get there?). The analyzer carries **two token views** because the raw
  metrics are not lossy: `tok` (gross input+output, where input *includes* cache re-reads) and
  `iet` (cache-excluded effective tokens, `(input − cacheRead) + output`). They **bracket** the
  real harm — a baseline that re-reads a large cache shows a huge `tok` but a modest `iet` — and
  **`cost` sits between them** as the truest single proxy. Quote `cost` for the harm claim; show
  `tok`/`iet` to reveal whether a token gap is fresh compute or cheap cache reflection.

A headline like "grounding is cheaper and at least as correct" may rest **only** on these.

**Informative signals** are everything about *how* the agent behaved: total tool calls,
**reasoning turns** (`turnCount` — iterations of the think→act loop, the cleanest measure of
flailing), `web_fetch`/`web_search`, `dotnet-inspect` invocations, NuGet-MCP calls, NuGet-cache
rummaging, and bash retry loops. **A tool call (or turn) is not itself a cost or a harm** — on
its own it adds nothing to the bill, and "fewer tool calls" is not a result we claim. Their value
is **interpretive**: token spend is a single point, but many signal points together trace the
*narrative arc* — web archaeology, cache-reflection, compile-retry flailing — that **explains
why** the normative metrics move. Signals corroborate and give shape to a claim; they are
never the claim.

So when a baseline burns 6× the tokens of a grounded arm, the **tokens** are the finding; the
74 reasoning turns, the 25 web fetches, and the cache pokes are the *story* of where those tokens
went. Cite signals to explain a metric, never in place of one.

A third kind of data is the **experimental parameter** — the size of the intervention itself.
The analyzer reports **grounding ~tok** (the `SKILL.md` loaded into each grounded arm) per
subject, so payoff can be read *against* the grounding budget. This is what lets us see the
distribution's modes: a compact `AGENTS.md` (~0.8k tok) and a broad skill (~2.6k tok) both drive
`web→0`, so the compact form buys the same protection at a third of the budget. Grounding tokens
are not a result either; they are the x-axis the metrics are plotted against.

## A confound: the baseline can read the package from the NuGet cache

The "baseline" arm is meant to be *ungrounded* — model knowledge only, web blocked. But a scenario
references a real package, so `dotnet build`/`run` restores it into `~/.nuget/packages`, and the
agent's `bash` can read whatever the package **ships on disk**. That can include the package's
`README.md`, its **shipped `AGENTS.md`** (the very grounding doc, once we pack it into the nupkg),
and the `lib/*.dll` (reflectable/decompilable). So the web-blocked baseline is not necessarily
ungrounded — it can self-serve the grounding straight from the restored package.

This is empirically active, not hypothetical. In the Markout n=3 runs (package `Markout 0.13.8`,
whose cache entry ships both `README.md` and `AGENTS.md`), the **baseline** read those files from
the cache. Attributing every session to its arm via `sessions.db` and counting only *successful*
tool results, the **baseline** arm made **28 successful reads** of the cached `README.md`/`AGENTS.md`
across its 18 sessions (6 scenarios × 3 runs); the two grounded arms made **0** cache-path reads —
they receive `AGENTS.md` through the skill mechanism, not the cache. (An earlier coarse `grep` of the
path string across all session logs reported 304/98; that over-counted — one read spans many log
lines — and conflated arms. The per-arm, success-aware figure is 28 baseline reads.) The consequence:
for a package that ships groundable artifacts, **the baseline-vs-grounded gap understates grounding's
value**, because the baseline already had (a fraction of) the grounding on disk.

**The docs ride inside the nupkg, so every restore extracts them.** `Markout 0.13.8`'s nupkg contains
`README.md` and `AGENTS.md` at the package root (declared via `<PackageReadmeFile>` / `<None Pack>`).
Verified directly: starting from a *completely empty* `NUGET_PACKAGES` dir, a single `dotnet restore`
of the package re-materializes both files on disk. This has a sharp implication: **you cannot have the
package restored (buildable) without its shipped docs being readable** — the docs travel with the
code. Since every Markout scenario asserts `dotnet build`/`run`, the agent's own build necessarily
restores the package and lands the docs in the cache. So in any build-based scenario the web-blocked
baseline is **never truly ungrounded**.

That makes only two baseline conditions physically meaningful:

1. **Warm / restored** — the only condition compatible with build-based scenarios. The package (and
   therefore its `README.md`/`AGENTS.md`) is on disk; the baseline competes against
   grounding-via-cache, not against model ignorance. **Every number in this repo is this condition.**
2. **Cold / no-restore** — a truly empty cache where the package is never restored. Here the baseline
   has model knowledge only. But this is only achievable for **advisory scenarios that never build**;
   the moment a task requires `dotnet build`, restore warms the cache and condition (1) returns.

### Why "empty NuGet cache" is not an eval state worth measuring

A reasonable question is whether the *starting* cache state — empty vs. pre-warmed — is its own
experimental variable. For build-based scenarios it is **not**, and we deliberately do not measure it.
The agent restores the package itself, almost immediately, as a natural first step toward the task.
Empirically, across all **18** Markout baseline sessions (6 scenarios × 3 runs) the agent's first
`dotnet build`/`restore` landed at tool-call **#5–11**, and **every** cache-doc read happened *after*
that first build — in **0/18** sessions did the baseline read a package doc before restoring. So an
empty starting cache collapses to the warm condition within the agent's first few actions; it cannot
persist through a build-based run. Measuring "empty cache at t=0" would therefore measure a transient
that the agent erases before it does any package-specific work — the package is effectively *always
present* by the time it matters. The only setup in which an empty cache is a stable, observable state
is an advisory task that never builds, which is condition (2) above. Treat starting cache state as
fixed (warm), not as a variable.

Stripping the docs out of a *restored* cache entry (lib kept) is therefore an **artificial third
state** that corresponds to no real developer setup — you would never have a restored, buildable
package on disk with its `README.md` surgically deleted. It is useful only as an upper-bound probe of
"how much does denying the cached docs cost the baseline," not as a realistic ungrounded baseline.

NuGetFetch `0.6.2` ships **no** docs in its nupkg (only the DLL), so its baseline leak is reflection
only (weaker) — which is itself a reason the NuGetFetch baseline looks stronger relative to grounding
than Markout's.

**The doc-strip probe (Markout `0.13.8`, n=3 matched).** As an upper-bound probe we relocated the
`0.13.8` docs out of the cache (lib kept) and re-ran the baseline. Method note: a HOME-redirected
isolation harness **does not work** — the Copilot CLI's auth state is HOME-bound, so a redirected
`HOME` fails with `Not authenticated`; the working approach (`.tools/baseline-cache-clean.sh`,
gitignored) keeps the real `HOME` and relocates only the doc files with a checksum-verified restore
trap. Stripping `0.13.8` dropped the baseline's successful cache-doc reads from **28 → 1**: the
single survivor was the agent **falling back to a sibling cached version** —
`cat .../0.13.8/README.md || cat .../0.13.7/README.md` — proving that stripping one version is *not* a
cold cache (the global cache here held five Markout versions: 0.10.2, 0.13.7, 0.13.8, 0.13.9,
10.0.2, four of them shipping a README). The effect on the baseline arm (mean / 6 scenarios):

| baseline (n=3) | quality | cost | iet |
| --- | --- | --- | --- |
| WARM (docs cached) | 4.23 | 11.75 | 51,951 |
| doc-stripped (0.13.8) | 4.05 | 11.24 | 47,937 |
| **delta** | **+0.18** | +0.51 | +4,014 |

So denying the baseline the `0.13.8` docs cost it **~0.18 quality** (a *lower bound* — sibling-version
READMEs still leaked); cost/iet moved within noise. Bottom line: the published baseline-vs-grounded
gap **understates** grounding, the understatement scales with how much groundable material the package
ships, and because the docs are packed in the nupkg, a build-based scenario can never fully remove
that understatement. (`.tools/baseline-cache-test.sh` is an earlier HOME-isolated variant kept only
for reference — it is blocked by the auth issue above.)

## How it relates to dotnet/skills

We follow the same pattern `dotnet/skills` uses for its own evals: **build** the
`skill-validator` binary from source (`dotnet publish eng/skill-validator/src/SkillValidator.csproj`)
and run it. skill-validator is **not published to any NuGet feed** (not nuget.org, not GitHub
Packages) — `dotnet/skills` only builds it in-repo and publishes a rolling `--prerelease`
nightly to a GitHub Release. So we pin a `dotnet/skills` commit in
[`eng/skill-validator.sha`](../eng/skill-validator.sha) and build the validator from it.
"Taking updates" = bump that SHA — automated by
[`.github/workflows/update-harness.yml`](../.github/workflows/update-harness.yml), which opens a
PR pointing at the latest `dotnet/skills` main commit.

## Source of truth: `AGENTS.md` → `SKILL.md`

`AGENTS.md` is the human-authored artifact under test (the file that ships in the package root).
`SKILL.md` is **generated** from `AGENTS.md` + `meta.yaml` by
`grounding sync-skill` purely so the harness has a *togglable* skill to
add/remove between arms. It is an implementation detail of the harness, **not** a marketplace
skill and **not** something the package ships. Never hand-edit `SKILL.md`. Edit `AGENTS.md`, then
run `grounding sync-skill`.

> **Two things named `SKILL.md`.** The file in `grounding/<slug>/SKILL.md` is this **generated
> wrapper** (never hand-edit it). A *package's* shipped **`SKILL.md` is the Complete Textbook** — a
> hand-authored, narrative full guide and the **rung-2 content arm** — an entirely different artifact
> that merely shares the filename. To test the Textbook the harness force-feeds *its* content through
> the same wrapper (`grounding run --source` selects which document fills each content arm).

Grounding `AGENTS.md` files must stay **concise**: `grounding sync-skill` fails if any exceeds the
budget in `eng/agents-line-limit.txt` (currently **60** lines). Keep content tight and prefer a
short "see also" link over inlining depth. Raise the limit deliberately, not casually.

## Layout

Each grounding unit lives in a folder named with a **lowercase-hyphen slug** (the
skill-validator skill name rule), e.g. `system-commandline` for the `System.CommandLine`
package. The real package id is recorded in `meta.yaml` (`package:`).

```
grounding/<slug>/
  AGENTS.md     # SOURCE OF TRUTH — ships in the package root
  meta.yaml     # name (== <slug>), package, description for the generated SKILL.md
  SKILL.md      # GENERATED (grounding sync-skill) — do not edit
tests/<slug>/
  eval.yaml     # scenarios: prompt + setup.copy_test_files + assertions
  <fixtures>    # sample project(s) copied into the agent workdir; gated by `dotnet test`
eng/
  skill-validator.sha    # pinned dotnet/skills commit we build the validator from
  agents-line-limit.txt  # max lines allowed in any AGENTS.md (start: 60)
  grounding              # launcher for the C# grounding CLI (sync-skill, gen-plugins, analyze, ...)
  install-grounding.sh   # publish the Native AOT binary onto PATH
  run-evals.sh           # builds skill-validator from the pinned SHA, then runs evaluate
```

The grounding folder name must match the tests folder name and the skill `name` (e.g.
`system-commandline`); the harness resolves `tests/<name>/eval.yaml`. Fixtures live under
`tests/` (never beside `AGENTS.md`) so the baseline arm cannot accidentally read the grounding.

## Run locally

```bash
# Prereq: a .NET SDK matching dotnet/skills' global.json, git, and
# `gh auth login` (skill-validator's Copilot SDK uses gh creds).
grounding sync-skill                  # regenerate SKILL.md from AGENTS.md
eng/run-evals.sh System.CommandLine
```

`run-evals.sh` clones `dotnet/skills` at the pinned SHA into `./.tools`, builds
`skill-validator`, and caches it per-SHA, so only the first run pays the build cost.

### Keeping content arms tool-clean

For a clean **content** measurement the agent must not substitute a tool for the document, so eval runs
**scrub `~/.dotnet/tools` from the agent's PATH** (removing `dotnet-inspect`, `ildasm`, `ilspycmd`) while
keeping the system `dotnet`/`dnx`. Tool availability — e.g. a `dotnet-inspect` pointer — is a **separate
lever**, layered in deliberately as its own arm, not part of the baseline / Missing Manual / Front Door /
Textbook content comparison. (Verify post-hoc: the `di` signal in `grounding analyze` must be `0` on the
content arms.)

## Adding a package

1. `grounding/<slug>/AGENTS.md` — the grounding content.
2. `grounding/<slug>/meta.yaml` — `name` (== `<slug>`), `package`, `description`.
3. `tests/<slug>/eval.yaml` — one or more scenarios.
4. `tests/<slug>/<fixture project(s)>` — the task, with a `dotnet test` correctness gate.
5. `grounding sync-skill` then `eng/run-evals.sh <slug>`.

## Channel-matrix runs

The delivery-channel study (raw package → NuGet MCP → shipped `AGENTS.md` → resident-index MCP)
is driven by [`eng/run-channel-matrix.sh`](../eng/run-channel-matrix.sh) and summarized by
`grounding channels extract`. See
[`docs/recommendation.md`](recommendation.md) for the results and
[`data/README.md`](../data/README.md) for the channel definitions.
