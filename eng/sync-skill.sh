#!/usr/bin/env bash
# Generate SKILL.md from AGENTS.md (+ meta.yaml) for every grounding unit.
# AGENTS.md is the source of truth (it ships in the package root); SKILL.md is a
# generated wrapper that the skill-validator harness can toggle on/off.
#
# Usage:
#   eng/sync-skill.sh            # regenerate all SKILL.md files
#   eng/sync-skill.sh --check    # fail if any SKILL.md is stale (for CI)
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
check_mode="${1:-}"
status=0

extract_meta() { # $1=meta.yaml $2=key  -> prints folded scalar value
  awk -v key="$2" '
    $0 ~ "^"key":" {
      sub("^"key":[[:space:]]*", "")
      val=$0
      if (val == ">-" || val == ">" || val == "|" || val == "|-") {
        val=""
        while ((getline line) > 0) {
          if (line ~ /^[^[:space:]]/) break
          sub(/^[[:space:]]+/, "", line)
          val = (val == "" ? line : val " " line)
        }
      }
      print val
      exit
    }
  ' "$1"
}

while IFS= read -r -d '' agents; do
  dir="$(dirname "$agents")"
  meta="$dir/meta.yaml"
  skill="$dir/SKILL.md"
  [ -f "$meta" ] || { echo "WARN: $dir has AGENTS.md but no meta.yaml; skipping"; continue; }

  name="$(extract_meta "$meta" name)"
  description="$(extract_meta "$meta" description)"

  tmp="$(mktemp)"
  {
    echo "---"
    echo "name: $name"
    echo "description: $description"
    echo "---"
    echo
    echo "<!-- GENERATED from AGENTS.md by eng/sync-skill.sh. Do not edit. -->"
    echo
    cat "$agents"
  } > "$tmp"

  if [ "$check_mode" = "--check" ]; then
    if ! diff -q "$tmp" "$skill" >/dev/null 2>&1; then
      echo "STALE: $skill (run eng/sync-skill.sh)"
      status=1
    fi
    rm -f "$tmp"
  else
    mv "$tmp" "$skill"
    echo "wrote $skill"
  fi
done < <(find "$repo_root/grounding" -name AGENTS.md -print0)

exit $status
