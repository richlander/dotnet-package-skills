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

    public static IetModel Current { get; set; } = Anthropic;

    public static IetModel Parse(string? name)
    {
        var normalized = (name ?? Anthropic.Name).Trim().ToLowerInvariant();
        return normalized switch
        {
            "" or "anthropic" or "claude" => Anthropic,
            "openai" or "gpt" => OpenAi,
            "no-cache" or "nocache" or "gpt-pro" or "gpt-5.5-pro" => NoCache,
            _ => throw new ArgumentException($"Unknown IET model '{name}'. Use: {Names}."),
        };
    }

    public static string Names => "anthropic, openai, no-cache";
}
