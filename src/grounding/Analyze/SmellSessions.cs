using System.Text.Json;
using Microsoft.Data.Sqlite;
using Grounding.Json;

namespace Grounding.Analyze;

// LLM-free smell reader. Rebuilds a ResultsFile straight from sessions.db (the agent runs the
// harness always persists) WITHOUT invoking any judge. Everything the SmellCard shows — tasks
// correct (functional assertions), archaeology (cache/web), tool split, output %, turns, IET — is
// deterministic and already sits in run_results.metrics_json. Only the pairwise/independent quality
// score (Arm.JudgeResult) needs the LLM; it is left null here. This is exactly how we measure "how
// much value the LLM judges add": the deterministic card vs. the same run rejudged.
//
// One casualty: which skills were pulled. The authoritative session.skills_loaded payload is emptied
// in the persisted metrics_json, so detectedSkills degrades to a best-effort activation boolean
// (skillEventCount > 0). The judged path still carries the full histogram in results.json.
internal static class SmellSessions
{
    // role (sessions.db) -> arm setter on the synthetic scenario. Mirrors Enrich.ArmKey: skill-eval
    // and agent-eval (push) share the three-way arm shape; both isolated roles fold into one arm.
    private static string? ArmKey(string? role) => role switch
    {
        "baseline" => "baseline",
        "baseline-reused" => "baseline",
        "with-skill-isolated" => "skilledIsolated",
        "with-skill-plugin" => "skilledPlugin",
        "with-agent-isolated" => "skilledIsolated",
        "with-agent-plugin" => "skilledPlugin",
        _ => null,
    };

    public static ResultsFile BuildFromSessions(string sessionsDb)
    {
        // One synthetic Scenario per (scenario_name, run_index) so runs=1 -> N scenario rows (clean)
        // and Aggregate's per-run means/Succ come out right without a runs>1 collapse.
        var scenarios = new Dictionary<(string scen, long run), Scenario>();
        string? model = null, skillName = null, skillPath = null;

        using (var con = new SqliteConnection($"Data Source={sessionsDb};Mode=ReadOnly"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText =
                "SELECT s.scenario_name, s.role, s.run_index, s.model, s.skill_name, s.skill_path, r.metrics_json " +
                "FROM run_results r JOIN sessions s ON r.session_id = s.id";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var scen = rd.IsDBNull(0) ? "" : rd.GetString(0);
                var arm = ArmKey(rd.IsDBNull(1) ? null : rd.GetString(1));
                var run = rd.IsDBNull(2) ? 0L : rd.GetInt64(2);
                model ??= rd.IsDBNull(3) ? null : rd.GetString(3);
                skillName ??= rd.IsDBNull(4) ? null : rd.GetString(4);
                skillPath ??= rd.IsDBNull(5) ? null : rd.GetString(5);
                var mj = rd.IsDBNull(6) ? null : rd.GetString(6);
                if (arm is null || string.IsNullOrEmpty(mj)) continue;

                var m = JsonSerializer.Deserialize(mj, GroundingJsonContext.Default.Metrics);
                if (m is null) continue;

                var key = (scen, run);
                if (!scenarios.TryGetValue(key, out var sc))
                    scenarios[key] = sc = new Scenario { ScenarioName = scen };
                var armObj = new Arm { Metrics = m };  // JudgeResult stays null: no LLM in this path.
                switch (arm)
                {
                    case "baseline": sc.Baseline = armObj; break;
                    case "skilledIsolated":
                        sc.SkilledIsolated = armObj;
                        sc.SkillActivationIsolated = Activation(m.Events);
                        break;
                    case "skilledPlugin":
                        sc.SkilledPlugin = armObj;
                        sc.SkillActivationPlugin = Activation(m.Events);
                        break;
                }
            }
        }

        return new ResultsFile
        {
            Model = model,
            JudgeModel = null,
            Verdicts = new List<Verdict>
            {
                new() { SkillName = skillName, SkillPath = skillPath, Scenarios = scenarios.Values.ToList() },
            },
        };
    }

    // Deterministic activation from the persisted events. detectedSkills is best-effort: the
    // skills_loaded payload is emptied in metrics_json, so names are usually unrecoverable here —
    // we still surface the activation boolean (skillEventCount > 0) that the card's "relied on
    // grounding" signal needs.
    private static SkillActivation Activation(List<EventRecord>? events)
    {
        var names = new List<string>();
        int skillEvents = 0;
        foreach (var e in events ?? new())
        {
            var t = e.Type ?? "";
            if (t.Contains("skill", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("instruction", StringComparison.OrdinalIgnoreCase))
            {
                skillEvents++;
                if (e.Data?.Name is { Length: > 0 } n) names.Add(n);
            }
        }
        return new SkillActivation
        {
            Activated = skillEvents > 0,
            SkillEventCount = skillEvents,
            DetectedSkills = names.Distinct().ToList(),
        };
    }
}
