using System.Text.Json;
using Grounding.Json;

namespace Grounding.Analyze;

// Per-arm, per-scenario raw row (the original `arm_row`).
internal sealed class ArmRow
{
    public double? Qual;          // null => judge score absent ("-")
    public int Fp, Ft;            // functional assertions passed / total
    public bool WebUsed;          // any reject-tools assertion failed
    public int Web, Di, Mcp, Cache, Bash;
    public double? Tools;
    public int? Turns;
    public long Tok, Iet, Out;
    public double ToolTurnSecs, ToolTurnSecsPct, ToolTurnIet, ToolTurnIetPct;
    public double Cost;           // rounded to 1 decimal, like Python (for aggregation)
    public string CostDisplay = ""; // raw-table cell, preserving JSON int/float type
    public long Secs;
}

internal static class Loader
{
    public static ArmRow? Row(Arm? arm)
    {
        if (arm?.Metrics is not { } m) return null;
        var ar = m.AssertionResults ?? new();
        var func = ar.Where(a => (a.Assertion?.Type ?? -1) != 11).ToList();
        var rej = ar.Where(a => (a.Assertion?.Type ?? -1) == 11).ToList();
        var tb = m.ToolCallBreakdown ?? new();
        int Tb(string k) => tb.TryGetValue(k, out var v) ? v : 0;
        var (di, mcp, cache) = CountToolEvents(m);
        var (toolTurnSecs, toolTurnIet) = CountToolTurns(m);
        long input = m.InputTokens, output = m.OutputTokens;
        var iet = (long)System.Math.Round(Iet(input, m.CacheReadTokens, m.CacheWriteTokens, output));
        return new ArmRow
        {
            Qual = arm.JudgeResult?.OverallScore,
            Fp = func.Count(a => a.Passed),
            Ft = func.Count,
            WebUsed = rej.Any(a => !a.Passed),
            Web = Tb("web_fetch") + Tb("web_search"),
            Tools = m.ToolCallCount,
            Turns = m.TurnCount,
            Di = di, Mcp = mcp, Cache = cache,
            Bash = Tb("bash"),
            Tok = input + output,
            // Price-weighted, input-equivalent cost stick (Claude-family ratios): fresh input 1x,
            // cacheRead 0.1x, cacheWrite 1.25x, output 5x. Maps to spend (IET x input_price ~= $),
            // and — unlike an unweighted count — does not undercount output, the class grounding
            // most reduces. See README.md "How we measure cost: IET".
            Iet = iet,
            Out = output,
            ToolTurnSecs = toolTurnSecs,
            ToolTurnSecsPct = m.WallTimeMs > 0 ? toolTurnSecs * 1000.0 / m.WallTimeMs * 100.0 : 0.0,
            ToolTurnIet = toolTurnIet,
            ToolTurnIetPct = iet > 0 ? toolTurnIet / iet * 100.0 : 0.0,
            Cost = Metrics.Round1(m.Cost),
            CostDisplay = m.CostIsInteger
                ? ((long)m.Cost).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : Metrics.Round1(m.Cost).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            Secs = (long)Math.Round(m.WallTimeMs / 1000.0, MidpointRounding.ToEven),
        };
    }

    private static double Iet(long input, long cacheRead, long cacheWrite, long output) =>
        (input - cacheRead) + 0.1 * cacheRead + 1.25 * cacheWrite + 5.0 * output;

    // (dotnet-inspect calls, MCP calls, NuGet-cache pokes) from the event log.
    private static (int di, int mcp, int cache) CountToolEvents(Json.Metrics m)
    {
        int di = 0, mcp = 0, cache = 0;
        foreach (var e in m.Events ?? new())
        {
            if (e.Type != "tool.execution_start") continue;
            var name = e.Data?.ToolName ?? "";
            var args = e.Data?.Arguments ?? "";
            if (name == "bash" && (args.Contains("dotnet-inspect") || args.Contains("dotnet inspect")))
                di++;
            if (name == "bash" && (args.Contains(".nuget/packages") || args.Contains(".nuget\\packages")
                                   || args.Contains("nuget/packages")))
                cache++;
            if (name.StartsWith("nuget-") || name.StartsWith("nuget_"))
                mcp++;
        }
        return (di, mcp, cache);
    }

    // Sum wall-clock duration and IET for turns that initiate at least one tool call.
    // Turns are inferred from the event stream's turn boundaries.
    private static (double secs, double iet) CountToolTurns(Json.Metrics m)
    {
        bool inTurn = false, hasTool = false;
        long start = 0, input = 0, cacheRead = 0, cacheWrite = 0, output = 0, toolDurationMs = 0;
        double toolIet = 0;

        foreach (var e in m.Events ?? new())
        {
            switch (e.Type)
            {
                case "assistant.turn_start":
                    inTurn = true;
                    hasTool = false;
                    start = e.Timestamp;
                    input = 0;
                    cacheRead = 0;
                    cacheWrite = 0;
                    output = 0;
                    break;
                case "assistant.usage" when inTurn:
                    input = e.Data?.InputTokens ?? 0;
                    cacheRead = e.Data?.CacheReadTokens ?? 0;
                    cacheWrite = e.Data?.CacheWriteTokens ?? 0;
                    output = e.Data?.OutputTokens ?? 0;
                    break;
                case "tool.execution_start" when inTurn:
                    hasTool = true;
                    break;
                case "assistant.turn_end" when inTurn:
                    if (hasTool)
                    {
                        toolIet += Iet(input, cacheRead, cacheWrite, output);
                        if (e.Timestamp >= start)
                            toolDurationMs += e.Timestamp - start;
                    }
                    inTurn = false;
                    break;
            }
        }

        return (toolDurationMs / 1000.0, toolIet);
    }

    public static Dictionary<string, ArmAgg> Aggregate(List<Scenario> scenarios)
    {
        var acc = Metrics.Arms.ToDictionary(a => a.Key, _ => new ArmAgg());
        var quals = Metrics.Arms.ToDictionary(a => a.Key, _ => new List<double>());
        foreach (var sc in scenarios)
        {
            foreach (var (key, _) in Metrics.Arms)
            {
                var r = Row(ArmOf(sc, key));
                if (r is null) continue;
                var a = acc[key];
                a.N++;
                if (r.Qual is { } q) quals[key].Add(q);
                a.Fp += r.Fp; a.Ft += r.Ft;
                // Functional success: all functional assertions passed. (Judge score is a
                // signal, not a gate — it wobbles near the floor and conflates with quality.)
                if (r.Ft > 0 && r.Fp == r.Ft)
                    a.Succ++;
                a.Iet += r.Iet; a.Cost += r.Cost; a.Tok += r.Tok;
                a.Out += r.Out; a.Web += r.Web; a.Cache += r.Cache;
                a.ToolTurnSecs += r.ToolTurnSecs; a.ToolTurnSecsPct += r.ToolTurnSecsPct;
                a.ToolTurnIet += r.ToolTurnIet; a.ToolTurnIetPct += r.ToolTurnIetPct;
            }
        }
        foreach (var (key, _) in Metrics.Arms)
        {
            var a = acc[key];
            var n = Math.Max(a.N, 1);
            a.Qual = quals[key].Count > 0 ? quals[key].Average() : null;
            a.Iet /= n; a.Cost /= n; a.Tok /= n; a.Out /= n;
            a.ToolTurnSecs /= n; a.ToolTurnSecsPct /= n;
            a.ToolTurnIet /= n; a.ToolTurnIetPct /= n;
        }
        return acc;
    }

    public static Arm? ArmOf(Scenario sc, string key) => key switch
    {
        "baseline" => sc.Baseline,
        "skilledIsolated" => sc.SkilledIsolated,
        "skilledPlugin" => sc.SkilledPlugin,
        _ => null,
    };

    public static ResultsFile Parse(string path)
    {
        using var fs = File.OpenRead(path);
        return JsonSerializer.Deserialize(fs, GroundingJsonContext.Default.ResultsFile)
               ?? new ResultsFile();
    }

    public static LoadedArm LoadArm(string path)
    {
        var d = Parse(path);
        var model = d.Model ?? "?";
        var v = d.Verdicts is { Count: > 0 } ? d.Verdicts[0] : new Verdict();
        var sn = v.SkillName ?? "?";
        var agg = Aggregate(v.Scenarios ?? new());
        // The grounding document is loaded into the grounded arms only; net it out of
        // "work" so a bigger doc (e.g. SKILL.md) isn't charged as agent effort.
        var gtok = GroundingTokens(v.SkillPath, sn) ?? 0;
        foreach (var (key, arm) in agg)
            arm.DocTok = key == "baseline" ? 0 : gtok;
        return new LoadedArm
        {
            Model = model,
            Judge = d.JudgeModel ?? model,
            Tier = Metrics.Tier(model),
            SkillName = sn,
            SkillPath = v.SkillPath,
            Agg = agg,
            IsReadme = System.IO.Path.GetFileName(path).ToLowerInvariant().Contains("readme"),
            Path = path,
        };
    }

    // ~tokens of the grounding loaded into each grounded arm (SKILL.md, else AGENTS.md).
    // Resolve the ACTUAL doc via the dataset's skillPath (the grounding/<unit> dir the run
    // used); fall back to skillName only when skillPath is absent. Using skillName alone is
    // wrong when several units share a frontmatter `name` (e.g. markout experiment variants).
    public static int? GroundingTokens(string? skillPath, string? skillName)
    {
        var root = RepoRoot.Find();
        if (root is null) return null;
        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(skillPath))
            dirs.Add(System.IO.Path.IsPathRooted(skillPath) ? skillPath : System.IO.Path.Combine(root, skillPath));
        if (!string.IsNullOrWhiteSpace(skillName))
            dirs.Add(System.IO.Path.Combine(root, "grounding", skillName));
        foreach (var dir in dirs)
            foreach (var art in new[] { "SKILL.md", "AGENTS.md" })
            {
                var p = System.IO.Path.Combine(dir, art);
                if (File.Exists(p))
                    return (int)Math.Round(File.ReadAllText(p).Length / 4.0, MidpointRounding.ToEven);
            }
        return null;
    }
}
