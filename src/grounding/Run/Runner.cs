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
    public bool Fresh;                    // --fresh: regenerate even when a provenance-matching dataset exists
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
        var hasAgents = File.Exists(agentsPath);
        // AGENTS.md is required only for --source agents (its body IS the grounding). SKILL.md-only
        // units ship no AGENTS.md; they carry name/description in meta.yaml. Every other arm
        // (skill/readme/none) only needs those two scalars for the SKILL wrapper.
        if (!hasAgents && o.Source == "agents")
        {
            Console.Error.WriteLine($"grounding: {agentsPath} missing (required for --source agents).");
            return 1;
        }
        if (!hasAgents && !File.Exists(metaPath))
        {
            Console.Error.WriteLine($"grounding: neither {agentsPath} nor {metaPath} present for unit '{o.Unit}'.");
            return 1;
        }

        // Resolve the tests dir. Explicit --tests-dir wins; otherwise auto-detect a
        // co-located bundle (grounding/<unit>/eval.yaml => tests-dir "grounding"),
        // else the classic split layout ("tests").
        if (o.TestsDir is null)
            o.TestsDir = File.Exists(Path.Combine(unitDir, "eval.yaml")) ? "grounding" : "tests";

        var doc = hasAgents ? SkillDoc.ParseAgents(agentsPath, metaPath) : SkillDoc.FromMeta(metaPath, o.Unit);
        string skillText;
        string? srcDocPath = null;   // the grounding doc feeding this arm (null for baseline) — its
                                     // content hash is the arm's provenance/pin key.
        switch (o.Source)
        {
            case "agents":
                skillText = doc.Render(doc.Body);
                srcDocPath = agentsPath;
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
                srcDocPath = authoredSkill;
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
                srcDocPath = readmePath;
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
        // For --source skill (pull), the graded artifact is the REAL authored skill under
        // skills/<unit>, and the whole skills/ plugin is loaded so the agent can pull ANY sibling
        // domain skill (matching production: a package can ship a base skill + domain workflow
        // skills). skill-validator resolves the eval by the skill's DIRECTORY NAME via
        // --tests-dir (grounding/<unit>/eval.yaml), independent of the skill's location, and
        // resolves setup fixture sources relative to that eval dir — so the eval + fixtures stay
        // under grounding/<unit> while the skills live under skills/. Other sources
        // (agents/readme/none, or push) drop the synthesized doc into grounding/<unit>.
        var multiSkill = o.Source == "skill" && o.Delivery != "push";
        var skillPath = multiSkill
            ? Path.Combine(root, "skills", o.Unit, "SKILL.md")
            : Path.Combine(unitDir, "SKILL.md");
        var groundingArg = multiSkill ? Path.Combine("skills", o.Unit) : Path.Combine("grounding", o.Unit);

        // Provenance = the pin key. Corpus (nuget + fixtures) + this arm's doc content-hash decide
        // whether an already-generated dataset can be REUSED instead of regenerated — the core of
        // cheap re-runs. Computed once here (model-independent) and stamped into each dataset.
        var fixturesDir = Path.Combine(root, o.TestsDir!, o.Unit, "fixtures");
        var prov = Provenance.Compute(root, o.Source, srcDocPath, fixturesDir);

        // skill-validator requires a plugin.json above the skill dir. The multi-skill arm loads the
        // REAL skills/plugin.json (skills:["."]) so every sibling skill is discoverable — it is a
        // committed package artifact, not scaffolding. Other arms synthesize a transient
        // grounding/plugin.json (cleaned up below) so target repos hold only grounding inputs.
        var pluginJson = multiSkill
            ? Path.Combine(root, "skills", "plugin.json")
            : Path.Combine(root, "grounding", "plugin.json");
        var wrotePlugin = false;
        if (multiSkill)
        {
            if (!File.Exists(pluginJson))
            {
                Console.Error.WriteLine(
                    $"grounding: --source skill needs a plugin manifest at {pluginJson} (e.g. {{\"skills\":[\".\"]}}) "
                    + "so all sibling domain skills load. Add one.");
                return 1;
            }
        }
        else if (!o.DryRun && !File.Exists(pluginJson))
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

                // Pin-reuse: if a dataset for this arm already exists with matching provenance
                // (same corpus + same doc content), reuse it instead of regenerating — this is what
                // makes re-running an unchanged arm free. `--fresh` forces regeneration. On a
                // mismatch we regenerate and say WHICH rule changed (symmetry with baseline validation).
                var destPath = Path.Combine(outDir, tag + ".json");
                var cached = o.Fresh ? null : Provenance.FromDataset(destPath);
                if (cached is not null && cached.ReusableAs(prov))
                {
                    Console.WriteLine($"    ↺ REUSED (provenance match: {prov.Source} doc {prov.DocContentHash ?? "—"}, "
                        + $"nuget {prov.NugetVersion ?? "?"}, fixtures {prov.FixtureHash ?? "?"}) — skipped generation.");
                    new Analyze.Cards().Table(new[] { destPath });
                    continue;
                }
                if (cached is not null)
                    Console.WriteLine($"    ⟳ regenerating {tag}: {string.Join("; ", cached.ViolationsAgainst(prov))}.");
                else if (o.Fresh && File.Exists(destPath))
                    Console.WriteLine($"    ⟳ regenerating {tag}: --fresh.");

                if (bin is null)
                {
                    Console.Error.WriteLine(
                        "grounding: skill-validator not found under .tools/skill-validator-*/. Build the harness first.");
                    return 1;
                }

                Directory.CreateDirectory(outDir);
                rc |= RunOne(root, skillPath, skillText, bin, o, model, resultsDir, groundingArg, outDir, tag, prov);
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
        RunOptions o, string model, string resultsDir, string groundingArg, string outDir, string tag, Provenance prov)
    {
        var unitDir = Path.GetDirectoryName(skillPath)!;
        var target = o.Delivery == "push" ? Path.Combine(unitDir, $"{o.Unit}.agent.md") : skillPath;
        // skill-validator resolves an AGENT's eval by the agent NAME (ResolveAgentEvalPath looks for
        // <tests-dir>/<name>/eval.yaml). The authored doc is named for the package (e.g. "markout"),
        // but the eval lives under grounding/<unit>/ (e.g. "markout-013"). For the push artifact,
        // rewrite the frontmatter name to the unit so the always-on agent's eval is discovered.
        // (Pull discovers by directory, so it needs no rewrite.)
        var artifactText = o.Delivery == "push" ? ForceFrontmatterName(skillText, o.Unit) : skillText;
        var backup = File.Exists(target) ? File.ReadAllText(target) : null;
        try
        {
            if (Directory.Exists(resultsDir)) Directory.Delete(resultsDir, true);
            File.WriteAllText(target, artifactText);

            var psi = new ProcessStartInfo(bin) { WorkingDirectory = root, UseShellExecute = false };
            psi.ArgumentList.Add("evaluate");
            psi.ArgumentList.Add("--tests-dir"); psi.ArgumentList.Add(o.TestsDir!);
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

            // Refuse a --baseline-from pin whose corpus (nuget + fixtures) does not match this run —
            // a baseline from a different world would make the comparison meaningless. Fail fast and
            // name the violated rule(s). A pre-provenance pin can't be checked, so it only warns.
            if (o.BaselineFrom is { } bfPin)
            {
                var pin = bfPin.Replace("{model}", ms);
                var violations = SharedBaseline.Validate(pin, prov);
                var hard = violations.Where(v => !v.StartsWith("unverified")).ToList();
                if (hard.Count > 0)
                {
                    Console.Error.WriteLine($"   !! invalid --baseline-from {Path.GetFileName(pin)}: refusing to reuse a baseline from a different corpus. Violated:");
                    foreach (var v in hard) Console.Error.WriteLine($"        - {v}");
                    return 1;
                }
                foreach (var v in violations) Console.Error.WriteLine($"   !! {v}; reusing anyway.");
            }

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
            // Shared-baseline consistency: persist the enriched baseline arms (--baseline-out) or
            // overwrite this run's reused baseline with the persisted ones (--baseline-from), so the
            // baseline's trajectory counts are identical across delivery arms, not recomputed from a
            // partial reused session set.
            var msb = ShortModel(model);
            if (o.BaselineOut is { } bo2) SharedBaseline.Save(dest, bo2.Replace("{model}", msb), prov);
            if (o.BaselineFrom is { } bf2) SharedBaseline.Apply(dest, bf2.Replace("{model}", msb));
            // Stamp provenance last so the dataset carries its pin key (corpus + doc identity) for
            // future reuse decisions.
            Provenance.Stamp(dest, prov);
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

    // Rewrite the YAML frontmatter `name:` to `unit`. Used only for the push artifact so
    // skill-validator's agent-eval discovery (keyed on the agent name) resolves the unit's
    // eval.yaml. No-op when there is no leading `---` frontmatter block.
    private static string ForceFrontmatterName(string text, string unit)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count == 0 || lines[0] != "---") return text;
        for (var i = 1; i < lines.Count && lines[i] != "---"; i++)
        {
            if (lines[i].StartsWith("name:"))
            {
                lines[i] = $"name: {unit}";
                return string.Join(nl, lines);
            }
        }
        return text;
    }

    // README markdown often starts with its own frontmatter / leading blanks; keep it
    // as-is but guarantee a trailing newline so the SKILL.md body is well-formed.
    private static string NormalizeBody(string text)
    {
        text = text.Replace("\r\n", "\n");
        return text.EndsWith('\n') ? text : text + "\n";
    }
}
