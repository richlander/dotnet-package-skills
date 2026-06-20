---
name: multi-package-ambiguous-mcp
description: "Experiment harness shell for the MCP delivery environment (ambiguous multi-package triage). Carries no inline grounding; package context for any referenced package is delivered only via the get_package_context / summarize_package_context MCP tools when the agent chooses to call them."
---

<!-- INTENTIONALLY INERT. This skill ships NO grounding content. It exists only to
attach the package-grounding MCP server (see plugin.json) so the agent self-selects
whether, for which package, and at what depth (summary vs full) to retrieve context.
The retrieval gate and tool shape under test live in the tool descriptions, not here. -->

# (no inline guidance)
