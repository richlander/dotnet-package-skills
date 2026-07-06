# Docs

Generic infrastructure for evaluating NuGet package **grounding**. Start at the top, then go deep.

## Primary

- **[overview.md](./overview.md)** — **read this first**: the whole system in one pass — the three
  documents (Brochure / Missing Manual / Complete Textbook), the three task tiers
  (Core-6 / MM-12 / CT-24), and the eval-diff triangulation that gives each its confidence signal.
- **[getting-started.md](./getting-started.md)** — build the `grounding` CLI, author a unit, run the
  ladder, read the card.
- **[running-eval.md](./running-eval.md)** — point the harness at a package repo's grounding and read
  the result. Grounding lives in the target repo; the harness reads it in place (no packing).
- **[grounding-eval-methodology.md](./grounding-eval-methodology.md)** — the *approach*: content arms
  (baseline / Missing Manual / Brochure / Complete Textbook), the three nested tiers
  (Core-6 / MM-12 / CT-24), the cost-tiered ladder, and the confounds.
- **[delivery-methodology.md](./delivery-methodology.md)** — the *delivery axis* (orthogonal to
  content): **push** (always-on `.agent.md` at t=0) vs **pull** (model-invoked `SKILL.md`). The
  push-advantage identity `≈ effect × (1 − pull activation)`, the shared-pinned-baseline procedure,
  and the anti-overclaim guardrails.
- **[scoring.md](./scoring.md)** — *grading and shipping*: the **BETTER / NEUTRAL / WORSE** grade model,
  the tier-aware ship gate, the cards, the PR contents + checklist, and the judge-floor finding.
- **[eval-protocol.md](./eval-protocol.md)** — *measurement discipline*: the pre-registered rules that
  keep numbers honest — arm hygiene, variance-aware n, pass-rate metric, robust assertions, no
  splicing — each tied to a real mistake it prevents.

## Supporting references

- **[authoring-principles.md](./authoring-principles.md)** — how to write the three documents
  (Brochure / Missing Manual / Complete Textbook).
- **[delivery-and-retrieval.md](./delivery-and-retrieval.md)** — how grounding reaches the agent: the
  resident index, MCP delivery, and retrieval gates.
- **[iet-model.md](./iet-model.md)** — how the analyzer maps Copilot token fields to IET, including
  prompt-cache evidence, provider models, and tool-turn IET.
- **[harness.md](./harness.md)** — how `skill-validator` is built and run, and the confounds.
- **[grounding-lifecycle.md](./grounding-lifecycle.md)** — the team playbook: create / update / delete /
  evaluate.

## Study artifacts

- **[recommendation.md](./recommendation.md)** — the NuGet v-team channel-matrix recommendation.
- **[reports/](./reports/)** — per-package eval reports.
- **[templates/canonical-grounding-pr.md](./templates/canonical-grounding-pr.md)** — the PR template.
