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
    Description = "table | card | model-diff | source-diff",
    DefaultValueFactory = _ => "table",
};
viewOpt.AcceptOnlyFromAmong("table", "card", "model-diff", "source-diff");
var noTitleOpt = new Option<bool>("--no-title")
{
    Description = "Omit the ### heading; fold the model into the italic descriptor.",
};

var analyze = new Command("analyze", "Render metric cards / tables from results.json.")
{
    filesArg, viewOpt, noTitleOpt,
};
analyze.SetAction(parse =>
{
    var files = FileArgs.Expand(parse.GetValue(filesArg) ?? Array.Empty<string>());
    if (files.Count == 0) { Console.Error.WriteLine("analyze: no input files."); return 1; }
    var cards = new Cards { NoTitle = parse.GetValue(noTitleOpt) };
    switch (parse.GetValue(viewOpt))
    {
        case "card": cards.Card(files); break;
        case "model-diff": cards.ModelDiff(files); break;
        case "source-diff": cards.SourceDiff(files); break;
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

root.SetAction(_ =>
{
    Console.WriteLine("grounding: specify a command (analyze, run). Try --help.");
    return 0;
});

return root.Parse(args).Invoke();
