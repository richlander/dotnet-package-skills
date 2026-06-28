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
    Description = "table | card | model-diff | source-diff | tools-card | web-card",
    DefaultValueFactory = _ => "table",
};
viewOpt.AcceptOnlyFromAmong("table", "card", "model-diff", "source-diff", "tools-card", "web-card");
var noTitleOpt = new Option<bool>("--no-title")
{
    Description = "Omit the ### heading; fold the model into the italic descriptor.",
};
// View flags (aliases for --view): --card / --model-diff / etc.
var legacy = new[] { "card", "model-diff", "source-diff", "tools-card", "web-card" }
    .ToDictionary(v => v, v => new Option<bool>($"--{v}") { Description = $"Alias for --view {v}." });

var analyze = new Command("analyze", "Render metric cards / tables from results.json.")
{
    filesArg, viewOpt, noTitleOpt,
};
foreach (var o in legacy.Values) analyze.Options.Add(o);
analyze.SetAction(parse =>
{
    var files = FileArgs.Expand(parse.GetValue(filesArg) ?? Array.Empty<string>());
    if (files.Count == 0) { Console.Error.WriteLine("analyze: no input files."); return 1; }
    var cards = new Cards { NoTitle = parse.GetValue(noTitleOpt) };
    var view = legacy.FirstOrDefault(kv => parse.GetValue(kv.Value)).Key ?? parse.GetValue(viewOpt);
    switch (view)
    {
        case "card": cards.Card(files); break;
        case "model-diff": cards.ModelDiff(files); break;
        case "source-diff": cards.SourceDiff(files); break;
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
    Description = "Grounding source to test: agents | readme | none.",
    DefaultValueFactory = _ => "agents",
};
sourceOpt.AcceptOnlyFromAmong("agents", "readme", "none");
var modelOpt = new Option<string[]>("--model", "-m")
{
    Description = "Model id(s); repeat or space-separate. Default claude-haiku-4.5.",
    Arity = ArgumentArity.ZeroOrMore,
    AllowMultipleArgumentsPerToken = true,
};
var runsOpt = new Option<int>("--runs") { Description = "Runs per scenario.", DefaultValueFactory = _ => 1 };
var judgeOpt = new Option<string>("--judge-model") { Description = "Judge model.", DefaultValueFactory = _ => "claude-haiku-4.5" };
var noJudgeOpt = new Option<bool>("--no-judge") { Description = "Skip judging." };
var testsDirOpt = new Option<string>("--tests-dir") { Description = "Tests directory.", DefaultValueFactory = _ => "tests" };
var outOpt = new Option<string?>("--out") { Description = "Output dataset dir (default data/<unit>-6q)." };
var readmeFileOpt = new Option<string?>("--readme-file") { Description = "README path for --source readme." };
var dryRunOpt = new Option<bool>("--dry-run") { Description = "Print the plan without invoking skill-validator." };
var emitSkillOpt = new Option<string?>("--emit-skill") { Description = "Write the generated SKILL.md to a path and exit." };

var run = new Command("run", "Run a grounding unit through skill-validator with a chosen source.")
{
    unitArg, sourceOpt, modelOpt, runsOpt, judgeOpt, noJudgeOpt,
    testsDirOpt, outOpt, readmeFileOpt, dryRunOpt, emitSkillOpt,
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
        Models = models,
        Runs = parse.GetValue(runsOpt),
        JudgeModel = parse.GetValue(judgeOpt)!,
        NoJudge = parse.GetValue(noJudgeOpt),
        TestsDir = parse.GetValue(testsDirOpt)!,
        OutDir = parse.GetValue(outOpt),
        ReadmeFile = parse.GetValue(readmeFileOpt),
        DryRun = parse.GetValue(dryRunOpt),
        EmitSkill = parse.GetValue(emitSkillOpt),
    };
    return Runner.Run(opts);
});
root.Subcommands.Add(run);

// ---- sync-skill ---------------------------------------------------------
var checkOpt = new Option<bool>("--check")
{
    Description = "Fail if any SKILL.md is stale instead of writing (for CI).",
};
var syncSkill = new Command("sync-skill", "Regenerate grounding/<unit>/SKILL.md from AGENTS.md.")
{
    checkOpt,
};
syncSkill.SetAction(parse => Grounding.Codegen.Codegen.SyncSkill(parse.GetValue(checkOpt)));
root.Subcommands.Add(syncSkill);

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

root.SetAction(_ =>
{
    Console.WriteLine("grounding: specify a command (analyze, run). Try --help.");
    return 0;
});

return root.Parse(args).Invoke();
