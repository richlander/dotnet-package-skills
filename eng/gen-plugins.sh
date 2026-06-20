#!/usr/bin/env bash
# Generate each MCP unit's plugin.json from its committed plugin.json.in template.
#
# The skill-validator harness drops cwd when it spawns the MCP server, so plugin.json
# must reference grounding_mcp.py by ABSOLUTE path. We cannot commit a machine-specific
# absolute path, so we commit a template with a __REPO_ROOT__ placeholder and expand it
# here against this clone's location. The generated plugin.json files are gitignored.
#
# Run once after cloning (and any time you move the repo):
#   ./eng/gen-plugins.sh
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

count=0
while IFS= read -r template; do
  out="${template%.in}"
  sed "s#__REPO_ROOT__#${REPO_ROOT}#g" "$template" > "$out"
  echo "generated ${out#"$REPO_ROOT/"}"
  count=$((count + 1))
done < <(find "$REPO_ROOT/grounding" -name 'plugin.json.in' | sort)

echo "done: $count plugin.json file(s) generated under $REPO_ROOT"
