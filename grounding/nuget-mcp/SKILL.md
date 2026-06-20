---
name: nuget-mcp
description: "Experiment harness shell that attaches the real upstream NuGet MCP server (NuGet.Mcp.Server). Carries no inline grounding; package context is delivered only via the server's get_package_context tool when the agent chooses to call it."
---

<!-- INTENTIONALLY INERT. This skill ships NO grounding content. It exists only to
attach the REAL NuGet.Mcp.Server (see plugin.json) to the evaluated run, so we can
observe its actual call behavior and delivered content on the same multi-package
triage scenario used by our controlled gates. -->

# (no inline guidance)
