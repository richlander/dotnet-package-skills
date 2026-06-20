---
name: multi-package-mcp
description: "Experiment harness shell for the MCP delivery environment (multi-package triage). Carries no inline grounding; package context for any referenced package is delivered only via the get_package_context MCP tool when the agent chooses to call it."
---

<!-- INTENTIONALLY INERT. This skill ships NO grounding content. It exists only to
attach the package-grounding MCP server (see plugin.json) to the evaluated run so
the agent self-selects whether (and for which package) to retrieve context via the
get_package_context / summarize_package_context tools. The retrieval gate and tool
shape under test live in the tool descriptions, not here. -->

# (no inline guidance)
