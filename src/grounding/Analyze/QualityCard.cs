using Markout;

namespace Grounding.Analyze;

// The grounding quality card as a single declarative model of Markout composite cells.
// One declaration renders BOTH the dense Markdown card (MarkdownFormatter) and the decomposed,
// typed JSONL rows (TableFormatter) — the multi-format promise the shapes were designed for.
//
// Each property is a `before → after` Change over a shape chosen by MEANING:
//   Fraction  count/total  (24/24)              — a single derivable ratio
//   Segments  slash-joined (21/171/236)         — independent parts, no shared denominator
//   Share     value (pct%) (5056 (24%))         — a value and its share of a whole
//   Percent   pct only     (93%)                — a bare percentage
//   scalar    long/int     (98555, +[MarkoutDelta] → "→ 61190 (−38%)")
//
// The goal polarity ((+)/(-)/context) rides in the property name, matching the existing card.
public sealed class QualityCard
{
    [MarkoutPropertyName("tasks correct (+)")]
    [MarkoutGoal(Goal.Higher)]
    public Change<Fraction> TasksCorrect { get; init; }

    [MarkoutPropertyName("func passed (assertions) (+)")]
    [MarkoutGoal(Goal.Higher)]
    public Change<Fraction> FuncPassed { get; init; }

    [MarkoutPropertyName("tool calls: web / bash / other (context)")]
    [MarkoutGoal(Goal.Context)]
    public Change<Segments> ToolCalls { get; init; }

    [MarkoutPropertyName("nuget archaeology: cache / nuget.org (-)")]
    [MarkoutGoal(Goal.Lower)]
    public Change<Segments> NugetArchaeology { get; init; }

    [MarkoutPropertyName("grounding load (tok) (context)")]
    [MarkoutGoal(Goal.Context)]
    public Change<int> GroundingLoad { get; init; }

    [MarkoutPropertyName("read grounding (%)")]
    [MarkoutGoal(Goal.Context)]
    public Change<Percent> ReadGrounding { get; init; }

    [MarkoutPropertyName("output tok (% of IET) (-)")]
    [MarkoutGoal(Goal.Lower)]
    public Change<Share> OutputTok { get; init; }

    [MarkoutPropertyName("tool-call turns (% of total) (-)")]
    [MarkoutGoal(Goal.Lower)]
    public Change<Share> ToolCallTurns { get; init; }

    [MarkoutPropertyName("tool-turn secs (% of turn time) (-)"), MarkoutUnit("s")]
    [MarkoutGoal(Goal.Lower)]
    public Change<Share> ToolTurnSecs { get; init; }

    [MarkoutPropertyName("tool-turn IET (% of turn IET) (-)")]
    [MarkoutGoal(Goal.Lower)]
    public Change<Percent> ToolTurnIet { get; init; }

    [MarkoutPropertyName("Session turns (-)")]
    [MarkoutGoal(Goal.Lower)]
    public Change<long> SessionTurns { get; init; }

    [MarkoutPropertyName("Session IET (-)"), MarkoutDelta(Delta.Percent)]
    [MarkoutGoal(Goal.Lower)]
    public Change<long> SessionIet { get; init; }

    [MarkoutPropertyName("verdict")]
    public string Verdict { get; init; } = "";

    // Build the card for one model from its baseline and grounded aggregates.
    internal static QualityCard Build(ArmAgg b, ArmAgg g, IetScheme iet, string verdict) => new()
    {
        TasksCorrect = new(new Fraction(b.Succ, b.N), new Fraction(g.Succ, g.N)),
        FuncPassed = new(new Fraction(b.Fp, b.Ft), new Fraction(g.Fp, g.Ft)),
        ToolCalls = new(Split(b), Split(g)),
        NugetArchaeology = new(Nuget(b), Nuget(g)),
        GroundingLoad = new(b.DocTok, g.DocTok),
        // Activation rate is already a per-scenario mean of {0,1}: part/whole=Activated/1 → 0%..100%.
        ReadGrounding = new(new Percent(b.Activated, 1), new Percent(g.Activated, 1)),
        // The displayed % is output's weighted share of IET (a stored mean). Carry it via a whole
        // that reproduces it exactly; Share decomposes to value+pct only, so the whole never leaks.
        OutputTok = new(SharePct(b.Out, b.OutIetPct), SharePct(g.Out, g.OutIetPct)),
        ToolCallTurns = new(SharePct(b.ToolTurns, b.ToolTurnPct), SharePct(g.ToolTurns, g.ToolTurnPct)),
        ToolTurnSecs = new(SharePct(b.ToolTurnSecs, b.ToolTurnSecsPct), SharePct(g.ToolTurnSecs, g.ToolTurnSecsPct)),
        ToolTurnIet = new(new Percent(b.ToolTurnIetPct, 100), new Percent(g.ToolTurnIetPct, 100)),
        SessionTurns = new((long)Math.Round(b.AllTurns), (long)Math.Round(g.AllTurns)),
        SessionIet = new((long)Math.Round(b.Iet), (long)Math.Round(g.Iet)),
        Verdict = verdict,
    };

    // Tool-call composition, rounded to whole calls to match the card's integer display.
    internal static Segments Split(ArmAgg a) => new(
        new Segment("web", Math.Round(a.Web)),
        new Segment("bash", Math.Round(a.Bash)),
        new Segment("other", Math.Round(a.Other)));

    internal static Segments Nuget(ArmAgg a) => new(
        new Segment("cache", Math.Round(a.Cache)),
        new Segment("nuget.org", Math.Round(a.NugetWeb)));

    // A value shown with a precomputed percentage, carried via a `Whole` chosen so
    // shown/whole*100 == pct (the whole is never emitted — Share decomposes to value+pct).
    //
    // Share derives its percent from the SHOWN value, so a shown value of 0 can only render 0%
    // (the stored pct is unrepresentable there) — give it a non-zero whole so it prints "0 (0%)"
    // rather than the em-dash placeholder a zero whole would produce. This degenerate case
    // (a per-scenario mean that rounds to 0 while its pct stays positive) does not occur in
    // current datasets, but the guard keeps the cell honest if it ever does.
    private static Share SharePct(double value, double pct)
    {
        var shown = Math.Round(value);
        if (shown == 0)
            return new Share(0, 1);            // 0 (0%)
        return new Share(shown, pct == 0 ? 0 : shown * 100.0 / pct);
    }

    internal static Share SharePctPublic(double value, double pct) => SharePct(value, pct);
}

[MarkoutContext(typeof(QualityCard))]
public partial class QualityCardContext : MarkoutSerializerContext { }
