using System.Globalization;
using System.Text;
using Grounding.Json;
using static Grounding.Analyze.Metrics;

namespace Grounding.Analyze;

// Port of the original analyze renderers. Output is matched byte-for-byte.
internal sealed partial class Cards
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly TextWriter _o = Console.Out;
    public bool NoTitle;

    // The content arm to grade. The clean content measure is skilledIsolated (ONLY the
    // target skill loaded). skilledPlugin loads every skill on the shelf, so for units
    // that share the grounding dir (e.g. markout alongside broadskill + prefer-dotnet-
    // inspect) it is CONTAMINATED and flatters the result. Override via GROUNDING_CARD_ARM.
    private static readonly string Arm =
        Environment.GetEnvironmentVariable("GROUNDING_CARD_ARM") is { Length: > 0 } v ? v : "skilledIsolated";

    // ---- shared headline-metric spec (Python _METRICS) -------------------

    private static string RawSuccess(ArmAgg a) => $"{a.Succ}/{a.N}";
    private static string RawFunc(ArmAgg a) => $"{a.Fp}/{a.Ft}";
    private static string RawArch(ArmAgg a) => a.Arch.ToString(Inv);
    private static string RawIet(ArmAgg a) => F0(a.Iet);
    private static string RawOut(ArmAgg a) => F0(a.Out);
    private static string RawCost(ArmAgg a) => F2(a.Cost);

    private static string DiffSuccess(ArmAgg n, ArmAgg o) => $"{SignedInt(n.Succ - o.Succ)} ({n.Succ}/{n.N})";
    private static string DiffFunc(ArmAgg n, ArmAgg o) => $"{SignedInt(n.Fp - o.Fp)} ({n.Fp}/{n.Ft})";
    private static string DiffArch(ArmAgg n, ArmAgg o) => $"{o.Arch}\u2192{n.Arch}";
    private static string DiffIet(ArmAgg n, ArmAgg o) => SignedPct(Pct(n.Iet, o.Iet));
    private static string DiffOut(ArmAgg n, ArmAgg o) => SignedPct(Pct(n.Out, o.Out));
    private static string DiffCost(ArmAgg n, ArmAgg o) => SignedPct(Pct(n.Cost, o.Cost));

    // Document cost (the grounding doc loaded into the arm; baseline = 0) and the
    // doc-excluded "work" IET, so a bigger doc isn't charged as agent effort.
    private static string RawDoc(ArmAgg a) => a.DocTok.ToString(Inv);
    private static string RawWorkIet(ArmAgg a) => F0(a.Iet - a.DocTok);
    private static string DiffDoc(ArmAgg n, ArmAgg o) => $"{o.DocTok}\u2192{n.DocTok}";
    private static string DiffWorkIet(ArmAgg n, ArmAgg o) => SignedPct(Pct(n.Iet - n.DocTok, o.Iet - o.DocTok));

    private static readonly (string Label, Func<ArmAgg, string> Raw, Func<ArmAgg, ArmAgg, string> Diff)[] Spec =
    {
        ("tasks correct (+)",                  RawSuccess, DiffSuccess),
        ("func passed (assertions) (+)",       RawFunc,    DiffFunc),
        ("resourcefulness (archaeology) (-)",  RawArch,    DiffArch),
        ("grounding load (tok) (context)",     RawDoc,     DiffDoc),
        ("work IET (iet - doc) (-)",           RawWorkIet, DiffWorkIet),
        ("output tok (-)",                     RawOut,     DiffOut),
        ("cost (-)",                           RawCost,    DiffCost),
    };

    // ---- grading (Python _grade) -----------------------------------------

    // Verdict model: FAIL is the only correctness gate (grounding made the model answer
    // fewer scenarios correctly). The rest — archaeology, web, IET, output, cost, judge —
    // are SIGNALS that rank BETTER / NEUTRAL / WORSE; none of them flips the verdict alone.
    private static string Grade(ArmAgg b, ArmAgg g)
    {
        var iet = Pct(g.Iet - g.DocTok, b.Iet - b.DocTok);   // work IET (document netted out)
        var cost = Pct(g.Cost, b.Cost);
        var @out = Pct(g.Out, b.Out);
        var dsucc = g.Succ - b.Succ;
        int bArch = b.Arch, gArch = g.Arch;
        var tail = $"tasks correct {g.Succ}/{g.N} vs {b.Succ}/{b.N}, "
                 + $"resourcefulness {bArch}\u2192{gArch}, work-IET {SignedPct(iet)}, cost {SignedPct(cost)}";

        // FAIL: grounding regressed correctness — fewer scenarios answered correctly.
        if (dsucc < 0)
            return $"**FAIL** — fewer tasks correct ({tail})";

        // WORSE: real cost/IET/output inflation (a harm signal), not a stray web call.
        var worse = new List<string>();
        if (iet > IetHarmCapFrac * 100) worse.Add($"IET +{F0(iet)}%");
        if (cost > CostHarmCapFrac * 100) worse.Add($"cost +{F0(cost)}%");
        if (@out > OutInflateFrac * 100) worse.Add($"output +{F0(@out)}%");
        if (worse.Count > 0)
            return $"**WORSE** — {string.Join(", ", worse)} ({tail})";

        // BETTER: solved more, eliminated archaeology, or materially cheaper.
        if (dsucc > 0 || -iet >= IetWinFrac * 100 || -cost >= CostWinFrac * 100 || (bArch > 0 && gArch == 0))
            return $"**BETTER** — {tail}";

        return $"**NEUTRAL** — no material change ({tail})";
    }

    private static string GradeLabel(ArmAgg b, ArmAgg g)
    {
        var v = Grade(b, g);
        var i = v.IndexOf("**", 2, StringComparison.Ordinal);
        return v[2..i]; // FAIL | WORSE | NEUTRAL | BETTER
    }

    // ---- cards ------------------------------------------------------------

    public void Primary(string path)
    {
        var a = Loader.LoadArm(path);
        var b = a.Agg["baseline"];
        var g = a.Agg[Arm];
        var gtok = Loader.GroundingTokens(a.SkillPath, a.SkillName);
        if (!NoTitle)
            _o.WriteLine($"### Grounding eval — {a.SkillName} | `{a.Model}`\n");
        var mpref = NoTitle ? $"`{a.Model}` | " : "";
        var tokNote = gtok is { } t ? $" (~{t} tok, via grounding tool)" : "";
        _o.WriteLine($"_{mpref}Baseline (no grounding) vs `AGENTS.md`{tokNote}. Judge `{a.Judge}`. Means across scenarios._\n");
        _o.WriteLine("| Metric (goal) | Baseline | AGENTS.md |");
        _o.WriteLine("| --- | ---: | ---: |");
        foreach (var (label, raw, _) in Spec)
            _o.WriteLine($"| {label} | {raw(b)} | {raw(g)} |");
        _o.WriteLine($"\n> **Conclusion:** {Grade(b, g)}.");
    }

    public void Card(IReadOnlyList<string> files)
    {
        var arms = files.Select(Loader.LoadArm).Where(a => !a.IsReadme)
            .OrderBy(a => a.Tier == "mini" ? 0 : 1).ThenBy(a => a.Model, StringComparer.Ordinal).ToList();
        if (arms.Count == 0)
        {
            _o.WriteLine("--card needs at least one AGENTS.md dataset (non-'readme' path)."); return;
        }
        var sn = arms[0].SkillName;
        var gtok = Loader.GroundingTokens(arms[0].SkillPath, sn);
        var tokNote = gtok is { } t ? $" (~{t} tok)" : "";
        if (!NoTitle) _o.WriteLine($"### Grounding eval — {sn}\n");
        _o.WriteLine($"_Each cell: baseline (no grounding) → `AGENTS.md`{tokNote}. Columns are models. Judge `{arms[0].Judge}`. Means across scenarios._\n");
        _o.WriteLine("| Metric (goal) | " + string.Join(" | ", arms.Select(a => $"`{a.Model}`")) + " |");
        _o.WriteLine("| --- |" + string.Concat(Enumerable.Repeat(" ---: |", arms.Count)));
        foreach (var (label, raw, _) in Spec)
            _o.WriteLine($"| {label} | " + string.Join(" | ", arms.Select(a => $"{raw(a.Agg["baseline"])} → {raw(a.Agg[Arm])}")) + " |");
        _o.WriteLine("| **verdict** | " + string.Join(" | ", arms.Select(a => $"**{GradeLabel(a.Agg["baseline"], a.Agg[Arm])}**")) + " |");
        _o.WriteLine("\n_**FAIL** = fewer tasks correct; **BETTER** = more tasks correct / archaeology→0 / IET/cost cut ≥20%; "
            + "**WORSE** = IET/cost/output inflated ≥20%; **NEUTRAL** = held. Archaeology, web, judge are signals, not gates._\n");
        _o.WriteLine("> Note: even ungrounded, the baseline self-grounds from the restored NuGet cache "
            + "(README/AGENTS are packed in the nupkg) and the open web — so its resourcefulness count is a "
            + "**lower bound** and grounding's advantage is understated.\n");
    }

    public void ModelDiff(IReadOnlyList<string> files)
    {
        var arms = files.Select(Loader.LoadArm).Where(a => !a.IsReadme).ToList();
        if (arms.Count == 0) { _o.WriteLine("model-diff needs at least one non-'readme' dataset."); return; }
        arms = arms
            .OrderBy(a => a.Tier == "mini" ? 0 : 1)
            .ThenBy(a => a.Model, StringComparer.Ordinal)
            .ToList();
        var sn = arms[0].SkillName;
        if (!NoTitle)
            _o.WriteLine($"### Model-diff — {sn} | AGENTS.md lift over baseline\n");
        _o.WriteLine($"_Each cell: `AGENTS.md` change vs that model's own baseline (count Δ; before→after for archaeology; % for IET/output/cost, − = cheaper). Columns are models. Judge `{arms[0].Judge}`. Means across scenarios._\n");
        _o.WriteLine("| Metric (goal) | " + string.Join(" | ", arms.Select(a => $"`{a.Model}`")) + " |");
        _o.WriteLine("| --- |" + string.Concat(Enumerable.Repeat(" ---: |", arms.Count)));
        foreach (var (label, _, diff) in Spec)
        {
            var cells = arms.Select(a => diff(a.Agg[Arm], a.Agg["baseline"]));
            _o.WriteLine($"| {label} | " + string.Join(" | ", cells) + " |");
        }
        var verdicts = arms.Select(a => $"**{GradeLabel(a.Agg["baseline"], a.Agg[Arm])}**");
        _o.WriteLine("| **verdict** | " + string.Join(" | ", verdicts) + " |");
    }

    public void SourceDiff(IReadOnlyList<string> files)
    {
        var loaded = files.Select(Loader.LoadArm).ToList();
        // Pair AGENTS + README datasets by model; columns are models (mini first).
        var models = loaded.GroupBy(a => a.Model)
            .Select(g => (Model: g.Key,
                          Agents: g.FirstOrDefault(a => !a.IsReadme),
                          Readme: g.FirstOrDefault(a => a.IsReadme)))
            .Where(x => x.Agents is not null && x.Readme is not null)
            .OrderBy(x => x.Agents!.Tier == "mini" ? 0 : 1).ThenBy(x => x.Model, StringComparer.Ordinal)
            .ToList();
        if (models.Count == 0)
        {
            _o.WriteLine("source-diff needs an AGENTS.md + README dataset per model (a path containing 'readme').");
            return;
        }
        var sn = models[0].Agents!.SkillName;
        if (!NoTitle) _o.WriteLine($"### Comparison to README.md — {sn}\n");
        _o.WriteLine($"_Each cell: `AGENTS.md` − `README.md`, both via the grounding tool, baseline removed (− = AGENTS cheaper; + on success/func; lower archaeology = AGENTS more self-sufficient). Columns are models. Judge `{models[0].Agents!.Judge}`. Means across scenarios._\n");
        _o.WriteLine("| Metric (goal) | " + string.Join(" | ", models.Select(m => $"`{m.Model}`")) + " |");
        _o.WriteLine("| --- |" + string.Concat(Enumerable.Repeat(" ---: |", models.Count)));
        foreach (var (label, _, diff) in Spec)
            _o.WriteLine($"| {label} | " + string.Join(" | ", models.Select(m => diff(m.Agents!.Agg[Arm], m.Readme!.Agg[Arm]))) + " |");
        _o.WriteLine("| **verdict** | " + string.Join(" | ", models.Select(m => $"**{GradeLabel(m.Readme!.Agg[Arm], m.Agents!.Agg[Arm])}**")) + " |");
    }

    // ---- raw per-scenario table (Python main) ----------------------------

    private const string Hdr =
        "scenario                     | arm      | qual | func |     tok |    iet | cost | secs \u2016 web | tools | turn | di | mcp | cache | bash";
    private const string Grp =
        "                                                 <<<<<<<<<< NORMATIVE METRICS         \u2016 INFORMATIVE SIGNALS >>>>>>>>>>";

    public void Table(IReadOnlyList<string> files)
    {
        foreach (var f in files.Distinct().OrderBy(x => x, StringComparer.Ordinal))
        {
            ResultsFile d;
            try { d = Loader.Parse(f); }
            catch (Exception e) { _o.WriteLine($"!! {f}: {e.Message}"); continue; }
            foreach (var v in d.Verdicts ?? new())
            {
                var sn = v.SkillName ?? "?";
                var gtok = Loader.GroundingTokens(v.SkillPath, sn);
                var gnote = gtok is { } t ? $"   grounding=~{t} tok (loaded into each grounded arm)" : "";
                _o.WriteLine($"\n===== {sn}   ({f})   model={d.Model}{gnote} =====");
                _o.WriteLine(Grp);
                _o.WriteLine(Hdr);
                _o.WriteLine(new string('-', Hdr.Length));
                foreach (var sc in v.Scenarios ?? new())
                {
                    var name = (sc.ScenarioName ?? "").Split(':')[0];
                    foreach (var (key, label) in Metrics.Arms)
                    {
                        var r = Loader.Row(Loader.ArmOf(sc, key));
                        if (r is null) continue;
                        _o.WriteLine(TableRow(name, label, r));
                    }
                    _o.WriteLine(new string('-', Hdr.Length));
                }
            }
        }
    }

    private static string TableRow(string name, string arm, ArmRow r)
    {
        var qual = r.Qual is { } q ? q.ToString("0.#", Inv) : "-";
        var web = $"{r.Web}{(r.WebUsed ? "Y" : ".")}";
        var sb = new StringBuilder();
        sb.Append(Pad(name, 28)).Append(" | ");
        sb.Append(Pad(arm, 8)).Append(" | ");
        sb.Append(PadL(qual, 4)).Append(" | ");
        sb.Append(PadL(r.Fp + "/" + r.Ft, 4)).Append(" | ");
        sb.Append(PadL(r.Tok.ToString(Inv), 7)).Append(" | ");
        sb.Append(PadL(r.Iet.ToString(Inv), 6)).Append(" | ");
        sb.Append(PadL(r.CostDisplay, 4)).Append(" | ");
        sb.Append(PadL(r.Secs.ToString(Inv), 4)).Append(" \u2016 ");
        sb.Append(PadL(web, 3)).Append(" | ");
        sb.Append(PadL(Str(r.Tools), 5)).Append(" | ");
        sb.Append(PadL(Str(r.Turns), 4)).Append(" | ");        sb.Append(PadL(r.Di.ToString(Inv), 2)).Append(" | ");
        sb.Append(PadL(r.Mcp.ToString(Inv), 3)).Append(" | ");
        sb.Append(PadL(r.Cache.ToString(Inv), 5)).Append(" | ");
        sb.Append(PadL(r.Bash.ToString(Inv), 4));
        return sb.ToString();
    }

    private static string Str(int? v) => v?.ToString(Inv) ?? "?";
    private static string Str(double? v) => v?.ToString(Inv) ?? "?";
    private static string Pad(string s, int w) => s.Length >= w ? s : s + new string(' ', w - s.Length);
    private static string PadL(string s, int w) => s.Length >= w ? s : new string(' ', w - s.Length) + s;
}
