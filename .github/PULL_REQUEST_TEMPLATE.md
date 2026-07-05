<!--
Grounding content PR? Follow the methodology: docs/grounding-eval-methodology.md
A grounding change is a claim — it ships with its evidence. Fill every section.
For non-grounding PRs, delete this template and describe your change normally.
-->

## Changes

<!-- What grounding text changed, and WHY it is package-specific knowledge (point at the package's
actual trap), not a generic process tip. -->

Unit: `grounding/<unit>` · Package: `<Name> <version>` · `AGENTS.md` body lines: `<n>/60`

## Metrics

<!-- Paste BOTH `--card` dumps verbatim: the mini-tier WIN card and the frontier-tier NO-HARM card.
     grounding analyze --card data/<unit>-6q/<unit>.n3.haiku.json   # mini WIN
     grounding analyze --card data/<unit>-6q/<unit>.n3.opus.json    # frontier NO-HARM
Each card prints its metrics table, a term Legend, and the tier gate (PASS/FAIL per threshold).
Keep the Legend — outside readers need it to interpret IET / output tok / func / the arm names.
Metric rows carry goal markers: (+) means higher is better; (-) means lower is better. -->

<!-- mini-tier (WIN) card here -->

<!-- frontier-tier (NO-HARM) card here -->

## Representative check

<!-- One concrete before/after: what the UNGROUNDED agent reaches for (wrong/hallucinated API) vs
what the grounding makes it do. -->

## Validation

```bash
grounding check-agents
RUNS=3 eng/run-<unit>-6q.sh                                   # -> data/<unit>-6q/<unit>.haiku.json (mini WIN)
RUNS=3 MODELS=claude-opus-4.8 eng/run-<unit>-6q.sh           # frontier NO-HARM run
grounding analyze --card data/<unit>-6q/<unit>.haiku.json
cp data/<unit>-6q/<unit>.haiku.json data/<unit>-6q/<unit>.n3.haiku.json   # commit matched n=3
```

## Caveats

<!-- Required: (1) baseline is partly self-grounded from the NuGet cache (gap understates grounding);
(2) starting cache state is not a variable. Plus any scenario-specific variance notes. -->

## Checklist

- [ ] `AGENTS.md` within line limit (`grounding check-agents` passes)
- [ ] Datasets committed under `data/<unit>-6q/`; both `--card` dumps match them
- [ ] n ≥ 3; model + judge named, for **both** tiers
- [ ] **mini WIN** gate passes (real cost/IET or quality win; no func/quality/web regression)
- [ ] **frontier NO-HARM** gate passes (zero output-token inflation; no quality/func regression)
- [ ] Claims cite normative metrics; signals only explain
- [ ] Grounding text is package-specific, justified by the package's trap
- [ ] Required caveats present; cache reads attributed per arm (not grepped)
- [ ] `docs/reports/<unit>.md` updated

<sub>Methodology: [docs/grounding-eval-methodology.md](../docs/grounding-eval-methodology.md)</sub>
