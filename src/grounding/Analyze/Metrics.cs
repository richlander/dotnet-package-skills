using System.Globalization;
using Grounding.Json;

namespace Grounding.Analyze;

// Per-arm, mean-per-scenario aggregate (the original `aggregate`).
internal sealed class ArmAgg
{
    public double? Qual;
    public int Fp, Ft, Succ, Web, Cache, N;
    public double Iet, Cost, Tok, Out;
    public double ToolTurnSecs, ToolTurnSecsPct, ToolTurnIet, ToolTurnIetPct;
    public int DocTok;   // grounding doc tokens loaded into THIS arm (0 for baseline)

    public int Arch => Web + Cache;
}

// One loaded results.json reduced to the plugin/baseline aggregates.
internal sealed class LoadedArm
{
    public required string Model;
    public required string Judge;
    public required string Tier;
    public required string SkillName;
    public string? SkillPath;
    public required Dictionary<string, ArmAgg> Agg;
    public required bool IsReadme;
    public required string Path;
}

internal static class Metrics
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // (key, label) arms in render order.
    public static readonly (string Key, string Label)[] Arms =
    {
        ("baseline", "baseline"),
        ("skilledIsolated", "isolated"),
        ("skilledPlugin", "plugin"),
    };

    // Win / harm bars. <10% = noise, 10–20% = inconclusive (re-eval), >=20% = real:
    // a >=20% IET/cost cut is BETTER, a >=20% inflation is WORSE; the band between is NEUTRAL.
    public const double IetWinFrac = 0.20;
    public const double CostWinFrac = 0.20;
    public const double IetHarmCapFrac = 0.20;
    public const double CostHarmCapFrac = 0.20;
    public const double OutInflateFrac = 0.20;

    private static readonly string[] FrontierHints =
        { "opus", "sonnet", "gpt-5", "gpt5", "gemini-3", "gemini-2.5-pro" };
    private static readonly string[] MiniHints =
        { "haiku", "mini", "flash", "small", "lite" };

    public static string Tier(string? model)
    {
        var m = (model ?? "").ToLowerInvariant();
        if (FrontierHints.Any(h => m.Contains(h))) return "frontier";
        return "mini"; // mini hints + unknown both treated as the needs-it tier
    }

    // --- numeric formatting that mirrors Python's str()/format() exactly ---

    // .NET fixed-format ("Fn") is IEEE-correctly-rounded (round-half-to-even on the
    // true double), matching Python's str()/format()/round() — unlike Math.Round(double,
    // digits), which scales by 10^digits and misrounds values like 1.6500000000000001.
    public static string F0(double v) => v.ToString("F0", Inv);

    public static string F2(double v) => v.ToString("F2", Inv);

    // Python round(x, 1): correctly-rounded to 1 decimal.
    public static double Round1(double v) => double.Parse(v.ToString("F1", Inv), Inv);

    public static double Pct(double @new, double old) =>
        old != 0 ? (@new - old) / old * 100.0 : 0.0;

    // Python `:+.0f` — sign follows the original value, magnitude correctly rounded.
    public static string SignedPct(double v)
    {
        var sign = v < 0 ? "-" : "+";
        var mag = Math.Abs(v).ToString("F0", Inv);
        return $"{sign}{mag}%";
    }

    // Python `:+d` — always shows a sign, including +0.
    public static string SignedInt(int d) => d >= 0 ? $"+{d}" : d.ToString(Inv);
}
