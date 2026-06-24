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
import json, sys, glob, os, re
from collections import Counter

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
    iet_harm_cap_frac=0.10,   # FRONTIER HARM (headline): grounded IET may not exceed baseline by >10%
    out_inflate_frac=0.05,    # FRONTIER HARM (secondary guard): output tokens may not rise more than 5%
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
    """HARM tier (frontier). Harm is a NUMBER, not a bool: the headline is the **IET diff from
    baseline** (grounded − baseline), held under a hard cap (a budget, not zero — grounding may
    cost a little on a model that didn't need it, not a lot). Guards: no func/quality regression,
    no web archaeology. Output tokens are a visible secondary guard so a pure output blow-up isn't
    masked when input nets down. Returns (ok, iet_harm_frac, rows)."""
    rows = []
    ok = True
    # Headline harm number: IET inflation vs baseline (negative = grounding is cheaper, no harm).
    iet_frac = (grnd["iet"] - base["iet"]) / base["iet"] if base["iet"] else 0.0
    h_iet = iet_frac <= GATE["iet_harm_cap_frac"]; ok &= h_iet
    rows.append(f"{'PASS' if h_iet else 'FAIL'}  HARM = IET diff {iet_frac*100:+.0f}% vs baseline "
                f"(cap +{GATE['iet_harm_cap_frac']*100:.0f}%)")
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
    rows.append(f"{'PASS' if h_out else 'FAIL'}  output-token guard "
                f"(Δ {out_frac*100:+.0f}%; cap +{GATE['out_inflate_frac']*100:.0f}%)")
    h_web = grnd["web"] == 0; ok &= h_web
    rows.append(f"{'PASS' if h_web else 'FAIL'}  no web archaeology (web={grnd['web']}; cache peeks allowed, here {grnd['cache']})")
    return ok, iet_frac, rows


def _fmt_q(q):
    return f"{q:.2f}" if isinstance(q, (int, float)) else "-"


def _load_arm(path):
    """Load one results.json -> (model, judge, tier, skillName, agg, is_readme)."""
    d = json.load(open(path))
    model = d.get("model", "?"); judge = d.get("judgeModel", model)
    tier = model_tier(model)
    v = (d.get("verdicts") or [{}])[0]
    sn = v.get("skillName", "?")
    agg = aggregate(v.get("scenarios", []))
    is_readme = "readme" in os.path.basename(path).lower()
    return dict(model=model, judge=judge, tier=tier, sn=sn, agg=agg, readme=is_readme, path=path)


def _three_col_table(base, readme_plg, agents_plg):
    """Baseline | README (fallback) | AGENTS.md. README column omitted if not provided."""
    cols = ["Baseline"]
    if readme_plg is not None:
        cols.append("README (fallback)")
    cols.append("AGENTS.md (grounding tool)")
    head = "| Metric | " + " | ".join(cols) + " |"
    sep = "| --- |" + " ---: |" * len(cols)
    print(head); print(sep)

    def row(label, fn):
        cells = [fn(base)]
        if readme_plg is not None:
            cells.append(fn(readme_plg))
        cells.append(fn(agents_plg))
        print(f"| {label} | " + " | ".join(cells) + " |")

    row("quality (1–5)", lambda a: _fmt_q(a["qual"]))
    row("func passed", lambda a: f"{a['fp']}/{a['ft']}")
    row("IET (mean)", lambda a: f"{a['iet']:.0f}")
    row("output tok (mean)", lambda a: f"{a['out']:.0f}")
    row("cost (mean)", lambda a: f"{a['cost']:.2f}")
    row("gross tok (mean)", lambda a: f"{a['tok']:.0f}")
    row("archaeology (web+cache)", lambda a: f"{a['web'] + a['cache']}")


def _tier_section(arms, tier_label, gate_label):
    """Render one tier's 3-col table + gate. arms: dict with 'agents' and optional 'readme'."""
    agents = arms.get("agents"); readme = arms.get("readme")
    primary = agents or readme
    base = primary["agg"]["baseline"]
    agents_plg = agents["agg"]["skilledPlugin"] if agents else None
    readme_plg = readme["agg"]["skilledPlugin"] if readme else None
    model = primary["model"]; judge = primary["judge"]
    print(f"#### {tier_label} — model `{model}`, judge `{judge}`\n")
    _three_col_table(base, readme_plg, agents_plg if agents_plg is not None else base)

    if agents:
        if agents["tier"] == "frontier":
            passed, iet_harm, rows = gate_frontier(base, agents_plg)
            within = iet_harm <= GATE["iet_harm_cap_frac"]
            print(f"\n**{gate_label} (AGENTS.md vs baseline): HARM = IET diff "
                  f"{iet_harm*100:+.0f}% vs baseline ({'within cap' if within else 'OVER cap'}) "
                  f"— overall {'✅ PASS' if passed else '❌ FAIL'}**\n")
            for line in rows:
                print(f"- {line}")
        else:
            passed, harm, win = gate_mini(base, agents_plg)
            print(f"\n**{gate_label} (AGENTS.md vs baseline): "
                  f"{'✅ PASS' if passed else '❌ FAIL'}**\n")
            print("_Guards (must hold):_")
            for line in harm:
                print(f"- {line}")
            print("\n_Win (at least one must clear):_")
            for line in win:
                print(f"- {line}")
    # README-as-fallback verdict (judged against README's own run baseline).
    if readme:
        rb = readme["agg"]["baseline"]; rp = readme["agg"]["skilledPlugin"]
        if readme["tier"] == "frontier":
            rpass, _, _ = gate_frontier(rb, rp)
        else:
            rpass, _, _ = gate_mini(rb, rp)
        dq = (rp["qual"] or 0) - (rb["qual"] or 0)
        note = "clears the bar — AGENTS.md would be unnecessary" if rpass else \
               "does NOT clear the bar — AGENTS.md is needed"
        print(f"\n_Fallback check — **README alone** {'✅' if rpass else '❌'}: {note} "
              f"(quality Δ {dq:+.2f} vs its own baseline). Grounding surfaces README.md when no "
              f"AGENTS.md is present, so this is the real floor AGENTS.md must beat._")


# --- diagnostic cards: what the agent reached for (tools) and searched (web) -----------
# These are INFRA DIAGNOSTICS, not the ship card. They expose the out-of-sandbox behavior
# the headline archaeology number summarizes: which external programs the agent invoked
# and what it searched the web for. The point they make: grounding collapses both to ~0.

_EXTRA_TOOLS = {"dotnet-inspect", "ilspycmd", "dotnet-ildasm", "ildasm", "dotnet-depends",
                "dotnet-linq2db", "dotnet-script", "dotnet-repl"}
_SHELL_ARCH = {"curl", "wget", "find", "ls", "cat", "grep", "strings", "file", "jq",
               "head", "tail", "nm", "objdump", "unzip", "xxd", "zcat", "gunzip", "tree"}
_DROP_LEAD = {"cd", "sudo", "env", "rm", "mv", "cp", "mkdir", "echo", "true", "export",
              "set", "for", "do", "done", "if", "then", "fi", "#", "while", ":"}


def _strip_heredocs(cmd):
    return re.sub(r"<<-?\s*'?\"?(\w+)'?\"?.*?(?:\n|$).*?(?:^|\n)\s*\1\b",
                  " <<heredoc>> ", cmd, flags=re.S | re.M)


def _lead_progs(cmd):
    """Leading program of each &&/;/| -separated segment, heredoc bodies removed.
    A `dotnet <path>/X.dll` invocation is surfaced by its DLL basename (e.g. `ilspycmd.dll`)
    so decompilers/inspectors run via the framework don't masquerade as a build."""
    out = []
    for part in re.split(r"&&|\|\||;|\|", _strip_heredocs(cmd)):
        toks = part.strip().split()
        if not toks or toks[0] in _DROP_LEAD:
            continue
        p = toks[0]
        if p == "dotnet" and len(toks) > 1:
            if toks[1] == "tool" and len(toks) > 2:
                p = "dotnet tool " + toks[2]
            elif toks[1].endswith(".dll"):
                p = "dll:" + os.path.basename(toks[1])
            else:
                p = "dotnet " + toks[1]
        out.append(p)
    return out


def _extract_behavior(path):
    """Per-arm Counters of tool invocations and web targets parsed from the event log."""
    d = json.load(open(path))
    scn = (d.get("verdicts") or [{}])[0].get("scenarios", [])
    arms = {}
    for arm, _ in ARMS:
        tc, web, kw = Counter(), Counter(), Counter()
        for s in scn:
            for e in (s.get(arm, {}).get("metrics", {}) or {}).get("events") or []:
                if e.get("type") != "tool.execution_start":
                    continue
                data = e.get("data") or {}
                name = data.get("toolName", "")
                try:
                    a = json.loads(data.get("arguments", "{}"))
                except Exception:
                    a = {}
                if name == "bash":
                    for p in _lead_progs(a.get("command", "")):
                        tc[p] += 1
                elif name == "web_fetch":
                    m = re.match(r"https?://([^/]+)(/[^?#]*)?", a.get("url", ""))
                    if m:
                        web[m.group(1)] += 1
                        for seg in re.findall(r"[A-Za-z][A-Za-z0-9.\-]{2,}", m.group(2) or ""):
                            if seg.lower() not in ("index", "json", "packages", "tree",
                                                   "blob", "main", "master", "wiki"):
                                kw[seg.lower()] += 1
                elif name == "web_search":
                    for w in re.findall(r"[A-Za-z][A-Za-z0-9.\-]{2,}", a.get("query", "").lower()):
                        kw[w] += 1
        arms[arm] = dict(tools=tc, web=web, kw=kw)
    return arms


def _arm_label(key):
    return {"baseline": "Baseline", "skilledIsolated": "AGENTS (isolated)",
            "skilledPlugin": "AGENTS (grounding tool)"}.get(key, key)


def _classify_tool(k):
    """Bucket a program name -> 'extra' | 'shell' | 'build' | None (ignore)."""
    base = k[4:] if k.startswith("dll:") else k
    stem = base.split()[0]
    decomp = ("ilspy", "ildasm", "inspect")
    if k.startswith("dotnet tool") or stem in _EXTRA_TOOLS \
            or k in ("dotnet repl", "dotnet script") \
            or (k.startswith("dll:") and any(x in base.lower() for x in decomp)):
        return "extra"
    if k.startswith("dll:"):
        return "shell"          # running some other DLL directly = probing the artifact
    if stem in _SHELL_ARCH:
        return "shell"
    if k.startswith("dotnet "):
        return "build"
    return "shell"              # any other bare external binary is archaeology, not build


def print_tools_card(paths, topn=8):
    """Diagnostic: top-N programs the agent invoked, per arm, split extra vs shell vs build."""
    for path in paths:
        b = _extract_behavior(path)
        d = json.load(open(path))
        model = d.get("model", "?")
        sn = (d.get("verdicts") or [{}])[0].get("skillName", "?")
        print(f"### Diagnostic — top tools, {sn} (`{model}`)\n")
        print("_Out-of-sandbox programs the agent invoked via `bash`, ranked. "
              "**Extra** = tools that merely happen to be in the environment "
              "(decompilers, inspectors, self-installed global tools) — the contamination "
              "we want gone from the baseline. **Shell-archaeology** = basic reverse-engineering "
              "(`curl`, `find`, `strings`, running the artifact DLL, …). **Build** = expected SDK "
              "calls. Grounded arms should show only Build._\n")
        print(f"| Arm | Extra tools (count) | Shell-archaeology | Build/SDK |")
        print(f"| --- | --- | --- | --- |")
        for arm, _ in ARMS:
            t = b[arm]["tools"]
            buckets = {"extra": [], "shell": [], "build": []}
            for k, v in t.most_common():
                buckets[_classify_tool(k)].append((k, v))
            disp = lambda k: k[4:] if k.startswith("dll:") else k
            fmt = lambda xs: ", ".join(f"`{disp(k)}`×{v}" for k, v in xs[:topn]) or "—"
            star = " ⚠️" if buckets["extra"] else ""
            print(f"| {_arm_label(arm)}{star} | {fmt(buckets['extra'])} | "
                  f"{fmt(buckets['shell'])} | {fmt(buckets['build'])} |")
        print()


def print_web_card(paths, topn=10):
    """Diagnostic: top-N web targets/keywords the agent searched, per arm."""
    for path in paths:
        b = _extract_behavior(path)
        d = json.load(open(path))
        model = d.get("model", "?")
        sn = (d.get("verdicts") or [{}])[0].get("skillName", "?")
        print(f"### Diagnostic — top web targets, {sn} (`{model}`)\n")
        print("_What the agent reached for on the open web (`web_fetch` domains + URL/query "
              "keywords). The reality a clean baseline must not need; grounded arms should be "
              "empty. Keywords reveal what the model went looking for — note any that name a "
              "grounding tool the model knows by reputation (e.g. `dotnet-inspect`)._\n")
        print(f"| Arm | Web domains (count) | Top keywords |")
        print(f"| --- | --- | --- |")
        for arm, _ in ARMS:
            dom = b[arm]["web"].most_common(topn)
            kw = b[arm]["kw"].most_common(topn)
            fmtd = ", ".join(f"`{k}`×{v}" for k, v in dom) or "—"
            fmtk = ", ".join(f"`{k}`×{v}" for k, v in kw) or "—"
            star = " ⚠️" if dom else ""
            print(f"| {_arm_label(arm)}{star} | {fmtd} | {fmtk} |")
        print()


def print_card(paths):
    """Render a complete ship card from one or more datasets.

    Datasets are grouped by tier (mini = haiku/sonnet/flash; frontier = opus/gpt-5/gemini)
    and, within a tier, by grounding source (a path containing 'readme' is the README arm).
    The card tells the two-sided story: a mini-tier WIN and a frontier-tier HARM number.
    """
    arms = [_load_arm(p) for p in paths]
    sn = arms[0]["sn"]
    gtok = grounding_tokens(sn)
    groups = {"mini": {}, "frontier": {}}
    for a in arms:
        groups[a["tier"]]["readme" if a["readme"] else "agents"] = a

    print(f"### Grounding eval — {sn}\n")
    print("_About this grounding._ `AGENTS.md` provides RAG-style, section-addressable "
          "information for agents, surfaced by a grounding tool like the NuGet MCP. `README.md` "
          "offers nice introductions and progressive disclosure for untrained humans; `AGENTS.md` "
          "fills in targeted, evaluated gaps for trained models. Its presence removes any pressure "
          "to make `README.md` agent-efficient or catered.\n")
    if gtok:
        print(f"Grounding: `grounding/{sn}/AGENTS.md` (~{gtok} tok loaded per grounded arm). "
              f"Means across scenarios.\n")
    print("_The ship decision is **two-sided**: a smaller model (mini tier) must show a **WIN**, "
          "and a frontier model must show **bounded HARM** (a number — the IET diff from baseline, under a hard cap). Both are read off the **AGENTS.md** column; "
          "the **README** column is the fallback floor (what grounding surfaces when no AGENTS.md "
          "ships) — if README already clears the bar, AGENTS.md is unnecessary._\n")

    if groups["mini"]:
        _tier_section(groups["mini"], "Mini tier — the WIN", "Mini WIN gate")
    else:
        print("#### Mini tier — the WIN\n\n_⏳ pending — run a haiku/sonnet dataset._")
    print()
    if groups["frontier"]:
        _tier_section(groups["frontier"], "Frontier tier — the HARM check", "Frontier HARM gate")
    else:
        print("#### Frontier tier — the HARM check\n\n_⏳ pending — run an opus/gpt-5/gemini "
              "dataset (`MODELS=claude-opus-4.6 eng/run-<unit>-6q.sh`) to complete the decision._")

    print("\n**Legend**\n")
    print("_Columns — every column runs the **same** task; they differ only in what grounding the "
          "agent is given:_")
    print("- **Baseline** — no grounding. The agent has model knowledge only and falls back to "
          "**archaeology** (the row below) — searching outside the sandbox to reconstruct what "
          "grounding would have told it. The reality today for a package the model doesn't know.")
    print("- **README (fallback)** — the package `README.md` is surfaced as grounding. This is what "
          "a grounding tool returns when no `AGENTS.md` ships, so it is the floor AGENTS.md must "
          "beat. README is written for humans, not models.")
    print("- **AGENTS.md (grounding tool)** — the curated package grounding, surfaced by a grounding "
          "tool (NuGet MCP, `dotnet-inspect`, …). The ship gate is read off this column.")
    print("\n_Rows — the metrics:_")
    print("- **quality** — pairwise-judge rubric score, 1–5 (higher better).")
    print("- **func passed** — functional assertions met (build + file + run-output checks); "
          "100% is the target.")
    print("- **IET** — Input-Equivalent Tokens = (input − cache-reads) + output; the "
          "cache-discounted token cost (lower better).")
    print("- **output tok** — output/thinking tokens (the priciest, most variable component; the "
          "key frontier-harm signal).")
    print("- **cost** — premium-request multiplier, cache-discounted (lower better).")
    print("- **gross tok** — raw input+output incl. cache re-reads (context only; not the bill).")
    print("- **archaeology (web+cache)** — out-of-sandbox lookups the agent makes to recover "
          "missing knowledge: web fetch/search **plus** rummaging the local NuGet cache "
          "(generalizes to any external source — decompiled DLLs, etc.). An informative signal, "
          "not a hard gate metric; grounding should collapse it toward 0. The **web** portion is "
          "the hard guard (a grounded run must never resort to the internet).")
    print("\n> Quality Δ is a **lower bound** — even ungrounded, the baseline can self-ground "
          "from the restored NuGet cache (README/AGENTS are packed in the nupkg), so it "
          "understates grounding's value. Starting cache state is not a variable. "
          "See docs/grounding-eval-methodology.md.\n")


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        print(__doc__); sys.exit(1)
    if args[0] in ("--card", "--tools-card", "--web-card"):
        rest = args[1:]
        if not rest:
            print(f"{args[0]} needs a results.json path"); sys.exit(1)
        files = []
        for p in rest:
            files.extend(glob.glob(p, recursive=True) or [p])
        files = sorted(set(files))
        if args[0] == "--card":
            print_card(files)
        elif args[0] == "--tools-card":
            print_tools_card(files)
        else:
            print_web_card(files)
        sys.exit(0)
    main(args)
