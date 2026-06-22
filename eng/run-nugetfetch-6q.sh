#!/usr/bin/env bash
# Run the NuGetFetch 6-question unit (N1-N6): baseline (ungrounded) vs grounded
# (candidate AGENTS.md, delivered inline + as a plugin). NuGetFetch is a second unknown
# library; this proves the grounding text BEFORE we publish AGENTS.md to the package.
#
# Metrics land in results.json; read them with: python3 eng/analyze-6q.py <results.json>
#   quality (judge), tokens, web calls, tool calls (incl dotnet-inspect / MCP), duration.
#
# Env:
#   RUNS    runs per scenario (default 1; use 5+ for significance)
#   MODELS  space-separated model ids (default claude-haiku-4.5)
#   NO_JUDGE=1  skip judging (then rejudge later to emit results.json)
set -euo pipefail
cd "$(dirname "$0")/.."

RUNS="${RUNS:-1}"
MODELS="${MODELS:-claude-haiku-4.5}"
BIN=$(ls -d .tools/skill-validator-*/skill-validator 2>/dev/null | head -1)
[ -x "$BIN" ] || { echo "skill-validator not built; run eng/run-evals.sh once first"; exit 1; }

OUT="data/nugetfetch-6q"
mkdir -p "$OUT"
short() { case "$1" in *opus*) echo opus;; *haiku*) echo haiku;; *sonnet*) echo sonnet;; *) echo "$1";; esac; }

judge_args=(--judge-model "claude-haiku-4.5")
[ "${NO_JUDGE:-0}" = "1" ] && judge_args=(--no-judge)

echo "======== NUGETFETCH 6Q: runs=$RUNS models=[$MODELS] no_judge=${NO_JUDGE:-0} ========"
for m in $MODELS; do
  ms=$(short "$m")
  tag="nugetfetch.$ms"
  echo "#### $(date +%H:%M:%S)  nugetfetch  $ms  (runs=$RUNS) ####"
  rm -rf ".skill-validator-results/nf-$tag"
  "$BIN" evaluate --tests-dir tests --model "$m" "${judge_args[@]}" \
    --runs "$RUNS" --keep-sessions --results-dir ".skill-validator-results/nf-$tag" \
    grounding/nugetfetch > ".tools/nf-$tag.out" 2>&1 || true
  rj=$(find ".skill-validator-results/nf-$tag" -name results.json | head -1)
  if [ -n "$rj" ]; then cp "$rj" "$OUT/$tag.json"; echo "   -> $OUT/$tag.json";
    python3 eng/analyze-6q.py "$rj" || true
  else
    echo "   !! no results.json (NO_JUDGE?). Rejudge:"
    echo "      $BIN evaluate grounding/nugetfetch rejudge .skill-validator-results/nf-$tag/<ts> --judge-model claude-haiku-4.5"
    tail -5 ".tools/nf-$tag.out"
  fi
done
