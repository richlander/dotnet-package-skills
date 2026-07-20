using System.Text.Json;
using Grounding.Json;

namespace Grounding.Analyze;

// Per-arm, per-scenario raw row (the original `arm_row`).
internal sealed class ArmRow
{
    public double? Qual;          // null => judge score absent ("-")
    public int Fp, Ft;            // functional assertions passed / total
    public bool WebUsed;          // any reject-tools assertion failed
    // Tool-call counts. Doubles because the enriched path (ToolStats) carries per-run MEANS.
    public double Web, Di, Mcp, Cache, Bash, NugetWeb, Other;
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
    public static ArmRow? Row(Arm? arm, IetScheme model)
    {
        if (arm?.Metrics is not { } m) return null;
        var ar = m.AssertionResults ?? new();
        var func = ar.Where(a => (a.Assertion?.Type ?? -1) != 11).ToList();
        var rej = ar.Where(a => (a.Assertion?.Type ?? -1) == 11).ToList();

        // Prefer the enriched, per-run-AVERAGED, event-derived stats (grounding enrich) — they are
        // consistent across all runs. Fall back to single-run events/breakdown for old datasets.
        double web, bash, other, tools, cache, nugetWeb, di, mcp;
        double toolTurnSecs, allTurnSecs, toolTurnIet, allTurnIet, toolTurns, allTurns;
        if (m.ToolStats is { } ts)
        {
            web = ts.Web; bash = ts.Bash; other = ts.Other; tools = ts.Tools;
            cache = ts.NugetCache; nugetWeb = ts.NugetWeb; di = ts.Di; mcp = ts.Mcp;
            toolTurnSecs = ts.ToolTurnSecs; allTurnSecs = ts.AllTurnSecs;
            toolTurnIet = ts.ToolTurnIet; allTurnIet = ts.AllTurnIet;
            toolTurns = ts.ToolTurns; allTurns = ts.AllTurns;
        }
        else
        {
            var c = CountToolCalls(m.Events);
            web = c.web; bash = c.bash; other = c.other; tools = c.tools;
            cache = c.cache; nugetWeb = c.nugetWeb; di = c.di; mcp = c.mcp;
            (toolTurnSecs, allTurnSecs, toolTurnIet, allTurnIet, toolTurns, allTurns) = CountToolTurns(m.Events, model);
        }
        long input = m.InputTokens, output = m.OutputTokens;
        var iet = (long)System.Math.Round(model.Iet(m));
        return new ArmRow
        {
            Qual = arm.JudgeResult?.OverallScore,
            Fp = func.Count(a => a.Passed),
            Ft = func.Count,
            WebUsed = rej.Any(a => !a.Passed),
            Web = web,
            Tools = tools,
            Turns = m.TurnCount,
            Di = di, Mcp = mcp, Cache = cache, NugetWeb = nugetWeb,
            Bash = bash,
            Other = other,
            Tok = input + output,
            // Price-weighted, input-equivalent cost stick. The default Anthropic model treats
            // the fresh suffix as effective cache-write input and the cached prefix as
            // cache-read input. See README.md "How we measure cost: IET".
            Iet = iet,
            Out = output,
            OutIetPct = iet > 0 ? model.WOutput * output / iet * 100.0 : 0.0,
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

    // All tool-call counts from an event log: total tools, web, bash, other (=tools-web-bash),
    // and the diagnostic subsets (dotnet-inspect, MCP, .nuget-cache pokes, nuget.org web calls).
    // Web/bash are counted from EVENTS (not toolCallBreakdown) so every count shares one basis.
    public static (double web, double bash, double other, double tools, double cache, double nugetWeb, double di, double mcp)
        CountToolCalls(List<Json.EventRecord>? events)
    {
        int tools = 0, web = 0, bash = 0, di = 0, mcp = 0, cache = 0, nugetWeb = 0;
        foreach (var e in events ?? new())
        {
            if (e.Type != "tool.execution_start") continue;
            var name = e.Data?.ToolName ?? "";
            var args = e.Data?.Arguments ?? "";
            tools++;
            if (name == "web_fetch" || name == "web_search")
            {
                web++;
                if (args.Contains("nuget.org")) nugetWeb++; // remote nuget archaeology
            }
            else if (name == "bash")
            {
                bash++;
                if (args.Contains("dotnet-inspect") || args.Contains("dotnet inspect")) di++;
                if (args.Contains(".nuget/packages") || args.Contains(".nuget\\packages")
                    || args.Contains("nuget/packages")) cache++; // local nuget archaeology
            }
            if (name.StartsWith("nuget-") || name.StartsWith("nuget_")) mcp++;
        }
        return (web, bash, Math.Max(0, tools - web - bash), tools, cache, nugetWeb, di, mcp);
    }

    // Sum wall-clock duration and IET for turns that initiate at least one tool call, plus the
    // IET over ALL turns (the consistent denominator for the tool-turn share). Turns are inferred
    // from the event stream's turn boundaries. Note: per-turn `input` is the cumulative re-sent
    // context, so per-turn IET is NOT the billed session IET — it is only meaningful as a ratio
    // (tool-turn IET / all-turn IET), both summed the same way here.
    public static (double toolSecs, double allSecs, double toolIet, double allIet, double toolTurns, double allTurns) CountToolTurns(List<Json.EventRecord>? events, IetScheme model)
    {
        bool inTurn = false, hasTool = false;
        long start = 0, input = 0, cacheRead = 0, output = 0, toolDurationMs = 0, allDurationMs = 0;
        // Accumulate raw token BUCKETS (scheme-neutral), then price once at the end. Pricing is
        // linear, so this equals summing per-turn IET — but keeping buckets means these sums stay
        // re-priceable (e.g. under the no-cache modifier) instead of baking in a weighting.
        IetValue? toolVal = null, allVal = null;
        int toolTurns = 0, allTurns = 0;

        foreach (var e in events ?? new())
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
                    var turnVal = model.Value(input, cacheRead, output, null);
                    var turnMs = e.Timestamp >= start ? e.Timestamp - start : 0;
                    allVal = allVal is { } av ? av + turnVal : turnVal;
                    allDurationMs += turnMs;
                    allTurns++;
                    if (hasTool)
                    {
                        toolVal = toolVal is { } tv ? tv + turnVal : turnVal;
                        toolDurationMs += turnMs;
                        toolTurns++;
                    }
                    inTurn = false;
                    break;
            }
        }

        double toolIet = toolVal is { } t ? model.Price(t, IetModels.NoCache) : 0;
        double allIet = allVal is { } a ? model.Price(a, IetModels.NoCache) : 0;
        return (toolDurationMs / 1000.0, allDurationMs / 1000.0, toolIet, allIet, toolTurns, allTurns);
    }

    public static Dictionary<string, ArmAgg> Aggregate(List<Scenario> scenarios, IetScheme model)
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
                a.Bash += r.Bash; a.Tools += r.Tools ?? 0;
                a.ToolTurnSecs += r.ToolTurnSecs; a.ToolTurnSecsPct += r.ToolTurnSecsPct;
                a.ToolTurnIet += r.ToolTurnIet; a.ToolTurnIetPct += r.ToolTurnIetPct;
                a.ToolTurns += r.ToolTurns; a.AllTurns += r.AllTurns; a.ToolTurnPct += r.ToolTurnPct;
                a.OutIetPct += r.OutIetPct;
                a.Activated += ActivatedOf(sc, key) ? 1 : 0;
                foreach (var s in DetectedSkillsOf(sc, key).Distinct())
                    a.SkillCounts[s] = a.SkillCounts.TryGetValue(s, out var sc2) ? sc2 + 1 : 1;
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

    // Which skills the grounded arm pulled off the shelf (plugin = self-select from whole shelf).
    public static IReadOnlyList<string> DetectedSkillsOf(Scenario sc, string key) => key switch
    {
        "skilledIsolated" => sc.SkillActivationIsolated?.DetectedSkills ?? new(),
        "skilledPlugin" => sc.SkillActivationPlugin?.DetectedSkills ?? new(),
        _ => new List<string>(),
    };

    public static ResultsFile Parse(string path)
    {
        using var fs = File.OpenRead(path);
        return JsonSerializer.Deserialize(fs, GroundingJsonContext.Default.ResultsFile)
               ?? new ResultsFile();
    }

    public static LoadedArm LoadArm(string path) => BuildLoadedArm(Parse(path), path);

    // Route by extension: a sessions.db goes through the LLM-free deterministic reader (no judge,
    // no results.json); a results.json goes through the normal parse. Lets `smell` render a run
    // straight from persisted agent sessions.
    public static LoadedArm LoadAny(string path) =>
        path.EndsWith(".db", StringComparison.OrdinalIgnoreCase)
            ? LoadArmFromSessions(path)
            : LoadArm(path);

    public static LoadedArm LoadArmFromSessions(string sessionsDb)
    {
        var d = SmellSessions.BuildFromSessions(sessionsDb);
        var sn = d.Verdicts is { Count: > 0 } ? d.Verdicts[0].SkillName ?? "skill" : "skill";
        var ms = d.Model ?? "model";
        // Synthetic filename so the arm's source flags (IsSkill) resolve; the directory is the db's
        // so a sibling results.json/skill dir could still be found if present.
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(sessionsDb)) ?? ".";
        var synthetic = System.IO.Path.Combine(dir, $"{sn}-skill.{ms}.json");
        return BuildLoadedArm(d, synthetic);
    }

    public static LoadedArm BuildLoadedArm(ResultsFile d, string path)
    {
        var model = d.Model ?? "?";
        var iet = IetModels.For(model);
        var v = d.Verdicts is { Count: > 0 } ? d.Verdicts[0] : new Verdict();
        var sn = v.SkillName ?? "?";
        var agg = Aggregate(v.Scenarios ?? new(), iet);
        var file = System.IO.Path.GetFileName(path).ToLowerInvariant();
        // Push delivery (.agent.md, always-on primary persona) is loaded at t=0 by construction,
        // so there is no activation gate — the SkillActivation field is a pull (skill-invoke)
        // signal and is absent for agent-eval. Treat push grounded arms as 100% activated.
        var isPush = file.Contains("push");
        // The grounding document is only charged when the grounded arm actually READ it
        // (skill invoked). Weight the doc's token load by the activation rate — a run that
        // never activated loaded 0 grounding tokens. Push is always resident (rate = 1).
        var gtok = GroundingTokens(v.SkillPath, sn) ?? 0;
        foreach (var (key, arm) in agg)
        {
            arm.Grounded = key != "baseline";
            if (isPush && arm.Grounded) arm.Activated = 1.0;
            arm.DocTok = arm.Grounded ? (int)Math.Round(gtok * arm.Activated) : 0;
            // Grounding IET = the doc's own carrying cost: written once, cache-read each later turn.
            // Work IET = everything else (agent thinking/output/tools). Total = Grounding + Work.
            var writeW = iet.FoldNonCachedIntoCacheWrite ? iet.WCacheWrite : iet.WFresh;
            var reads = Math.Max(0.0, arm.AllTurns - 1);
            arm.GroundingIet = arm.DocTok * (writeW + iet.WCacheRead * reads);
            arm.WorkIet = arm.Iet - arm.GroundingIet;
        }
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
            IsPush = isPush,
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
