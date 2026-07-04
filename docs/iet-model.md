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

## Default model: Anthropic conversational cache

The default analyzer model is `anthropic`. It matches Claude-style conversational cache economics:

```text
cached = cacheReadTokens
fresh = inputTokens - cacheReadTokens

IET = 1.25*fresh + 0.10*cached + 5.00*outputTokens
```

Under this model, no prompt input is charged at the `1.00` base-input rate in normal cache-enabled Copilot agent turns. The input is either cached-prefix read or fresh suffix that becomes cache-readable on the next turn.

Use this model for Claude-family Copilot evals and for the default quality cards.

## Other analyzer models

The analyzer supports named IET models:

```bash
grounding analyze --iet-model anthropic ...
grounding analyze --iet-model openai ...
grounding analyze --iet-model no-cache ...
```

| Model | Formula | Use |
|---|---|---|
| `anthropic` | `1.25*(input-cacheRead) + 0.10*cacheRead + 5.00*output` | Default. Claude/Copilot conversational cache. |
| `openai` | `1.00*(input-cacheRead) + 0.10*cacheRead + 6.00*output` | OpenAI cached-input models with no cache-write premium. |
| `no-cache` | `1.00*input + 6.00*output` | Models or request modes without prompt-cache pricing. |

The `openai` model still decomposes gross input into cached and fresh classes. The difference is that fresh input stays at `1.00` because OpenAI prompt caching has no explicit cache-write surcharge in the public docs.

The `no-cache` model ignores `cacheReadTokens` and treats every input token as base input. Use it for model modes without cached-input support, such as a no-cache API mode or a model tier where cached input is not available.

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

Use IET as the hero token metric for quality cards:

* `tok`: Gross visibility into prompt size.
* `IET`: Cost-shaped prompt and output work.
* `output tok`: The most expensive output class shown separately.
* `cost`: Copilot-reported request cost, useful when it has enough granularity.

For Copilot agent sessions, the `anthropic` model makes the intended comparison explicit: grounding wins when it moves expensive output and repeated search into cheaper cached prefix input without hurting correctness.
