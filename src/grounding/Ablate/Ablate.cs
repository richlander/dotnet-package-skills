using System.Text.Json;
using Microsoft.Data.Sqlite;
using Grounding.Json;
using Grounding.Analyze;
using Grounding.Run;

namespace Grounding.Ablate;

// Leave-one-out skill ablation driver (composition-axis LIET). For a multi-skill shelf, the
// value of a single skill X on a scenario is its MARGINAL contribution:
//
//     marginal(X) = outcome(full shelf) − outcome(shelf − X)
//
// This orchestrates the measurement end-to-end on the holistic (self-selecting plugin) arm:
//   1. Run the FULL shelf once (holistic, --keep-sessions) — the reference outcome.
//   2. Mine per-run skill pulls from that run's sessions.db and keep only skills a scenario
//      pulls CONSISTENTLY (pull-rate ≥ --consistency, default 4/5) — a strong activation signal.
//   3. For each such domain skill X, re-run the shelf MINUS X (holistic, --exclude-skill X).
//   4. Report the per-(skill, scenario) marginal = full − (shelf−X). Negative marginals flag
//      DESTRUCTIVE interference (the free rider wasn't neutral — it cost tokens or encoded
//      disagreement); ~0 marginals flag a free rider that adds nothing.
//
// Only the plugin arm re-runs per cell (baseline is reused via --baseline-from when supplied),
// so the cost is roughly (1 + |ablated skills|) plugin passes over the selected scenarios.
internal sealed class AblateOptions
{
    public required string Unit;
    public required List<string> Models;   // first model is used
    public int Runs = 5;
    public string? Root;                   // skills root (e.g. a target package repo)
    public string? TestsDir;               // eval root (default "grounding")
    public string? BaselineFrom;           // reuse a persisted baseline (plugin-arm-only cells)
    public double Consistency = 0.8;       // pull-rate threshold to qualify a skill (4/5 = 0.8)
    public List<string>? Skills;           // explicit ablation set; null = auto from consistent pulls
    public List<string>? Scenarios;        // scenario name prefixes to keep; null = all
    public bool DryRun;
    public string? OutDir;
}

internal static class Ablate
{
    public static int Run(AblateOptions o)
    {
        if (o.Models.Count == 0) { Console.Error.WriteLine("ablate: no --model."); return 1; }
        var model = o.Models[0];
        var ms = ShortModel(model);
        // Write ablation datasets to a dedicated dir so the canonical full-shelf cache dataset
        // (<unit>-6q/<unit>-skill.<m>.json, possibly 24-scenario) is not clobbered by a scenario-
        // subset ablation run. Sessions still land under results/<unit>-<tag> (tag-keyed).
        var outDir = o.OutDir ?? Path.Combine(DataCache.Base(), $"{o.Unit}-ablate");
        Directory.CreateDirectory(outDir);
        var baseSkill = o.Unit; // the base skill dir is the unit name (never ablated)

        // Optionally stage a scenario-filtered eval so a "clean" subset runs cheaply. The staged
        // tree copies meta.yaml + fixtures verbatim and keeps only the requested scenario blocks.
        string? testsDirArg = o.TestsDir;
        string? staged = null;
        if (o.Scenarios is { Count: > 0 })
        {
            staged = StageFilteredTests(o, out var keptCount);
            if (staged is null) return 1;
            testsDirArg = staged;
            Console.WriteLine($"ablate: staged {keptCount} scenario(s) -> {Path.Combine(staged, o.Unit)}");
        }

        // Reuse ONE baseline across all cells. If the caller supplied a baseline, use it
        // everywhere; otherwise the full-shelf run persists a fresh baseline that the
        // shelf-minus runs reuse — so the (identical) baseline arm runs only once.
        var sharedBaseline = o.BaselineFrom
            ?? Path.Combine(Path.GetTempPath(), $"abl-baseline-{o.Unit}-{ms}.json");
        var persistBaseline = o.BaselineFrom is null;

        try
        {
            // 1. Full-shelf holistic reference run (fresh, so a new sessions.db is produced to mine).
            Console.WriteLine($"\n=== ablate {o.Unit} [{ms}] · full shelf (reference) ===");
            var fullTag = $"{o.Unit}-skill.{ms}";
            var rc = RunCell(o, model, exclude: null, testsDirArg, fresh: true, outDir: outDir,
                baselineOut: persistBaseline ? sharedBaseline : null,
                baselineFrom: persistBaseline ? null : sharedBaseline);
            if (o.DryRun)
            {
                if (o.Skills is { Count: > 0 })
                    foreach (var x in o.Skills)
                        Console.WriteLine($"=== ablate {o.Unit} [{ms}] · shelf − {x} (dry-run) ===\n    (would run --exclude-skill {x})");
                return rc;
            }
            if (rc != 0) return rc;

            var fullDs = Path.Combine(outDir, fullTag + ".json");
            var sdb = LatestSessionsDb(o.Unit, fullTag);
            if (sdb is null)
            {
                Console.Error.WriteLine($"ablate: no sessions.db for the full-shelf run under {DataCache.ResultsDir(o.Unit, fullTag)}.");
                return 1;
            }

            // 2. Mine per-run pulls and per-scenario consistent skills.
            var (runsPerScen, pulls) = MinePulls(sdb);
            var consistentByScenario = ConsistentPulls(pulls, runsPerScen, o.Consistency, baseSkill);

            // 3. Determine the ablation set: explicit --skills, else the union of consistent pulls.
            var ablSkills = (o.Skills is { Count: > 0 }
                    ? o.Skills
                    : consistentByScenario.Values.SelectMany(s => s).Distinct().ToList())
                .Where(s => !s.Equals(baseSkill, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            PrintConsistency(runsPerScen, pulls, consistentByScenario, o.Consistency, baseSkill);

            if (ablSkills.Count == 0)
            {
                Console.WriteLine("\nablate: no consistently-pulled domain skills to ablate. Done.");
                return 0;
            }
            Console.WriteLine($"\nablate: ablating {ablSkills.Count} skill(s): {string.Join(", ", ablSkills)}");

            // 4. Shelf-minus-X runs.
            var minusDs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var x in ablSkills)
            {
                Console.WriteLine($"\n=== ablate {o.Unit} [{ms}] · shelf − {x} ===");
                // fresh: ablation is a measurement, not a cheap re-run — always regenerate so a
                // stale cached minus dataset from a different --scenarios set is never reused
                // (pin-reuse is keyed on doc+corpus, not the scenario subset).
                var mrc = RunCell(o, model, exclude: x, testsDirArg, fresh: true, outDir: outDir,
                    baselineOut: null, baselineFrom: sharedBaseline);
                if (mrc != 0) return mrc;
                minusDs[x] = Path.Combine(outDir, $"{o.Unit}-skill-minus-{x}.{ms}.json");
            }

            // 5. Marginal report.
            Report(fullDs, minusDs, consistentByScenario);
            return 0;
        }
        finally
        {
            if (staged is not null)
                try { Directory.Delete(staged, recursive: true); } catch { /* best effort */ }
            if (persistBaseline)
                foreach (var bf in new[] { sharedBaseline, sharedBaseline + ".arms.json", sharedBaseline + ".prov.json" })
                    try { if (File.Exists(bf)) File.Delete(bf); } catch { /* best effort */ }
        }
    }

    // Build a RunOptions and drive one plugin-arm cell through the shared runner.
    private static int RunCell(AblateOptions o, string model, string? exclude, string? testsDir, bool fresh,
        string outDir, string? baselineOut, string? baselineFrom)
    {
        var ro = new RunOptions
        {
            Unit = o.Unit,
            Source = "skill",
            Models = new List<string> { model },
            Runs = o.Runs,
            EvalMode = "holistic",
            Root = o.Root,
            TestsDir = testsDir,
            BaselineOut = baselineOut,
            BaselineFrom = baselineFrom,
            OutDir = outDir,
            DryRun = o.DryRun,
            Fresh = fresh,
            ExcludeSkills = exclude is null ? new List<string>() : new List<string> { exclude },
        };
        return Runner.Run(ro);
    }

    // ---- consistency mining -------------------------------------------------

    // Per (scenario, skill) pull count and per-scenario run count, read from the full-shelf run's
    // persisted per-run event logs. A "pull" is a skill.invoked event naming that skill in a run;
    // a skill counts at most once per run (distinct names within the run).
    private static (Dictionary<string, int> runsPerScen, Dictionary<string, Dictionary<string, int>> pulls)
        MinePulls(string sessionsDb)
    {
        var runsPerScen = new Dictionary<string, int>();
        var pulls = new Dictionary<string, Dictionary<string, int>>();

        using var con = new SqliteConnection($"Data Source={sessionsDb};Mode=ReadOnly");
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText =
            "SELECT s.scenario_name, r.metrics_json FROM run_results r " +
            "JOIN sessions s ON r.session_id = s.id WHERE s.role = 'with-skill-plugin'";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var scen = rd.IsDBNull(0) ? "" : rd.GetString(0);
            var mj = rd.IsDBNull(1) ? null : rd.GetString(1);
            if (string.IsNullOrEmpty(scen) || string.IsNullOrEmpty(mj)) continue;

            var m = JsonSerializer.Deserialize(mj, GroundingJsonContext.Default.Metrics);
            var pulledThisRun = SkillsInvoked(m?.Events);

            runsPerScen[scen] = runsPerScen.GetValueOrDefault(scen) + 1;
            if (!pulls.TryGetValue(scen, out var byScen)) pulls[scen] = byScen = new();
            foreach (var sk in pulledThisRun)
                byScen[sk] = byScen.GetValueOrDefault(sk) + 1;
        }
        return (runsPerScen, pulls);
    }

    // Distinct skill names invoked in one run's event log (skill.invoked → data.name).
    private static HashSet<string> SkillsInvoked(List<EventRecord>? events)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (events is null) return set;
        foreach (var e in events)
        {
            var t = e.Type?.ToLowerInvariant() ?? "";
            if (!t.Contains("skill") && !t.Contains("instruction")) continue;
            var name = e.Data?.Name;
            if (!string.IsNullOrEmpty(name)) set.Add(name);
        }
        return set;
    }

    // Per scenario, the skills whose pull-rate meets the threshold (base skill excluded).
    private static Dictionary<string, List<string>> ConsistentPulls(
        Dictionary<string, Dictionary<string, int>> pulls,
        Dictionary<string, int> runsPerScen, double threshold, string baseSkill)
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var (scen, byScen) in pulls)
        {
            var n = runsPerScen.GetValueOrDefault(scen, 0);
            if (n == 0) continue;
            var keep = byScen
                .Where(kv => !kv.Key.Equals(baseSkill, StringComparison.OrdinalIgnoreCase))
                .Where(kv => (double)kv.Value / n >= threshold)
                .Select(kv => kv.Key)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
            result[scen] = keep;
        }
        return result;
    }

    private static void PrintConsistency(
        Dictionary<string, int> runsPerScen,
        Dictionary<string, Dictionary<string, int>> pulls,
        Dictionary<string, List<string>> consistent, double threshold, string baseSkill)
    {
        Console.WriteLine($"\n--- pull consistency (threshold {threshold:P0}, base '{baseSkill}' excluded) ---");
        foreach (var scen in runsPerScen.Keys.OrderBy(s => s, StringComparer.Ordinal))
        {
            var n = runsPerScen[scen];
            var parts = pulls[scen]
                .OrderByDescending(kv => kv.Value)
                .Select(kv => $"{kv.Key} {kv.Value}/{n}{(consistent[scen].Contains(kv.Key) ? "✓" : "")}");
            Console.WriteLine($"  {Short(scen)}: {string.Join(" · ", parts)}");
        }
    }

    // ---- marginal report ----------------------------------------------------

    private static void Report(
        string fullDs, Dictionary<string, string> minusDs,
        Dictionary<string, List<string>> consistent)
    {
        var full = ScenarioScores(fullDs);
        Console.WriteLine("\n===================== ablation marginals =====================");
        Console.WriteLine("marginal(X) = score(full shelf) − score(shelf − X)   [holistic plugin arm]");
        Console.WriteLine("  > 0  X helps · ~0  free rider · < 0  destructive interference\n");
        Console.WriteLine($"{"skill",-24} {"scenario",-40} {"full",7} {"-X",7} {"margin",8}");
        Console.WriteLine(new string('-', 90));

        var perSkill = new Dictionary<string, List<double>>();
        foreach (var (skill, dsPath) in minusDs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!File.Exists(dsPath)) { Console.WriteLine($"{skill,-24} (no dataset: {dsPath})"); continue; }
            var minus = ScenarioScores(dsPath);
            var scens = consistent.Where(kv => kv.Value.Contains(skill, StringComparer.OrdinalIgnoreCase))
                                   .Select(kv => kv.Key);
            foreach (var scen in scens.OrderBy(s => s, StringComparer.Ordinal))
            {
                if (!full.TryGetValue(scen, out var f) || !minus.TryGetValue(scen, out var mx)) continue;
                var margin = f - mx;
                perSkill.TryAdd(skill, new List<double>());
                perSkill[skill].Add(margin);
                var flag = margin < -0.02 ? " ⚠ destructive" : Math.Abs(margin) <= 0.02 ? " · free rider" : "";
                Console.WriteLine($"{skill,-24} {Short(scen),-40} {f,7:F3} {mx,7:F3} {margin,8:F3}{flag}");
            }
        }

        Console.WriteLine(new string('-', 90));
        foreach (var (skill, margins) in perSkill.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            Console.WriteLine($"{skill,-24} {"mean marginal",-40} {"",7} {"",7} {margins.Average(),8:F3}");
    }

    private static Dictionary<string, double> ScenarioScores(string datasetPath)
    {
        var map = new Dictionary<string, double>();
        var rf = Loader.Parse(datasetPath);
        foreach (var v in rf.Verdicts ?? new())
            foreach (var sc in v.Scenarios ?? new())
            {
                var name = sc.ScenarioName ?? sc.Name;
                if (name is null || sc.ImprovementScore is null) continue;
                map[name] = sc.ImprovementScore.Value;
            }
        return map;
    }

    // ---- staging + helpers --------------------------------------------------

    // Copy grounding/<unit> to a temp tree keeping only the requested scenario blocks in eval.yaml.
    private static string? StageFilteredTests(AblateOptions o, out int kept)
    {
        kept = 0;
        var root = o.Root ?? Directory.GetCurrentDirectory();
        var testsDir = o.TestsDir ?? "grounding";
        var srcUnitDir = Path.IsPathRooted(testsDir)
            ? Path.Combine(testsDir, o.Unit)
            : Path.Combine(root, testsDir, o.Unit);
        var evalPath = Path.Combine(srcUnitDir, "eval.yaml");
        if (!File.Exists(evalPath))
        {
            Console.Error.WriteLine($"ablate: eval.yaml not found at {evalPath}.");
            return null;
        }

        var stagedRoot = Path.Combine(Path.GetTempPath(), $"abl-tests-{Guid.NewGuid():N}");
        var stagedUnit = Path.Combine(stagedRoot, o.Unit);
        Directory.CreateDirectory(stagedUnit);

        // Copy everything except eval.yaml verbatim (meta.yaml, fixtures/, …).
        foreach (var file in Directory.EnumerateFiles(srcUnitDir))
            if (!Path.GetFileName(file).Equals("eval.yaml", StringComparison.OrdinalIgnoreCase))
                File.Copy(file, Path.Combine(stagedUnit, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(srcUnitDir))
            CopyDir(dir, Path.Combine(stagedUnit, Path.GetFileName(dir)));

        var filtered = FilterEvalScenarios(File.ReadAllLines(evalPath), o.Scenarios!, out kept);
        if (kept == 0)
        {
            Console.Error.WriteLine($"ablate: --scenarios matched no scenarios in {evalPath}.");
            try { Directory.Delete(stagedRoot, recursive: true); } catch { }
            return null;
        }
        File.WriteAllText(Path.Combine(stagedUnit, "eval.yaml"), filtered);
        return stagedRoot;
    }

    // Line-based scenario filter. Scenarios are top-level list items (`  - name: ...`) under a
    // `scenarios:` key; a block runs from its `- name:` line to the next `- name:` at the same
    // indent (or EOF). Header lines before the first scenario are preserved verbatim.
    private static string FilterEvalScenarios(string[] lines, List<string> keepTokens, out int kept)
    {
        kept = 0;
        var header = new List<string>();
        var blocks = new List<(string name, List<string> body)>();
        List<string>? current = null;
        string? currentName = null;
        int itemIndent = -1;

        static bool IsItem(string line, out string name, out int indent)
        {
            name = ""; indent = 0;
            var trimmed = line.TrimStart();
            indent = line.Length - trimmed.Length;
            if (!trimmed.StartsWith("- ")) return false;
            var afterDash = trimmed.Substring(2).TrimStart();
            if (!afterDash.StartsWith("name:")) return false;
            name = afterDash.Substring("name:".Length).Trim().Trim('"', '\'');
            return true;
        }

        foreach (var line in lines)
        {
            if (IsItem(line, out var nm, out var ind) && (itemIndent < 0 || ind == itemIndent))
            {
                itemIndent = ind;
                if (current is not null) blocks.Add((currentName!, current));
                current = new List<string> { line };
                currentName = nm;
            }
            else if (current is null) header.Add(line);
            else current.Add(line);
        }
        if (current is not null) blocks.Add((currentName!, current));

        var sb = new System.Text.StringBuilder();
        foreach (var h in header) sb.AppendLine(h);
        foreach (var (name, body) in blocks)
        {
            if (!keepTokens.Any(tok => name.StartsWith(tok, StringComparison.OrdinalIgnoreCase)
                                    || name.Contains(tok, StringComparison.OrdinalIgnoreCase)))
                continue;
            kept++;
            foreach (var b in body) sb.AppendLine(b);
        }
        return sb.ToString();
    }

    private static string? LatestSessionsDb(string unit, string tag)
    {
        var dir = DataCache.ResultsDir(unit, tag);
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateDirectories(dir)
            .Select(d => Path.Combine(d, "sessions.db"))
            .Where(File.Exists)
            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
            .FirstOrDefault();
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    private static string Short(string scenarioName)
    {
        var i = scenarioName.IndexOf(':');
        return i > 0 ? scenarioName.Substring(0, i) : scenarioName;
    }

    private static string ShortModel(string m) =>
        m.Contains("haiku") ? "haiku" : m.Contains("opus") ? "opus" : m.Contains("sonnet") ? "sonnet" : m;
}
