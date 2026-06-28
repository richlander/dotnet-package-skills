using System.Diagnostics;

namespace Grounding.Run;

internal sealed class RunOptions
{
    public required string Unit;
    public required string Source;        // agents | readme | none
    public required List<string> Models;
    public int Runs = 1;
    public string JudgeModel = "claude-haiku-4.5";
    public bool NoJudge;
    public string TestsDir = "tests";
    public string? OutDir;
    public string? ReadmeFile;
    public bool DryRun;
    public string? EmitSkill;             // write generated SKILL.md here and exit
}

internal static class Runner
{
    public static int Run(RunOptions o)
    {
        var root = RepoRoot.Find();
        if (root is null)
        {
            Console.Error.WriteLine("grounding: cannot locate repo root (need grounding/ and eng/).");
            return 1;
        }

        var unitDir = Path.Combine(root, "grounding", o.Unit);
        if (!Directory.Exists(unitDir))
        {
            Console.Error.WriteLine($"grounding: unit not found: grounding/{o.Unit}");
            return 1;
        }
        var agentsPath = Path.Combine(unitDir, "AGENTS.md");
        var metaPath = Path.Combine(unitDir, "meta.yaml");
        if (!File.Exists(agentsPath))
        {
            Console.Error.WriteLine($"grounding: grounding/{o.Unit}/AGENTS.md missing.");
            return 1;
        }

        var doc = SkillDoc.ParseAgents(agentsPath, metaPath);
        string skillText;
        switch (o.Source)
        {
            case "agents":
                skillText = doc.Render(doc.Body);
                break;
            case "readme":
                var readmePath = o.ReadmeFile ?? Path.Combine(unitDir, "README.md");
                if (!File.Exists(readmePath))
                {
                    Console.Error.WriteLine(
                        $"grounding: --source readme needs a README. Pass --readme-file or add grounding/{o.Unit}/README.md.");
                    return 1;
                }
                skillText = doc.Render(NormalizeBody(File.ReadAllText(readmePath)));
                break;
            case "none":
                skillText = doc.Render("(no package grounding supplied.)\n");
                break;
            default:
                Console.Error.WriteLine($"grounding: unknown --source '{o.Source}'.");
                return 1;
        }

        if (o.EmitSkill is not null)
        {
            File.WriteAllText(o.EmitSkill, skillText);
            Console.WriteLine($"wrote generated SKILL.md ({o.Source}) -> {o.EmitSkill}");
            return 0;
        }

        var outDir = o.OutDir ?? Path.Combine(root, "data", $"{o.Unit}-6q");
        var bin = FindSkillValidator(root);
        var skillPath = Path.Combine(unitDir, "SKILL.md");
        var groundingArg = Path.Combine("grounding", o.Unit);

        var rc = 0;
        foreach (var model in o.Models)
        {
            var ms = ShortModel(model);
            var tag = o.Source switch
            {
                "readme" => $"{o.Unit}-readme.{ms}",
                "none" => $"{o.Unit}-none.{ms}",
                _ => $"{o.Unit}.{ms}",
            };
            var resultsDir = Path.Combine(root, ".skill-validator-results", $"{o.Unit}-{tag}");
            var cmd = BuildCommand(bin ?? "<skill-validator>", o, model, resultsDir, groundingArg);

            Console.WriteLine($"#### {o.Unit}  source={o.Source}  model={ms}  runs={o.Runs} ####");
            Console.WriteLine($"    SKILL.md <- {o.Source}   dataset -> {Path.Combine(outDir, tag + ".json")}");
            Console.WriteLine("    " + cmd);

            if (o.DryRun)
                continue;

            if (bin is null)
            {
                Console.Error.WriteLine(
                    "grounding: skill-validator not found under .tools/skill-validator-*/. Build the harness first.");
                return 1;
            }

            Directory.CreateDirectory(outDir);
            rc |= RunOne(root, skillPath, skillText, bin, o, model, resultsDir, groundingArg, outDir, tag);
        }
        return rc;
    }

    // Reversibly swap SKILL.md to the chosen source, invoke skill-validator,
    // copy results.json into the dataset, then always restore the original SKILL.md.
    private static int RunOne(string root, string skillPath, string skillText, string bin,
        RunOptions o, string model, string resultsDir, string groundingArg, string outDir, string tag)
    {
        var backup = File.Exists(skillPath) ? File.ReadAllText(skillPath) : null;
        try
        {
            if (Directory.Exists(resultsDir)) Directory.Delete(resultsDir, true);
            File.WriteAllText(skillPath, skillText);

            var psi = new ProcessStartInfo(bin) { WorkingDirectory = root, UseShellExecute = false };
            psi.ArgumentList.Add("evaluate");
            psi.ArgumentList.Add("--tests-dir"); psi.ArgumentList.Add(o.TestsDir);
            psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(model);
            if (o.NoJudge) psi.ArgumentList.Add("--no-judge");
            else { psi.ArgumentList.Add("--judge-model"); psi.ArgumentList.Add(o.JudgeModel); }
            psi.ArgumentList.Add("--runs"); psi.ArgumentList.Add(o.Runs.ToString());
            psi.ArgumentList.Add("--keep-sessions");
            psi.ArgumentList.Add("--results-dir"); psi.ArgumentList.Add(resultsDir);
            psi.ArgumentList.Add(groundingArg);

            using var p = Process.Start(psi)!;
            p.WaitForExit();

            var rj = Directory.Exists(resultsDir)
                ? Directory.EnumerateFiles(resultsDir, "results.json", SearchOption.AllDirectories).FirstOrDefault()
                : null;
            if (rj is null)
            {
                Console.Error.WriteLine($"   !! no results.json for {tag} (skill-validator exit {p.ExitCode}).");
                return 1;
            }
            var dest = Path.Combine(outDir, tag + ".json");
            File.Copy(rj, dest, overwrite: true);
            Console.WriteLine($"   -> {dest}");
            new Analyze.Cards().Table(new[] { dest });
            return 0;
        }
        finally
        {
            if (backup is not null) File.WriteAllText(skillPath, backup);
        }
    }

    private static string BuildCommand(string bin, RunOptions o, string model, string resultsDir, string groundingArg)
    {
        var judge = o.NoJudge ? "--no-judge" : $"--judge-model {o.JudgeModel}";
        return $"{bin} evaluate --tests-dir {o.TestsDir} --model {model} {judge} " +
               $"--runs {o.Runs} --keep-sessions --results-dir {resultsDir} {groundingArg}";
    }

    private static string? FindSkillValidator(string root)
    {
        var tools = Path.Combine(root, ".tools");
        if (!Directory.Exists(tools)) return null;
        return Directory.EnumerateDirectories(tools, "skill-validator-*")
            .Select(d => Path.Combine(d, "skill-validator"))
            .FirstOrDefault(File.Exists);
    }

    private static string ShortModel(string m) =>
        m.Contains("opus") ? "opus" :
        m.Contains("haiku") ? "haiku" :
        m.Contains("sonnet") ? "sonnet" : m;

    // README markdown often starts with its own frontmatter / leading blanks; keep it
    // as-is but guarantee a trailing newline so the SKILL.md body is well-formed.
    private static string NormalizeBody(string text)
    {
        text = text.Replace("\r\n", "\n");
        return text.EndsWith('\n') ? text : text + "\n";
    }
}
