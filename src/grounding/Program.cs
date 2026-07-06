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
    Description = "table | card | doc-card | model-diff | source-diff | skill-diff | tools-card | web-card",
    DefaultValueFactory = _ => "table",
};
viewOpt.AcceptOnlyFromAmong("table", "card", "doc-card", "model-diff", "source-diff", "skill-diff", "tools-card", "web-card");
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
var legacy = new[] { "card", "model-diff", "source-diff", "skill-diff", "tools-card", "web-card" }
    .ToDictionary(v => v, v => new Option<bool>($"--{v}") { Description = $"Alias for --view {v}." });

var analyze = new Command("analyze", "Render metric cards / tables from results.json.")
{
    filesArg, viewOpt, noTitleOpt, ietModelOpt, jsonlOpt,
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
        case "doc-card": cards.DocCard(files, parse.GetValue(jsonlOpt)); break;
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

var run = new Command("run", "Run a grounding unit through skill-validator with a chosen source.")
{
    unitArg, sourceOpt, deliveryOpt, modelOpt, runsOpt, judgeOpt, noJudgeOpt,
    testsDirOpt, outOpt, readmeFileOpt, dryRunOpt, emitSkillOpt, rootOpt,
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
    };
    return Runner.Run(opts);
});
root.Subcommands.Add(run);

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
