#!/usr/bin/env python3
"""Extract the delivery-channel matrix from data/<task>/*.json.

Maps the skill-validator arms in each captured results.json to the study's channels:
  realmcp-agents.<m>.json   : baseline -> A' (raw, AGENTS present/invisible) ; plugin -> C (NuGet MCP -> AGENTS)
  realmcp-noagents.<m>.json : baseline -> A  (raw, README)                    ; plugin -> B (NuGet MCP -> README)
  custommcp.<m>.json        : baseline -> A  (raw, README; dup)               ; plugin -> D (custom MCP resident_index)

Usage: eng/extract-channels.py data/markout

Primary metric: weighted IET (Input-Equivalent Tokens), a cost-equivalent measure that
normalizes each token class to fresh-input units:

    IET = fresh + 0.1*cacheRead + 1.25*cacheWrite + 5*output
    fresh = inputTokens - cacheReadTokens   # Copilot/OpenAI report inputTokens as TOTAL
                                            # prompt tokens, with cacheRead a subset of it.

This avoids over-counting cheap prompt-cache reads. The harness `tokenEstimate`
(== inputTokens + outputTokens, cache reads counted at full price) is reported as a
secondary column for traceability.
"""
import json, sys, glob, os

# Cost-equivalent weights (input-token units; ~Anthropic Sonnet pricing ratios).
W_CACHE_READ, W_CACHE_WRITE, W_OUTPUT = 0.1, 1.25, 5.0

def weighted_iet(m):
    inp = m.get("inputTokens", 0) or 0
    out = m.get("outputTokens", 0) or 0
    cr = m.get("cacheReadTokens", 0) or 0
    cw = m.get("cacheWriteTokens", 0) or 0
    fresh = max(inp - cr, 0)
    return round(fresh + W_CACHE_READ * cr + W_CACHE_WRITE * cw + W_OUTPUT * out)

CH = [  # (channel, source-file-tag, arm, label)
    ("A",  "realmcp-noagents", "baseline",      "raw pkg on disk -> README"),
    ("A'", "realmcp-agents",   "baseline",      "raw pkg on disk, AGENTS present (invisible)"),
    ("B",  "realmcp-noagents", "skilledPlugin", "real NuGet MCP -> README"),
    ("C",  "realmcp-agents",   "skilledPlugin", "real NuGet MCP -> AGENTS.md"),
    ("D",  "custommcp",        "skilledPlugin", "custom MCP (resident_index)"),
]

def arm_metrics(path, arm):
    sc = json.load(open(path))["verdicts"][0]["scenarios"][0]
    m = sc[arm]["metrics"]
    return weighted_iet(m), m.get("tokenEstimate"), m.get("toolCallCount"), m.get("taskCompleted"), m.get("toolCallBreakdown", {})

def models(task_dir):
    ms = set()
    for f in glob.glob(os.path.join(task_dir, "*.json")):
        ms.add(f.rsplit(".", 2)[-2])
    return sorted(ms)

def main(task_dir):
    for mdl in models(task_dir):
        print(f"\n==== {os.path.basename(task_dir)}  /  {mdl}  ====")
        print(f"{'Ch':<3} {'IET':>9} {'(raw tEst)':>11} {'tools':>6} {'done':>6}  {'mcp_calls':>9}  delivery")
        for ch, tag, arm, label in CH:
            path = os.path.join(task_dir, f"{tag}.{mdl}.json")
            if not os.path.exists(path):
                print(f"{ch:<3} {'-- missing --':>28}  {label}")
                continue
            iet, test, tools, done, bd = arm_metrics(path, arm)
            mcp = sum(v for k, v in bd.items() if "package_context" in k)
            print(f"{ch:<3} {iet:>9} {test:>11} {tools:>6} {str(done):>6}  {mcp:>9}  {label}")

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "data/markout")
