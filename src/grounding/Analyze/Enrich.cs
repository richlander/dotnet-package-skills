using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Grounding.Json;

namespace Grounding.Analyze;

// Fixes the runs>1 aggregation gap. skill-validator collapses N runs into one scenario record by
// taking run[0]'s tool-call breakdown and run[last]'s events — neither averaged — so tool-call and
// nuget-archaeology counts are inconsistent single-run samples. The per-run truth IS collected: every
// run's full event log is persisted in sessions.db (run_results.metrics_json). This reads all runs,
// computes per-run AVERAGED, event-derived counts per (scenario, arm), and injects them into the
// dataset JSON as `metrics.toolStats` — making the dataset self-sufficient and internally consistent.
internal static class Enrich
{
    private sealed class Acc
    {
        public int Runs;
        public double Web, Bash, Other, Tools, Cache, NugetWeb, Di, Mcp;
        public double ToolTurnSecs, AllTurnSecs, ToolTurnIet, AllTurnIet, ToolTurns, AllTurns;
    }

    // role (sessions.db) -> arm key (dataset json).
    private static string? ArmKey(string? role) => role switch
    {
        "baseline" => "baseline",
        "with-skill-isolated" => "skilledIsolated",
        "with-skill-plugin" => "skilledPlugin",
        _ => null,
    };

    public static int Run(string datasetPath, string? sessionsDb, IetScheme model)
    {
        if (!File.Exists(datasetPath)) { Console.Error.WriteLine($"enrich: dataset not found: {datasetPath}"); return 1; }
        sessionsDb ??= FindSessionsDb(datasetPath);
        if (sessionsDb is null || !File.Exists(sessionsDb))
        {
            Console.Error.WriteLine("enrich: sessions.db not found. Pass --sessions-db <path> or --results-dir <dir>.");
            return 1;
        }

        // (scenario, arm) -> accumulated per-run stats.
        var acc = new Dictionary<(string scen, string arm), Acc>();
        using (var con = new SqliteConnection($"Data Source={sessionsDb};Mode=ReadOnly"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText =
                "SELECT s.scenario_name, s.role, r.metrics_json " +
                "FROM run_results r JOIN sessions s ON r.session_id = s.id";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var scen = rd.IsDBNull(0) ? "" : rd.GetString(0);
                var arm = ArmKey(rd.IsDBNull(1) ? null : rd.GetString(1));
                var mj = rd.IsDBNull(2) ? null : rd.GetString(2);
                if (arm is null || string.IsNullOrEmpty(mj)) continue;

                var m = JsonSerializer.Deserialize(mj, GroundingJsonContext.Default.Metrics);
                var events = m?.Events;
                var (web, bash, other, tools, cache, nugetWeb, di, mcp) = Loader.CountToolCalls(events);
                var (tSecs, aSecs, tIet, aIet, tTurns, aTurns) = Loader.CountToolTurns(events, model);

                var key = (scen, arm);
                if (!acc.TryGetValue(key, out var a)) acc[key] = a = new Acc();
                a.Runs++;
                a.Web += web; a.Bash += bash; a.Other += other; a.Tools += tools;
                a.Cache += cache; a.NugetWeb += nugetWeb; a.Di += di; a.Mcp += mcp;
                a.ToolTurnSecs += tSecs; a.AllTurnSecs += aSecs;
                a.ToolTurnIet += tIet; a.AllTurnIet += aIet;
                a.ToolTurns += tTurns; a.AllTurns += aTurns;
            }
        }

        if (acc.Count == 0) { Console.Error.WriteLine("enrich: no run_results found in sessions.db."); return 1; }

        // Inject averaged stats into the dataset JSON (DOM edit preserves all other fields).
        var root = JsonNode.Parse(File.ReadAllText(datasetPath))!;
        int injected = 0;
        foreach (var verdict in root["verdicts"]?.AsArray() ?? new JsonArray())
        foreach (var sc in verdict?["scenarios"]?.AsArray() ?? new JsonArray())
        {
            var scen = sc?["scenarioName"]?.GetValue<string>() ?? "";
            foreach (var arm in new[] { "baseline", "skilledIsolated", "skilledPlugin" })
            {
                if (!acc.TryGetValue((scen, arm), out var a) || a.Runs == 0) continue;
                var metrics = sc?[arm]?["metrics"];
                if (metrics is null) continue;
                double n = a.Runs;
                var ts = new ToolStats
                {
                    Runs = a.Runs,
                    Web = a.Web / n, Bash = a.Bash / n, Other = a.Other / n, Tools = a.Tools / n,
                    NugetCache = a.Cache / n, NugetWeb = a.NugetWeb / n, Di = a.Di / n, Mcp = a.Mcp / n,
                    ToolTurnSecs = a.ToolTurnSecs / n, AllTurnSecs = a.AllTurnSecs / n,
                    ToolTurnIet = a.ToolTurnIet / n, AllTurnIet = a.AllTurnIet / n,
                    ToolTurns = a.ToolTurns / n, AllTurns = a.AllTurns / n,
                };
                metrics["toolStats"] = JsonSerializer.SerializeToNode(ts, GroundingJsonContext.Default.ToolStats);
                injected++;
            }
        }

        File.WriteAllText(datasetPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        Console.WriteLine($"enrich: wrote toolStats for {injected} (scenario, arm) cells from {sessionsDb}.");
        return 0;
    }

    // sessions.db lives beside the results.json the dataset was copied from. When the dataset is a
    // cache copy we cannot know its results dir, so this only finds a sibling sessions.db.
    private static string? FindSessionsDb(string datasetPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(datasetPath));
        if (dir is null) return null;
        var sib = Path.Combine(dir, "sessions.db");
        return File.Exists(sib) ? sib : null;
    }
}
