#!/usr/bin/env python3
"""Minimal stdio MCP server that serves NuGet package grounding (AGENTS.md).

Built for the package-grounding "three environments" experiment. It mirrors the
NuGet MCP server's `get_package_context` tool, but the WHEN-TO-CALL guidance in the
tool *description* is the experimental variable (selected by the GROUNDING_GATE env
var). Everything else — the content served and the delivery mechanism — is held
fixed, so an A/B over the description isolates the retrieval *gate*.

Dependency-free: speaks newline-delimited JSON-RPC 2.0 over stdio (the MCP stdio
transport), so it runs under a bare `python3` with no packages installed.

Gates (GROUNDING_GATE):
  task_type           - one tool; "call when the user asks about a package" (mimics today's NuGet MCP)
  uncertainty_version - one tool; "call when unsure of the current API / version may post-date training"
  progressive         - TWO tools, skill-style progressive disclosure: a cheap
                        summarize_package_context (returns the AGENTS.md frontmatter / first
                        lines) and a get_package_context that returns the full body. The agent
                        reads the summary, then decides whether the full body is worth the tokens.

Every tools/call is appended to <repo>/.tools/mcp-calls.log as a JSON line (including the
tool name), giving independent P(summary) / P(full) signals alongside the harness's own
tool.execution_start events.
"""
import json
import os
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
GROUNDING_DIR = REPO_ROOT / "grounding"
CALL_LOG = REPO_ROOT / ".tools" / "mcp-calls.log"

GATE = os.environ.get("GROUNDING_GATE", "uncertainty_version").strip()

TOOL_NAME = "get_package_context"
SUMMARY_TOOL_NAME = "summarize_package_context"
SUMMARY_FALLBACK_LINES = 8

GATE_DESCRIPTIONS = {
    # Task-type gate: fires on a *kind of request* (capability-blind). This is the
    # behaviour we want to show under-serves coding tasks and taxes confident models.
    "task_type": (
        "Retrieves authoritative documentation and usage guidance for a NuGet "
        "package. Call this whenever the user asks a question about a package, its "
        "API, or how to use it."
    ),
    # Uncertainty + version-delta gate: fires on *model uncertainty* and on the
    # installed version post-dating the model's training. Lets confident models on
    # current APIs self-select out, and current-but-unknown versions pull context.
    "uncertainty_version": (
        "Retrieves the authoritative, version-specific API reference and migration "
        "guidance for an installed NuGet package. Call this before writing or editing "
        "code against the package when you are not fully confident of the current API "
        "for the installed version, or when the installed version may post-date your "
        "training data (e.g. a new major/GA release). Do not call it for packages whose "
        "current API you already know with confidence."
    ),
}

# Progressive (skill-style) mode: two tools. The gating decision is moved out of the
# tool *description* and into the agent's own judgement after reading a cheap summary —
# the same mechanic skills use (always-loaded frontmatter, body loaded on demand).
PROGRESSIVE_SUMMARY_DESCRIPTION = (
    "Returns a short summary (name + when-to-use) of the agent guidance available for "
    "an installed NuGet package, without the full body. Cheap to call. Use it to decide "
    "whether the package's full guidance is worth retrieving with get_package_context."
)
PROGRESSIVE_FULL_DESCRIPTION = (
    "Returns the full agent guidance (the package's AGENTS.md body) for an installed "
    "NuGet package. Call this only after summarize_package_context shows the guidance is "
    "relevant to the task at hand."
)

# Resident-index (faithful skills) mode: ONE body tool, but the per-package summaries are
# pushed into the tool description itself, so they are always in context for free (no call) —
# the property that makes skills' progressive disclosure work. The agent reads the resident
# manifest, decides which package (if any) is relevant, and calls the single body tool only
# for that one. Decline is the default (just don't call).
RESIDENT_INDEX_BASE = (
    "Returns the full agent guidance (AGENTS.md body) for an installed NuGet package. "
    "Below is the always-available index of packages that ship guidance, with a one-line "
    "summary of when each one matters. Read the index for free; call this tool only for a "
    "package whose summary is relevant to the task. If no summary is relevant, do not call it."
)


def log(msg):
    sys.stderr.write(f"[grounding-mcp] {msg}\n")
    sys.stderr.flush()


def tool_description():
    if GATE == "resident_index":
        manifest = resident_manifest()
        if manifest:
            return f"{RESIDENT_INDEX_BASE}\n\nAvailable package guidance:\n{manifest}"
        return RESIDENT_INDEX_BASE
    return GATE_DESCRIPTIONS.get(GATE, GATE_DESCRIPTIONS["uncertainty_version"])


def split_frontmatter(text):
    """Return (frontmatter_block, body). frontmatter_block is None when absent."""
    lines = text.splitlines()
    if lines and lines[0].strip() == "---":
        for i in range(1, len(lines)):
            if lines[i].strip() == "---":
                fm = "\n".join(lines[1:i]).strip("\n")
                body = "\n".join(lines[i + 1:]).lstrip("\n")
                return fm, body
    return None, text


def summary_of(text):
    """The cheap summary channel: the YAML frontmatter if present, else first N lines."""
    fm, _ = split_frontmatter(text)
    if fm:
        return fm
    return "\n".join(text.splitlines()[:SUMMARY_FALLBACK_LINES])


def _frontmatter_fields(fm):
    """Parse the flat `key: value` pairs we use in AGENTS.md frontmatter, including
    YAML folded/literal block scalars (`>-`, `>`, `|`, `|-`) whose value spans the
    following indented lines."""
    fields = {}
    if not fm:
        return fields
    lines = fm.splitlines()
    i = 0
    while i < len(lines):
        line = lines[i]
        if ":" in line and not line.lstrip().startswith("#") and not line.startswith((" ", "\t")):
            key, val = line.split(":", 1)
            key = key.strip()
            val = val.strip()
            if val in (">", ">-", "|", "|-", ">+", "|+"):
                block = []
                i += 1
                while i < len(lines) and (lines[i].startswith((" ", "\t")) or not lines[i].strip()):
                    block.append(lines[i].strip())
                    i += 1
                fields[key] = " ".join(p for p in block if p).strip()
                continue
            fields[key] = val.strip("'\"")
        i += 1
    return fields


def resident_manifest():
    """One resident line per package that ships guidance: '- <PackageId>: <description>'.

    This is the index that skills push for free. We build it from every unit's AGENTS.md
    frontmatter (and the NuGet id from meta.yaml) and bake it into the single body tool's
    description, so it is always in context with no agent action.
    """
    lines = []
    if not GROUNDING_DIR.is_dir():
        return ""
    seen = set()
    for unit in sorted(GROUNDING_DIR.iterdir()):
        agents = unit / "AGENTS.md"
        if not agents.is_file():
            continue
        fm, _ = split_frontmatter(agents.read_text(encoding="utf-8"))
        fields = _frontmatter_fields(fm)
        name = fields.get("name") or unit.name
        meta = unit / "meta.yaml"
        if meta.is_file():
            for ml in meta.read_text(encoding="utf-8").splitlines():
                ml = ml.strip()
                if ml.startswith("package:"):
                    pkg = ml.split(":", 1)[1].strip().strip("'\"")
                    if pkg:
                        name = pkg
                    break
        if name in seen:
            continue
        seen.add(name)
        desc = fields.get("description") or "(no summary provided)"
        lines.append(f"- {name}: {desc}")
    return "\n".join(lines)


def body_of(text):
    """The full channel: the AGENTS.md body with any YAML frontmatter removed."""
    _, body = split_frontmatter(text)
    return body



def _norm(s):
    return "".join(c for c in s.lower() if c.isalnum())


def _package_index():
    """Map normalized package name (and dir name) -> AGENTS.md path."""
    index = {}
    if not GROUNDING_DIR.is_dir():
        return index
    for unit in sorted(GROUNDING_DIR.iterdir()):
        agents = unit / "AGENTS.md"
        if not agents.is_file():
            continue
        index[_norm(unit.name)] = agents
        meta = unit / "meta.yaml"
        if meta.is_file():
            for line in meta.read_text(encoding="utf-8").splitlines():
                line = line.strip()
                if line.startswith("package:"):
                    pkg = line.split(":", 1)[1].strip().strip("'\"")
                    if pkg:
                        index[_norm(pkg)] = agents
                    break
    return index


def resolve_agents(package_name):
    return _package_index().get(_norm(package_name or ""))


def record_call(package_name, package_version):
    try:
        CALL_LOG.parent.mkdir(parents=True, exist_ok=True)
        with CALL_LOG.open("a", encoding="utf-8") as f:
            f.write(json.dumps({
                "ts": time.time(),
                "gate": GATE,
                "packageName": package_name,
                "packageVersion": package_version,
                "pid": os.getpid(),
            }) + "\n")
    except Exception as e:  # never let logging break the tool
        log(f"call-log write failed: {e}")


def record_call(tool, package_name, package_version):
    try:
        CALL_LOG.parent.mkdir(parents=True, exist_ok=True)
        with CALL_LOG.open("a", encoding="utf-8") as f:
            f.write(json.dumps({
                "ts": time.time(),
                "gate": GATE,
                "tool": tool,
                "packageName": package_name,
                "packageVersion": package_version,
                "pid": os.getpid(),
            }) + "\n")
    except Exception as e:  # never let logging break the tool
        log(f"call-log write failed: {e}")


def _context_text(package_name, package_version, summary):
    """Build the response text. summary=True -> cheap summary channel; else full body."""
    agents = resolve_agents(package_name)
    if agents is None:
        return f"No grounding context is available for package '{package_name}'."
    raw = agents.read_text(encoding="utf-8")
    payload = summary_of(raw) if summary else body_of(raw)
    label = "Summary for" if summary else "Package context for"
    header = f"# {label} {package_name}"
    if package_version:
        header += f" ({package_version})"
    return f"{header}\n\n{payload}"


def handle_tools_call(params):
    params = params or {}
    tool = params.get("name") or TOOL_NAME
    args = params.get("arguments") or {}
    package_name = args.get("packageName") or args.get("package_name")
    package_version = args.get("packageVersion") or args.get("package_version")
    record_call(tool, package_name, package_version)
    log(f"tools/call {tool} package={package_name!r} version={package_version!r} gate={GATE}")

    summary = tool == SUMMARY_TOOL_NAME
    text = _context_text(package_name, package_version, summary)
    return {"content": [{"type": "text", "text": text}], "isError": False}



def _input_schema():
    return {
        "type": "object",
        "properties": {
            "packageName": {
                "type": "string",
                "description": "NuGet package id, e.g. 'System.CommandLine'.",
            },
            "packageVersion": {
                "type": "string",
                "description": "Installed package version, if known.",
            },
        },
        "required": ["packageName"],
    }


def list_tools():
    if GATE == "progressive":
        return [
            {
                "name": SUMMARY_TOOL_NAME,
                "description": PROGRESSIVE_SUMMARY_DESCRIPTION,
                "inputSchema": _input_schema(),
            },
            {
                "name": TOOL_NAME,
                "description": PROGRESSIVE_FULL_DESCRIPTION,
                "inputSchema": _input_schema(),
            },
        ]
    return [{
        "name": TOOL_NAME,
        "description": tool_description(),
        "inputSchema": _input_schema(),
    }]


def handle(msg):
    """Return a JSON-RPC response dict, or None for notifications."""
    method = msg.get("method")
    msg_id = msg.get("id")

    # Notifications (no id) -> no response.
    if msg_id is None:
        return None

    if method == "initialize":
        client_proto = (msg.get("params") or {}).get("protocolVersion") or "2024-11-05"
        return {
            "jsonrpc": "2.0",
            "id": msg_id,
            "result": {
                "protocolVersion": client_proto,
                "capabilities": {"tools": {"listChanged": False}},
                "serverInfo": {"name": "package-grounding", "version": "0.1.0"},
            },
        }

    if method == "tools/list":
        return {
            "jsonrpc": "2.0",
            "id": msg_id,
            "result": {"tools": list_tools()},
        }

    if method == "tools/call":
        return {"jsonrpc": "2.0", "id": msg_id, "result": handle_tools_call(msg.get("params"))}

    if method == "ping":
        return {"jsonrpc": "2.0", "id": msg_id, "result": {}}

    return {
        "jsonrpc": "2.0",
        "id": msg_id,
        "error": {"code": -32601, "message": f"Method not found: {method}"},
    }


def main():
    log(f"started; gate={GATE!r}; repo={REPO_ROOT}")
    for raw in sys.stdin:
        raw = raw.strip()
        if not raw:
            continue
        try:
            msg = json.loads(raw)
        except json.JSONDecodeError as e:
            log(f"bad json: {e}")
            continue
        try:
            resp = handle(msg)
        except Exception as e:  # keep the server alive on any handler error
            log(f"handler error: {e}")
            mid = msg.get("id")
            if mid is None:
                continue
            resp = {"jsonrpc": "2.0", "id": mid, "error": {"code": -32603, "message": str(e)}}
        if resp is not None:
            sys.stdout.write(json.dumps(resp) + "\n")
            sys.stdout.flush()
    log("stdin closed; exiting")


if __name__ == "__main__":
    main()
