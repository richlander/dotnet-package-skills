using Grounding.Run;

namespace Grounding.Codegen;

// Implements the sync-skill and gen-plugins commands.
internal static class Codegen
{
    // Validate every grounding/<unit>/AGENTS.md is within the body line budget.
    // SKILL.md is NOT generated: it is an optional, maintainer-authored Textbook that the
    // eval consumes only when present (grounding run --source skill). We never write it.
    public static int CheckAgents()
    {
        var root = RepoRoot.Find();
        if (root is null) { Console.Error.WriteLine("grounding: cannot locate repo root."); return 1; }

        var limit = ReadLineLimit(root);
        var status = 0;

        foreach (var agents in Directory
                     .EnumerateFiles(Path.Combine(root, "grounding"), "AGENTS.md", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var dir = Path.GetDirectoryName(agents)!;
            var metaPath = Path.Combine(dir, "meta.yaml");
            if (!File.Exists(metaPath))
            {
                Console.WriteLine($"WARN: {Rel(root, dir)} has AGENTS.md but no meta.yaml; skipping");
                continue;
            }

            var doc = SkillDoc.ParseAgents(agents, metaPath);
            if (doc.BodyLineCount > limit)
            {
                Console.WriteLine($"TOO LONG: {Rel(root, agents)} body has {doc.BodyLineCount} lines "
                    + $"(limit {limit}). Trim it or raise eng/agents-line-limit.txt.");
                status = 1;
            }
        }
        if (status == 0) Console.WriteLine("AGENTS.md line budget OK.");
        return status;
    }

    // Expand every grounding/**/plugin.json.in (__REPO_ROOT__) into plugin.json.
    public static int GenPlugins()
    {
        var root = RepoRoot.Find();
        if (root is null) { Console.Error.WriteLine("grounding: cannot locate repo root."); return 1; }

        var count = 0;
        foreach (var template in Directory
                     .EnumerateFiles(Path.Combine(root, "grounding"), "plugin.json.in", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var outPath = template[..^3]; // strip ".in"
            var text = File.ReadAllText(template).Replace("__REPO_ROOT__", root);
            File.WriteAllText(outPath, text);
            Console.WriteLine($"generated {Rel(root, outPath)}");
            count++;
        }
        Console.WriteLine($"done: {count} plugin.json file(s) generated under {root}");
        return 0;
    }

    private static int ReadLineLimit(string root)
    {
        var p = Path.Combine(root, "eng", "agents-line-limit.txt");
        if (File.Exists(p) && int.TryParse(File.ReadAllText(p).Trim(), out var n)) return n;
        return 60;
    }

    private static string Rel(string root, string path) =>
        Path.GetRelativePath(root, path);
}
