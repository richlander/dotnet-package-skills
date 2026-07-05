using Grounding.Json;

namespace Grounding.Analyze;

// ============================================================================
// IET (input-equivalent tokens) is split into three concerns so the pieces stay
// legible and composable:
//
//   1. IetValue   — the *shape*: raw token counts bucketed by how they bill, tagged
//                   with the calculation CURRENCY they were bucketed for and the
//                   producing MODEL. Pricing-agnostic and summable.
//   2. IetScheme  — the *pricer*: per-currency bucket weights, plus the corrective
//                   math that turns raw metrics into an IetValue. Applies the
//                   no-cache modifier at price time.
//   3. IetModels  — the *registry/selection*: the known schemes, model->scheme
//                   mapping, the `--iet-model` override, and the no-cache modifier.
//
// IET is NOT portable across currencies: the buckets mean different things under
// each scheme, so a value may only be priced by its own scheme (asserted). The one
// valid re-pricing is the no-cache *modifier* — a pure input-side reweighting that
// applies to either scheme without changing the buckets or the output weight.
// ============================================================================

// The IET *shape*: token counts bucketed by how they are billed, in a specific
// calculation CURRENCY (the scheme the buckets were bucketed for), stamped with the
// producing MODEL (tokenizer identity — two models can emit different token counts
// for the same text). Buckets are raw counts, never pre-weighted; that is what keeps
// aggregation and re-pricing valid (pricing is linear, so sum-then-price == price-then-sum).
internal readonly record struct IetValue(
    long Fresh,       // input billed at the base rate (first send, no cache)
    long CacheWrite,  // input billed at the cache-write rate (conversational-cache first send)
    long CacheRead,   // input billed at the (discounted) cache-read rate (re-sent prefix)
    long Output,      // generated tokens
    string Currency,  // the scheme these buckets were bucketed for ("anthropic", "openai")
    string? Model)    // producing model / tokenizer identity (null once summed across models)
{
    public static IetValue Zero(string currency, string? model) => new(0, 0, 0, 0, currency, model);

    // Scheme-neutral aggregation: sum the buckets. Only same-currency values combine —
    // mixing currencies is a modeling error (the buckets are architecture-specific). Model
    // collapses to null when the producers differ.
    public static IetValue operator +(IetValue a, IetValue b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException(
                $"IET currency mismatch: cannot sum '{a.Currency}' with '{b.Currency}'.");
        return new IetValue(
            a.Fresh + b.Fresh, a.CacheWrite + b.CacheWrite,
            a.CacheRead + b.CacheRead, a.Output + b.Output,
            a.Currency, a.Model == b.Model ? a.Model : null);
    }
}

internal static class IetValueExtensions
{
    // Fold a run of same-currency values into one. Empty is a modeling error (no currency to
    // seed from); callers aggregate over non-empty scopes (turns of a session, etc.).
    public static IetValue Sum(this IEnumerable<IetValue> values)
    {
        IetValue? acc = null;
        foreach (var v in values) acc = acc is { } a ? a + v : v;
        return acc ?? throw new InvalidOperationException("Cannot sum an empty set of IET values (no currency).");
    }
}

// The IET *pricer* for one calculation currency. Carries the bucket weights and the
// corrective math (Value) that maps raw metrics into that currency's buckets. Pricing
// honours the no-cache modifier: a pure input-side reweighting (every input bucket at the
// base rate) that never touches the output weight.
internal sealed record IetScheme(
    string Currency,       // pricing currency / calculation scheme name
    string Description,
    double WFresh,         // weight for base-rate input (normally 1.0)
    double WCacheWrite,    // weight for cache-write input (Anthropic 1.25; unused where 0 tokens)
    double WCacheRead,     // weight for cache-read input (0.10)
    double WOutput,        // weight for output
    bool FoldNonCachedIntoCacheWrite) // true (Anthropic): the non-cached suffix bills as cache-write
{
    // Corrective math: bucket raw per-turn/aggregate metrics into this currency.
    //
    // The non-cached portion is `input - cacheRead`. Under the Anthropic conversational-cache
    // scheme the harness reports cacheWrite as 0 (untrustworthy) and the suffix effectively
    // bills at the cache-write rate, so that portion lands in CacheWrite. Under OpenAI it is
    // plain base input (Fresh). cacheRead is clamped to input.
    public IetValue Value(long inputTokens, long cacheReadTokens, long outputTokens, string? model)
    {
        var cacheRead = Math.Min(inputTokens, Math.Max(0, cacheReadTokens));
        var nonCached = inputTokens - cacheRead;
        return FoldNonCachedIntoCacheWrite
            ? new IetValue(0, nonCached, cacheRead, outputTokens, Currency, model)
            : new IetValue(nonCached, 0, cacheRead, outputTokens, Currency, model);
    }

    public IetValue Value(Grounding.Json.Metrics m, string? model) =>
        Value(m.InputTokens, m.CacheReadTokens, m.OutputTokens, model);

    // Price a value in this scheme's currency. The no-cache modifier reprices every input
    // bucket (fresh, cache-write, cache-read) at the base rate 1.0; the output weight is the
    // scheme's own and never changes — no-cache is purely an input-side reweighting.
    public double Price(IetValue v, bool noCache)
    {
        if (v.Currency != Currency)
            throw new InvalidOperationException(
                $"IET currency mismatch: value is '{v.Currency}' but pricer is '{Currency}'. " +
                "Re-pricing across schemes is not meaningful; only the no-cache modifier re-prices.");
        var (wf, wcw, wcr) = noCache ? (1.0, 1.0, 1.0) : (WFresh, WCacheWrite, WCacheRead);
        return wf * v.Fresh + wcw * v.CacheWrite + wcr * v.CacheRead + WOutput * v.Output;
    }

    // --- Convenience: build-and-price straight from metrics, honouring the ambient modifier.
    public double Iet(Grounding.Json.Metrics m) => Price(Value(m, null), IetModels.NoCache);

    public double Iet(long inputTokens, long cacheReadTokens, long outputTokens) =>
        Price(Value(inputTokens, cacheReadTokens, outputTokens, null), IetModels.NoCache);

    public string Formula
    {
        get
        {
            if (IetModels.NoCache)
                return $"1*input + {WOutput:0.##}*output";
            return FoldNonCachedIntoCacheWrite
                ? $"{WCacheWrite:0.##}*(input-cacheRead) + {WCacheRead:0.##}*cacheRead + {WOutput:0.##}*output"
                : $"{WFresh:0.##}*(input-cacheRead) + {WCacheRead:0.##}*cacheRead + {WOutput:0.##}*output";
        }
    }
}

internal static class IetModels
{
    // Claude/Copilot conversational cache: the non-cached suffix bills as cache-write input
    // (reported cacheWrite is 0/untrustworthy), the cached prefix as cache-read input.
    public static readonly IetScheme Anthropic = new(
        Currency: "anthropic",
        Description: "Claude/Copilot conversational cache: non-cached suffix bills as cache-write, cached prefix as cache-read.",
        WFresh: 1.00,
        WCacheWrite: 1.25,
        WCacheRead: 0.10,
        WOutput: 5.00,
        FoldNonCachedIntoCacheWrite: true);

    // OpenAI cached-input model: the non-cached suffix is base input, the cached prefix is
    // discounted cache-read input (no cache-write premium).
    public static readonly IetScheme OpenAi = new(
        Currency: "openai",
        Description: "OpenAI cached-input model: non-cached suffix is base input, cached prefix is discounted cache-read.",
        WFresh: 1.00,
        WCacheWrite: 1.25,   // unused under this scheme (no tokens land in cache-write)
        WCacheRead: 0.10,
        WOutput: 6.00,
        FoldNonCachedIntoCacheWrite: false);

    // Explicit scheme override from `--iet-model`. When null (the default, "auto"), the scheme
    // is chosen per run from the model that produced it — so a mixed Opus+GPT card prices the
    // Claude columns with the Anthropic scheme and the GPT columns with the OpenAI scheme.
    public static IetScheme? Override { get; set; }

    // The no-cache modifier. Orthogonal to scheme selection: it reprices input at the base rate
    // under whichever scheme is in play, so a no-cache Claude run still uses the Anthropic output
    // weight (5) and a no-cache GPT run the OpenAI output weight (6).
    public static bool NoCache { get; set; }

    // The scheme for a run produced by `model` (honours an explicit override, else maps family).
    public static IetScheme For(string? model) => Override ?? FromModel(model);

    // Family -> scheme. GPT/o-series -> OpenAI; everything else (Claude/Gemini/unknown) ->
    // Anthropic conversational-cache, the shape most agents run under.
    public static IetScheme FromModel(string? model)
    {
        var m = (model ?? "").ToLowerInvariant();
        if (m.Contains("gpt") || m.Contains("openai") || m.Contains("o1") || m.Contains("o3") || m.Contains("o4"))
            return OpenAi;
        return Anthropic;
    }

    // Parsed `--iet-model` selection: an optional forced scheme plus the no-cache modifier.
    public readonly record struct Selection(IetScheme? Forced, bool NoCache);

    public static Selection ParseSelection(string? name)
    {
        var n = (name ?? "auto").Trim().ToLowerInvariant();
        return n switch
        {
            "" or "auto" => new(null, false),
            "anthropic" or "claude" => new(Anthropic, false),
            "openai" or "gpt" => new(OpenAi, false),
            "no-cache" or "nocache" => new(null, true),        // modifier only; scheme stays per-model
            "gpt-pro" or "gpt-5.5-pro" => new(OpenAi, true),   // OpenAI scheme, no prompt cache
            _ => throw new ArgumentException($"Unknown IET model '{name}'. Use: {Names}."),
        };
    }

    // Apply a parsed selection to the ambient override + modifier.
    public static void Apply(Selection s)
    {
        Override = s.Forced;
        NoCache = s.NoCache;
    }

    // A caption fragment describing the cost scheme(s) in play for the given run models.
    public static string CaptionFor(IEnumerable<string?> models)
    {
        if (Override is { } ov) return NoCache ? $"`{ov.Currency}`, no-cache (forced)" : $"`{ov.Currency}` (forced)";
        var names = models.Select(FromModel).Select(x => x.Currency).Distinct().ToList();
        var body = names.Count == 1 ? $"`{names[0]}`" : "per model (`anthropic`=Claude, `openai`=GPT)";
        return NoCache ? $"{body}, no-cache" : body;
    }

    public static string Names => "auto, anthropic, openai, no-cache";
}
