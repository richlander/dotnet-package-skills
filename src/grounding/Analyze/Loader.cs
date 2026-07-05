using System.Text.Json;
using Grounding.Json;

namespace Grounding.Analyze;

// Per-arm, per-scenario raw row (the original `arm_row`).
internal sealed class ArmRow
{
    public double? Qual;          // null => judge score absent ("-")
    public int Fp, Ft;            // functional assertions passed / total
    public bool WebUsed;          // any reject-tools assertion failed
    public int Web, Di, Mcp, Cache, Bash, NugetWeb;
    public double? Tools;
    public int? Turns;
    public long Tok, Iet, Out;
    public double ToolTurnSecs, ToolTurnSecsPct, ToolTurnIet, ToolTurnIetPct;
    public double ToolTurns, AllTurns, ToolTurnPct;
    public double OutIetPct;      // output's share of IET (OutputWeight*output / IET), % — the expensive class
    public bool Activated;        // grounded arm actually read the grounding (skill invoked)
    public double Cost;           // rounded to 1 decimal, like Python (for aggregation)
    public string CostDisplay = ""; // raw-table cell, preserving JSON int/float type
    public long Secs;
}

internal static class Loader
{
    public static ArmRow? Row(Arm? arm, IetModel model)
    {
        if (arm?.Metrics is not { } m) return null;
        var ar = m.AssertionResults ?? new();
        var func = ar.Where(a => (a.Assertion?.Type ?? -1) != 11).ToList();
        var rej = ar.Where(a => (a.Assertion?.Type ?? -1) == 11).ToList();
        var tb = m.ToolCallBreakdown ?? new();
        int Tb(string k) => tb.TryGetValue(k, out var v) ? v : 0;
        var (di, mcp, cache, nugetWeb) = CountToolEvents(m);
        var (toolTurnSecs, allTurnSecs, toolTurnIet, allTurnIet, toolTurns, allTurns) = CountToolTurns(m, model);
        long input = m.InputTokens, output = m.OutputTokens;
        var iet = (long)System.Math.Round(model.Iet(m));
        return new ArmRow
        {
            Qual = arm.JudgeResult?.OverallScore,
            Fp = func.Count(a => a.Passed),
            Ft = func.Count,
            WebUsed = rej.Any(a => !a.Passed),
            Web = Tb("web_fetch") + Tb("web_search"),
            Tools = m.ToolCallCount,
            Turns = m.TurnCount,
            Di = di, Mcp = mcp, Cache = cache, NugetWeb = nugetWeb,
            Bash = Tb("bash"),
            Tok = input + output,
            // Price-weighted, input-equivalent cost stick. The default Anthropic model treats
            // the fresh suffix as effective cache-write input and the cached prefix as
            // cache-read input. See README.md "How we measure cost: IET".
            Iet = iet,
            Out = output,
            OutIetPct = iet > 0 ? model.OutputWeight * output / iet * 100.0 : 0.0,
            ToolTurnSecs = toolTurnSecs,
            // Share of model turn-time spent in tool-calling turns. Denominator is the all-turn
            // duration sum (same basis as the numerator) — NOT WallTimeMs, which is measured on a
            // different basis (excludes tool-execution wait) and would let this exceed 100%.
            ToolTurnSecsPct = allTurnSecs > 0 ? toolTurnSecs / allTurnSecs * 100.0 : 0.0,
            ToolTurnIet = toolTurnIet,
            // Share of per-turn IET spent in tool-calling turns. Denominator is the all-turn
            // event-sum (same basis as the numerator) — NOT the session IET, which is on a
            // different accounting basis and would let this exceed 100%.
            ToolTurnIetPct = allTurnIet > 0 ? toolTurnIet / allTurnIet * 100.0 : 0.0,
            // Count of turns that fired >=1 tool call, and all assistant turns. Deterministic
            // activity signal; the share = tool-call turns / total turns (bounded by construction).
            ToolTurns = toolTurns,
            AllTurns = allTurns,
            ToolTurnPct = allTurns > 0 ? (double)toolTurns / allTurns * 100.0 : 0.0,
            Cost = Metrics.Round1(m.Cost),
            CostDisplay = m.CostIsInteger
                ? ((long)m.Cost).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : Metrics.Round1(m.Cost).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            Secs = (long)Math.Round(m.WallTimeMs / 1000.0, MidpointRounding.ToEven),
        };
    }

    // (dotnet-inspect calls, MCP calls, NuGet-cache pokes, nuget.org web calls) from the event log.
    private static (int di, int mcp, int cache, int nugetWeb) CountToolEvents(Json.Metrics m)
    {
        int di = 0, mcp = 0, cache = 0, nugetWeb = 0;
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
            // Remote nuget archaeology: a web tool fetching nuget.org (the other channel the
            // agent uses to recover package info the grounding did not supply).
            if ((name == "web_fetch" || name == "web_search") && args.Contains("nuget.org"))
                nugetWeb++;
            if (name.StartsWith("nuget-") || name.StartsWith("nuget_"))
                mcp++;
        }
        return (di, mcp, cache, nugetWeb);
    }

    // Sum wall-clock duration and IET for turns that initiate at least one tool call, plus the
    // IET over ALL turns (the consistent denominator for the tool-turn share). Turns are inferred
    // from the event stream's turn boundaries. Note: per-turn `input` is the cumulative re-sent
    // context, so per-turn IET is NOT the billed session IET — it is only meaningful as a ratio
    // (tool-turn IET / all-turn IET), both summed the same way here.
    private static (double toolSecs, double allSecs, double toolIet, double allIet, int toolTurns, int allTurns) CountToolTurns(Json.Metrics m, IetModel model)
    {
        bool inTurn = false, hasTool = false;
        long start = 0, input = 0, cacheRead = 0, output = 0, toolDurationMs = 0, allDurationMs = 0;
        double toolIet = 0, allIet = 0;
        int toolTurns = 0, allTurns = 0;

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
                    output = 0;
                    break;
                case "assistant.usage" when inTurn:
                    input = e.Data?.InputTokens ?? 0;
                    cacheRead = e.Data?.CacheReadTokens ?? 0;
                    output = e.Data?.OutputTokens ?? 0;
                    break;
                case "tool.execution_start" when inTurn:
                    hasTool = true;
                    break;
                case "assistant.turn_end" when inTurn:
                    var turnIet = model.Iet(input, cacheRead, output);
                    var turnMs = e.Timestamp >= start ? e.Timestamp - start : 0;
                    allIet += turnIet;
                    allDurationMs += turnMs;
                    allTurns++;
                    if (hasTool)
                    {
                        toolIet += turnIet;
                        toolDurationMs += turnMs;
                        toolTurns++;
                    }
                    inTurn = false;
                    break;
            }
        }

        return (toolDurationMs / 1000.0, allDurationMs / 1000.0, toolIet, allIet, toolTurns, allTurns);
    }

    public static Dictionary<string, ArmAgg> Aggregate(List<Scenario> scenarios, IetModel model)
    {
        var acc = Metrics.Arms.ToDictionary(a => a.Key, _ => new ArmAgg());
        var quals = Metrics.Arms.ToDictionary(a => a.Key, _ => new List<double>());
        foreach (var sc in scenarios)
        {
            foreach (var (key, _) in Metrics.Arms)
            {
                var r = Row(ArmOf(sc, key), model);
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
                a.Out += r.Out; a.Web += r.Web; a.Cache += r.Cache; a.NugetWeb += r.NugetWeb;
                a.Bash += r.Bash; a.Tools += (int)Math.Round(r.Tools ?? 0);
                a.ToolTurnSecs += r.ToolTurnSecs; a.ToolTurnSecsPct += r.ToolTurnSecsPct;
                a.ToolTurnIet += r.ToolTurnIet; a.ToolTurnIetPct += r.ToolTurnIetPct;
                a.ToolTurns += r.ToolTurns; a.AllTurns += r.AllTurns; a.ToolTurnPct += r.ToolTurnPct;
                a.OutIetPct += r.OutIetPct;
                a.Activated += ActivatedOf(sc, key) ? 1 : 0;
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
            a.ToolTurns /= n; a.AllTurns /= n; a.ToolTurnPct /= n;
            a.OutIetPct /= n; a.Activated /= n;
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

    // Did the grounded arm actually read the grounding (skill invoked)? Baseline has none.
    public static bool ActivatedOf(Scenario sc, string key) => key switch
    {
        "skilledIsolated" => sc.SkillActivationIsolated?.Activated ?? false,
        "skilledPlugin" => sc.SkillActivationPlugin?.Activated ?? false,
        _ => false,
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
        var iet = IetModels.For(model);
        var v = d.Verdicts is { Count: > 0 } ? d.Verdicts[0] : new Verdict();
        var sn = v.SkillName ?? "?";
        var agg = Aggregate(v.Scenarios ?? new(), iet);
        // The grounding document is only charged when the grounded arm actually READ it
        // (skill invoked). Weight the doc's token load by the activation rate — a run that
        // never activated loaded 0 grounding tokens.
        var gtok = GroundingTokens(v.SkillPath, sn) ?? 0;
        foreach (var (key, arm) in agg)
        {
            arm.Grounded = key != "baseline";
            arm.DocTok = arm.Grounded ? (int)Math.Round(gtok * arm.Activated) : 0;
        }
        var file = System.IO.Path.GetFileName(path).ToLowerInvariant();
        return new LoadedArm
        {
            Model = model,
            Iet = iet,
            Judge = d.JudgeModel ?? model,
            Tier = Metrics.Tier(model),
            SkillName = sn,
            SkillPath = v.SkillPath,
            Agg = agg,
            // Source of the grounding content fed into the SKILL.md the run loaded (the run
            // tag encodes it: `<unit>-readme.*` / `<unit>-skill.*`; bare `<unit>.*` = agents).
            IsReadme = file.Contains("readme"),
            IsSkill = file.Contains("skill"),
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
