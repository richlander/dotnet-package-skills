# Push vs. pull grounding delivery — evaluation methodology

How to measure, honestly, whether delivering package grounding **push** (always-on, at t=0) beats
delivering it **pull** (a model-invoked skill the agent must discover). This is the "good process"
we follow and ask others to follow; it is written to be *defensible*, because the conclusions are
advocacy.

## The two delivery modes

| | Pull | Push |
|---|---|---|
| Artifact | `SKILL.md` (model-invoked skill) | `<unit>.agent.md` (always-on agent) |
| When the body enters context | only if the agent invokes the skill | turn zero, by construction |
| `read grounding %` (activation) | a real, measured rate (0–100%) | ~100% by construction |
| Harness arm | `with-skill-isolated` | `with-agent-isolated` (agent selected as primary persona) |

Content is identical (both carry the same `AGENTS.md` body). **Only delivery differs.**

## The claim under test

> Push compresses the session (fewer turns, less archaeology, lower cost) and raises success by
> putting the grounding in front of the agent *before* it acts — instead of after it has explored,
> guessed, and burned turns.

## The central identity (read this before running anything)

The push *advantage* is bounded by how often pull fails to deliver:

```
push_advantage  ≈  content_trajectory_effect  ×  (1 − pull_activation_rate)
```

- **Where pull already activates** (e.g. a frontier model that reliably invokes the skill), push and
  pull deliver the *same content* → push ≈ pull on trajectory. Push's value there is **insurance**:
  it removes the discovery risk and cannot regress, but it wins nothing on trajectory.
- **The trajectory win lives entirely in the sessions pull misses.** So the empirical magnitude of
  push's advantage is *regime-dependent* — it is large only where pull activation is low.

Consequences for the experiment:
1. **`read grounding %` (activation) is the pivot metric** — always report it.
2. **To demonstrate a large advantage, choose a low-pull-activation regime** (measure activation
   first; a harder/broader set, or a smaller model, where the agent skips the skill).
3. **Never infer push > pull from a regime where pull already activated ~100%** — there is no room
   for it to, and claiming otherwise is indefensible.

## Confounds and the controls that remove them

| Confound | Symptom | Control |
|---|---|---|
| **Per-run baseline variance** | each delivery run computes its own baseline; at low `runs` the baselines diverge (we observed 4/6 vs 6/6 tasks and 74k vs 96k IET for the *same* ungrounded model) | **Shared pinned baseline** — `--baseline-out` once, `--baseline-from` for the other arm, so push and pull grounded arms compare against the **same** baseline |
| **Low statistical power** | one run is dominated by session noise | **`runs ≥ 3`**; report variance (skill-validator's CV / high-variance flag) |
| **Judge noise** | the pairwise judge wobbles near the floor and mis-scores truncated transcripts (we saw a −66% "judge" score on a run that solved 6/6 tasks) | **The functional gate governs** — `tasks correct` and `func passed (assertions)` decide; the judge score is advisory |
| **Content drift** | comparing different docs | same `AGENTS.md`, same scenario set; delivery is the only variable |
| **Regime blindness** | comparing where pull didn't fail | measure pull activation first; select the regime deliberately (see the identity above) |

## Metrics to report

- **`read grounding %`** — activation; the delivery-discriminating metric.
- **`tasks correct` / `func passed`** — the functional gate (the outcome that governs the verdict).
- **Trajectory** — `Session turns`, `tool calls (web/bash/other)`, `nuget archaeology`, `Session IET`.
- **Verdict** — `BETTER / WORSE / NEUTRAL / FAIL` (goal-aware; the functional gate is the only hard gate).

## The reproducible procedure

```bash
# 0. Select the scenario set. Measure pull activation per model first (a pilot run);
#    a low activation rate is where push is expected to pull ahead.

# 1. PULL arm — runs the baseline AND the pull-grounded arm, and PINS the baseline.
grounding run <unit> --delivery pull  --model <M> --runs 3 \
  --tests-dir <set> --out <data-dir> --baseline-out <dir>/bl-{model}.json

# 2. PUSH arm — reuses the SAME pinned baseline (skips the baseline arm).
grounding run <unit> --delivery push  --model <M> --runs 3 \
  --tests-dir <set> --out <data-dir> --baseline-from <dir>/bl-{model}.json

# 3. Render and compare. Both datasets now share one baseline, so the push-grounded vs
#    pull-grounded contrast is clean.
grounding analyze <data-dir>/<unit>.<M>.json       --view doc-card   # pull (baseline -> pull-grounded)
grounding analyze <data-dir>/<unit>-push.<M>.json  --view doc-card   # push (same baseline -> push-grounded)
```

Run every model you care about, including at least one **frontier control** (expected to activate
under pull ~100%, so push ≈ pull) alongside the **needs-it tier** (where pull activation is low).
The control is what proves push does not *harm* the easy case.

## Reading — and defending — the result

- **Pull activation ≈ 100% (frontier control):** expect push ≈ pull on trajectory. Report it that
  way. Push's contribution is *removing discovery risk*, not compressing an already-grounded session.
  Claiming a trajectory win here is not defensible.
- **Pull activation low (needs-it tier):** push should recover the trajectory win in exactly the
  fraction pull missed. This is the advocacy case — and it is only credible with the shared baseline
  and `runs ≥ 3` in place.
- **The verdict is the functional gate**, goal-aware; the judge score is a signal, not a gate.

## Honesty guardrails (the anti-overclaim checklist)

- [ ] Shared, pinned baseline across push and pull (not two independent baselines).
- [ ] `runs ≥ 3`; variance reported.
- [ ] Pull activation stated; the push advantage is read against `(1 − activation)`, not asserted.
- [ ] Functional gate (tasks/assertions) drives the verdict; judge score labeled advisory.
- [ ] A frontier control included, to show push does not regress the easy case.
- [ ] No push > pull claim from a `runs = 1` or unshared-baseline run. *(We made this mistake once —
      a lucky low push-baseline made push look better than it was. The shared baseline exists to
      prevent exactly that.)*

Push's *structural* properties — presence (it always fires), timing (turn zero), position (stable
front of the prompt) — are true **by construction** and need no experiment. The only *empirical*
question is the **magnitude** of the trajectory win, and that is what this methodology measures
without fooling itself.
