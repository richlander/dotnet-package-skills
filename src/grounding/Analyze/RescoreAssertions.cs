using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Grounding.Analyze;

// rescore-assertions — the judgment-side half of cheap re-runs. Agent generation is the expensive
// part and is done once; assertions are a JUDGMENT over the kept trajectory, so a changed assertion
// (e.g. tightening file_contains "ShowWhen" -> "ShowWhenProperty") is re-scored over the recorded
// events WITHOUT re-running the agent.
//
// For each (scenario, arm) it reconstructs the final workspace files (seed the scenario's fixtures,
// then replay the agent's create/write/edit tool calls from metrics.events) and re-evaluates the
// current eval.yaml assertions that are judgeable from the trajectory:
//   type 2  file_contains  -> substring over the reconstructed file (matched by basename)
//   type 11 reject_tools   -> pass iff the rejected tool never appears in the events
//   type 9  run_command    -> KEPT as recorded (build/output need a deterministic rebuild; TODO)
// It aligns eval.yaml assertions to the dataset's recorded assertionResults by index, recomputes the
// functional pass, reports every flip, and (with --write) updates the dataset in place.
internal sealed partial class RescoreAssertions
{
    private readonly TextWriter _o;
    private string? _pendingSeedPath;
    public RescoreAssertions(TextWriter? o = null) => _o = o ?? Console.Out;

    // ---- eval.yaml model ---------------------------------------------------
    private sealed record Seed(string TargetBasename, string Source);         // Program.cs <- fixtures/x/Program.cs
    private sealed record Asrt(int Type, string? Value, string? Path, string? Command); // parsed from eval.yaml
    private sealed record Scen(string Id, List<Seed> Seeds, List<Asrt> Asserts, List<string> Reject);

    public int Run(IReadOnlyList<string> datasets, string? unitOpt, string? testsDirOpt, string? rootOpt, bool write, bool all)
    {
        var root = rootOpt ?? RepoRoot.Find();
        if (root is null) { _o.WriteLine("rescore-assertions: no repo root."); return 1; }
        int rc = 0;
        foreach (var ds in datasets)
        {
            var node = JsonNode.Parse(File.ReadAllText(ds))!;
            var unit = unitOpt ?? node["verdicts"]?[0]?["skillName"]?.GetValue<string>() ?? "";
            var testsDir = testsDirOpt ?? (File.Exists(System.IO.Path.Combine(root, "grounding", unit, "eval.yaml")) ? "grounding" : "tests");
            var evalPath = System.IO.Path.Combine(root, testsDir, unit, "eval.yaml");
            var fixturesRoot = System.IO.Path.Combine(root, testsDir, unit);
            if (!File.Exists(evalPath)) { _o.WriteLine($"rescore-assertions: eval.yaml not found: {evalPath}"); rc |= 1; continue; }
            var scens = ParseEval(evalPath).ToDictionary(s => s.Id, s => s);
            rc |= Rescore(ds, node, scens, fixturesRoot, write, all);
        }
        return rc;
    }

    private int Rescore(string ds, JsonNode node, Dictionary<string, Scen> scens, string fixturesRoot, bool write, bool all)
    {
        _o.WriteLine($"### rescore-assertions — `{System.IO.Path.GetFileName(ds)}`\n");
        var changedFlips = new List<string>();
        var driftFlips = new List<string>();
        int reScored = 0, kept = 0, funcChanged = 0;
        int changedWritten = 0;
        var arms = new[] { "baseline", "skilledIsolated", "skilledPlugin" };
        foreach (var sc in node["verdicts"]?[0]?["scenarios"]?.AsArray() ?? new JsonArray())
        {
            var name = sc?["scenarioName"]?.GetValue<string>() ?? "";
            var id = name.Contains(':') ? name[..name.IndexOf(':')].Trim() : name.Trim();
            if (!scens.TryGetValue(id, out var scen)) continue;

            foreach (var armKey in arms)
            {
                var arm = sc?[armKey];
                var results = arm?["metrics"]?["assertionResults"]?.AsArray();
                if (results is null) continue;
                var events = arm?["metrics"]?["events"]?.AsArray();
                var files = Reconstruct(scen, events, fixturesRoot);
                var usedTools = UsedTools(events);

                // Align eval.yaml assertions to recorded results by index.
                for (int i = 0; i < results.Count && i < scen.Asserts.Count; i++)
                {
                    var spec = scen.Asserts[i];
                    if (spec.Type is not (2 or 11)) { kept++; continue; }   // run_command: keep (needs rebuild)
                    var recorded = results[i]?["passed"]?.GetValue<bool>() ?? false;
                    var recAssert = results[i]?["assertion"];
                    var recordedVal = recAssert?["value"]?.GetValue<string>();
                    var recordedPath = recAssert?["path"]?.GetValue<string>();
                    var recordedType = recAssert?["type"]?.GetValue<int>();
                    // Changed = full assertion identity differs (type/value/path), not just value — a
                    // path or type change is a real change and must be re-scored too.
                    var changed = recordedType != spec.Type || recordedVal != spec.Value || recordedPath != spec.Path;

                    // Default: only re-score assertions whose definition CHANGED vs what was recorded
                    // (the tightening case) — this is safe against reconstruction drift on unchanged
                    // assertions. `--all` re-scores everything to audit reconstruction faithfulness.
                    if (!all && !changed) { kept++; continue; }

                    var rescored = spec.Type == 2
                        ? spec.Value is { } v && spec.Path is { } p
                            && files.TryGetValue(System.IO.Path.GetFileName(p), out var content) && content.Contains(v)
                        : spec.Value is { } tool && !usedTools.Contains(tool);
                    reScored++;
                    // On a CHANGED assertion, --write must update BOTH the verdict AND the recorded
                    // assertion definition, or the dataset is left internally inconsistent (old value,
                    // new pass/fail) and a later run would re-detect it as "changed" and re-flip.
                    if (write && changed && recAssert is JsonObject ra)
                    {
                        ra["type"] = spec.Type;
                        if (spec.Value is not null) ra["value"] = spec.Value; else ra.Remove("value");
                        // Always synchronize path to the spec identity (not just for type 2), so a
                        // type/path transition (e.g. file_contains -> reject_tools) stays idempotent.
                        if (spec.Path is not null) ra["path"] = spec.Path; else ra.Remove("path");
                        results[i]!["passed"] = rescored;
                        changedWritten++;
                    }
                    if (rescored != recorded)
                    {
                        var line = $"{id}/{Short(armKey)} [{Kind(spec.Type)} {recordedVal}→{spec.Value}] {recorded}→{rescored}";
                        if (changed) changedFlips.Add(line);
                        else driftFlips.Add($"{id}/{Short(armKey)} [{Kind(spec.Type)} {spec.Value}] {recorded}→{rescored}");
                    }
                }
            }
        }
        _o.WriteLine($"- re-scored {reScored} assertion(s) (file_contains + reject_tools); kept {kept} unchanged/run_command as recorded.");
        _o.WriteLine($"- **{changedFlips.Count} verdict change(s)** from changed assertions:{(changedFlips.Count == 0 ? " none." : "")}");
        foreach (var f in changedFlips) _o.WriteLine($"  - {f}");
        if (all)
        {
            _o.WriteLine($"- reconstruction audit: {driftFlips.Count} drift flip(s) on UNCHANGED assertions "
                + "(reconstruction imperfection — multi-edit replay / bash-written files; not applied):");
            foreach (var f in driftFlips) _o.WriteLine($"  - {f}");
        }
        if (write && changedWritten > 0)
        {
            File.WriteAllText(ds, node.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            _o.WriteLine($"\n_updated {changedWritten} changed assertion(s) ({changedFlips.Count} verdict flip(s)) -> {System.IO.Path.GetFileName(ds)} (run `analyze` to re-derive cards)_");
        }
        _o.WriteLine();
        return 0;
    }

    // ---- workspace reconstruction -----------------------------------------
    // Seed the scenario's setup files from their fixtures, then replay the agent's file-mutating tool
    // calls in order. Files are keyed by basename (the event paths are temp workdirs; assertions use
    // the relative name). create/write set content; edit/str_replace do a first-occurrence replace.
    private static Dictionary<string, string> Reconstruct(Scen scen, JsonArray? events, string fixturesRoot)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var seed in scen.Seeds)
        {
            var src = System.IO.Path.Combine(fixturesRoot, seed.Source);
            files[seed.TargetBasename] = File.Exists(src) ? File.ReadAllText(src) : "";
        }
        foreach (var e in events ?? new JsonArray())
        {
            if (e?["type"]?.GetValue<string>() != "tool.execution_start") continue;
            var data = e["data"];
            var tool = data?["toolName"]?.GetValue<string>() ?? "";
            var a = ParseArgs(data?["arguments"]);
            var path = a.GetValueOrDefault("path") ?? a.GetValueOrDefault("file_path") ?? a.GetValueOrDefault("filename");
            if (path is null) continue;
            var key = System.IO.Path.GetFileName(path);
            switch (tool)
            {
                case "create" or "write":
                    var content = a.GetValueOrDefault("content") ?? a.GetValueOrDefault("file_text") ?? a.GetValueOrDefault("text");
                    if (content is not null) files[key] = content;
                    break;
                case "edit" or "str_replace" or "str_replace_editor":
                    var oldS = a.GetValueOrDefault("old_str"); var newS = a.GetValueOrDefault("new_str") ?? "";
                    if (oldS is { } os && files.TryGetValue(key, out var cur))
                    {
                        var idx = cur.IndexOf(os, StringComparison.Ordinal);
                        if (idx >= 0) files[key] = cur[..idx] + newS + cur[(idx + os.Length)..];
                    }
                    if (oldS is null && a.GetValueOrDefault("content") is { } cc) files[key] = cc;
                    break;
            }
        }
        return files;
    }

    private static HashSet<string> UsedTools(JsonArray? events)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in events ?? new JsonArray())
            if (e?["type"]?.GetValue<string>() == "tool.execution_start")
                set.Add(e["data"]?["toolName"]?.GetValue<string>() ?? "");
        return set;
    }

    private static Dictionary<string, string> ParseArgs(JsonNode? args)
    {
        var outp = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            JsonNode? obj = args switch
            {
                JsonObject o => o,
                JsonValue v when v.TryGetValue<string>(out var s) && s.TrimStart().StartsWith('{') => JsonNode.Parse(s),
                _ => null,
            };
            if (obj is JsonObject jo)
                foreach (var kv in jo)
                    if (kv.Value is JsonValue jv && jv.TryGetValue<string>(out var sv)) outp[kv.Key] = sv;
        }
        catch (JsonException) { }
        return outp;
    }

    // ---- minimal eval.yaml parser (setup.files + assertions + reject_tools) -
    [GeneratedRegex(@"^\s*-\s+name:\s*""?(?<name>[^""]+)""?")]
    private static partial Regex NameRe();
    [GeneratedRegex(@"path:\s*""?(?<path>[^"",}]+?)""?\s*,\s*source:\s*""?(?<src>[^"",}]+?)""?\s*}")]
    private static partial Regex InlineFileRe();
    [GeneratedRegex(@"type:\s*(?<type>[a-z_]+)")]
    private static partial Regex TypeRe();
    [GeneratedRegex(@"value:\s*""?(?<v>[^""}]*?)""?\s*[}\n]?$")]
    private static partial Regex ValueRe();
    [GeneratedRegex(@"path:\s*""?(?<p>[^"",}\n]+)")]
    private static partial Regex PathRe();

    private static int TypeCode(string t) => t switch
    {
        "file_contains" => 2,
        "run_command_and_assert" => 9,
        _ => 0,
    };

    private List<Scen> ParseEval(string path)
    {
        var scens = new List<Scen>();
        Scen? cur = null;
        string section = "";
        _pendingSeedPath = null;
        var lines = File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var nm = NameRe().Match(line);
            if (nm.Success)
            {
                var raw = nm.Groups["name"].Value;
                var id = raw.Contains(':') ? raw[..raw.IndexOf(':')].Trim() : raw.Trim();
                cur = new Scen(id, new(), new(), new());
                scens.Add(cur);
                section = "";
                continue;
            }
            if (cur is null) continue;
            var trimmed = line.TrimStart();
            var indent = line.Length - trimmed.Length;

            if (trimmed.StartsWith("setup:")) { section = "setup"; continue; }
            if (trimmed.StartsWith("assertions:")) { section = "assertions"; continue; }
            if (trimmed.StartsWith("reject_tools:"))
            {
                section = "reject";
                var inl = Regex.Match(trimmed, @"\[(?<items>[^\]]+)\]");
                if (inl.Success)
                    foreach (var it in inl.Groups["items"].Value.Split(',')) cur.Reject.Add(it.Trim());
                continue;
            }
            if (trimmed.StartsWith("rubric:") || trimmed.StartsWith("timeout:") || trimmed.StartsWith("prompt:")) { section = ""; continue; }

            if (section == "setup")
            {
                // Inline form: `- { path: X, source: Y }`. Block form: `- path: X` then `source: Y`.
                var mf = InlineFileRe().Match(line);
                if (mf.Success) { cur.Seeds.Add(new Seed(System.IO.Path.GetFileName(mf.Groups["path"].Value), mf.Groups["src"].Value)); continue; }
                var pOnly = Regex.Match(line, @"path:\s*""?(?<p>[^"",}\n]+?)""?\s*$");
                if (pOnly.Success) { _pendingSeedPath = pOnly.Groups["p"].Value.Trim(); continue; }
                var sOnly = Regex.Match(line, @"source:\s*""?(?<s>[^"",}\n]+?)""?\s*$");
                if (sOnly.Success && _pendingSeedPath is { } pp)
                {
                    cur.Seeds.Add(new Seed(System.IO.Path.GetFileName(pp), sOnly.Groups["s"].Value.Trim()));
                    _pendingSeedPath = null;
                }
            }
            else if (section == "reject")
            {
                if (trimmed.StartsWith("- ")) cur.Reject.Add(trimmed[2..].Trim());
            }
            else if (section == "assertions")
            {
                // An assertion begins at a "- " item; it may be inline {..} or a block spanning lines.
                if (!trimmed.StartsWith("-")) continue;
                // Gather this assertion's text: the "- ..." line plus following deeper-indented lines.
                var buf = trimmed.TrimStart('-').Trim();
                int j = i + 1;
                while (j < lines.Length)
                {
                    var nxt = lines[j];
                    var nt = nxt.TrimStart();
                    var nind = nxt.Length - nt.Length;
                    if (nt.StartsWith("- ") || nind <= indent || nt.StartsWith("assertions:") || NameRe().IsMatch(nxt)
                        || nt.StartsWith("rubric:") || nt.StartsWith("timeout:")) break;
                    buf += "\n" + nt;
                    j++;
                }
                i = j - 1;
                var tm = TypeRe().Match(buf);
                if (!tm.Success) continue;
                var type = TypeCode(tm.Groups["type"].Value);
                string? val = ValueRe().Match(buf) is { Success: true } vm ? vm.Groups["v"].Value.Trim() : null;
                string? p = PathRe().Match(buf) is { Success: true } pm ? pm.Groups["p"].Value.Trim() : null;
                string? cmd = Regex.Match(buf, @"command_arguments:\s*""?(?<c>[^""\n]+)") is { Success: true } cm ? cm.Groups["c"].Value : null;
                cur.Asserts.Add(new Asrt(type, val, p, cmd));
            }
        }
        // reject_tools become type-11 assertions appended in the recorded order (after functionals).
        foreach (var s in scens)
            foreach (var r in s.Reject)
                s.Asserts.Add(new Asrt(11, r, null, null));
        return scens;
    }

    private static string Short(string arm) => arm switch
    {
        "baseline" => "base", "skilledIsolated" => "iso", "skilledPlugin" => "plug", _ => arm,
    };
    private static string Kind(int t) => t switch { 2 => "file_contains", 11 => "reject", 9 => "run", _ => "?" };
}
