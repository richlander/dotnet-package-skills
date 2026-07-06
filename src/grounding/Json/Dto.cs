using System.Text.Json;
using System.Text.Json.Serialization;

namespace Grounding.Json;

// DTOs mirror the skill-validator results.json schema (only the fields the
// analyzer reads). Unknown fields are ignored.

public sealed class ResultsFile
{
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("judgeModel")] public string? JudgeModel { get; set; }
    [JsonPropertyName("verdicts")] public List<Verdict>? Verdicts { get; set; }
}

public sealed class Verdict
{
    [JsonPropertyName("skillName")] public string? SkillName { get; set; }
    [JsonPropertyName("skillPath")] public string? SkillPath { get; set; }
    [JsonPropertyName("scenarios")] public List<Scenario>? Scenarios { get; set; }
}

public sealed class Scenario
{
    [JsonPropertyName("scenarioName")] public string? ScenarioName { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("improvementScore")] public double? ImprovementScore { get; set; }
    [JsonPropertyName("baseline")] public Arm? Baseline { get; set; }
    [JsonPropertyName("skilledIsolated")] public Arm? SkilledIsolated { get; set; }
    [JsonPropertyName("skilledPlugin")] public Arm? SkilledPlugin { get; set; }
    [JsonPropertyName("skillActivationIsolated")] public SkillActivation? SkillActivationIsolated { get; set; }
    [JsonPropertyName("skillActivationPlugin")] public SkillActivation? SkillActivationPlugin { get; set; }
}

public sealed class SkillActivation
{
    [JsonPropertyName("activated")] public bool Activated { get; set; }
}

public sealed class Arm
{
    [JsonPropertyName("metrics")] public Metrics? Metrics { get; set; }
    [JsonPropertyName("judgeResult")] public JudgeResult? JudgeResult { get; set; }
}

public sealed class JudgeResult
{
    [JsonPropertyName("overallScore")] public double? OverallScore { get; set; }
}

public sealed class Metrics
{
    [JsonPropertyName("inputTokens")] public long InputTokens { get; set; }
    [JsonPropertyName("outputTokens")] public long OutputTokens { get; set; }
    [JsonPropertyName("cacheReadTokens")] public long CacheReadTokens { get; set; }
    [JsonPropertyName("cacheWriteTokens")] public long CacheWriteTokens { get; set; }
    [JsonPropertyName("cost")] public JsonElement CostElement { get; set; }
    // Preserve the JSON token type: Python renders int `31` as "31" but float `1.76`
    // as "1.8" in the raw table. Math/aggregates use the double value uniformly.
    [JsonIgnore] public double Cost =>
        CostElement.ValueKind == JsonValueKind.Number ? CostElement.GetDouble() : 0.0;
    [JsonIgnore] public bool CostIsInteger
    {
        get
        {
            if (CostElement.ValueKind != JsonValueKind.Number) return false;
            var t = CostElement.GetRawText();
            return t.IndexOf('.') < 0 && t.IndexOf('e') < 0 && t.IndexOf('E') < 0;
        }
    }
    [JsonPropertyName("wallTimeMs")] public double WallTimeMs { get; set; }
    [JsonPropertyName("toolCallCount")] public double? ToolCallCount { get; set; }
    [JsonPropertyName("turnCount")] public int? TurnCount { get; set; }
    [JsonPropertyName("tokenEstimate")] public long? TokenEstimate { get; set; }
    [JsonPropertyName("taskCompleted")] public bool TaskCompleted { get; set; }
    [JsonPropertyName("toolCallBreakdown")] public Dictionary<string, int>? ToolCallBreakdown { get; set; }
    [JsonPropertyName("assertionResults")] public List<AssertionResult>? AssertionResults { get; set; }
    [JsonPropertyName("events")] public List<EventRecord>? Events { get; set; }
    // Authoritative per-run-AVERAGED tool-call/tool-turn stats, injected by `grounding enrich`
    // (read from sessions.db across ALL runs). Present => the analyzer uses these instead of the
    // single-run `events`/`toolCallBreakdown`, which skill-validator collapses inconsistently for
    // runs>1 (breakdown=run[0], events=run[last]). Absent (old datasets) => fall back to events.
    [JsonPropertyName("toolStats")] public ToolStats? ToolStats { get; set; }
}

// Per-run means across all runs of a (scenario, arm). Counts are event-derived and consistent.
public sealed class ToolStats
{
    [JsonPropertyName("runs")] public int Runs { get; set; }
    [JsonPropertyName("web")] public double Web { get; set; }
    [JsonPropertyName("bash")] public double Bash { get; set; }
    [JsonPropertyName("other")] public double Other { get; set; }
    [JsonPropertyName("tools")] public double Tools { get; set; }
    [JsonPropertyName("nugetCache")] public double NugetCache { get; set; }
    [JsonPropertyName("nugetWeb")] public double NugetWeb { get; set; }
    [JsonPropertyName("di")] public double Di { get; set; }
    [JsonPropertyName("mcp")] public double Mcp { get; set; }
    [JsonPropertyName("toolTurnSecs")] public double ToolTurnSecs { get; set; }
    [JsonPropertyName("allTurnSecs")] public double AllTurnSecs { get; set; }
    [JsonPropertyName("toolTurnIet")] public double ToolTurnIet { get; set; }
    [JsonPropertyName("allTurnIet")] public double AllTurnIet { get; set; }
    [JsonPropertyName("toolTurns")] public double ToolTurns { get; set; }
    [JsonPropertyName("allTurns")] public double AllTurns { get; set; }
}

public sealed class AssertionResult
{
    [JsonPropertyName("assertion")] public Assertion? Assertion { get; set; }
    [JsonPropertyName("passed")] public bool Passed { get; set; }
}

public sealed class Assertion
{
    [JsonPropertyName("type")] public int Type { get; set; }
    // The assertion's semantic target, used to match it to AGENTS.md content:
    //   type 2  (file_contains)  -> value = the required API identifier (e.g. "Metric").
    //   type 11 (reject_tools)   -> value = the tool name rejected (archaeology guard).
    //   type 9  (run_command)    -> commandArgs.expectedStdOutputMatches = the required output.
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("commandArgs")] public CommandArgs? CommandArgs { get; set; }
}

public sealed class CommandArgs
{
    [JsonPropertyName("commandToRun")] public string? CommandToRun { get; set; }
    [JsonPropertyName("commandArguments")] public string? CommandArguments { get; set; }
    [JsonPropertyName("expectedStdOutputMatches")] public string? ExpectedStdOutputMatches { get; set; }
}

public sealed class EventRecord
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("timestamp")] public long Timestamp { get; set; }
    [JsonPropertyName("data")] public EventData? Data { get; set; }
}

public sealed class EventData
{
    [JsonPropertyName("toolName")] public string? ToolName { get; set; }
    [JsonPropertyName("arguments")] public string? Arguments { get; set; }
    [JsonPropertyName("inputTokens")] public long? InputTokens { get; set; }
    [JsonPropertyName("cacheReadTokens")] public long? CacheReadTokens { get; set; }
    [JsonPropertyName("cacheWriteTokens")] public long? CacheWriteTokens { get; set; }
    [JsonPropertyName("outputTokens")] public long? OutputTokens { get; set; }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ResultsFile))]
[JsonSerializable(typeof(Metrics))]
[JsonSerializable(typeof(ToolStats))]
public sealed partial class GroundingJsonContext : JsonSerializerContext;
