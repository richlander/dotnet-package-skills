#!/usr/bin/env bash
# Markout 6-question study: run the 4-arm delivery matrix over the M1-M6 scenarios and
# capture raw results.json into data/markout-6q/.
#
# WHY MARKOUT: it has ~zero presence in model training data, so the no-grounding baseline
# cannot reconstruct the API from intuition (it hallucinates System.Text.Json-style,
# context-less calls that do not compile). Any correct token must come from grounding —
# the cleanest possible demonstration of grounding necessity. The 6 scenarios (see
# tests/markout/eval.yaml) span the capability surface and are graded so the COMPACT
# AGENTS.md covers M1-M5 while M6 (MarkoutLink / MarkoutValueMap / MarkoutSection.GroupBy)
# is documented only in the BROAD skill — the wedge that keeps the broad arm non-redundant.
#
# THE 4 ARMS (and the unit/arm each comes from):
#   1. baseline        no grounding, web-blocked          -> any unit's `baseline` arm
#   2. nuget-mcp       compact AGENTS.md via real NuGet MCP -> markout-realmcp `skilledPlugin`
#   3. dotnet-inspect  compact AGENTS.md via the CLI        -> prefer-dotnet-inspect skilled arm
#   4. broad-skill     full SKILL.md handed over inline     -> markout-broadskill `skilledIsolated`
#
# Running 3 grounding units yields all 4 arms:
#   * grounding/markout-broadskill   -> baseline (arm 1) + broad-skill inline (arm 4)
#   * grounding/markout-realmcp      -> nuget-mcp plugin (arm 2)
#   * grounding/prefer-dotnet-inspect-> dotnet-inspect (arm 3)
#
# The published Markout 0.13.8 already ships AGENTS.md at the package root, so both the
# NuGet MCP (get_package_context) and `dotnet-inspect package Markout@0.13.8 --readme`
# resolve the compact AGENTS.md naturally — no cache toggling required.
#
# Prereqs: dotnet-inspect >= 0.12.0 on PATH (or available via `dnx dotnet-inspect`); the
# skill-validator built once (eng/run-evals.sh); ambient Copilot auth (no API key needed).
#
# Usage:
#   eng/run-markout-6q.sh                                   # defaults: runs=3, haiku+opus
#   RUNS=5 MODELS="claude-opus-4.8" eng/run-markout-6q.sh
#   NO_JUDGE=1 RUNS=1 MODELS="claude-haiku-4.5" eng/run-markout-6q.sh   # cheap smoke run
set -uo pipefail
cd "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

RUNS="${RUNS:-3}"
MODELS="${MODELS:-claude-opus-4.8 claude-haiku-4.5}"
NO_JUDGE="${NO_JUDGE:-}"
BIN=$(ls -d .tools/skill-validator-*/skill-validator 2>/dev/null | head -1)
[ -x "$BIN" ] || { echo "skill-validator not built; run eng/run-evals.sh once first"; exit 1; }

# Units that participate, and the arm each contributes to the 4-arm matrix.
UNITS=("markout-broadskill" "markout-realmcp" "prefer-dotnet-inspect")

./eng/gen-plugins.sh >/dev/null

OUT="data/markout-6q"; mkdir -p "$OUT"
short() { case "$1" in *opus*) echo opus;; *haiku*) echo haiku;; *sonnet*) echo sonnet;; *) echo "$1";; esac; }

judge_args=(--judge-model claude-haiku-4.5)
[ -n "$NO_JUDGE" ] && judge_args=(--no-judge)

echo "======== MARKOUT 6Q MATRIX: runs=$RUNS models=[$MODELS] no_judge=${NO_JUDGE:-0} ========"
for unit in "${UNITS[@]}"; do
  for m in $MODELS; do
    ms=$(short "$m")
    tag="$unit.$ms"
    echo "#### $(date +%H:%M:%S)  $unit  $ms  (runs=$RUNS) ####"
    rm -rf ".skill-validator-results/m6q-$tag"
    "$BIN" evaluate --tests-dir tests --model "$m" "${judge_args[@]}" \
      --runs "$RUNS" --keep-sessions --results-dir ".skill-validator-results/m6q-$tag" \
      "grounding/$unit" > ".tools/m6q-$tag.out" 2>&1
    rj=$(find ".skill-validator-results/m6q-$tag" -name results.json | head -1)
    if [ -n "$rj" ]; then cp "$rj" "$OUT/$tag.json"; echo "   -> $OUT/$tag.json"; else
      echo "   !! no results.json (see .tools/m6q-$tag.out)"; tail -5 ".tools/m6q-$tag.out"; fi
  done
done

echo "======== DONE $(date +%H:%M:%S). Raw data in $OUT/ ========"
echo "Arm map:  baseline+broad-skill <- markout-broadskill.*  |  nuget-mcp <- markout-realmcp.*  |  dotnet-inspect <- prefer-dotnet-inspect.*"
ls -la "$OUT"
