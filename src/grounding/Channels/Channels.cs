using System.Globalization;
using Grounding.Json;

namespace Grounding.Channels;

// IET economics for the channel matrix (extract weights and
// rescore arm_iet / arm_iet_norm / arm_hiet).
internal static class Economics
{
    public const double WCacheRead = 0.10;
    public const double WCacheWrite = 1.25;
    public const double WOutput = 5.0;

    public static long WeightedIet(Metrics m)
    {
        long inp = m.InputTokens, outv = m.OutputTokens, cr = m.CacheReadTokens, cw = m.CacheWriteTokens;
        var fresh = Math.Max(inp - cr, 0);
        return (long)Math.Round(fresh + WCacheRead * cr + WCacheWrite * cw + WOutput * outv, MidpointRounding.ToEven);
    }

    public static double ArmIet(Metrics m, double wOut)
    {
        var fresh = Math.Max(0, m.InputTokens - m.CacheReadTokens - m.CacheWriteTokens);
        return fresh + m.CacheReadTokens * WCacheRead + m.CacheWriteTokens * WCacheWrite + m.OutputTokens * wOut;
    }

    public static double Turn1CacheRead(Metrics m)
    {
        foreach (var e in m.Events ?? new())
            if (e.Type == "assistant.usage")
                return e.Data?.CacheReadTokens ?? 0;
        return 0.0;
    }

    public static double ArmIetNorm(Metrics m, double wOut) =>
        ArmIet(m, wOut) + (1.0 - WCacheRead) * Turn1CacheRead(m);

    public static double HaikuRatio(string model)
    {
        var s = model.ToLowerInvariant();
        if (s.Contains("opus")) return 15.0;
        if (s.Contains("sonnet")) return 3.0;
        if (s.Contains("haiku")) return 1.0;
        return 1.0;
    }

    public static double ArmHiet(Metrics m, double wOut, string model) => ArmIet(m, wOut) * HaikuRatio(model);
}

internal static class Channels
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---- channels extract ---------------------------------------------

    private static readonly (string Ch, string Tag, string Arm, string Label)[] ExtractCh =
    {
        ("A", "realmcp-noagents", "baseline", "raw pkg on disk -> README"),
        ("A'", "realmcp-agents", "baseline", "raw pkg on disk, AGENTS present (invisible)"),
        ("B", "realmcp-noagents", "skilledPlugin", "real NuGet MCP -> README"),
        ("C", "realmcp-agents", "skilledPlugin", "real NuGet MCP -> AGENTS.md"),
        ("D", "custommcp", "skilledPlugin", "custom MCP (resident_index)"),
    };

    public static int Extract(string taskDir)
    {
        foreach (var mdl in Models(taskDir))
        {
            Console.WriteLine($"\n==== {Path.GetFileName(taskDir.TrimEnd('/'))}  /  {mdl}  ====");
            Console.WriteLine($"{L("Ch", 3)} {R("IET", 9)} {R("(raw tEst)", 11)} {R("tools", 6)} {R("done", 6)}  {R("mcp_calls", 9)}  delivery");
            foreach (var (ch, tag, arm, label) in ExtractCh)
            {
                var path = Path.Combine(taskDir, $"{tag}.{mdl}.json");
                if (!File.Exists(path))
                {
                    Console.WriteLine($"{L(ch, 3)} {R("-- missing --", 28)}  {label}");
                    continue;
                }
                var m = Grounding.Analyze.Loader.Parse(path).Verdicts![0].Scenarios![0];
                var armObj = ArmByName(m, arm)!;
                var met = armObj.Metrics!;
                var iet = Economics.WeightedIet(met);
                var bd = met.ToolCallBreakdown ?? new();
                var mcp = bd.Where(kv => kv.Key.Contains("package_context")).Sum(kv => kv.Value);
                Console.WriteLine(
                    $"{L(ch, 3)} {R(iet.ToString(Inv), 9)} {R(Str(met.TokenEstimate), 11)} "
                    + $"{R(Str(met.ToolCallCount), 6)} {R(met.TaskCompleted ? "True" : "False", 6)}  {R(mcp.ToString(Inv), 9)}  {label}");
            }
        }
        return 0;
    }

    private static IEnumerable<string> Models(string taskDir)
    {
        var ms = new SortedSet<string>(StringComparer.Ordinal);
        if (Directory.Exists(taskDir))
            foreach (var f in Directory.EnumerateFiles(taskDir, "*.json"))
            {
                var parts = Path.GetFileName(f).Split('.');
                if (parts.Length >= 2) ms.Add(parts[^2]);
            }
        return ms;
    }

    // ---- channels compare (hardcoded to data/markout) ------------------

    private static readonly (string Label, string Stem, string Arm)[] CompareCh =
    {
        ("A  baseline (no MCP/CLI, README on disk)", "realmcp-noagents", "baseline"),
        ("B  NuGet MCP -> README", "realmcp-noagents", "skilledPlugin"),
        ("C  NuGet MCP -> AGENTS.md", "realmcp-agents", "skilledPlugin"),
        ("D  custom MCP (resident index) -> AGENTS", "custommcp", "skilledPlugin"),
        ("E  dotnet-inspect CLI -> README", "inspect-readme", "skilledIsolated"),
        ("E' dotnet-inspect CLI -> AGENTS.md", "inspect-agents", "skilledIsolated"),
    };

    private static readonly (string Tier, string Model)[] Tiers =
        { ("opus", "claude-opus-4.8"), ("haiku", "claude-haiku-4.5") };

    public static int Compare()
    {
        var root = RepoRoot.Find() ?? Directory.GetCurrentDirectory();
        var data = Path.Combine(root, "data", "markout");
        const double w = Economics.WOutput;

        var ietBy = new Dictionary<(string, string), double>();
        var hietBy = new Dictionary<(string, string), double>();
        var line = new string('=', 104);

        foreach (var (tier, model) in Tiers)
        {
            var ratio = Economics.HaikuRatio(model);
            Console.WriteLine(line);
            Console.WriteLine($"  MARKOUT — {tier.ToUpperInvariant()} ({model})   IET = fresh + 0.1*cacheRead + 1.25*cacheWrite + {G(w)}*output  (avg of 3 runs)");
            Console.WriteLine($"  HIET = IET x {G(ratio)} (Haiku-equivalent input tokens; dollar-comparable across tiers)");
            Console.WriteLine("  IET* = delivery-normalized IET (turn-1 resident prefix re-priced 0.1x->1x; fair MCP-vs-directive)");
            Console.WriteLine(line);
            Console.WriteLine($"{L("channel", 42)}{R("IET", 10)}{R("IET*", 10)}{R("HIET", 12)}{R("out", 8)}{R("tools", 7)}{R("qual/5", 8)}{R("done", 5)}");
            Console.WriteLine(new string('-', 104));
            double? @base = null;
            foreach (var (label, stem, arm) in CompareCh)
            {
                var a = ArmByName(Scen(data, stem, tier), arm)!;
                var m = a.Metrics!;
                var iet = Economics.ArmIet(m, w);
                var ietn = Economics.ArmIetNorm(m, w);
                var hiet = Economics.ArmHiet(m, w, model);
                ietBy[(tier, label)] = iet;
                hietBy[(tier, label)] = hiet;
                if (arm == "baseline") @base = iet;
                var red = @base is { } b && arm != "baseline" ? $"({SignedPct0((b - iet) / b * 100)}%)" : "";
                Console.WriteLine(
                    $"{L(label, 42)}{R(F0(iet), 10)}{R(F0(ietn), 10)}{R(F0(hiet), 12)}{R(F0(m.OutputTokens), 8)}"
                    + $"{R(F1(m.ToolCallCount ?? 0), 7)}{R(F2(a.JudgeResult?.OverallScore ?? 0), 8)}{R((m.TaskCompleted ? "True" : "False")[..1], 5)}  {red}");
            }
            Console.WriteLine();
        }

        Console.WriteLine(line);
        Console.WriteLine("  CROSS-TIER (HIET, Haiku-equivalent input tokens)  — is the lower-IET Opus run actually cheaper?");
        Console.WriteLine(line);
        Console.WriteLine($"{L("channel", 42)}{R("Opus IET", 10)}{R("Haiku IET", 11)}{R("Opus HIET", 12)}{R("Haiku HIET", 12)}{R("O/H $", 8)}");
        Console.WriteLine(new string('-', 104));
        foreach (var (label, _, _) in CompareCh)
        {
            if (!ietBy.TryGetValue(("opus", label), out var oi) || !ietBy.TryGetValue(("haiku", label), out var hi)
                || !hietBy.TryGetValue(("opus", label), out var oh) || !hietBy.TryGetValue(("haiku", label), out var hh))
                continue;
            var ratio = hh != 0 ? oh / hh : 0.0;
            Console.WriteLine($"{L(label, 42)}{R(F0(oi), 10)}{R(F0(hi), 11)}{R(F0(oh), 12)}{R(F0(hh), 12)}{R(F1(ratio), 7)}x");
        }
        Console.WriteLine();
        Console.WriteLine("IET compares arms within one model; HIET (=IET x input-price-vs-Haiku: Opus 15x, Sonnet 3x,");
        Console.WriteLine("Haiku 1x) compares dollars across tiers. \"O/H $\" = Opus cost / Haiku cost for the same channel.");
        return 0;
    }

    private static Scenario Scen(string data, string stem, string tier) =>
        Grounding.Analyze.Loader.Parse(Path.Combine(data, $"{stem}.{tier}.json")).Verdicts![0].Scenarios![0];

    private static Arm? ArmByName(Scenario s, string arm) => arm switch
    {
        "baseline" => s.Baseline,
        "skilledIsolated" => s.SkilledIsolated,
        "skilledPlugin" => s.SkilledPlugin,
        _ => null,
    };

    // ---- formatting ------------------------------------------------------

    private static string Str(long? v) => v?.ToString(Inv) ?? "None";
    private static string Str(double? v) => v is { } d ? d.ToString(Inv) : "None";
    private static string F0(double v) => v.ToString("F0", Inv);
    private static string F1(double v) => v.ToString("F1", Inv);
    private static string F2(double v) => v.ToString("F2", Inv);
    private static string G(double v) => v.ToString("0.######", Inv);
    private static string SignedPct0(double v) => (v < 0 ? "-" : "+") + Math.Abs(v).ToString("F0", Inv);
    private static string L(string s, int w) => s.Length >= w ? s : s + new string(' ', w - s.Length);
    private static string R(string s, int w) => s.Length >= w ? s : new string(' ', w - s.Length) + s;
}
