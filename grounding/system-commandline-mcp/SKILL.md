---
name: system-commandline-mcp
description: "Experiment harness shell for the MCP delivery environment. Carries no inline grounding; package context is delivered only via the get_package_context MCP tool when the agent chooses to call it."
---

<!-- INTENTIONALLY INERT. This skill ships NO grounding content. It exists only to
attach the package-grounding MCP server (see plugin.json) to the evaluated run so
the agent self-selects whether to retrieve context via the get_package_context
tool. The retrieval gate under test lives in the tool's description, not here. -->

# (no inline guidance)
