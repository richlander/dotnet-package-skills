using System.Text;
using System.Text.RegularExpressions;

namespace Grounding.Bundle;

internal sealed record Scenario(string Id, string Title, List<string> Apis, List<string> Anchors);

// Generates TASKS.md from eval.yaml and exports a self-contained grounding bundle
// into a target repo. Minimal eval.yaml reader (no YAML dep, AOT-safe).
internal static partial class Bundle
{
    [GeneratedRegex(@"^\s*-\s*name:\s*""(?<name>.*)""")] private static partial Regex NameRe();
    [GeneratedRegex(@"^\s*value:\s*""(?<v>.*)""")] private static partial Regex ValueRe();
    [GeneratedRegex(@"^\s*expected_std_output_matches:\s*""(?<v>.*)""")] private static partial Regex AnchorRe();

    public static List<Scenario> Parse(string evalYaml)
    {
        var list = new List<Scenario>();
        Scenario? cur = null;
        foreach (var line in File.ReadAllText(evalYaml).Replace("\r\n", "\n").Split('\n'))
        {
            var nm = NameRe().Match(line);
            if (nm.Success)
            {
                var raw = nm.Groups["name"].Value;
                var i = raw.IndexOf(':');
                var id = i > 0 ? raw[..i].Trim() : raw.Trim();
                var title = i > 0 ? raw[(i + 1)..].Trim() : raw.Trim();
                cur = new Scenario(id, title, new(), new());
                list.Add(cur);
                continue;
            }
            if (cur is null) continue;
            var v = ValueRe().Match(line);
            if (v.Success) cur.Apis.Add(v.Groups["v"].Value);
            var a = AnchorRe().Match(line);
            if (a.Success) cur.Anchors.Add(Clean(a.Groups["v"].Value));
        }
        return list;
    }

    private static string Clean(string anchor) => anchor
        .Replace("\\\\", "\\").Replace(")(?=[\\s\\S]*", " / ").Replace("(?=[\\s\\S]*", "")
        .Replace("\\d{6,}", "<bytes>").Replace("\\d+", "<n>").Replace("\\.", ".")
        .Replace(")", "").Replace("\\", "").Trim();

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string RenderTasks(string unit, List<Scenario> s)
    {
        var sb = new StringBuilder();
        sb.Append($"# {unit} — tasks the grounding is evaluated on\n\n");
        sb.Append("Real jobs a developer asks an AI to do with this package. Each is gated by a\n");
        sb.Append("build + run with a deterministic anchor, so the grounding (AGENTS.md) is proven\n");
        sb.Append("to move an agent from \"fails / hand-rolls\" to \"uses the API correctly, first try.\"\n");
        sb.Append("Machine form + fixtures: `eval.yaml`. Regenerate results with `run.sh` — datasets\n");
        sb.Append("land in the grounding cache (`$GROUNDING_DATA_DIR`, not the repo); the distilled\n");
        sb.Append("quality card lives in the PR.\n\n");
        sb.Append("| # | Task | Key API | Anchor |\n| --- | --- | --- | --- |\n");
        for (var i = 0; i < s.Count; i++)
        {
            var api = string.Join(", ", s[i].Apis.Select(a => $"`{a}`"));
            var anc = string.Join(" / ", s[i].Anchors.Select(a => $"`{a}`"));
            sb.Append($"| {i + 1} | {Cap(s[i].Title)} | {Esc(api)} | {Esc(anc)} |\n");
        }
        return sb.ToString();
    }

    private static string Esc(string cell) => cell.Replace("|", "\\|");

    public static int Tasks(string unit, string? outPath)
    {
        var root = RepoRoot.Find(); if (root is null) { Console.Error.WriteLine("no repo root"); return 1; }
        var ey = Path.Combine(root, "tests", unit, "eval.yaml");
        if (!File.Exists(ey)) { Console.Error.WriteLine($"missing {ey}"); return 1; }
        var md = RenderTasks(unit, Parse(ey));
        if (outPath is null) Console.Write(md);
        else { File.WriteAllText(outPath, md); Console.WriteLine($"wrote {outPath}"); }
        return 0;
    }

    public static int Export(string unit, string to)
    {
        var root = RepoRoot.Find(); if (root is null) { Console.Error.WriteLine("no repo root"); return 1; }
        Directory.CreateDirectory(to);
        var gdir = Path.Combine(root, "grounding", unit);
        File.Copy(Path.Combine(gdir, "AGENTS.md"), Path.Combine(to, "AGENTS.md"), true);
        File.Copy(Path.Combine(root, "tests", unit, "eval.yaml"), Path.Combine(to, "eval.yaml"), true);
        CopyDir(Path.Combine(root, "tests", unit, "fixtures"), Path.Combine(to, "fixtures"));
        File.WriteAllText(Path.Combine(to, "TASKS.md"), RenderTasks(unit, Parse(Path.Combine(to, "eval.yaml"))));
        // Datasets are regenerable outputs and are NOT bundled: `run.sh` writes them to
        // the user cache ($GROUNDING_DATA_DIR); the distilled card lives in the PR.
        Console.WriteLine($"exported {unit} bundle -> {to}");
        return 0;
    }

    private static void CopyDir(string src, string dst)
    {
        if (!Directory.Exists(src)) return;
        foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(d.Replace(src, dst));
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(f, f.Replace(src, dst), true);
    }
}
