#!/usr/bin/env python3
"""Generate the two LIET figures in docs/liet.md using the grounding tool itself.

These are illustrative *synthetic* datasets (the "well-known package" regime from
docs/liet.md), fed through `grounding analyze --view liet --svg --oracle-from-plugin`
so the figures are produced by the same renderer that draws real eval curves.

  Figure 1: AGENTS.md answers all 6 rungs, riding under the oracle (efficiency premium).
  Figure 2: identical, except AGENTS.md fails rung 6 -> no point plotted; the oracle's
            cost there is drawn as the ceiling (max price of generalization).

Run from the repo root:  python3 eng/gen-liet-figures.py
"""
import json, os, subprocess, sys, tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BIN = os.path.join(ROOT, "src/grounding/bin/Release/net11.0/grounding")
DOCS = os.path.join(ROOT, "docs")

# Per-rung IET targets (raw units). IET model reduces to 5*output when input/cache are 0,
# so output = target // 5 reproduces the target IET. None = the arm fails that rung.
BASELINE = [300, 350, 2000, None, None, None]   # flat while known, spikes, then drops out
ORACLE   = [1000, 1050, 1200, 1350, 1500, 1650] # SKILL.md: gentle rise, high doc tax, answers all
AGENTS_1 = [550, 580, 900, 1050, 1150, 1250]    # Figure 1: answers all 6, under the oracle
AGENTS_2 = [550, 580, 900, 1050, 1150, None]    # Figure 2: fails rung 6


def arm(iet):
    """One arm's metrics for a rung. iet=None => the arm failed (functional assertion fails)."""
    passed = iet is not None
    return {
        "metrics": {
            "inputTokens": 0,
            "outputTokens": (iet // 5) if passed else 0,
            "cacheReadTokens": 0,
            "cacheWriteTokens": 0,
            "assertionResults": [
                {"assertion": {"type": 2, "path": "Program.cs", "value": "x"}, "passed": passed}
            ],
        }
    }


def dataset(agents):
    scenarios = []
    for i in range(6):
        scenarios.append({
            "scenarioName": f"{i + 1}: rung {i + 1}",
            "baseline": arm(BASELINE[i]),
            "skilledIsolated": arm(agents[i]),
            "skilledPlugin": arm(ORACLE[i]),
        })
    return {"model": "claude-haiku-4.5", "judgeModel": "claude-haiku-4.5",
            "verdicts": [{"skillName": "example", "scenarios": scenarios}]}


def gen(agents, out_svg):
    with tempfile.TemporaryDirectory() as d:
        ds = os.path.join(d, "fig.json")
        with open(ds, "w") as f:
            json.dump(dataset(agents), f)
        subprocess.run([BIN, "analyze", ds, "--view", "liet", "--no-title",
                        "--oracle-from-plugin", "--svg", out_svg], check=True,
                       cwd=ROOT, stdout=subprocess.DEVNULL)
        print(f"wrote {out_svg}")


if __name__ == "__main__":
    if not os.path.exists(BIN):
        sys.exit(f"build the tool first: dotnet build src/grounding/grounding.csproj -c Release")
    gen(AGENTS_1, os.path.join(DOCS, "liet-curve-figure.svg"))
    gen(AGENTS_2, os.path.join(DOCS, "liet-curve-figure-2.svg"))
