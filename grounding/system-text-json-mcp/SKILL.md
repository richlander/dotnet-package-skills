---
name: system-text-json-mcp
description: "Experiment harness shell for the MCP delivery environment (resident-API probe). Carries no inline grounding; System.Text.Json context is delivered only via the get_package_context MCP tool when the agent chooses to call it."
---

<!-- INTENTIONALLY INERT. This skill ships NO grounding content. It exists only to
attach the package-grounding MCP server (see plugin.json) to the evaluated run so
the agent self-selects whether to retrieve context via the get_package_context /
summarize_package_context tools. The retrieval gate under test lives in the tool
description (GROUNDING_GATE), not here. This unit drives the RESIDENT-API scenario:
a greenfield System.Text.Json task whose correct solution does NOT need the migration
grounding, so a confident model should decline the full body. -->

# (no inline guidance)
