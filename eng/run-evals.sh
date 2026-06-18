#!/usr/bin/env bash
# Run the package-grounding evals using the published skill-validator tool.
# We consume Microsoft.DotNet.SkillValidator (we do NOT vendor its source), so
# "updating the harness" is just bumping eng/tool-version.txt.
#
# Usage:
#   eng/run-evals.sh                         # eval all grounding units
#   eng/run-evals.sh System.CommandLine      # eval one unit
#   FEED=./.tools eng/run-evals.sh           # use a local folder feed of the nupkg
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="$(tr -d '[:space:]' < "$repo_root/eng/tool-version.txt")"
unit="${1:-}"

# Keep generated SKILL.md files in sync before evaluating.
"$repo_root/eng/sync-skill.sh"

# Source for dnx: a NuGet feed (or a local folder containing the downloaded
# nupkg). Override with FEED=... for the nightly nupkg downloaded from the
# skill-validator-nightly GitHub Release.
feed="${FEED:-https://api.nuget.org/v3/index.json}"

paths=()
if [ -n "$unit" ]; then
  paths+=("$repo_root/grounding/$unit")
else
  paths+=("$repo_root/grounding")
fi

# eval.yaml + fixtures live in a parallel tree: tests/<Package>/eval.yaml.
# The harness resolves <tests-dir>/<grounding-dir-name>/eval.yaml, so the
# grounding folder name (e.g. System.CommandLine) must match the tests folder.
set -x
dnx Microsoft.DotNet.SkillValidator --source "$feed" --version "$version" \
  evaluate \
  --tests-dir "$repo_root/tests" \
  "${paths[@]}"
