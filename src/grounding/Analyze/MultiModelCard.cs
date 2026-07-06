using Markout;
using static Grounding.Analyze.Metrics;

namespace Grounding.Analyze;

// The multi-model quality card as a declarative Markout model (Markout 0.17.0 multi-source rows):
// rows are metrics, columns are models. Each cell is that model's baseline → grounded Change<Shape>
// (the shape chosen by meaning, same as the single-model QualityCard); the verdict row is a
// first-class GateStatus/Verdict per model rather than a formatted string.
//
// WriteMultiSourceTable pivots the roles (models) into Markdown columns — in the caller-supplied
// order (mini-tier first, then model name) — and decomposes to flat typed {role}_{side}_{field}
// JSONL keys (e.g. claude_opus_4_8_before_value). One declaration → the dense Markdown card AND the
// decomposed typed rows, replacing the hand-rolled multi-model wide table.
public sealed class MultiModelCard
{
    [MarkoutIgnoreInTable]
    [MarkoutLabelHeader("Metric (goal)")]
    public List<MultiSourceRow> Rows { get; init; } = new();

    // models are pre-ordered (mini-tier first, then model name); Grade is FAIL | WORSE | NEUTRAL | BETTER.
    internal static MultiModelCard Build(IReadOnlyList<(string Model, ArmAgg B, ArmAgg G, string Grade)> models)
    {
        MultiSourceRow Row(string label, Func<ArmAgg, ArmAgg, IMarkoutCell> cell, MarkoutCellFormat fmt = default) =>
            new(label, models.Select(m => new Source(m.Model, cell(m.B, m.G), fmt)).ToArray());

        return new MultiModelCard
        {
            Rows =
            [
                Row("tasks correct (+)",
                    (b, g) => new Change<Fraction>(new Fraction(b.Succ, b.N), new Fraction(g.Succ, g.N))),
                Row("func passed (assertions) (+)",
                    (b, g) => new Change<Fraction>(new Fraction(b.Fp, b.Ft), new Fraction(g.Fp, g.Ft))),
                Row("tool calls: web / bash / other (context)",
                    (b, g) => new Change<Segments>(QualityCard.Split(b), QualityCard.Split(g))),
                Row("nuget archaeology: cache / nuget.org (-)",
                    (b, g) => new Change<Segments>(QualityCard.Nuget(b), QualityCard.Nuget(g))),
                Row("grounding load (tok) (context)",
                    (b, g) => new Change<int>(b.DocTok, g.DocTok)),
                Row("read grounding (%)",
                    (b, g) => new Change<Percent>(new Percent(b.Activated, 1), new Percent(g.Activated, 1))),
                Row("output tok (% of IET) (-)",
                    (b, g) => new Change<Share>(QualityCard.SharePctPublic(b.Out, b.OutIetPct), QualityCard.SharePctPublic(g.Out, g.OutIetPct))),
                Row("tool-call turns (% of total) (-)",
                    (b, g) => new Change<Share>(QualityCard.SharePctPublic(b.ToolTurns, b.ToolTurnPct), QualityCard.SharePctPublic(g.ToolTurns, g.ToolTurnPct))),
                Row("tool-turn secs (% of turn time) (-)",
                    (b, g) => new Change<Share>(QualityCard.SharePctPublic(b.ToolTurnSecs, b.ToolTurnSecsPct), QualityCard.SharePctPublic(g.ToolTurnSecs, g.ToolTurnSecsPct)),
                    new MarkoutCellFormat(Delta.None, "s")),
                Row("tool-turn IET (% of turn IET) (-)",
                    (b, g) => new Change<Percent>(new Percent(b.ToolTurnIetPct, 100), new Percent(g.ToolTurnIetPct, 100))),
                Row("Session turns (-)",
                    (b, g) => new Change<long>((long)Math.Round(b.AllTurns), (long)Math.Round(g.AllTurns))),
                Row("Session IET (-)",
                    (b, g) => new Change<long>((long)Math.Round(b.Iet), (long)Math.Round(g.Iet)),
                    new MarkoutCellFormat(Delta.Percent)),
                new("verdict", models.Select(m => new Source(m.Model, new Verdict(GateStatusOf(m.Grade), m.Grade))).ToArray()),
            ],
        };
    }

    private static GateStatus GateStatusOf(string grade) => grade switch
    {
        "BETTER" => GateStatus.Good,
        "NEUTRAL" => GateStatus.Neutral,
        "WORSE" => GateStatus.Warning,
        "FAIL" => GateStatus.Bad,
        _ => GateStatus.Unknown,
    };
}

[MarkoutContext(typeof(MultiModelCard))]
public partial class MultiModelCardContext : MarkoutSerializerContext { }
