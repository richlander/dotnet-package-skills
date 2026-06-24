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

# When True, card renderers omit their own `### …` heading (the embedding doc supplies
# the section heading instead) and fold the agent model into the italic descriptor line.
NO_TITLE = False

ARMS = [("baseline", "baseline"), ("skilledIsolated", "isolated"), ("skilledPlugin", "plugin")]

# --- Grading thresholds (uniform — applied identically to every model). ---
# The card grades grounding's measured effect vs baseline with ONE model-independent
# rubric (BETTER / NEUTRAL / WORSE — see _grade). Grading keys off OBJECTIVE axes only:
# success rate, open-web archaeology, and cost/IET — never a subjective judge-quality diff
# (the top 1pt of the 1-5 judge scale is instruction-sensitive noise; see judge-philosophy doc).
# The *ship decision* (which models must be BETTER vs merely not-WORSE) is higher-level
# analysis, not the card's job — see docs §3.
GATE = dict(
    iet_win_frac=0.25,        # BETTER: >=25% IET reduction vs baseline, OR
    cost_win_frac=0.25,       # BETTER: >=25% cost reduction vs baseline, OR resourcefulness→0
    iet_harm_cap_frac=0.10,   # WORSE: grounded IET may not exceed baseline by >10%
    cost_harm_cap_frac=0.10,  # WORSE: grounded cost may not exceed baseline by >10%
    out_inflate_frac=0.05,    # WORSE (secondary guard): output tokens may not rise more than 5%
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
    """Mean-per-scenario aggregates for each arm.

    Reports each arm in ISOLATION (no cross-arm judge diff). Key axes:
      succ  success rate — a scenario succeeds for an arm if every functional assertion
            passed AND the judge's overall quality cleared the floor (>= 4.0, i.e. "meets
            expectations"). The judge's 1-5 score is used ONLY as this pass/fail floor; its
            top band (the 4->5 gradient) is discarded as subjective noise.
      arch  resourcefulness — out-of-sandbox lookups (web + local NuGet-cache rummaging) the
            agent had to do to recover the API. High = had to be resourceful; grounding's job
            is to drive this to ~0. Measured objectively from the timeline, not the judge.
    """
    acc = {k: dict(quals=[], fp=0, ft=0, succ=0, iet=0.0, cost=0.0, tok=0.0, out=0.0, web=0, cache=0, n=0)
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
            fp_i, ft_i = int(fp), int(ft)
            a["fp"] += fp_i; a["ft"] += ft_i
            # Scenario success: all assertions passed AND judge cleared the >=4 floor.
            if ft_i > 0 and fp_i == ft_i and (q is None or q >= 4.0):
                a["succ"] += 1
            a["iet"] += r["iet"]; a["cost"] += r["cost"]; a["tok"] += r["tok"]
            a["out"] += r["out"]; a["web"] += r["web"]; a["cache"] += r["cache"]
    out = {}
    for key, _ in ARMS:
        a = acc[key]; n = max(a["n"], 1)
        out[key] = dict(
            qual=(sum(a["quals"]) / len(a["quals"])) if a["quals"] else None,
            fp=a["fp"], ft=a["ft"], succ=a["succ"],
            iet=a["iet"] / n, cost=a["cost"] / n, tok=a["tok"] / n, out=a["out"] / n,
            web=a["web"], cache=a["cache"], n=a["n"],
        )
    return out


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


def _pct(new, old):
    """Percent change new-vs-old (negative = new is smaller/cheaper)."""
    return ((new - old) / old * 100.0) if old else 0.0


def _arch(a):
    return a["web"] + a["cache"]


# Shared headline-metric spec. Each entry drives every card:
#   key, label, raw(a)->str, diff(new,old)->str, lower_is_better
# `diff` expresses the *change from old to new* in the metric's natural unit:
# % for token/cost magnitudes, absolute Δ for quality, count Δ for func, an arrow
# for archaeology. lower_is_better drives the WIN/benefit interpretation only.
_METRICS = [
    ("success",  "success (scenarios)",
     lambda a: f"{a['succ']}/{a['n']}",
     lambda n, o: f"{n['succ'] - o['succ']:+d} ({n['succ']}/{n['n']})",
     False),
    ("func",  "func passed (assertions)",
     lambda a: f"{a['fp']}/{a['ft']}",
     lambda n, o: f"{n['fp'] - o['fp']:+d} ({n['fp']}/{n['ft']})",
     False),
    ("arch",  "resourcefulness (archaeology)",
     lambda a: f"{_arch(a)}",
     lambda n, o: f"{_arch(o)}→{_arch(n)}",
     True),
    ("iet",   "IET",
     lambda a: f"{a['iet']:.0f}",
     lambda n, o: f"{_pct(n['iet'], o['iet']):+.0f}%",
     True),
    ("out",   "output tok",
     lambda a: f"{a['out']:.0f}",
     lambda n, o: f"{_pct(n['out'], o['out']):+.0f}%",
     True),
    ("cost",  "cost",
     lambda a: f"{a['cost']:.2f}",
     lambda n, o: f"{_pct(n['cost'], o['cost']):+.0f}%",
     True),
]


def _grade(base, grnd):
    """Uniform verdict — the **same grading for every model**, keyed off OBJECTIVE axes only
    (success rate, open-web archaeology, cost/IET). It does NOT use a judge-quality diff: the
    judge's score enters only as the >=4 success floor (see aggregate); its subjective top band
    is discarded. The card grades grounding's measured effect vs baseline; it does not decide
    shipping (that is higher-level analysis — see docs §3).

      BETTER  — same-or-better success, no harm, AND a real win: IET/cost cut past the bar, or
                resourcefulness (archaeology) eliminated.
      WORSE   — grounding regressed something objective: success dropped, the grounded arm did
                open-web archaeology, or cost/IET/output inflated past the cap.
      NEUTRAL — neither: success held, but no material efficiency win.
    """
    iet  = _pct(grnd["iet"],  base["iet"])     # − = cheaper
    cost = _pct(grnd["cost"], base["cost"])
    out  = _pct(grnd["out"],  base["out"])
    dsucc = grnd["succ"] - base["succ"]        # < 0 => grounding solved fewer scenarios
    b_arch, g_arch = _arch(base), _arch(grnd)
    tail = (f"success {grnd['succ']}/{grnd['n']} vs {base['succ']}/{base['n']}, "
            f"resourcefulness {b_arch}→{g_arch}, IET {iet:+.0f}%, cost {cost:+.0f}%")

    worse = []
    if dsucc < 0:                                 worse.append(f"success {dsucc:+d}")
    if grnd["web"] > 0:                           worse.append(f"web archaeology {grnd['web']}")
    if iet  > GATE["iet_harm_cap_frac"]  * 100:   worse.append(f"IET +{iet:.0f}%")
    if cost > GATE["cost_harm_cap_frac"] * 100:   worse.append(f"cost +{cost:.0f}%")
    if out  > GATE["out_inflate_frac"]   * 100:   worse.append(f"output +{out:.0f}%")
    if worse:
        return f"**WORSE** — {', '.join(worse)} ({tail})"

    if (dsucc > 0 or -iet >= GATE["iet_win_frac"] * 100 or -cost >= GATE["cost_win_frac"] * 100
            or (b_arch > 0 and g_arch == 0)):
        return f"**BETTER** — {tail}"

    return f"**NEUTRAL** — no material change ({tail})"

def print_primary(path):
    """① Primary card — one model, Baseline vs AGENTS.md (the grounding tool). The data."""
    a = _load_arm(path)
    base = a["agg"]["baseline"]; grnd = a["agg"]["skilledPlugin"]
    sn = a["sn"]; gtok = grounding_tokens(sn)
    if not NO_TITLE:
        print(f"### Grounding eval — {sn} · `{a['model']}`\n")
    mpref = f"`{a['model']}` · " if NO_TITLE else ""
    print(f"_{mpref}Baseline (no grounding) vs `AGENTS.md`"
          + (f" (~{gtok} tok, via grounding tool)" if gtok else "")
          + f". Judge `{a['judge']}`. Means across scenarios._\n")
    print("| Metric | Baseline | AGENTS.md |")
    print("| --- | ---: | ---: |")
    for _, label, raw, _diff, _lb in _METRICS:
        print(f"| {label} | {raw(base)} | {raw(grnd)} |")
    print(f"\n> **Conclusion:** {_grade(base, grnd)}.")


def print_model_diff(paths):
    """② Model-diff card — columns = models, value = AGENTS lift over baseline per metric.
    Shows whether the grounding effect holds across tiers."""
    arms = [a for a in (_load_arm(p) for p in paths) if not a["readme"]]
    # mini first, then frontier; stable within tier by model name
    arms.sort(key=lambda a: (0 if a["tier"] == "mini" else 1, a["model"]))
    sn = arms[0]["sn"]
    if not NO_TITLE:
        print(f"### Model-diff — {sn} · AGENTS.md lift over baseline\n")
    print("_Each cell = grounded (AGENTS.md) change vs that model's own baseline. "
          "count Δ for success/func, before→after for resourcefulness, "
          "% for IET/output/cost (− = cheaper)._\n")
    head = "| Metric | " + " | ".join(f"`{a['model']}`" for a in arms) + " |"
    print(head); print("| --- |" + " ---: |" * len(arms))
    for _, label, _raw, diff, _lb in _METRICS:
        cells = [diff(a["agg"]["skilledPlugin"], a["agg"]["baseline"]) for a in arms]
        print(f"| {label} | " + " | ".join(cells) + " |")
    verdicts = [_grade(a["agg"]["baseline"], a["agg"]["skilledPlugin"])
                for a in arms]
    print("| **→ verdict** | " + " | ".join(verdicts) + " |")


def print_source_diff(paths):
    """③ Source-diff card — one model, single column = the benefit AGENTS.md adds over
    README.md (both surfaced via the grounding tool). Is AGENTS worth authoring?"""
    arms = [_load_arm(p) for p in paths]
    agents = next((a for a in arms if not a["readme"]), None)
    readme = next((a for a in arms if a["readme"]), None)
    if not agents or not readme:
        print("source-diff needs one AGENTS.md dataset and one README dataset "
              "(a path containing 'readme')."); return
    ag = agents["agg"]["skilledPlugin"]; rd = readme["agg"]["skilledPlugin"]
    sn = agents["sn"]
    if not NO_TITLE:
        print(f"### Source-diff — {sn} · `{agents['model']}` · AGENTS.md benefit over README.md\n")
    mpref = f"`{agents['model']}` · " if NO_TITLE else ""
    print(f"_{mpref}Both surfaced via the grounding tool; baseline removed. Single column = "
          "AGENTS.md change vs README.md (− = AGENTS cheaper on cost metrics, "
          "+ on success/func, lower resourcefulness = AGENTS more self-sufficient). "
          "This is what authoring AGENTS.md buys over the README floor._\n")
    print("| Metric | AGENTS.md − README.md |")
    print("| --- | ---: |")
    for _, label, _raw, diff, _lb in _METRICS:
        print(f"| {label} | {diff(ag, rd)} |")
    print(f"\n> **Conclusion:** {_grade(rd, ag)} _(AGENTS.md graded against the README floor)._")


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
    """① Primary card(s). Renders one Baseline-vs-AGENTS.md card per non-README model
    dataset (README datasets are ignored here — see --source-diff). A shared legend follows."""
    arms = [_load_arm(p) for p in paths]
    model_files = [a["path"] for a in arms if not a["readme"]]
    if not model_files:
        print("--card needs at least one AGENTS.md dataset (non-'readme' path)."); return
    for i, p in enumerate(model_files):
        if i:
            print()
        print_primary(p)
    print("\n**Legend & grades** — full term table at "
          "[grounding-lifecycle.md §4](https://github.com/richlander/dotnet-package-grounding/blob/main/docs/grounding-lifecycle.md#4-evaluate--the-three-cards). "
          "In short, each metric is read **per arm in isolation** (no judge-quality diff): "
          "_success_ = func assertions pass **and** judge ≥4 floor (higher better); "
          "_resourcefulness (archaeology)_ = web+cache lookups to recover the API — grounding drives it to 0, so lower is the win, and grounded **web** must be 0; "
          "_IET / output tok / cost_ = token/spend cost (lower better). "
          "**Conclusion:** **BETTER** = success held + a real win (more solved, resourcefulness eliminated, or IET/cost cut), "
          "**NEUTRAL** = success held with no material win, **WORSE** = success dropped, grounded web archaeology, or cost/IET/output inflated past the cap.\n")
    print("> Note: even ungrounded, the baseline self-grounds from the restored NuGet cache "
          "(README/AGENTS are packed in the nupkg) and the open web — so its resourcefulness count is a "
          "**lower bound** and grounding's advantage is understated.\n")


if __name__ == "__main__":
    args = sys.argv[1:]
    if "--no-title" in args:
        NO_TITLE = True
        args = [a for a in args if a != "--no-title"]
    if not args:
        print(__doc__); sys.exit(1)
    if args[0] in ("--card", "--tools-card", "--web-card", "--model-diff", "--source-diff"):
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
        elif args[0] == "--web-card":
            print_web_card(files)
        elif args[0] == "--model-diff":
            print_model_diff(files)
        else:
            print_source_diff(files)
        sys.exit(0)
    main(args)
