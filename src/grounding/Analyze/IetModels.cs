using Grounding.Json;

namespace Grounding.Analyze;

internal sealed record IetModel(
    string Name,
    string Description,
    double FreshInputWeight,
    double CacheReadWeight,
    double OutputWeight,
    bool UseReportedCacheReads)
{
    public double InputCharge(Grounding.Json.Metrics m) => InputCharge(m.InputTokens, m.CacheReadTokens);

    public double InputCharge(long inputTokens, long cacheReadTokens)
    {
        var cached = UseReportedCacheReads ? Math.Min(inputTokens, cacheReadTokens) : 0;
        var fresh = inputTokens - cached;
        return FreshInputWeight * fresh + CacheReadWeight * cached;
    }

    public double Iet(Grounding.Json.Metrics m) => InputCharge(m) + OutputWeight * m.OutputTokens;

    public double Iet(long inputTokens, long cacheReadTokens, long outputTokens) =>
        InputCharge(inputTokens, cacheReadTokens) + OutputWeight * outputTokens;

    public string Formula =>
        UseReportedCacheReads
            ? $"{FreshInputWeight:0.##}*(input-cacheRead) + {CacheReadWeight:0.##}*cacheRead + {OutputWeight:0.##}*output"
            : $"{FreshInputWeight:0.##}*input + {OutputWeight:0.##}*output";
}

internal static class IetModels
{
    public static readonly IetModel Anthropic = new(
        "anthropic",
        "Claude/Copilot conversational cache: fresh suffix is effective cache-write input, cached prefix is cache-read input.",
        FreshInputWeight: 1.25,
        CacheReadWeight: 0.10,
        OutputWeight: 5.00,
        UseReportedCacheReads: true);

    public static readonly IetModel OpenAi = new(
        "openai",
        "OpenAI cached-input model: fresh suffix is base input, cached prefix is discounted cached input.",
        FreshInputWeight: 1.00,
        CacheReadWeight: 0.10,
        OutputWeight: 6.00,
        UseReportedCacheReads: true);

    public static readonly IetModel NoCache = new(
        "no-cache",
        "No prompt-cache model: all prompt input is base input.",
        FreshInputWeight: 1.00,
        CacheReadWeight: 0.00,
        OutputWeight: 6.00,
        UseReportedCacheReads: false);

    // Explicit override from `--iet-model`. When null (the default, "auto"), the cost model
    // is chosen per run from the model that produced it — so a mixed Opus+GPT card prices the
    // Claude columns with the Anthropic model and the GPT columns with the OpenAI model.
    public static IetModel? Override { get; set; }

    // The cost model for a run produced by `model`. Honours an explicit `--iet-model` override;
    // otherwise maps the model family to its pricing shape.
    public static IetModel For(string? model) => Override ?? FromModel(model);

    // Family -> pricing shape. Claude/Copilot conversational cache is the Anthropic shape;
    // the GPT family is the OpenAI cached-input shape. Unknown families default to Anthropic
    // (the conversational-cache shape most agents run under).
    public static IetModel FromModel(string? model)
    {
        var m = (model ?? "").ToLowerInvariant();
        if (m.Contains("gpt") || m.Contains("openai") || m.Contains("o1") || m.Contains("o3") || m.Contains("o4"))
            return OpenAi;
        return Anthropic; // claude/opus/sonnet/haiku + gemini + unknown
    }

    public static IetModel Parse(string? name)
    {
        var normalized = (name ?? "auto").Trim().ToLowerInvariant();
        return normalized switch
        {
            "" or "auto" => Anthropic, // caller decides auto vs. override; Parse yields a concrete model
            "anthropic" or "claude" => Anthropic,
            "openai" or "gpt" => OpenAi,
            "no-cache" or "nocache" or "gpt-pro" or "gpt-5.5-pro" => NoCache,
            _ => throw new ArgumentException($"Unknown IET model '{name}'. Use: {Names}."),
        };
    }

    // A caption fragment describing the cost model(s) in play for the given set of run models.
    // One family -> just its name; a mix -> "per model (anthropic=Claude, openai=GPT)".
    public static string CaptionFor(IEnumerable<string?> models)
    {
        if (Override is { } ov) return $"`{ov.Name}` (forced)";
        var names = models.Select(FromModel).Select(x => x.Name).Distinct().ToList();
        return names.Count == 1 ? $"`{names[0]}`" : "per model (`anthropic`=Claude, `openai`=GPT)";
    }

    public static string Names => "auto, anthropic, openai, no-cache";
}
