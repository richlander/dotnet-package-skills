#!/usr/bin/env python3
"""Cross-channel IET comparison for the Markout task (delivery-channel cost regime).

Reads the per-channel results.json captured in data/markout/ and prints, per model
tier, the absolute IET (Input-Equivalent Tokens; see eng/rescore.py) and quality of
the arm that actually carries each delivery channel:

  A   baseline           realmcp-noagents : baseline       (no MCP/CLI; reads README on disk)
  B   NuGet MCP -> README realmcp-noagents : skilledPlugin
  C   NuGet MCP -> AGENTS realmcp-agents   : skilledPlugin
  D   custom MCP -> AGENTS custommcp       : skilledPlugin  (resident-index gate)
  E   dotnet-inspect->README inspect-readme : skilledIsolated (CLI via `dotnet-inspect --readme`)
  E'  dotnet-inspect->AGENTS inspect-agents : skilledIsolated

Usage: python3 eng/compare-channels.py
"""
import json, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__)))
from rescore import arm_iet, arm_iet_norm, arm_hiet, haiku_ratio, turn1_cache_read, DEFAULT_OUTPUT_MULT as W

DATA = os.path.join(os.path.dirname(__file__), '..', 'data', 'markout')
CH = [
 ('A  baseline (no MCP/CLI, README on disk)', 'realmcp-noagents', 'baseline'),
 ('B  NuGet MCP -> README',                   'realmcp-noagents', 'skilledPlugin'),
 ('C  NuGet MCP -> AGENTS.md',                'realmcp-agents',   'skilledPlugin'),
 ('D  custom MCP (resident index) -> AGENTS', 'custommcp',        'skilledPlugin'),
 ('E  dotnet-inspect CLI -> README',          'inspect-readme',   'skilledIsolated'),
 ("E' dotnet-inspect CLI -> AGENTS.md",       'inspect-agents',   'skilledIsolated'),
]

# model tier name per data tag (for HIET cross-model scaling)
TIER_MODEL = {'opus': 'claude-opus-4.8', 'haiku': 'claude-haiku-4.5'}

def scen(stem, tier):
    return json.load(open(os.path.join(DATA, f'{stem}.{tier}.json')))['verdicts'][0]['scenarios'][0]

iet_by = {}   # (tier, label) -> IET
hiet_by = {}  # (tier, label) -> HIET
for tier in ('opus', 'haiku'):
    model = TIER_MODEL[tier]; ratio = haiku_ratio(model)
    print('=' * 104)
    print(f'  MARKOUT — {tier.upper()} ({model})   IET = fresh + 0.1*cacheRead + 1.25*cacheWrite + {W:g}*output  (avg of 3 runs)')
    print(f'  HIET = IET x {ratio:g} (Haiku-equivalent input tokens; dollar-comparable across tiers)')
    print(f'  IET* = delivery-normalized IET (turn-1 resident prefix re-priced 0.1x->1x; fair MCP-vs-directive)')
    print('=' * 104)
    print(f"{'channel':<42}{'IET':>10}{'IET*':>10}{'HIET':>12}{'out':>8}{'tools':>7}{'qual/5':>8}{'done':>5}")
    print('-' * 104)
    base = None
    for label, stem, arm in CH:
        a = scen(stem, tier)[arm]; m = a['metrics']
        iet = arm_iet(m, W); ietn = arm_iet_norm(m, W); hiet = arm_hiet(m, W, model)
        iet_by[(tier, label)] = iet; hiet_by[(tier, label)] = hiet
        if arm == 'baseline': base = iet
        red = f"({(base-iet)/base*100:+.0f}%)" if base and arm != 'baseline' else ''
        print(f"{label:<42}{iet:>10.0f}{ietn:>10.0f}{hiet:>12.0f}{m['outputTokens']:>8.0f}"
              f"{m['toolCallCount']:>7.1f}{a['judgeResult']['overallScore']:>8.2f}{str(m['taskCompleted'])[:1]:>5}  {red}")
    print()

# Cross-tier, dollar-comparable view: same channel, Opus vs Haiku in Haiku-equivalent tokens.
print('=' * 104)
print('  CROSS-TIER (HIET, Haiku-equivalent input tokens)  — is the lower-IET Opus run actually cheaper?')
print('=' * 104)
print(f"{'channel':<42}{'Opus IET':>10}{'Haiku IET':>11}{'Opus HIET':>12}{'Haiku HIET':>12}{'O/H $':>8}")
print('-' * 104)
for label, stem, arm in CH:
    oi, hi = iet_by.get(('opus', label)), iet_by.get(('haiku', label))
    oh, hh = hiet_by.get(('opus', label)), hiet_by.get(('haiku', label))
    if None in (oi, hi, oh, hh):
        continue
    ratio = oh / hh if hh else 0.0
    print(f"{label:<42}{oi:>10.0f}{hi:>11.0f}{oh:>12.0f}{hh:>12.0f}{ratio:>7.1f}x")
print()
print('IET compares arms within one model; HIET (=IET x input-price-vs-Haiku: Opus 15x, Sonnet 3x,')
print('Haiku 1x) compares dollars across tiers. "O/H $" = Opus cost / Haiku cost for the same channel.')
