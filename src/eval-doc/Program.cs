using Markout;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: eval-doc <eval.yaml> [eval.md]   (renders a reviewer-facing eval.md; output defaults to the input with a .md extension)");
    return 1;
}

var inputPath = args[0];
var outputPath = args.Length > 1 ? args[1] : Path.ChangeExtension(inputPath, ".md");

using var input = File.OpenText(inputPath);
var yaml = new DeserializerBuilder()
    .WithNamingConvention(UnderscoredNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build()
    .Deserialize<EvalYaml>(input);

var document = EvalMarkdownDocument.FromYaml(yaml, inputPath);

var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
if (!string.IsNullOrEmpty(outputDirectory))
{
    Directory.CreateDirectory(outputDirectory);
}

await using var output = File.CreateText(outputPath);
MarkoutSerializer.Serialize(document, output, EvalDocContext.Default);

Console.WriteLine(outputPath);
return 0;

internal static class EvalDocHelpers
{
    // Fallback only — the real version is read from a fixture .csproj (the source of truth) via
    // ResolveMarkoutVersion so eval.md never drifts from the fixtures again. Kept current anyway.
    private const string MarkoutPackageVersionFallback = "0.23.0";

    // Read the Markout package version from a fixture .csproj next to the eval.yaml (the same version
    // the scenarios actually build against), so the rendered eval.md always matches the fixtures.
    public static string ResolveMarkoutVersion(string inputPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".";
            var fixturesDir = Path.Combine(dir, "fixtures");
            if (Directory.Exists(fixturesDir))
            {
                var csproj = Directory
                    .EnumerateFiles(fixturesDir, "*.csproj", SearchOption.AllDirectories)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (csproj is not null)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        File.ReadAllText(csproj),
                        "<PackageReference\\s+Include=\"Markout\"\\s+Version=\"(?<v>[^\"]+)\"");
                    if (m.Success)
                    {
                        return m.Groups["v"].Value;
                    }
                }
            }
        }
        catch
        {
            // fall through to the fallback constant
        }

        return MarkoutPackageVersionFallback;
    }

    // Wrap API/code identifiers in inline-code backticks so reviewers can tell code from prose.
    // Ordered alternation: attribute forms (with optional args) first so [MarkoutX(...)] is guarded as
    // a whole before the bare-identifier rule can bite the inner token; then dotted/bare Markout
    // identifiers; then a curated set of non-Markout API words that appear in the rubric prose.
    private static readonly System.Text.RegularExpressions.Regex CodeToken = new(
        @"\[Markout\w+(?:\([^)\]]*\))?\]"
        + @"|\bMarkout\w+(?:\.\w+)?"
        + @"|\b(?:TitleProperty|DescriptionProperty|IncludeSections|ShowWhenProperty|FieldLayout|TableMode|IMarkoutCell|IMarkoutValueFormatter|JsonTypedValues|PlainTextFormatter|UnicodeFormatter)\b",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    // Guard code tokens in free prose (idempotent for our inputs — the source yaml has no backticks).
    public static string CodeGuard(string? text)
        => string.IsNullOrEmpty(text) ? text ?? string.Empty : CodeToken.Replace(text, m => $"`{m.Value}`");

    // Per-scenario fixture files as relative markdown links (e.g. "CT01/Program.cs"), for reviewers to
    // jump from a question straight to the code it applies to. Links are relative to eval.md's dir.
    public static string FormatFixtures(IReadOnlyList<EvalFile>? files)
    {
        if (files is null || files.Count == 0)
        {
            return string.Empty;
        }

        var links = files
            .Where(file => !string.IsNullOrWhiteSpace(file.Source))
            .Select(file =>
            {
                var source = file.Source!;
                var dir = Path.GetFileName(Path.GetDirectoryName(source)) ?? string.Empty;
                var text = string.IsNullOrEmpty(dir) ? Path.GetFileName(source) : $"{dir}/{Path.GetFileName(source)}";
                return $"[{text}]({source})";
            })
            .ToArray();

        return string.Join(", ", links);
    }

    public static string GetPackageLabel(string inputPath)
    {
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var version = ResolveMarkoutVersion(inputPath);
        return string.Equals(name, "eval", StringComparison.OrdinalIgnoreCase)
            ? $"Markout {version}"
            : $"{name} / Markout {version}";
    }

    public static string FormatRejectTools(IReadOnlyList<string>? rejectTools)
    {
        if (rejectTools is null || rejectTools.Count == 0)
        {
            return string.Empty;
        }

        var friendly = rejectTools
            .Select(tool => tool switch
            {
                "web_search" => "web search",
                "web_fetch" => "fetch",
                _ => tool.Replace('_', ' ')
            })
            .ToArray();

        return $"No {string.Join(" / ", friendly)} allowed.";
    }

    public static string SummarizeAssertion(EvalAssertion assertion)
    {
        if (string.Equals(assertion.Type, "file_not_contains", StringComparison.OrdinalIgnoreCase))
        {
            return assertion.Value switch
            {
                { } value when value.Contains("---", StringComparison.Ordinal) => "No hand-written Markdown tables",
                { Length: > 0 } value => $"{assertion.Path ?? "File"} omits {value}",
                _ => "File avoids forbidden content"
            };
        }

        if (!string.Equals(assertion.Type, "run_command_and_assert", StringComparison.OrdinalIgnoreCase))
        {
            return assertion.Type ?? "Assertion";
        }

        var command = assertion.CommandToRun ?? string.Empty;
        var arguments = assertion.CommandArguments ?? string.Empty;
        var commandLine = string.IsNullOrWhiteSpace(arguments) ? command : $"{command} {arguments}";

        if (command == "dotnet" && arguments == "build")
        {
            return "Project builds";
        }

        if (command == "dotnet" && arguments.Contains("run --no-build", StringComparison.Ordinal))
        {
            return assertion.ExpectedStdOutputMatches is { Length: > 0 }
                ? "Rendered output matches the expected structure"
                : "Project runs";
        }

        if (command == "grep")
        {
            return arguments switch
            {
                var text when text.Contains("MarkoutSerializer.Serialize", StringComparison.Ordinal) =>
                    "Drives output through MarkoutSerializer.Serialize",
                var text when text.Contains("MarkoutContext", StringComparison.Ordinal) =>
                    "Registers a Markout serializer context",
                var text when text.Contains("MarkoutBoolFormat", StringComparison.Ordinal) =>
                    "Uses declarative boolean formatting",
                var text when text.Contains("MarkoutPropertyName", StringComparison.Ordinal) =>
                    "Uses declarative field renaming",
                var text when text.Contains("MarkoutIgnore", StringComparison.Ordinal) =>
                    "Uses declarative field hiding",
                var text when text.Contains("MarkoutSection", StringComparison.Ordinal) =>
                    "Declares rendered sections",
                var text when text.Contains("PlainTextFormatter", StringComparison.Ordinal) =>
                    "Uses the plain-text formatter",
                var text when text.Contains("FieldLayout", StringComparison.Ordinal) =>
                    "Uses declarative field layout",
                var text when text.Contains("DescriptionProperty", StringComparison.Ordinal) =>
                    "Uses a description paragraph",
                var text when text.Contains("ShowWhenProperty", StringComparison.Ordinal) =>
                    "Gates sections declaratively",
                var text when text.Contains("IncludeSections", StringComparison.Ordinal) =>
                    "Selects sections at serialization time",
                var text when text.Contains("TableMode", StringComparison.Ordinal) =>
                    "Uses table formatter modes",
                var text when text.Contains("UnicodeFormatter", StringComparison.Ordinal) =>
                    "Uses the Unicode formatter for terminal shapes",
                var text when text.Contains("MarkoutDelta", StringComparison.Ordinal) =>
                    "Uses declarative change deltas",
                var text when text.Contains("MarkoutIgnoreColumnWhen", StringComparison.Ordinal) =>
                    "Hides table columns declaratively",
                var text when text.Contains("JsonTypedValues", StringComparison.Ordinal) =>
                    "Emits typed JSONL values",
                var text when text.Contains("MetricChange", StringComparison.Ordinal) =>
                    "Uses metric-change card rows",
                var text when text.Contains("Goal", StringComparison.Ordinal) =>
                    "Applies goal polarity declaratively",
                var text when text.Contains("MaxItems", StringComparison.Ordinal) =>
                    "Caps rendered rows through writer options",
                var text when text.Contains("MultiSourceRow", StringComparison.Ordinal) =>
                    "Uses multi-source comparison rows",
                var text when text.Contains("Verdict", StringComparison.Ordinal) =>
                    "Uses verdict cells",
                _ => $"Source contains required pattern: {commandLine}"
            };
        }

        return commandLine;
    }
}

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Introduction),
    FieldLayout = FieldLayout.Bulleted)]
public sealed class EvalMarkdownDocument
{
    public string Title { get; init; } = "Markout CT Eval Guide";

    public string Introduction { get; init; } = string.Empty;

    [MarkoutPropertyName("Scenario count")]
    public int ScenarioCount { get; init; }

    [MarkoutPropertyName("Package")]
    public string Package { get; init; } = string.Empty;

    [MarkoutPropertyName("Prompt note")]
    public string PromptNote { get; init; } = string.Empty;

    [MarkoutSection(Name = "Scenarios")]
    [MarkoutUnwrap]
    public List<ScenarioDoc> Scenarios { get; init; } = [];

    public static EvalMarkdownDocument FromYaml(EvalYaml yaml, string inputPath)
    {
        var scenarios = (yaml.Scenarios ?? []).Where(s => !s.HeldOut).ToList();
        var package = EvalDocHelpers.GetPackageLabel(inputPath);
        return new EvalMarkdownDocument
        {
            Title = $"Markout CT Eval Guide ({scenarios.Count} scenarios)",
            Introduction = $"A reviewer-oriented rendering of {scenarios.Count} CT scenarios from {Path.GetFileName(inputPath)} for {package}.",
            ScenarioCount = scenarios.Count,
            Package = package,
            PromptNote = "Prompts describe the library functionally and never name it.",
            Scenarios = scenarios.Select(ScenarioDoc.FromYaml).ToList()
        };
    }
}

[MarkoutSerializable(
    TitleProperty = nameof(Name),
    FieldLayout = FieldLayout.Bulleted)]
public sealed class ScenarioDoc
{
    [MarkoutIgnore]
    public string Name { get; init; } = string.Empty;

    [MarkoutPropertyName("Target skill")]
    public string TargetSkill { get; init; } = string.Empty;

    [MarkoutPropertyName("Tool restriction")]
    [MarkoutSkipDefault]
    public string RejectToolsNote { get; init; } = string.Empty;

    [MarkoutPropertyName("Fixtures")]
    [MarkoutSkipDefault]
    public string Fixtures { get; init; } = string.Empty;

    [MarkoutIgnoreInTable]
    public CodeSection Prompt { get; init; }

    [MarkoutSection(Name = "Rubric")]
    public List<string> Rubric { get; init; } = [];

    [MarkoutSection(Name = "Checks")]
    public List<CheckDoc> Checks { get; init; } = [];

    public static ScenarioDoc FromYaml(EvalScenario scenario) => new()
    {
        Name = scenario.Name ?? "Unnamed scenario",
        TargetSkill = scenario.ExpectedSkill ?? "unspecified",
        RejectToolsNote = EvalDocHelpers.FormatRejectTools(scenario.RejectTools),
        Fixtures = EvalDocHelpers.FormatFixtures(scenario.Setup?.Files),
        Prompt = new CodeSection("text", (scenario.Prompt ?? string.Empty).Trim()),
        Rubric = (scenario.Rubric ?? []).Select(EvalDocHelpers.CodeGuard).ToList(),
        Checks = (scenario.Assertions ?? []).Select(CheckDoc.FromYaml).ToList()
    };
}

[MarkoutSerializable]
public sealed class CheckDoc
{
    public string Check { get; init; } = string.Empty;

    public string Evidence { get; init; } = string.Empty;

    public static CheckDoc FromYaml(EvalAssertion assertion) => new()
    {
        // Table cells are escaped by Markout (backticks -> &#96;) to protect the pipe-delimited table,
        // so code-guarding is applied to the bullet sections (Rubric/Fixtures), not here.
        Check = EvalDocHelpers.SummarizeAssertion(assertion),
        Evidence = assertion.Type switch
        {
            "run_command_and_assert" => string.IsNullOrWhiteSpace(assertion.CommandArguments)
                ? assertion.CommandToRun ?? string.Empty
                : $"{assertion.CommandToRun} {assertion.CommandArguments}",
            "file_not_contains" => $"{assertion.Path}: {assertion.Value}",
            _ => assertion.Type ?? string.Empty
        }
    };
}

public sealed class EvalYaml
{
    public EvalConfig? Config { get; set; }

    public List<EvalScenario>? Scenarios { get; set; }
}

public sealed class EvalConfig
{
    public int MaxParallelScenarios { get; set; }

    public int MaxParallelRuns { get; set; }
}

public sealed class EvalScenario
{
    public string? Name { get; set; }

    public string? ExpectedSkill { get; set; }

    // Scenarios flagged `held_out: true` are authored but NOT part of the shipped benchmark (e.g.
    // CT25/CT26 target a skill not on the shelf). They are excluded from the rendered eval.md so the
    // doc always reflects the benchmark count, not the full authored set.
    public bool HeldOut { get; set; }

    public string? Prompt { get; set; }

    public EvalSetup? Setup { get; set; }

    public List<string>? RejectTools { get; set; }

    public List<EvalAssertion>? Assertions { get; set; }

    public List<string>? Rubric { get; set; }

    public int? Timeout { get; set; }
}

public sealed class EvalSetup
{
    public List<EvalFile>? Files { get; set; }
}

public sealed class EvalFile
{
    public string? Path { get; set; }

    public string? Source { get; set; }
}

public sealed class EvalAssertion
{
    public string? Type { get; set; }

    public string? CommandToRun { get; set; }

    public string? CommandArguments { get; set; }

    public int? ExpectedExitCode { get; set; }

    public string? ExpectedStdOutputMatches { get; set; }

    public int? CommandTimeout { get; set; }

    public string? Path { get; set; }

    public string? Value { get; set; }
}

[MarkoutContext(typeof(EvalMarkdownDocument))]
[MarkoutContext(typeof(ScenarioDoc))]
[MarkoutContext(typeof(CheckDoc))]
public partial class EvalDocContext : MarkoutSerializerContext { }
