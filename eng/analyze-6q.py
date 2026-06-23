#!/usr/bin/env python3
"""Analyze markout / nugetfetch results.json, separating METRICS from SIGNALS.

The study draws on two epistemically different kinds of data, and the output
keeps them in distinct column groups so a claim is never confused with its
corroboration:

NORMATIVE METRICS -- the quantities we actually claim as value or harm. These
are what a conclusion ("grounding is cheaper / better") is allowed to rest on.

  qual   judge quality, overallScore 1-5 (rubric-weighted)   [value]
  func   functional assertions passed (build + file + run-output regex)  [value]
  tok    GROSS tokens (input+output); input INCLUDES cache reads  [harm: spend]
  iet    cache-excluded effective tokens: (input - cacheRead) + output.
         tok and iet bracket the real harm -- a baseline that re-reads a big
         cache shows a huge tok but a modest iet; cost sits between them and is
         the truest single harm proxy. Carry both so neither over- nor
         under-states the gap (raw metrics are not lossy: cacheRead is stored).
  cost   premium request multiplier (sum)                     [harm: spend]
  secs   wall seconds                                          [harm: latency]

INFORMATIVE SIGNALS -- corroborating behavioral data. A tool call or web fetch
is NOT itself a cost or a harm; on its own it adds nothing to the bill. Its
value is interpretive: many signal points together trace the narrative arc
(archaeology, cache-reflection, compile-retry loops) that EXPLAINS why the
normative metrics move. Token spend is a single point; signals give it a shape.

  web    web_fetch + web_search calls  (archaeology; grounded arms -> 0)
         a trailing 'Y' means a reject_tools assertion fired (web was used)
  tools  total tool calls
  turns  agent reasoning turns (turnCount) -- iterations of the think->act loop.
         A turn is not itself a cost; it is the cleanest measure of *flailing*
         (a baseline retry-loop spikes turns long before tokens fully explain it).
  di     dotnet-inspect CLI invocations (bash commands calling dotnet-inspect)
  mcp    NuGet MCP calls (nuget-* tools)
  cache  bash commands rummaging ~/.nuget/packages to reverse-engineer the API
  bash   bash calls (proxy for compile/run retry loops)

Per-subject header also reports GROUNDING ~tok: the size of the SKILL.md loaded
into every grounded arm -- the fixed *investment* the payoff is measured against.
It characterizes the intervention mode (compact AGENTS ~0.8k tok vs a broad skill
~2.6k tok), so payoff can be read against grounding size across the distribution.

Usage:
  eng/analyze-6q.py data/markout-6q/*.json
  eng/analyze-6q.py .skill-validator-results/m6q-*/**/results.json
"""
import json, sys, glob, os

ARMS = [("baseline", "baseline"), ("skilledIsolated", "isolated"), ("skilledPlugin", "plugin")]


def grounding_tokens(skill_name):
    """Estimate the loaded grounding size (~tokens) for a subject.

    The grounded arms load grounding/<skill_name>/SKILL.md (generated from AGENTS.md).
    Rough estimate at ~4 chars/token. Returns None if the artifact can't be resolved.
    """
    root = os.path.join(os.path.dirname(__file__), "..")
    for art in ("SKILL.md", "AGENTS.md"):
        p = os.path.join(root, "grounding", skill_name, art)
        if os.path.exists(p):
            return round(len(open(p, encoding="utf-8").read()) / 4)
    return None


def count_tool_events(metrics):
    """Return (dotnet_inspect_calls, mcp_calls, cache_pokes) parsed from the event log.

    cache_pokes counts bash commands that rummage the local NuGet cache / installed DLL to
    reverse-engineer the API (ls/find/cat/reflection against ~/.nuget/packages). It is a
    distinct 'flailing' signal from web fetches: the agent recovering the API from the
    restored package on disk rather than from grounding.
    """
    di = mcp = cache = 0
    for e in metrics.get("events") or []:
        if e.get("type") != "tool.execution_start":
            continue
        data = e.get("data") or {}
        name = data.get("toolName", "")
        args = data.get("arguments", "") or ""
        if name == "bash" and ("dotnet-inspect" in args or "dotnet inspect" in args):
            di += 1
        if name == "bash" and (".nuget/packages" in args or ".nuget\\packages" in args
                               or "nuget/packages" in args):
            cache += 1
        if name.startswith("nuget-") or name.startswith("nuget_"):
            mcp += 1
    return di, mcp, cache


def arm_row(arm):
    if not arm:
        return None
    m = arm.get("metrics", {}) or {}
    jr = arm.get("judgeResult") or {}
    ar = m.get("assertionResults") or []
    func = [a for a in ar if a["assertion"].get("type") != 11]
    rej = [a for a in ar if a["assertion"].get("type") == 11]
    fp = sum(1 for a in func if a["passed"])
    web_used = any(not a["passed"] for a in rej)
    tb = m.get("toolCallBreakdown", {}) or {}
    web = tb.get("web_fetch", 0) + tb.get("web_search", 0)
    di, mcp, cache = count_tool_events(m)
    tok = (m.get("inputTokens", 0) or 0) + (m.get("outputTokens", 0) or 0)
    iet = ((m.get("inputTokens", 0) or 0) - (m.get("cacheReadTokens", 0) or 0)
           + (m.get("outputTokens", 0) or 0))
    return dict(
        qual=jr.get("overallScore", "-"),
        func=f"{fp}/{len(func)}",
        web=web,
        web_flag="Y" if web_used else ".",
        tools=m.get("toolCallCount", "?"),
        turns=m.get("turnCount", "?"),
        di=di,
        mcp=mcp,
        cache=cache,
        bash=tb.get("bash", 0),
        tok=tok,
        iet=iet,
        cost=round(m.get("cost", 0) or 0, 1),
        secs=round((m.get("wallTimeMs", 0) or 0) / 1000),
    )


HDR = (f"{'scenario':28} | {'arm':8} | qual | func | {'tok':>7} | {'iet':>6} | cost | secs "
       f"\u2016 web | tools | turn | di | mcp | cache | bash")
GRP = (f"{'':28}   {'':8}   {'<<<<<<<<<< NORMATIVE METRICS':^43} "
       f"\u2016 {'INFORMATIVE SIGNALS >>>>>>>>>>':<29}")


def main(paths):
    files = []
    for p in paths:
        files.extend(glob.glob(p, recursive=True))
    for f in sorted(set(files)):
        try:
            d = json.load(open(f))
        except Exception as e:
            print(f"!! {f}: {e}"); continue
        for v in d.get("verdicts", []):
            sn = v.get("skillName", "?")
            gtok = grounding_tokens(sn)
            gnote = f"   grounding=~{gtok} tok (loaded into each grounded arm)" if gtok else ""
            print(f"\n===== {sn}   ({f})   model={d.get('model')}{gnote} =====")
            print(GRP)
            print(HDR)
            print("-" * len(HDR))
            for sc in v.get("scenarios", []):
                name = sc["scenarioName"].split(":")[0]
                for key, label in ARMS:
                    r = arm_row(sc.get(key))
                    if not r:
                        continue
                    print(f"{name:28} | {label:8} | {str(r['qual']):>4} | {r['func']:>4} | "
                          f"{r['tok']:>7} | {r['iet']:>6} | {str(r['cost']):>4} | {r['secs']:>4} "
                          f"\u2016 {str(r['web'])+r['web_flag']:>3} | {str(r['tools']):>5} | "
                          f"{str(r['turns']):>4} | {r['di']:>2} | {r['mcp']:>3} | "
                          f"{r['cache']:>5} | {r['bash']:>4}")
                print("-" * len(HDR))


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__); sys.exit(1)
    main(sys.argv[1:])
