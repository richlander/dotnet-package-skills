#!/usr/bin/env bash
# Run the package-grounding evals using skill-validator.
#
# Mirrors the pattern dotnet/skills uses for its own evals: BUILD the validator
# from source (`dotnet publish eng/skill-validator/src/SkillValidator.csproj`)
# and run the produced `skill-validator` binary. skill-validator is not on any
# NuGet feed, so we build it from a pinned dotnet/skills commit recorded in
# eng/skill-validator.sha. "Updating the harness" = bump that SHA.
#
# Usage:
#   eng/run-evals.sh                         # eval all grounding units
#   eng/run-evals.sh System.CommandLine      # eval one unit
#   TOOLS_DIR=/path eng/run-evals.sh         # reuse a cached source/build
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
sha="$(tr -d '[:space:]' < "$repo_root/eng/skill-validator.sha")"
tools_dir="${TOOLS_DIR:-$repo_root/.tools}"
unit="${1:-}"

# Keep generated SKILL.md files in sync before evaluating.
"$repo_root/eng/grounding" check-agents

src_dir="$tools_dir/skills-src"
bin_dir="$tools_dir/skill-validator-$sha"
bin="$bin_dir/skill-validator"

# Build the validator from the pinned dotnet/skills commit (once per SHA).
if [ ! -x "$bin" ]; then
  echo "Building skill-validator from dotnet/skills@$sha ..."
  if [ ! -d "$src_dir/.git" ]; then
    rm -rf "$src_dir"; mkdir -p "$src_dir"
    git -C "$src_dir" init -q
    git -C "$src_dir" remote add origin https://github.com/dotnet/skills.git
  fi
  git -C "$src_dir" fetch --depth 1 origin "$sha" -q
  git -C "$src_dir" checkout -q FETCH_HEAD
  dotnet publish "$src_dir/eng/skill-validator/src/SkillValidator.csproj" \
    -c Release -o "$bin_dir"
fi

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
"$bin" evaluate \
  --tests-dir "$repo_root/tests" \
  "${paths[@]}"
