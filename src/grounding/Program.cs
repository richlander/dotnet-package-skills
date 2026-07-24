using System.CommandLine;
using Grounding;
using Grounding.Analyze;
using Grounding.Run;

var root = new RootCommand("grounding — package-grounding eval orchestration & analysis");

// ---- analyze ------------------------------------------------------------
var filesArg = new Argument<string[]>("files")
{
    Description = "One or more results.json paths (globs allowed).",
    Arity = ArgumentArity.OneOrMore,
};
var viewOpt = new Option<string>("--view", "-v")
{
    Description = "table | card | ladder | smell | doc-card | liet | h2h | model-diff | source-diff | skill-diff | tools-card | web-card",
    DefaultValueFactory = _ => "table",
};
viewOpt.AcceptOnlyFromAmong("table", "card", "ladder", "smell", "doc-card", "liet", "h2h", "model-diff", "source-diff", "skill-diff", "tools-card", "web-card");
var svgOpt = new Option<string?>("--svg")
{
    Description = "For --view liet: also write the LIET curve as an SVG to this path.",
};
var oraclePluginOpt = new Option<bool>("--oracle-from-plugin")
{
    Description = "For --view liet: read the skilledPlugin arm as the SKILL.md oracle (opt-in; use only when that arm carried the fuller doc).",
};
var noTitleOpt = new Option<bool>("--no-title")
{
    Description = "Omit the ### heading; fold the model into the italic descriptor.",
};
var jsonlOpt = new Option<bool>("--jsonl")
{
    Description = "For doc-card: also emit the decomposed typed JSONL rows.",
};
var ietModelOpt = new Option<string>("--iet-model")
{
    Description = $"IET cost model: {IetModels.Names}. Default 'auto' = per-model (Anthropic for Claude, OpenAI for GPT); any other value forces that model for every column.",
    DefaultValueFactory = _ => "auto",
};
ietModelOpt.AcceptOnlyFromAmong("auto", "anthropic", "claude", "openai", "gpt", "no-cache", "nocache", "gpt-pro", "gpt-5.5-pro");
// View flags (aliases for --view): --card / --model-diff / etc.
var legacy = new[] { "card", "smell", "model-diff", "source-diff", "skill-diff", "tools-card", "web-card" }
    .ToDictionary(v => v, v => new Option<bool>($"--{v}") { Description = $"Alias for --view {v}." });

var analyze = new Command("analyze", "Render metric cards / tables from results.json.")
{
    filesArg, viewOpt, noTitleOpt, ietModelOpt, jsonlOpt, svgOpt, oraclePluginOpt,
};
foreach (var o in legacy.Values) analyze.Options.Add(o);
analyze.SetAction(parse =>
{
    var files = FileArgs.Expand(parse.GetValue(filesArg) ?? Array.Empty<string>());
    if (files.Count == 0) { Console.Error.WriteLine("analyze: no input files."); return 1; }
    var ietSel = (parse.GetValue(ietModelOpt) ?? "auto").Trim().ToLowerInvariant();
    // "auto" (default): choose the scheme per run from its model. "anthropic"/"openai" force a
    // scheme; "no-cache" is a modifier (per-model scheme, input repriced at base rate).
    IetModels.Apply(IetModels.ParseSelection(ietSel));
    var cards = new Cards { NoTitle = parse.GetValue(noTitleOpt) };
    var view = legacy.FirstOrDefault(kv => parse.GetValue(kv.Value)).Key ?? parse.GetValue(viewOpt);
    switch (view)
    {
        case "card": cards.Card(files); break;
        case "ladder": new Ladder(Console.Out) { NoTitle = parse.GetValue(noTitleOpt) }.Render(files); break;
        case "smell": cards.SmellCard(files); break;
        case "doc-card": cards.DocCard(files, parse.GetValue(jsonlOpt)); break;
        case "h2h": cards.H2H(files); break;
        case "liet": new Liet(Console.Out) { NoTitle = parse.GetValue(noTitleOpt), OracleFromPlugin = parse.GetValue(oraclePluginOpt) }.Render(files, parse.GetValue(svgOpt)); break;
        case "model-diff": cards.ModelDiff(files); break;
        case "source-diff": cards.SourceDiff(files); break;
        case "skill-diff": cards.SkillDiff(files); break;
        case "tools-card": cards.ToolsCard(files); break;
        case "web-card": cards.WebCard(files); break;
        default: cards.Table(files); break;
    }
    return 0;
});
root.Subcommands.Add(analyze);

// ---- ledger -------------------------------------------------------------
// The content ledger — attribute each AGENTS.md block to the ladder rung(s) where baseline shows
// a deficit (fail / archaeology / IET premium). Emits justification, orphans, and coverage gaps.
// A filter over existing per-scenario data (no re-run), like LIET.
var ledgerFilesArg = new Argument<string[]>("files")
{
    Description = "One or more results.json paths (globs allowed).",
    Arity = ArgumentArity.OneOrMore,
};
var ledgerDocOpt = new Option<string?>("--doc") { Description = "AGENTS.md to attribute (default: resolved from the dataset's grounding dir)." };
var ledgerPremiumOpt = new Option<double>("--iet-premium")
{
    Description = "Baseline IET premium over grounded (fraction) that counts as a deficit even when baseline passed clean. Default 0.20.",
    DefaultValueFactory = _ => 0.20,
};
var ledgerMinOverlapOpt = new Option<int>("--min-overlap")
{
    Description = "Distinctive terms a block must share with a rung's dig-subjects to be attributed (guards against coincidental matches; assertion API-ids are curated, so 1 is the default). Default 1.",
    DefaultValueFactory = _ => 1,
};
var ledgerIetOpt = new Option<string>("--iet-model") { Description = $"IET model: {IetModels.Names}.", DefaultValueFactory = _ => "auto" };
var ledger = new Command("ledger", "Attribute AGENTS.md content blocks to the ladder rungs where baseline has a deficit.")
{
    ledgerFilesArg, ledgerDocOpt, ledgerPremiumOpt, ledgerMinOverlapOpt, ledgerIetOpt,
};
ledger.SetAction(parse =>
{
    var files = FileArgs.Expand(parse.GetValue(ledgerFilesArg) ?? Array.Empty<string>());
    if (files.Count == 0) { Console.Error.WriteLine("ledger: no input files."); return 1; }
    IetModels.Apply(IetModels.ParseSelection((parse.GetValue(ledgerIetOpt) ?? "auto").Trim().ToLowerInvariant()));
    return new Ledger().Run(files, parse.GetValue(ledgerDocOpt), parse.GetValue(ledgerPremiumOpt), parse.GetValue(ledgerMinOverlapOpt));
});
root.Subcommands.Add(ledger);

// ---- rescore-assertions -------------------------------------------------
// Re-evaluate the current eval.yaml's judgeable assertions (file_contains, reject_tools) over the
// kept trajectory, with no agent re-run — the judgment-side half of cheap re-runs. Reproduces
// recorded results when assertions are unchanged; surfaces the honest verdict when they are tightened.
var rescoreAsrtFilesArg = new Argument<string[]>("datasets")
{
    Description = "One or more dataset results.json paths (globs allowed).",
    Arity = ArgumentArity.OneOrMore,
};
var rescoreUnitOpt = new Option<string?>("--unit") { Description = "Grounding unit (default: the dataset's skillName)." };
var rescoreTestsDirOpt = new Option<string?>("--tests-dir") { Description = "Tests dir holding <unit>/eval.yaml + fixtures (default: auto)." };
var rescoreRootOpt = new Option<string?>("--root") { Description = "Repo root holding the eval.yaml + fixtures (default: infra repo root)." };
var rescoreWriteOpt = new Option<bool>("--write") { Description = "Write updated assertionResults back into the dataset(s). Default: report only." };
var rescoreAllOpt = new Option<bool>("--all") { Description = "Re-score ALL judgeable assertions (audit reconstruction faithfulness), not just the ones whose definition changed. Never written." };
var rescoreAsrt = new Command("rescore-assertions", "Re-score file_contains/reject_tools assertions over kept sessions without re-running the agent.")
{
    rescoreAsrtFilesArg, rescoreUnitOpt, rescoreTestsDirOpt, rescoreRootOpt, rescoreWriteOpt, rescoreAllOpt,
};
rescoreAsrt.SetAction(parse =>
{
    var files = FileArgs.Expand(parse.GetValue(rescoreAsrtFilesArg) ?? Array.Empty<string>());
    if (files.Count == 0) { Console.Error.WriteLine("rescore-assertions: no input files."); return 1; }
    return new RescoreAssertions().Run(files, parse.GetValue(rescoreUnitOpt), parse.GetValue(rescoreTestsDirOpt),
        parse.GetValue(rescoreRootOpt), parse.GetValue(rescoreWriteOpt), parse.GetValue(rescoreAllOpt));
});
root.Subcommands.Add(rescoreAsrt);

// ---- run ----------------------------------------------------------------
var unitArg = new Argument<string>("unit") { Description = "Grounding unit (grounding/<unit>)." };
var sourceOpt = new Option<string>("--source", "-s")
{
    Description = "Grounding source to test: agents | skill | readme | none.",
    DefaultValueFactory = _ => "agents",
};
sourceOpt.AcceptOnlyFromAmong("agents", "skill", "readme", "none");
var deliveryOpt = new Option<string>("--delivery")
{
    Description = "Delivery mode: pull = model-invoked skill (SKILL.md, must be activated); push = always-on agent (.agent.md, in context at t=0).",
    DefaultValueFactory = _ => "pull",
};
deliveryOpt.AcceptOnlyFromAmong("pull", "push");
var modelOpt = new Option<string[]>("--model", "-m")
{
    Description = "Model id(s); repeat or space-separate. Default claude-haiku-4.5.",
    Arity = ArgumentArity.ZeroOrMore,
    AllowMultipleArgumentsPerToken = true,
};
var runsOpt = new Option<int>("--runs") { Description = "Runs per scenario.", DefaultValueFactory = _ => 1 };
var judgeOpt = new Option<string>("--judge-model") { Description = "Judge model.", DefaultValueFactory = _ => "claude-haiku-4.5" };
var noJudgeOpt = new Option<bool>("--no-judge") { Description = "Skip judging." };
var testsDirOpt = new Option<string?>("--tests-dir") { Description = "Tests directory (default: auto — 'grounding' for a co-located bundle, else 'tests')." };
var outOpt = new Option<string?>("--out") { Description = "Output dataset dir (default $GROUNDING_DATA_DIR or ~/.cache/grounding/<unit>-6q; not committed)." };
var readmeFileOpt = new Option<string?>("--readme-file") { Description = "README path for --source readme." };
var dryRunOpt = new Option<bool>("--dry-run") { Description = "Print the plan without invoking skill-validator." };
var emitSkillOpt = new Option<string?>("--emit-skill") { Description = "Write the generated SKILL.md to a path and exit." };
var rootOpt = new Option<string?>("--root") { Description = "Grounding root holding grounding/<unit> — a target package repo (default: the infra repo). Also GROUNDING_ROOT. Eval reads AGENTS.md in place; no packing." };
var baselineOutOpt = new Option<string?>("--baseline-out") { Description = "Shared-baseline flow: run and persist the ungrounded baseline to this path (a {model} token is substituted per model). Reuse it with --baseline-from so push/pull compare against one pinned baseline." };
var baselineFromOpt = new Option<string?>("--baseline-from") { Description = "Shared-baseline flow: reuse a baseline persisted by --baseline-out (skips the baseline arm). Must match model/judge/prompts. {model} substituted per model." };
var freshOpt = new Option<bool>("--fresh") { Description = "Regenerate even when a dataset with matching provenance (same corpus + doc content) already exists. Default reuses it (cheap re-runs)." };
var evalModeOpt = new Option<string>("--eval-mode") { Description = "Evaluation lens: 'per-skill' (grade min(isolated, plugin) — one skill's standalone value) or 'holistic' (skip the isolated arm; grade the self-selecting plugin arm — the whole-shelf CT-24 benchmark).", DefaultValueFactory = _ => "per-skill" };
evalModeOpt.AcceptOnlyFromAmong("per-skill", "holistic");
var excludeSkillOpt = new Option<string[]>("--exclude-skill") { Description = "Leave-one-out ablation: omit the named skill from the plugin arm's shelf (forwarded to skill-validator). Repeatable. Datasets are tagged '<unit>-skill-minus-<X>' so shelf-minus-X sits beside the full-shelf dataset for marginal comparison.", AllowMultipleArgumentsPerToken = true };

var run = new Command("run", "Run a grounding unit through skill-validator with a chosen source.")
{
    unitArg, sourceOpt, deliveryOpt, modelOpt, runsOpt, judgeOpt, noJudgeOpt,
    testsDirOpt, outOpt, readmeFileOpt, dryRunOpt, emitSkillOpt, rootOpt, baselineOutOpt, baselineFromOpt, freshOpt, evalModeOpt, excludeSkillOpt,
};
run.SetAction(parse =>
{
    var models = (parse.GetValue(modelOpt) ?? Array.Empty<string>())
        .SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .ToList();
    if (models.Count == 0) models.Add("claude-haiku-4.5");
    var opts = new RunOptions
    {
        Unit = parse.GetValue(unitArg)!,
        Source = parse.GetValue(sourceOpt)!,
        Delivery = parse.GetValue(deliveryOpt)!,
        Models = models,
        Runs = parse.GetValue(runsOpt),
        JudgeModel = parse.GetValue(judgeOpt)!,
        NoJudge = parse.GetValue(noJudgeOpt),
        TestsDir = parse.GetValue(testsDirOpt),
        OutDir = parse.GetValue(outOpt),
        ReadmeFile = parse.GetValue(readmeFileOpt),
        DryRun = parse.GetValue(dryRunOpt),
        EmitSkill = parse.GetValue(emitSkillOpt),
        Root = parse.GetValue(rootOpt),
        BaselineOut = parse.GetValue(baselineOutOpt),
        BaselineFrom = parse.GetValue(baselineFromOpt),
        Fresh = parse.GetValue(freshOpt),
        EvalMode = parse.GetValue(evalModeOpt)!,
        ExcludeSkills = (parse.GetValue(excludeSkillOpt) ?? Array.Empty<string>()).ToList(),
    };
    return Runner.Run(opts);
});
root.Subcommands.Add(run);

// ---- smell --------------------------------------------------------------
// Unjudged "finger in the wind" smell test. Runs the self-selecting shelf (pull) with --no-judge
// in holistic mode (no isolated arm, no pairwise judge), then renders the compact single-arm
// SmellCard: tasks correct, grounding reliance, archaeology (cache/web), skill pulls, tool/session
// turns, output, and IET. Cheap signal after editing a shelf — did the skills activate, avoid
// archaeology, and stay cheap — without paying for the judge.
var smellUnitArg = new Argument<string>("unit") { Description = "Grounding unit (e.g. markout)." };
var smellModelOpt = new Option<string[]>("--model", "-m") { Description = "Model(s) to run (space/repeat-separated).", AllowMultipleArgumentsPerToken = true };
var smellRunsOpt = new Option<int>("--runs") { Description = "Runs per scenario.", DefaultValueFactory = _ => 3 };
var smellRootOpt = new Option<string?>("--root") { Description = "Grounding root holding grounding/<unit> (a target package repo). Also GROUNDING_ROOT." };
var smellTestsDirOpt = new Option<string?>("--tests-dir") { Description = "Tests directory (default: auto)." };
var smellOutOpt = new Option<string?>("--out") { Description = "Output dataset dir (default cache <unit>-6q)." };
var smellFreshOpt = new Option<bool>("--fresh") { Description = "Regenerate even when a provenance-matching dataset exists (default reuses it)." };
var smell = new Command("smell", "Unjudged smell test: run the self-selecting shelf (no judge) and report IET, turns, archaeology, and skill pulls.")
{
    smellUnitArg, smellModelOpt, smellRunsOpt, smellRootOpt, smellTestsDirOpt, smellOutOpt, smellFreshOpt,
};
smell.SetAction(parse =>
{
    var models = (parse.GetValue(smellModelOpt) ?? Array.Empty<string>())
        .SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .ToList();
    if (models.Count == 0) models.Add("claude-haiku-4.5");
    var opts = new RunOptions
    {
        Unit = parse.GetValue(smellUnitArg)!,
        Source = "skill",
        Delivery = "pull",
        Models = models,
        Runs = parse.GetValue(smellRunsOpt),
        NoJudge = true,
        EvalMode = "holistic",
        Smell = true,
        TestsDir = parse.GetValue(smellTestsDirOpt),
        OutDir = parse.GetValue(smellOutOpt),
        Root = parse.GetValue(smellRootOpt),
        Fresh = parse.GetValue(smellFreshOpt),
    };
    return Runner.Run(opts);
});
root.Subcommands.Add(smell);

// ---- ablate -------------------------------------------------------------
// Leave-one-out skill ablation (composition-axis LIET). Runs the full shelf holistically, mines
// which skills a scenario pulls consistently, then re-runs the shelf minus each such skill and
// reports the per-(skill, scenario) marginal = full − (shelf−X). Negative marginals flag
// destructive interference; ~0 flags a free rider.
var ablUnitArg = new Argument<string>("unit") { Description = "Grounding unit (grounding/<unit>) whose shelf to ablate." };
var ablModelOpt = new Option<string[]>("--model", "-m") { Description = "Model to run (first is used).", AllowMultipleArgumentsPerToken = true };
var ablRunsOpt = new Option<int>("--runs") { Description = "Runs per scenario (≥5 recommended for consistency).", DefaultValueFactory = _ => 5 };
var ablRootOpt = new Option<string?>("--root") { Description = "Skills root holding skills/ + grounding/<unit> (a target package repo). Also GROUNDING_ROOT." };
var ablTestsDirOpt = new Option<string?>("--tests-dir") { Description = "Eval root (default 'grounding')." };
var ablBaselineFromOpt = new Option<string?>("--baseline-from") { Description = "Reuse a persisted baseline so only the plugin arm re-runs per cell." };
var ablSkillsOpt = new Option<string[]>("--skills") { Description = "Explicit skills to ablate (repeatable). Default: auto — every domain skill a scenario pulls consistently.", AllowMultipleArgumentsPerToken = true };
var ablScenariosOpt = new Option<string[]>("--scenarios") { Description = "Restrict the eval to scenarios whose name starts with/contains these tokens (e.g. CT05). Cheapest for a focused first pass.", AllowMultipleArgumentsPerToken = true };
var ablConsistencyOpt = new Option<double>("--consistency") { Description = "Pull-rate threshold to qualify a skill as consistently pulled (0.8 = 4/5).", DefaultValueFactory = _ => 0.8 };
var ablOutOpt = new Option<string?>("--out") { Description = "Output dataset dir (default cache <unit>-6q)." };
var ablDryRunOpt = new Option<bool>("--dry-run") { Description = "Plan the full-shelf run and print the intended ablation cells without running the shelf-minus arms." };
var ablate = new Command("ablate", "Leave-one-out skill ablation: measure each skill's marginal contribution on the shelf.")
{
    ablUnitArg, ablModelOpt, ablRunsOpt, ablRootOpt, ablTestsDirOpt, ablBaselineFromOpt,
    ablSkillsOpt, ablScenariosOpt, ablConsistencyOpt, ablOutOpt, ablDryRunOpt,
};
ablate.SetAction(parse =>
{
    var models = (parse.GetValue(ablModelOpt) ?? Array.Empty<string>())
        .SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
    if (models.Count == 0) models.Add("claude-haiku-4.5");
    var skills = (parse.GetValue(ablSkillsOpt) ?? Array.Empty<string>()).ToList();
    var scenarios = (parse.GetValue(ablScenariosOpt) ?? Array.Empty<string>()).ToList();
    var opts = new Grounding.Ablate.AblateOptions
    {
        Unit = parse.GetValue(ablUnitArg)!,
        Models = models,
        Runs = parse.GetValue(ablRunsOpt),
        Root = parse.GetValue(ablRootOpt),
        TestsDir = parse.GetValue(ablTestsDirOpt),
        BaselineFrom = parse.GetValue(ablBaselineFromOpt),
        Skills = skills.Count > 0 ? skills : null,
        Scenarios = scenarios.Count > 0 ? scenarios : null,
        Consistency = parse.GetValue(ablConsistencyOpt),
        OutDir = parse.GetValue(ablOutOpt),
        DryRun = parse.GetValue(ablDryRunOpt),
    };
    return Grounding.Ablate.Ablate.Run(opts);
});
root.Subcommands.Add(ablate);

// ---- provenance ---------------------------------------------------------
// Inspect the pin key stamped into datasets. With one dataset: show it. With two+: report whether
// each later dataset is REUSABLE as the first (same corpus + doc identity) — the cheap-re-run check.
var provFilesArg = new Argument<string[]>("datasets")
{
    Description = "One or more dataset results.json paths.",
    Arity = ArgumentArity.OneOrMore,
};
var provenance = new Command("provenance", "Show dataset provenance (pin key); with 2+, check reuse compatibility.")
{
    provFilesArg,
};
provenance.SetAction(parse =>
{
    var files = FileArgs.Expand(parse.GetValue(provFilesArg) ?? Array.Empty<string>());
    if (files.Count == 0) { Console.Error.WriteLine("provenance: no input files."); return 1; }
    return Grounding.Run.Provenance.Report(files);
});
root.Subcommands.Add(provenance);

// ---- check-agents -------------------------------------------------------
// SKILL.md is NOT generated — it is an optional, maintainer-authored Textbook the eval
// consumes only when present (grounding run --source skill). This command just enforces
// the AGENTS.md body line budget.
var checkAgents = new Command("check-agents", "Validate every grounding/<unit>/AGENTS.md is within the line budget.");
checkAgents.SetAction(_ => Grounding.Codegen.Codegen.CheckAgents());
root.Subcommands.Add(checkAgents);

// ---- enrich -------------------------------------------------------------
// Inject per-run-averaged, event-derived tool-call stats (from sessions.db) into a dataset so
// runs>1 tool-call/nuget numbers are correct and consistent. Runs automatically after `run`.
var enrichDataset = new Argument<string>("dataset") { Description = "Dataset results.json to enrich in place." };
var enrichSessionsOpt = new Option<string?>("--sessions-db") { Description = "Path to sessions.db (default: sibling of the dataset, or under --results-dir)." };
var enrichResultsDirOpt = new Option<string?>("--results-dir") { Description = "Results dir holding <timestamp>/sessions.db." };
var enrichIetOpt = new Option<string>("--iet-model") { Description = $"IET model for tool-turn IET: {IetModels.Names}.", DefaultValueFactory = _ => "auto" };
var enrich = new Command("enrich", "Backfill correct per-run tool-call stats into a dataset from sessions.db.")
{
    enrichDataset, enrichSessionsOpt, enrichResultsDirOpt, enrichIetOpt,
};
enrich.SetAction(parse =>
{
    var ds = parse.GetValue(enrichDataset)!;
    var sdb = parse.GetValue(enrichSessionsOpt);
    var resultsDir = parse.GetValue(enrichResultsDirOpt);
    if (sdb is null && resultsDir is not null)
        sdb = Directory.EnumerateFiles(resultsDir, "sessions.db", SearchOption.AllDirectories).FirstOrDefault();
    var sel = (parse.GetValue(enrichIetOpt) ?? "auto").Trim().ToLowerInvariant();
    IetModels.Apply(IetModels.ParseSelection(sel));
    return Grounding.Analyze.Enrich.Run(ds, sdb);
});
root.Subcommands.Add(enrich);


// ---- gen-plugins --------------------------------------------------------
var genPlugins = new Command("gen-plugins", "Expand grounding/**/plugin.json.in into plugin.json.");
genPlugins.SetAction(_ => Grounding.Codegen.Codegen.GenPlugins());
root.Subcommands.Add(genPlugins);

// ---- rescore ------------------------------------------------------------
var specsArg = new Argument<string[]>("specs")
{
    Description = "model=path specs (path is a results.json or its dir). Omit with --all.",
    Arity = ArgumentArity.ZeroOrMore,
};
var wOpt = new Option<double>("--w")
{
    Description = "Output-token IET weight (5 Anthropic / 6 OpenAI).",
    DefaultValueFactory = _ => 5.0,
};
var allOpt = new Option<bool>("--all")
{
    Description = "Batch-rescore every .skill-validator-results/*/results.json.",
};
var rescore = new Command("rescore", "Re-score skill-validator results under the IET rubric.")
{
    specsArg, wOpt, allOpt,
};
rescore.SetAction(parse =>
{
    var w = parse.GetValue(wOpt);
    if (parse.GetValue(allOpt))
        return Grounding.Rescore.Rescore.All(w);
    return Grounding.Rescore.Rescore.Single(parse.GetValue(specsArg) ?? Array.Empty<string>(), w);
});
root.Subcommands.Add(rescore);

// ---- channels -----------------------------------------------------------
var channels = new Command("channels", "Delivery-channel matrix (Markout study).");
var taskDirArg = new Argument<string>("task-dir")
{
    Description = "data/<task> directory.",
    DefaultValueFactory = _ => "data/markout",
    Arity = ArgumentArity.ZeroOrOne,
};
var extract = new Command("extract", "Per-model channel matrix from data/<task>/*.json.") { taskDirArg };
extract.SetAction(parse => Grounding.Channels.Channels.Extract(parse.GetValue(taskDirArg)!));
channels.Subcommands.Add(extract);
var compare = new Command("compare", "Cross-channel IET comparison (data/markout).");
compare.SetAction(_ => Grounding.Channels.Channels.Compare());
channels.Subcommands.Add(compare);
root.Subcommands.Add(channels);

// ---- mcp ----------------------------------------------------------------
var mcpRootOpt = new Option<string?>("--root")
{
    Description = "Repo root holding grounding/ (harness spawns with an unrelated cwd).",
};
var mcp = new Command("mcp", "Run the stdio JSON-RPC grounding MCP server (GROUNDING_GATE).")
{
    mcpRootOpt,
};
mcp.SetAction(parse => Grounding.Mcp.McpServer.Run(parse.GetValue(mcpRootOpt)));
root.Subcommands.Add(mcp);

// ---- tasks / export -----------------------------------------------------
var tUnit = new Argument<string>("unit") { Description = "Grounding unit." };
var tOut = new Option<string?>("--out") { Description = "Write TASKS.md here (else stdout)." };
var tasks = new Command("tasks", "Render TASKS.md (jobs-to-be-done) from tests/<unit>/eval.yaml.") { tUnit, tOut };
tasks.SetAction(p => Grounding.Bundle.Bundle.Tasks(p.GetValue(tUnit)!, p.GetValue(tOut)));
root.Subcommands.Add(tasks);

var eUnit = new Argument<string>("unit") { Description = "Grounding unit." };
var eTo = new Option<string>("--to") { Description = "Target dir (e.g. <repo>/grounding/<unit>).", Required = true };
var export = new Command("export", "Export a self-contained grounding bundle into a target repo.") { eUnit, eTo };
export.SetAction(p => Grounding.Bundle.Bundle.Export(p.GetValue(eUnit)!, p.GetValue(eTo)!));
root.Subcommands.Add(export);

root.SetAction(_ =>
{
    Console.WriteLine("grounding: specify a command (analyze, run). Try --help.");
    return 0;
});

return root.Parse(args).Invoke();
