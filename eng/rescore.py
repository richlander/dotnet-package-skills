#!/usr/bin/env python3
"""Re-score skill-validator results under the grounding-specific model.

The upstream skill-validator improvement score is tuned for *skills*: quality is
0.70 of the weight, all efficiency (tokens+tools+time) is 0.10 and clamped, and
"tokens" is a flat input+output sum. That hides grounding's real value, which is
(a) task completion for weak agents and (b) *cost* (especially expensive output /
thinking tokens) for strong agents.

This re-scorer reads the saved results.json for one scenario across several agent
models and recomputes the verdict under the model we settled on.

Cost unit - IET (Input-Equivalent Tokens)
-----------------------------------------
Instead of per-model dollars we use a single vendor-neutral unit. Within a vendor
the price *ratios* between token classes are constant across every model, so the
per-model base price cancels out of any same-model arm comparison:

    Anthropic   cache_read 0.1x   cache_write 1.25x   output 5x   (Opus=Sonnet=Haiku)
    OpenAI      cache_read 0.1x   (no write premium)  output 6x   (GPT-5.5..nano)

So define, in units of base-input-tokens:

    IET = fresh_input + 0.1*cache_read + 1.25*cache_write + W*output

cache_read = 0.1 is literally identical across both vendors; the only knob is the
output weight W (5 Anthropic / 6 OpenAI). Default W=5 (Anthropic-native, the
conservative choice on the expensive bucket). Pass --w=6 to bias toward OpenAI.
There is no per-model price table to verify - only the durable ratios.

Scoring (the ONE fixed rubric - it is all just context):
    score = 0.70*dQuality + 0.30*costReduction        (costReduction in IET)
  - Quality stays dominant, but cost gets real teeth (0.30) and is output-weighted
    (W=5) rather than a flat token count, so displacing thinking tokens is rewarded
    ~5:1 against the input the grounding adds.
  - Tier behaviour EMERGES, it is not hardcoded: a mini model has a huge dQuality
    that swamps any token cost (it is "willing to pay"); a frontier model has
    dQuality ~ 0, so the cost term decides (it ships only if grounding is a big
    quality jump OR net-cheaper in IET).
  - Because grounding is auto-installed and un-removable, the headline verdict is a
    PARETO GATE, not a mean: SHIP iff it materially helps the weak tier AND does no
    meaningful harm to the frontier tier; RIP OUT iff it helps neither.

Usage:
  eng/rescore.py claude-haiku-4.5=<dir-or-json> claude-opus-4.6=<dir-or-json> ...
  eng/rescore.py --w=6 <model>=<path> ...     # OpenAI output weight
  ('=' or ':' both separate model from path)
"""
from __future__ import annotations
import json
import os
import sys

# IET weights in units of base-input-tokens. cache_read is identical across
# vendors (0.1); cache_write is Anthropic-only (OpenAI auto-caches, no premium);
# output W is the single knob (5 Anthropic / 6 OpenAI), overridable with --w=.
CACHE_READ_MULT = 0.10
CACHE_WRITE_MULT = 1.25
DEFAULT_OUTPUT_MULT = 5.0

# Scoring weights (the ONE fixed rubric).
W_QUALITY = 0.70
W_COST = 0.30

# Pareto-gate thresholds.
HELP_THRESHOLD = 0.20      # normalized quality gain that counts as "materially helps"
HARM_COST_FRAC = 0.10      # >10% net IET increase on a tier with no quality gain = harm


def clamp(x, lo=-1.0, hi=1.0):
    return max(lo, min(hi, x))


def arm_iet(m: dict, w_out: float) -> float:
    """Input-Equivalent Tokens for one arm from its averaged token metrics."""
    fresh_in = max(0, m["inputTokens"] - m.get("cacheReadTokens", 0) - m.get("cacheWriteTokens", 0))
    cr = m.get("cacheReadTokens", 0)
    cw = m.get("cacheWriteTokens", 0)
    out = m["outputTokens"]
    return (fresh_in
            + cr * CACHE_READ_MULT
            + cw * CACHE_WRITE_MULT
            + out * w_out)


# HIET - Haiku-Equivalent IET (cross-model cost view)
# ---------------------------------------------------
# IET normalizes the token *classes* within one model (output=5x input etc.), so it is a
# within-model cost proxy in units of that model's own input tokens - it does NOT capture
# that an Opus input token costs far more dollars than a Haiku one. HIET adds exactly that
# missing dimension: multiply IET by the model's input list-price relative to Haiku, so the
# result is expressed in *Haiku input-token equivalents* and is directly dollar-comparable
# across tiers. Anthropic list input price per MTok: Opus $15, Sonnet $3, Haiku $1.
#   HIET = IET * (input_price_model / input_price_haiku)
# Keep IET for same-model arm comparisons; read HIET when comparing Opus/Sonnet against the
# Haiku tier (e.g. "is the cheaper-IET Opus run actually cheaper in dollars?" - usually no).
HAIKU_PRICE_RATIO = {
    "opus": 15.0,
    "sonnet": 3.0,
    "haiku": 1.0,
}


def haiku_ratio(model: str) -> float:
    """Input-price multiplier of `model` relative to Claude Haiku (1.0). Unknown -> 1.0."""
    s = model.lower()
    for tier, ratio in HAIKU_PRICE_RATIO.items():
        if tier in s:
            return ratio
    return 1.0


def arm_hiet(m: dict, w_out: float, model: str) -> float:
    """Haiku-Equivalent IET: arm_iet scaled by the model's input price vs Haiku."""
    return arm_iet(m, w_out) * haiku_ratio(model)


# Delivery-normalized IET (fair cross-channel delivery cost)
# ----------------------------------------------------------
# Resident delivery is under-measured: an MCP server's tool schema (and the system prompt) is
# cache-*written* during an UNMEASURED setup phase (session.mcp_servers_loaded / tools_updated emit
# no usage event), so the arm only ever pays 0.1x cacheRead for it. A skill/directive instead lands
# as 1x *fresh* input on turn 1. Net: the same resident payload costs 0.1x via MCP but 1x via a
# directive - an apples-to-oranges discount that can be larger than the whole channel gap.
# The fix is convention, not new data: charge every arm's turn-1 resident prefix once at full price.
# The only unfairly-discounted quantity is each arm's turn-1 cacheRead (its pre-warmed prefix), so:
#   IET_norm = IET + (1 - CACHE_READ_MULT) * turn1_cacheRead      # move turn-1 cacheRead 0.1x -> 1x
# turn1_cacheRead is read from the per-turn assistant.usage events (metrics["events"]). An arm whose
# delivery is genuinely fresh on turn 1 (directive/skill) has turn1_cacheRead==0 and is unchanged.
def turn1_cache_read(m: dict) -> float:
    """cacheRead on the FIRST assistant.usage turn = the pre-warmed (setup-cached) resident prefix."""
    for e in m.get("events", []) or []:
        if isinstance(e, dict) and e.get("type") == "assistant.usage":
            return e.get("data", {}).get("cacheReadTokens", 0) or 0
    return 0.0


def arm_iet_norm(m: dict, w_out: float) -> float:
    """Delivery-normalized IET: reported IET with the turn-1 resident prefix re-priced 0.1x -> 1x."""
    return arm_iet(m, w_out) + (1.0 - CACHE_READ_MULT) * turn1_cache_read(m)


def load_scenario(path: str):
    if os.path.isdir(path):
        path = os.path.join(path, "results.json")
    d = json.load(open(path))
    v = d["verdicts"][0]
    s = v["scenarios"][0]
    return d, v, s


def q(arm: dict) -> float:
    return arm["judgeResult"]["overallScore"] / 5.0  # normalize 0..1


def analyze(model: str, path: str, w_out: float):
    d, v, s = load_scenario(path)
    base = s["baseline"]
    arms = {}
    for name in ("skilledIsolated", "skilledPlugin"):
        if s.get(name):
            arms[name] = s[name]

    base_q = q(base)
    base_iet = arm_iet(base["metrics"], w_out)

    results = {}
    for name, arm in arms.items():
        dq = q(arm) - base_q
        iet = arm_iet(arm["metrics"], w_out)
        cost_reduction = clamp((base_iet - iet) / base_iet) if base_iet else 0.0
        score = W_QUALITY * clamp(dq) + W_COST * cost_reduction
        results[name] = dict(dq=dq, iet=iet, cost_reduction=cost_reduction, score=score)

    # Effective (gating) arm = lowest of our scores (worst realistic delivery path).
    gate_name = min(results, key=lambda n: results[n]["score"])
    gate = results[gate_name]

    return dict(
        model=model,
        base_q=base_q,
        base_iet=base_iet,
        base_hiet=base_iet * haiku_ratio(model),
        hiet_ratio=haiku_ratio(model),
        base_out=base["metrics"]["outputTokens"],
        base_completed=base["metrics"]["taskCompleted"],
        gate_name=gate_name,
        gate=gate,
        arms=results,
        harness_score=s.get("improvementScore"),
        harness_iso=s.get("isolatedImprovementScore"),
        harness_plugin=s.get("pluginImprovementScore"),
        agent_model_in_file=d.get("model"),
    )


def fmt_pct(x):
    return f"{x*100:+.1f}%" if x is not None else "  n/a"


def main(argv):
    args = argv[1:]
    w_out = DEFAULT_OUTPUT_MULT
    specs = []
    for a in args:
        if a.startswith("--w=") or a.startswith("w="):
            w_out = float(a.split("=", 1)[1])
        else:
            specs.append(a)
    if not specs:
        print(__doc__)
        return 1

    runs = []
    for spec in specs:
        if "=" in spec:
            model, path = spec.split("=", 1)
        elif ":" in spec and not spec[1:3] == ":\\":
            model, path = spec.split(":", 1)
        else:
            raise SystemExit(f"Bad spec '{spec}'; use model=path")
        runs.append(analyze(model.strip(), path.strip(), w_out))

    # Order weakest -> strongest by baseline quality (capability on this task with
    # no help). Self-contained: no price table needed for the ordering.
    runs.sort(key=lambda r: (r["base_q"], r["base_iet"]))

    print("=" * 100)
    print("GROUNDING RE-SCORE  (one scenario, varying agent model; judge fixed)")
    print(f"IET = fresh_in + 0.1*cacheRead + 1.25*cacheWrite + {w_out:g}*output   (units: base-input-tokens)")
    print("HIET = IET x input-price-vs-Haiku (Opus 15x / Sonnet 3x / Haiku 1x): dollar-comparable across tiers")
    print("Our score = 0.70*dQuality + 0.30*costReduction(IET)")
    print("=" * 100)
    hdr = f"{'agent model':<20}{'baseQ/5':>8}{'gate dQ':>9}{'base IET':>11}{'gate IET':>11}{'gate HIET':>12}{'costRed':>9}{'OUR':>8}"
    print(hdr)
    print("-" * len(hdr))
    for r in runs:
        g = r["gate"]
        print(f"{r['model']:<20}"
              f"{r['base_q']*5:>8.1f}"
              f"{fmt_pct(g['dq']):>9}"
              f"{r['base_iet']:>11.0f}"
              f"{g['iet']:>11.0f}"
              f"{g['iet']*r['hiet_ratio']:>12.0f}"
              f"{fmt_pct(g['cost_reduction']):>9}"
              f"{fmt_pct(g['score']):>8}")
    print("-" * len(hdr))
    print("(IET in base-input-token units; HIET in Haiku-input-token equivalents; 'gate' = worse arm under OUR score)")
    print()

    print("CURVE  (weakest -> strongest by baseline quality):")
    for r in runs:
        g = r["gate"]
        print(f"  {r['model']:<20} dQuality={fmt_pct(g['dq']):>8}  costReduction={fmt_pct(g['cost_reduction']):>8}  OUR={fmt_pct(g['score']):>8}  harness={fmt_pct(r['harness_score']):>8}")
    print()

    weakest = runs[0]
    strongest = runs[-1]
    helps_weak = weakest["gate"]["dq"] >= HELP_THRESHOLD
    sg = strongest["gate"]
    harms_frontier = (sg["dq"] <= 0) and (sg["cost_reduction"] <= -HARM_COST_FRAC)
    helps_any = any(r["gate"]["dq"] >= HELP_THRESHOLD or r["gate"]["cost_reduction"] >= HARM_COST_FRAC for r in runs)

    print("PARETO VERDICT (grounding is auto-installed & un-removable):")
    print(f"  weakest tier ({weakest['model']}): dQuality={fmt_pct(weakest['gate']['dq'])}  -> materially helps? {helps_weak}")
    print(f"  frontier tier ({strongest['model']}): dQuality={fmt_pct(sg['dq'])}, costReduction={fmt_pct(sg['cost_reduction'])} -> harms frontier? {harms_frontier}")
    if helps_weak and not harms_frontier:
        verdict = "SHIP - helps the tier that needs it; no meaningful harm to the frontier."
    elif helps_weak and harms_frontier:
        verdict = "SHIP WITH CARE - helps weak tier but taxes the frontier; rely on retrieval to keep it out of frontier context."
    elif not helps_any:
        verdict = "RIP OUT - helps no tier on quality or cost."
    else:
        verdict = "MARGINAL - re-examine scope / retrieval."
    print(f"  => {verdict}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
