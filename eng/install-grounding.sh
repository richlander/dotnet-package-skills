#!/usr/bin/env bash
# Publish the Native AOT `grounding` binary and install it on PATH
# (~/.dotnet/tools, already on PATH) so it runs without `dotnet run`.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
rid="${1:-$(dotnet --version >/dev/null 2>&1 && echo "$(uname -s | tr '[:upper:]' '[:lower:]' | sed 's/darwin/osx/')-$(uname -m | sed 's/x86_64/x64/;s/aarch64/arm64/')")}"
dest="${DOTNET_TOOLS:-$HOME/.dotnet/tools}"
echo "Publishing Native AOT for $rid ..."
dotnet publish "$root/src/grounding/grounding.csproj" -c Release -r "$rid" >/dev/null
bin="$root/src/grounding/bin/Release/net11.0/$rid/publish/grounding"
mkdir -p "$dest"
cp "$bin" "$dest/grounding"
chmod +x "$dest/grounding"
echo "Installed native grounding -> $dest/grounding"
"$dest/grounding" --version
