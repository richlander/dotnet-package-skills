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
  eng/analyze-6q.py --card data/markout-6q/markout.n3.haiku.json   # copy-paste PR dump + gate
"""
import json, sys, glob, os

ARMS = [("baseline", "baseline"), ("skilledIsolated", "isolated"), ("skilledPlugin", "plugin")]

# --- Ship gate thresholds. See docs/grounding-eval-methodology.md. ---
# Pareto gate (authoring-principles §4), modeled on the decompiler quality-diff card:
# require a real WIN on the tier that needs grounding (mini, λ low — quality is the
# binding constraint, tokens are cheap), and tolerate ~ZERO HARM on the tier that does
# not (frontier, λ high). Frontier harm is the direct analog of the decompiler's "no
# drop in recovery, no increase in malformed": no quality regression and no output/
# thinking-token inflation. Correctness (func) may never regress on either tier.
GATE = dict(
    iet_win_frac=0.25,        # mini WIN: >=25% IET reduction vs baseline, OR
    cost_win_frac=0.25,       # mini WIN: >=25% cost reduction vs baseline, OR
    qual_win_delta=0.3,       # mini WIN: >=+0.3 quality lift vs baseline
    qual_regress_max=0.1,     # HARM (both tiers): quality may not drop more than this
    out_inflate_frac=0.05,    # FRONTIER HARM: output tokens may not rise more than 5%
)

FRONTIER_HINTS = ("opus", "sonnet", "gpt-5", "gpt5", "gemini-3", "gemini-2.5-pro")
MINI_HINTS = ("haiku", "mini", "flash", "small", "lite")


def model_tier(model):
    m = (model or "").lower()
    if any(h in m for h in FRONTIER_HINTS):
        return "frontier"
    if any(h in m for h in MINI_HINTS):
        return "mini"
    return "mini"  # default: treat unknown as the needs-it tier




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
        out=(m.get("outputTokens", 0) or 0),
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


def _num(x):
    return x if isinstance(x, (int, float)) else None


def aggregate(scenarios):
    """Mean-per-scenario aggregates for each arm: qual, func, iet, cost, tok, out, web."""
    acc = {k: dict(quals=[], fp=0, ft=0, iet=0.0, cost=0.0, tok=0.0, out=0.0, web=0, cache=0, n=0)
           for k, _ in ARMS}
    for sc in scenarios:
        for key, _ in ARMS:
            r = arm_row(sc.get(key))
            if not r:
                continue
            a = acc[key]; a["n"] += 1
            q = _num(r["qual"])
            if q is not None:
                a["quals"].append(q)
            fp, ft = r["func"].split("/")
            a["fp"] += int(fp); a["ft"] += int(ft)
            a["iet"] += r["iet"]; a["cost"] += r["cost"]; a["tok"] += r["tok"]
            a["out"] += r["out"]; a["web"] += r["web"]; a["cache"] += r["cache"]
    out = {}
    for key, _ in ARMS:
        a = acc[key]; n = max(a["n"], 1)
        out[key] = dict(
            qual=(sum(a["quals"]) / len(a["quals"])) if a["quals"] else None,
            fp=a["fp"], ft=a["ft"],
            iet=a["iet"] / n, cost=a["cost"] / n, tok=a["tok"] / n, out=a["out"] / n,
            web=a["web"], cache=a["cache"], n=a["n"],
        )
    return out


def gate_mini(base, grnd):
    """WIN tier (mini, λ low): require a real win; correctness may never regress."""
    harm, win = [], []
    ok = True
    dfunc = grnd["fp"] - base["fp"]
    h_func = dfunc >= 0; ok &= h_func
    harm.append(f"{'PASS' if h_func else 'FAIL'}  func no regression "
                f"(Δ {dfunc:+d}; {grnd['fp']}/{grnd['ft']} vs {base['fp']}/{base['ft']})")
    if base["qual"] is not None and grnd["qual"] is not None:
        dq = grnd["qual"] - base["qual"]
        h_q = dq >= -GATE["qual_regress_max"]; ok &= h_q
        harm.append(f"{'PASS' if h_q else 'FAIL'}  quality no regression "
                    f"(Δ {dq:+.2f}; floor −{GATE['qual_regress_max']})")
    h_web = grnd["web"] == 0; ok &= h_web
    harm.append(f"{'PASS' if h_web else 'FAIL'}  no web archaeology (web={grnd['web']}; cache peeks allowed, here {grnd['cache']})")
    iet_frac = (base["iet"] - grnd["iet"]) / base["iet"] if base["iet"] else 0
    cost_frac = (base["cost"] - grnd["cost"]) / base["cost"] if base["cost"] else 0
    w_iet = iet_frac >= GATE["iet_win_frac"]
    w_cost = cost_frac >= GATE["cost_win_frac"]
    w_q = (base["qual"] is not None and grnd["qual"] is not None
           and (grnd["qual"] - base["qual"]) >= GATE["qual_win_delta"])
    win.append(f"{'WIN ' if w_iet else '--  '}  IET reduction {iet_frac*100:.0f}% "
               f"(bar {GATE['iet_win_frac']*100:.0f}%)")
    win.append(f"{'WIN ' if w_cost else '--  '}  cost reduction {cost_frac*100:.0f}% "
               f"(bar {GATE['cost_win_frac']*100:.0f}%)")
    if base["qual"] is not None and grnd["qual"] is not None:
        win.append(f"{'WIN ' if w_q else '--  '}  quality lift Δ "
                   f"{grnd['qual']-base['qual']:+.2f} (bar +{GATE['qual_win_delta']})")
    return (ok and (w_iet or w_cost or w_q)), harm, win


def gate_frontier(base, grnd):
    """HARM tier (frontier, λ high): ZERO tolerance. The decompiler analog — no drop in
    recovery (func), no increase in malformed (quality), no output-token inflation."""
    rows = []
    ok = True
    dfunc = grnd["fp"] - base["fp"]
    h_func = dfunc >= 0; ok &= h_func
    rows.append(f"{'PASS' if h_func else 'FAIL'}  no correctness/recovery drop "
                f"(func Δ {dfunc:+d})")
    if base["qual"] is not None and grnd["qual"] is not None:
        dq = grnd["qual"] - base["qual"]
        h_q = dq >= -GATE["qual_regress_max"]; ok &= h_q
        rows.append(f"{'PASS' if h_q else 'FAIL'}  no quality regression "
                    f"(Δ {dq:+.2f}; floor −{GATE['qual_regress_max']})")
    out_frac = (grnd["out"] - base["out"]) / base["out"] if base["out"] else 0
    h_out = out_frac <= GATE["out_inflate_frac"]; ok &= h_out
    rows.append(f"{'PASS' if h_out else 'FAIL'}  no output/thinking-token inflation "
                f"(Δ {out_frac*100:+.0f}%; cap +{GATE['out_inflate_frac']*100:.0f}%)")
    h_web = grnd["web"] == 0; ok &= h_web
    rows.append(f"{'PASS' if h_web else 'FAIL'}  no web archaeology (web={grnd['web']}; cache peeks allowed, here {grnd['cache']})")
    return ok, rows


def _fmt_q(q):
    return f"{q:.2f}" if isinstance(q, (int, float)) else "-"


def print_card(path):
    d = json.load(open(path))
    model = d.get("model", "?"); judge = d.get("judgeModel", model)
    tier = model_tier(model)
    for v in d.get("verdicts", []):
        sn = v.get("skillName", "?")
        gtok = grounding_tokens(sn)
        scs = v.get("scenarios", [])
        agg = aggregate(scs)
        b, iso, plg = agg["baseline"], agg["skilledIsolated"], agg["skilledPlugin"]
        print(f"### Grounding eval — {sn} (model={model}, judge={judge}, tier={tier})\n")
        print("_About this grounding._ `AGENTS.md` provides RAG-style, section-addressable "
              "information for agents, surfaced by a grounding tool like the NuGet MCP. `README.md` "
              "offers nice introductions and progressive disclosure for untrained humans; `AGENTS.md` "
              "fills in targeted, evaluated gaps for trained models. Its presence removes any pressure "
              "to make `README.md` agent-efficient or catered.\n")
        if gtok:
            print(f"Grounding: `grounding/{sn}/AGENTS.md` (~{gtok} tok loaded per grounded arm). "
                  f"{b['n']} scenarios; means across scenarios.\n")
        print("| Metric | Baseline | Grounding tool |")
        print("| --- | ---: | ---: |")
        print(f"| quality (1–5) | {_fmt_q(b['qual'])} | {_fmt_q(plg['qual'])} |")
        print(f"| func passed | {b['fp']}/{b['ft']} | {plg['fp']}/{plg['ft']} |")
        print(f"| IET (mean) | {b['iet']:.0f} | {plg['iet']:.0f} |")
        print(f"| output tok (mean) | {b['out']:.0f} | {plg['out']:.0f} |")
        print(f"| cost (mean) | {b['cost']:.2f} | {plg['cost']:.2f} |")
        print(f"| gross tok (mean) | {b['tok']:.0f} | {plg['tok']:.0f} |")
        print(f"| archaeology (web+cache) | {b['web'] + b['cache']} | {plg['web'] + plg['cache']} |")
        print("\n**Legend**\n")
        print("_Columns — both run the **same** task; they differ only in whether the package "
              "grounding is available to the agent:_")
        print("- **Baseline** — no grounding. The agent has model knowledge only and falls back to "
              "**archaeology** (the row below) — searching outside the sandbox to reconstruct what "
              "grounding would have told it. This is the reality today for a package the model "
              "doesn't know.")
        print("- **Grounding tool** — the package grounding (`AGENTS.md`) is surfaced by a grounding "
              "tool, like the NuGet MCP, `dotnet-inspect`, or similar. The ship gate is read off this "
              "column.")
        print("\n_Rows — the metrics:_")
        print("- **quality** — pairwise-judge rubric score, 1–5 (higher better).")
        print("- **func passed** — functional assertions met (build + file + run-output checks); "
              "100% is the target.")
        print("- **IET** — Input-Equivalent Tokens = (input − cache-reads) + output; the "
              "cache-discounted token cost (lower better).")
        print("- **output tok** — output/thinking tokens (the priciest, most variable component).")
        print("- **cost** — premium-request multiplier, cache-discounted (lower better).")
        print("- **gross tok** — raw input+output incl. cache re-reads (context only; not the bill).")
        print("- **archaeology (web+cache)** — out-of-sandbox lookups the agent makes to recover "
              "missing knowledge: web fetch/search **plus** rummaging the local NuGet cache "
              "(generalizes to any external source — decompiled DLLs, etc.). An informative signal, "
              "not a hard gate metric; grounding should collapse it toward 0. The **web** portion is "
              "the hard guard (a grounded run must never resort to the internet).")
        # Gate evaluated on the grounding-tool arm (closest to the shipping experience).
        if tier == "frontier":
            passed, rows = gate_frontier(b, plg)
            print(f"\n**Frontier HARM gate (grounding tool vs baseline): "
                  f"{'✅ NO HARM' if passed else '❌ HARM'}** — zero tolerance.\n")
            for line in rows:
                print(f"- {line}")
            print("\n_This tier measures harm, not win: frontier quality is near the ceiling, so "
                  "grounding need not improve it — it must not damage it. A full ship decision also "
                  "needs a mini-tier WIN run._")
        else:
            passed, harm, win = gate_mini(b, plg)
            print(f"\n**Mini WIN gate (grounding tool vs baseline): {'✅ PASS' if passed else '❌ FAIL'}**\n")
            print("_Guards (must hold):_")
            for line in harm:
                print(f"- {line}")
            print("\n_Win (at least one must clear):_")
            for line in win:
                print(f"- {line}")
            print("\n_This tier measures the win. A full ship decision also needs a frontier-tier "
                  "NO-HARM run (zero output-token inflation, no quality regression)._")
        print("\n> Quality Δ is a **lower bound** — even ungrounded, the baseline can self-ground "
              "from the restored NuGet cache (README/AGENTS are packed in the nupkg), so it "
              "understates grounding's value. Starting cache state is not a variable. "
              "See docs/grounding-eval-methodology.md.\n")


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        print(__doc__); sys.exit(1)
    if args[0] == "--card":
        rest = args[1:]
        if not rest:
            print("--card needs a results.json path"); sys.exit(1)
        files = []
        for p in rest:
            files.extend(glob.glob(p, recursive=True))
        for f in sorted(set(files)):
            print_card(f)
        sys.exit(0)
    main(args)
