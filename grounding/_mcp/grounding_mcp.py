#!/usr/bin/env python3
# Shim: ported to the C# `grounding` CLI (src/grounding, `grounding mcp`).
# The MCP eval units now spawn it via `dotnet <grounding.dll> mcp --root <repo>`.
# This shim keeps direct `python3 grounding_mcp.py` invocations working.
import os, sys
root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
launcher = os.path.join(root, "eng", "grounding")
os.execvp(launcher, [launcher, "mcp", *sys.argv[1:]])
