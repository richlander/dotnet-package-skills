# Authoring principles for grounding docs

Grounding docs (`AGENTS.md`) are **not** READMEs. A README explains a package to a
human from scratch; a grounding doc supplies *only* what an AI agent provably lacks.
These principles keep the content tight, measurable, and worth its place in the
`AGENTS.md` line budget.

## 1. Record only what the model is proven to need (skip model-resident knowledge)

Only include information that an agent has been **demonstrated** (by eval signal) to
need and lack. If a web-blocked baseline already produces the correct answer, that
material is *model-resident* and must **not** go in the grounding doc — it adds length,
dilutes RAG matches, and buys no metric movement.

Concretely: write the content, then run the eval. If the grounded arm doesn't beat the
baseline on a scenario, the model already knew it — cut it.

### Evidence (System.CommandLine unit)

| Scenario | Baseline → Grounded | Improvement | Lesson |
| --- | --- | --- | --- |
| S1: `AcceptOnlyFromAmong` comparer overload | 5.0 → 5.0 | **−2.2%** | A "new in 3.x" member with a guessable name is model-resident. No signal. |
| G1: greenfield CLI on 3.x | 5.0 → 5.0 | **+1.1%** | The model writes correct current-API CLIs from scratch (2.0 GA API == 3.x). Greenfield authoring is model-resident. No signal. |
| M1: migrate a real 2.0.0-beta4 CLI to 3.x | 4.0 → 5.0 | **+5.3%** | Transforming code written against a **removed** API requires the specific member mapping the model lacks. Signal. |

**Takeaway:** signal comes from *transforming code written against an API the model can
no longer rely on* (migration), not from greenfield authoring or guessable members. The
grounding's value is the migration mapping table, not "how to write a CLI."

## 2. Optimize for RAG retrieval, not prose coherence

The alternative to a single complete grounding doc is **section-based RAG with similarity
matching**: an agent retrieves the *section* whose text is most similar to its task, not
the whole document. So:

- Spend **minimum** effort on narrative flow, transitions, and "completeness." A grounding
  doc may read as a disjoint set of targeted sections — that is fine, even preferred.
- Make each section **self-describing and keyword-dense** for the task it serves (e.g. a
  migration section should name the old and new member identifiers verbatim, because those
  are what an agent's query will match on).
- Focus every section on *describing the missing model knowledge* needed to move the metric
  to (at least) the success threshold — nothing more.

This is the opposite of how we write `README.md` files, which are authored to be read
top-to-bottom by a human.

## Practical consequences

- **Measure before you keep.** New grounding content is a hypothesis; the eval is the test.
  Keep only sections that move a scenario above the improvement threshold.
- **Prefer migration/transformation scenarios** when probing whether a knowledge gap is
  real; greenfield tasks tend to be model-resident for strong models.
- **Stay within the line budget** (`eng/agents-line-limit.txt`). Cutting model-resident
  content is the first lever when you're over budget.
