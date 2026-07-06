using System.Diagnostics;

namespace Grounding.Run;

internal sealed class RunOptions
{
    public required string Unit;
    public required string Source;        // agents | readme | none
    public string Delivery = "pull";      // pull = model-invoked skill (SKILL.md); push = always-on agent (.agent.md)
    public required List<string> Models;
    public int Runs = 1;
    public string JudgeModel = "claude-haiku-4.5";
    public bool NoJudge;
    public string? TestsDir;              // null => auto (co-located bundle "grounding", else "tests")
    public string? OutDir;
    public string? ReadmeFile;
    public bool DryRun;
    public string? EmitSkill;             // write generated SKILL.md here and exit
    public string? Root;                  // grounding root (a target repo); default = infra root
    // Shared-baseline flow (push-vs-pull methodology): pin ONE ungrounded baseline and reuse it
    // across delivery/source arms so the comparison isn't confounded by per-run baseline variance.
    // A `{model}` token is substituted with the short model name (for multi-model invocations).
    public string? BaselineOut;           // --baseline-out: run + persist the baseline here
    public string? BaselineFrom;          // --baseline-from: reuse a persisted baseline (skip the baseline arm)
}

internal static class Runner
{
    public static int Run(RunOptions o)
    {
        // The INFRA root holds the harness (skill-validator under .tools/). The GROUNDING
        // root holds grounding/<unit> — it may be a target package repo (--root / GROUNDING_ROOT),
        // so we eval that repo's AGENTS.md in place with no packing or publishing.
        var infraRoot = RepoRoot.Find();
        if (infraRoot is null)
        {
            Console.Error.WriteLine("grounding: cannot locate infra root (need grounding/ and eng/ with .tools/).");
            return 1;
        }
        var gRoot = o.Root ?? Environment.GetEnvironmentVariable("GROUNDING_ROOT");
        var root = string.IsNullOrWhiteSpace(gRoot) ? infraRoot : Path.GetFullPath(gRoot);

        var unitDir = Path.Combine(root, "grounding", o.Unit);
        if (!Directory.Exists(unitDir))
        {
            Console.Error.WriteLine($"grounding: unit not found: {Path.Combine(root, "grounding", o.Unit)}");
            return 1;
        }
        var agentsPath = Path.Combine(unitDir, "AGENTS.md");
        var metaPath = Path.Combine(unitDir, "meta.yaml");
        if (!File.Exists(agentsPath))
        {
            Console.Error.WriteLine($"grounding: {agentsPath} missing.");
            return 1;
        }

        // Resolve the tests dir. Explicit --tests-dir wins; otherwise auto-detect a
        // co-located bundle (grounding/<unit>/eval.yaml => tests-dir "grounding"),
        // else the classic split layout ("tests").
        if (o.TestsDir is null)
            o.TestsDir = File.Exists(Path.Combine(unitDir, "eval.yaml")) ? "grounding" : "tests";

        var doc = SkillDoc.ParseAgents(agentsPath, metaPath);
        string skillText;
        switch (o.Source)
        {
            case "agents":
                skillText = doc.Render(doc.Body);
                break;
            case "skill":
                // The authored Complete Textbook is a REAL Claude skill, not grounding: it lives
                // in a conventional skills/<unit>/SKILL.md (with a plugin.json), per Anthropic
                // guidance — NOT under grounding/. Feed it verbatim (it is already a full SKILL.md).
                var authoredSkill = Path.Combine(root, "skills", o.Unit, "SKILL.md");
                if (!File.Exists(authoredSkill))
                {
                    Console.Error.WriteLine(
                        $"grounding: --source skill needs skills/{o.Unit}/SKILL.md (an authored Textbook skill). "
                        + "SKILL.md is optional and maintainer-authored; add one under skills/ to eval the Textbook arm.");
                    return 1;
                }
                skillText = File.ReadAllText(authoredSkill);
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

        var outDir = o.OutDir ?? DataCache.DatasetDir(o.Unit);
        var bin = FindSkillValidator(infraRoot);
        var skillPath = Path.Combine(unitDir, "SKILL.md");
        var groundingArg = Path.Combine("grounding", o.Unit);

        // skill-validator requires a plugin.json above the skill dir. Infra ships one at
        // grounding/plugin.json; a target repo need not. If absent, synthesize a transient
        // one (cleaned up below) so target repos hold ONLY grounding inputs — no scaffolding.
        var pluginJson = Path.Combine(root, "grounding", "plugin.json");
        var wrotePlugin = false;
        if (!o.DryRun && !File.Exists(pluginJson))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pluginJson)!);
            File.WriteAllText(pluginJson,
                "{\n  \"name\": \"grounding\",\n  \"version\": \"0.0.0\",\n" +
                "  \"description\": \"Transient plugin manifest (grounding harness).\",\n" +
                "  \"skills\": [\"./\"]\n}\n");
            wrotePlugin = true;
        }

        var rc = 0;
        try
        {
            foreach (var model in o.Models)
            {
                var ms = ShortModel(model);
                // Delivery encodes into the tag so push/pull datasets sit side by side.
                var dv = o.Delivery == "push" ? "-push" : "";
                var tag = o.Source switch
                {
                    "skill" => $"{o.Unit}-skill{dv}.{ms}",
                    "readme" => $"{o.Unit}-readme{dv}.{ms}",
                    "none" => $"{o.Unit}-none{dv}.{ms}",
                    _ => $"{o.Unit}{dv}.{ms}",
                };
                var resultsDir = DataCache.ResultsDir(o.Unit, tag);
                var cmd = BuildCommand(bin ?? "<skill-validator>", o, model, resultsDir, groundingArg);

                Console.WriteLine($"#### {o.Unit}  source={o.Source}  delivery={o.Delivery}  model={ms}  runs={o.Runs} ####");
                var artifact = o.Delivery == "push" ? $"{o.Unit}.agent.md" : "SKILL.md";
                Console.WriteLine($"    {artifact} <- {o.Source}   dataset -> {Path.Combine(outDir, tag + ".json")}");
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
        }
        finally
        {
            if (wrotePlugin && File.Exists(pluginJson)) File.Delete(pluginJson);
        }
        return rc;
    }

    // Reversibly swap the grounding artifact to the chosen source, invoke skill-validator,
    // copy results.json into the dataset, then always restore/clean up. Delivery picks the
    // artifact: pull -> SKILL.md (model-invoked skill); push -> <unit>.agent.md (always-on
    // agent, selected as primary persona at t=0). skill-validator auto-discovers which.
    private static int RunOne(string root, string skillPath, string skillText, string bin,
        RunOptions o, string model, string resultsDir, string groundingArg, string outDir, string tag)
    {
        var unitDir = Path.GetDirectoryName(skillPath)!;
        var target = o.Delivery == "push" ? Path.Combine(unitDir, $"{o.Unit}.agent.md") : skillPath;
        var backup = File.Exists(target) ? File.ReadAllText(target) : null;
        try
        {
            if (Directory.Exists(resultsDir)) Directory.Delete(resultsDir, true);
            File.WriteAllText(target, skillText);

            var psi = new ProcessStartInfo(bin) { WorkingDirectory = root, UseShellExecute = false };
            psi.ArgumentList.Add("evaluate");
            psi.ArgumentList.Add("--tests-dir"); psi.ArgumentList.Add(o.TestsDir);
            psi.ArgumentList.Add("--model"); psi.ArgumentList.Add(model);
            if (o.NoJudge) psi.ArgumentList.Add("--no-judge");
            else { psi.ArgumentList.Add("--judge-model"); psi.ArgumentList.Add(o.JudgeModel); }
            psi.ArgumentList.Add("--runs"); psi.ArgumentList.Add(o.Runs.ToString());
            psi.ArgumentList.Add("--keep-sessions");
            psi.ArgumentList.Add("--results-dir"); psi.ArgumentList.Add(resultsDir);
            // Shared-baseline flow: persist (--baseline-out) or reuse (--baseline-from) one pinned
            // baseline so push and pull grounded arms compare against the SAME reference. The
            // `{model}` token keeps per-model baselines distinct in multi-model invocations.
            var ms = ShortModel(model);
            if (o.BaselineOut is { } bo)
            { psi.ArgumentList.Add("--baseline-out"); psi.ArgumentList.Add(bo.Replace("{model}", ms)); }
            if (o.BaselineFrom is { } bf)
            { psi.ArgumentList.Add("--baseline-from"); psi.ArgumentList.Add(bf.Replace("{model}", ms)); }
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
            // Inject correct per-run-averaged tool-call stats from sessions.db so runs>1 numbers
            // are consistent (skill-validator collapses breakdown=run[0] / events=run[last]).
            var sdb = Path.Combine(Path.GetDirectoryName(rj)!, "sessions.db");
            if (File.Exists(sdb))
                Analyze.Enrich.Run(dest, sdb);
            Console.WriteLine($"   -> {dest}");
            new Analyze.Cards().Table(new[] { dest });
            return 0;
        }
        finally
        {
            // Restore a pre-existing artifact; if we created a transient one (target-repo
            // bundle ships no SKILL.md/.agent.md), remove it so we never leave an artifact.
            if (backup is not null) File.WriteAllText(target, backup);
            else if (File.Exists(target)) File.Delete(target);
        }
    }

    private static string BuildCommand(string bin, RunOptions o, string model, string resultsDir, string groundingArg)
    {
        var judge = o.NoJudge ? "--no-judge" : $"--judge-model {o.JudgeModel}";
        var ms = ShortModel(model);
        var baseline = o.BaselineOut is { } bo ? $"--baseline-out {bo.Replace("{model}", ms)} "
                     : o.BaselineFrom is { } bf ? $"--baseline-from {bf.Replace("{model}", ms)} "
                     : "";
        return $"{bin} evaluate --tests-dir {o.TestsDir} --model {model} {judge} " +
               $"--runs {o.Runs} --keep-sessions --results-dir {resultsDir} {baseline}{groundingArg}";
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
