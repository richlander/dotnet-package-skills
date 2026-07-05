# IET token model

Input Equivalent Tokens (IET) is the token cost model used by the grounding analyzer. It converts the token classes reported by Copilot into one comparable unit: base input tokens. The goal is to compare grounding arms by cost-shaped token work, not by raw gross token count.

## Why raw input is not enough

Copilot `assistant.usage` events report gross prompt input:

```text
inputTokens
cacheReadTokens
cacheWriteTokens
outputTokens
```

The useful relationship is:

```text
inputTokens = cached prefix + fresh suffix
cached prefix = cacheReadTokens
fresh suffix = inputTokens - cacheReadTokens
```

In this document, `input` means the prompt input reported for the configured API request mode. It does not always mean `base input` charged at the `1.00` rate. The rate applied to the non-cached part of `inputTokens` depends on the model and cache mode. In Copilot's Claude-style conversational cache mode, the non-cached suffix behaves as cache-write input. In a no-cache mode, the same kind of prompt text is base input.

Raw `inputTokens + outputTokens` treats cached prompt reuse as full-price input. That overstates harm in multi-turn agent sessions, where most of the growing prompt is a cached prefix.

## Evidence from repeated-turn probes

The token relationship was checked outside the eval harness with the Copilot SDK. Each probe used a single session, a fixed review prompt, no tools, and the same document repeated for four turns. The probe recorded only `assistant.usage` fields.

The transition comparison is the key evidence:

```text
cacheReadTokens(turn N + 1) ~= inputTokens(turn N) - 10
```

That pattern means the previous turn's gross input becomes the next turn's cached prefix. The small `~10` token delta appears to be prompt-boundary slop.

| Probe | Transition | input(N) | cacheRead(N+1) | Delta | Coverage |
|---|---:|---:|---:|---:|---:|
| 10k chars | 1→2 | 9,564 | 9,554 | -10 | 99.895% |
| 10k chars | 2→3 | 12,146 | 12,136 | -10 | 99.918% |
| 10k chars | 3→4 | 14,719 | 14,709 | -10 | 99.932% |
| 50k chars | 1→2 | 20,846 | 20,836 | -10 | 99.952% |
| 50k chars | 2→3 | 34,966 | 34,956 | -10 | 99.971% |
| 50k chars | 3→4 | 49,050 | 49,040 | -10 | 99.980% |
| 100k chars | 1→2 | 36,004 | 35,994 | -10 | 99.972% |
| 100k chars | 2→3 | 65,020 | 65,010 | -10 | 99.985% |
| 100k chars | 3→4 | 94,030 | 94,020 | -10 | 99.989% |

This also shows that `cacheWriteTokens` is not a trustworthy effective-write signal in Copilot agent sessions. It reported `0` in the probes, while the next-turn cache reads prove that the prior prompt was effectively written into cache.

## Prefix placement matters

Prompt caching is prefix-based. A token becomes cheap only after it enters the stable prompt prefix. Content injected after the current-turn divergence point is fresh on that turn, even if the same text appeared in a previous user message.

This matters for grounding delivery:

* Grounding placed in the stable prefix becomes a cheap cache read on later turns.
* Grounding reattached after the divergence point remains fresh each turn.
* Delivery mechanism is part of grounding design, not just an implementation detail.

Provider docs that inform this model:

* [Anthropic prompt caching](https://platform.claude.com/docs/en/build-with-claude/prompt-caching): prompt caching resumes from specific prefixes, automatic caching moves the cache point forward as conversations grow, and cache-write/read prices are separate.
* [OpenAI prompt caching](https://developers.openai.com/api/docs/guides/prompt-caching): cache hits require exact prefix matches; static content belongs at the beginning and variable content at the end.
* [GitHub Copilot models and pricing](https://docs.github.com/en/copilot/reference/copilot-billing/models-and-pricing): Copilot pricing separates input, cached input, output, and for Anthropic models cache write.

## Default: per-model cost model (`auto`)

By default (`--iet-model auto`) the analyzer picks the cost model **per run** from the model that
produced it, so a single card can mix models and price each faithfully:

| Model family | Cost model | Why |
|---|---|---|
| Claude / Opus / Sonnet / Haiku (and unknown) | `anthropic` | Copilot conversational cache: fresh suffix is effective cache-write. |
| GPT / OpenAI / o-series | `openai` | OpenAI cached-input pricing: no cache-write premium, 6× output. |

This is what makes the end-game card honest: an **Opus vs GPT** comparison prices the Opus columns
with `anthropic` and the GPT columns with `openai` automatically. The caption records which model(s)
are in play (`IET model per model (anthropic=Claude, openai=GPT)`), or `… (forced)` when overridden.

To force one model for every column (for an apples-to-apples "what if everything were Anthropic-priced"
view), pass an explicit model — it overrides the per-run selection:

```bash
grounding analyze --iet-model auto ...        # default: per-run, by model
grounding analyze --iet-model anthropic ...   # force Anthropic for all columns
grounding analyze --iet-model openai ...       # force OpenAI for all columns
grounding analyze --iet-model no-cache ...     # no-cache *modifier* (keeps the per-model scheme)
```

## `no-cache` is a modifier, not a third model

`no-cache` is **orthogonal** to scheme selection. It does not replace `anthropic`/`openai` with a
separate pricer — it reprices the input side of whichever scheme is in play so that **every** input
token (fresh, cache-write, and cache-read alike) is charged at the base `1.00` rate. The output
weight is unchanged: it stays the scheme's own (`5×` under `anthropic`, `6×` under `openai`).

This matches the physical picture: cache-write and cache-read are the *same* tokens at different
points in their lifecycle (a token is "written" the turn it first appears, then "read" every
subsequent turn it stays in the prompt). Caching makes the re-sends cheap; `no-cache` says every
send pays full freight. So `no-cache` is a pure input-side reweighting — it never touches output.

```text
anthropic, no-cache:  1.00*(cacheWrite + cacheRead) + 5.00*output   =  1.00*input + 5.00*output
openai,    no-cache:  1.00*(fresh + cacheRead)      + 6.00*output   =  1.00*input + 6.00*output
```

Because it keeps the per-model scheme, a no-cache Claude run still prices output at `5×` and a
no-cache GPT run at `6×` — the modifier only lifts the input discount, it does not swap output rates.

## The `anthropic` model: conversational cache

```text
cached = cacheReadTokens
fresh = inputTokens - cacheReadTokens

IET = 1.25*fresh + 0.10*cached + 5.00*outputTokens
```

Under this model, no prompt input is charged at the `1.00` base-input rate in normal cache-enabled Copilot agent turns. The input is either cached-prefix read or fresh suffix that becomes cache-readable on the next turn.

## The named models

| Model | Formula | Use |
|---|---|---|
| `anthropic` | `1.25*(input-cacheRead) + 0.10*cacheRead + 5.00*output` | Claude/Copilot conversational cache. Auto-selected for Claude families. |
| `openai` | `1.00*(input-cacheRead) + 0.10*cacheRead + 6.00*output` | OpenAI cached-input models with no cache-write premium. Auto-selected for GPT/o-series. |
| `no-cache` | *modifier* → `1.00*input + <scheme>*output` | Input repriced to base rate; output stays the scheme's own (`5×` Anthropic, `6×` OpenAI). See above. |

The `openai` model still decomposes gross input into cached and fresh classes. The difference is that fresh input stays at `1.00` because OpenAI prompt caching has no explicit cache-write surcharge in the public docs.

## Tool-turn IET

The analyzer also reports tool-turn IET share. Tool-turn IET uses the same selected IET model as the session card.

Tool-turn percentages use a per-turn denominator:

```text
tool-turn IET share = tool-turn per-turn IET / all-turn per-turn IET
```

This denominator is intentionally different from session IET. Per-turn inputs are cumulative re-sent context, while session IET is an arm-level cost metric. Using the same per-turn basis for numerator and denominator keeps the percentage bounded and interpretable.

## Cache canary

Clean conversational runs show high next-turn cache coverage. A non-first turn with `cacheReadTokens == 0` is a useful canary for prompt rewrite, compaction, truncation, or a cache-breaking change.

Very large prompts can trigger this behavior. In a 280k-character probe, turn 2 followed the normal transition pattern, then later turns lost the cached prefix. Treat those runs as a different prompt condition, not directly comparable cache economics.

## Practical card interpretation

The quality card reads top-to-bottom as **outcome → detail → session summary**. IET is the hero
cost metric; sizes stay in tokens, costs in IET/$, so no row mixes dimensions.

Outcome and validity:

* `tasks correct` / `func passed`: did grounding keep (or improve) correctness — the only ship gate.
* `nuget-cache reads (archaeology)`: tool calls into `~/.nuget/packages` — the agent reading or
  decompiling the restored package binary because the grounding did not tell it what it needed. The
  sharp "grounding was insufficient" signal; grounding should drive it toward `0`.
* `tool calls: web / bash / other`: the tool-call mix. `web` is external retrieval (another escape
  hatch grounding should zero out); `bash` is shell work (nuget-cache reads are a subset); `other`
  is ordinary view/edit/skill work. Shown as counts so the composition is legible.
* `grounding load (tok)`: the doc's **size**, weighted by whether it was actually read (see below).
* `read grounding (%)`: did the grounded arm invoke the `skill` tool and load the grounding? Baseline
  is `0%`. A grounded run at `0%` never read the grounding — it is effectively a baseline run and
  dilutes the arm; its `grounding load` is `0` accordingly.

Cost detail (where the spend goes):

* `output tok (% of IET)`: output tokens and their share of IET. Output is a small fraction of the
  token **count** but a large share of **IET** (~1–2% of tokens, ~24–30% of IET) — the 50×-vs-cache
  leverage made visible. Grounding wins by moving expensive output/thinking into cheap cached input.
* `tool-call turns (% of total)`, `tool-turn secs (% of turn time)`, `tool-turn IET (% of turn IET)`:
  how much of the session was tool-mediated. Counts/seconds are the signal; the shares saturate on
  build-and-run tasks. Shares use a per-turn denominator (see *Tool-turn IET* above), bounded ≤ 100%.

Session summary (the bottom line):

* `Session turns`: total assistant turns.
* `Session IET`: the full price-weighted cost, **doc included** (not netted — the doc is a real cost).
* `Session Cost`: Copilot-reported request cost, when it has enough granularity.

For Copilot agent sessions, the `anthropic` model makes the intended comparison explicit: grounding
wins when it moves expensive output and repeated search into cheaper cached-prefix input without
hurting correctness.
