using System.Globalization;
using System.Text;
using Grounding.Json;
using static Grounding.Analyze.Metrics;

namespace Grounding.Analyze;

// Port of analyze-6q.py renderers. Output is matched byte-for-byte.
internal sealed class Cards
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly TextWriter _o = Console.Out;
    public bool NoTitle;

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

    private static readonly (string Label, Func<ArmAgg, string> Raw, Func<ArmAgg, ArmAgg, string> Diff)[] Spec =
    {
        ("success (scenarios)",       RawSuccess, DiffSuccess),
        ("func passed (assertions)",  RawFunc,    DiffFunc),
        ("resourcefulness (archaeology)", RawArch, DiffArch),
        ("IET",                       RawIet,     DiffIet),
        ("output tok",                RawOut,     DiffOut),
        ("cost",                      RawCost,    DiffCost),
    };

    // ---- grading (Python _grade) -----------------------------------------

    private static string Grade(ArmAgg b, ArmAgg g)
    {
        var iet = Pct(g.Iet, b.Iet);
        var cost = Pct(g.Cost, b.Cost);
        var @out = Pct(g.Out, b.Out);
        var dsucc = g.Succ - b.Succ;
        int bArch = b.Arch, gArch = g.Arch;
        var tail = $"success {g.Succ}/{g.N} vs {b.Succ}/{b.N}, "
                 + $"resourcefulness {bArch}\u2192{gArch}, IET {SignedPct(iet)}, cost {SignedPct(cost)}";

        var worse = new List<string>();
        if (dsucc < 0) worse.Add($"success {SignedInt(dsucc)}");
        if (g.Web > 0) worse.Add($"web archaeology {g.Web}");
        if (iet > IetHarmCapFrac * 100) worse.Add($"IET +{F0(iet)}%");
        if (cost > CostHarmCapFrac * 100) worse.Add($"cost +{F0(cost)}%");
        if (@out > OutInflateFrac * 100) worse.Add($"output +{F0(@out)}%");
        if (worse.Count > 0)
            return $"**WORSE** — {string.Join(", ", worse)} ({tail})";

        if (dsucc > 0 || -iet >= IetWinFrac * 100 || -cost >= CostWinFrac * 100 || (bArch > 0 && gArch == 0))
            return $"**BETTER** — {tail}";

        return $"**NEUTRAL** — no material change ({tail})";
    }

    // ---- cards ------------------------------------------------------------

    public void Primary(string path)
    {
        var a = Loader.LoadArm(path);
        var b = a.Agg["baseline"];
        var g = a.Agg["skilledPlugin"];
        var gtok = Loader.GroundingTokens(a.SkillName);
        if (!NoTitle)
            _o.WriteLine($"### Grounding eval — {a.SkillName} · `{a.Model}`\n");
        var mpref = NoTitle ? $"`{a.Model}` · " : "";
        var tokNote = gtok is { } t ? $" (~{t} tok, via grounding tool)" : "";
        _o.WriteLine($"_{mpref}Baseline (no grounding) vs `AGENTS.md`{tokNote}. Judge `{a.Judge}`. Means across scenarios._\n");
        _o.WriteLine("| Metric | Baseline | AGENTS.md |");
        _o.WriteLine("| --- | ---: | ---: |");
        foreach (var (label, raw, _) in Spec)
            _o.WriteLine($"| {label} | {raw(b)} | {raw(g)} |");
        _o.WriteLine($"\n> **Conclusion:** {Grade(b, g)}.");
    }

    public void Card(IReadOnlyList<string> files)
    {
        var arms = files.Select(Loader.LoadArm).ToList();
        var modelFiles = arms.Where(a => !a.IsReadme).Select(a => a.Path).ToList();
        if (modelFiles.Count == 0)
        {
            _o.WriteLine("--card needs at least one AGENTS.md dataset (non-'readme' path).");
            return;
        }
        for (var i = 0; i < modelFiles.Count; i++)
        {
            if (i > 0) _o.WriteLine();
            Primary(modelFiles[i]);
        }
        _o.WriteLine("\n**Legend & grades** — full term table at "
            + "[grounding-lifecycle.md §4](https://github.com/richlander/dotnet-package-grounding/blob/main/docs/grounding-lifecycle.md#4-evaluate--the-three-cards). "
            + "In short, each metric is read **per arm in isolation** (no judge-quality diff): "
            + "_success_ = func assertions pass **and** judge ≥4 floor (higher better); "
            + "_resourcefulness (archaeology)_ = web+cache lookups to recover the API — grounding drives it to 0, so lower is the win, and grounded **web** must be 0; "
            + "_IET / output tok / cost_ = token/spend cost (lower better). "
            + "**Conclusion:** **BETTER** = success held + a real win (more solved, resourcefulness eliminated, or IET/cost cut), "
            + "**NEUTRAL** = success held with no material win, **WORSE** = success dropped, grounded web archaeology, or cost/IET/output inflated past the cap.\n");
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
            _o.WriteLine($"### Model-diff — {sn} · AGENTS.md lift over baseline\n");
        _o.WriteLine("_Each cell = grounded (AGENTS.md) change vs that model's own baseline. "
            + "count Δ for success/func, before→after for resourcefulness, "
            + "% for IET/output/cost (− = cheaper)._\n");
        _o.WriteLine("| Metric | " + string.Join(" | ", arms.Select(a => $"`{a.Model}`")) + " |");
        _o.WriteLine("| --- |" + string.Concat(Enumerable.Repeat(" ---: |", arms.Count)));
        foreach (var (label, _, diff) in Spec)
        {
            var cells = arms.Select(a => diff(a.Agg["skilledPlugin"], a.Agg["baseline"]));
            _o.WriteLine($"| {label} | " + string.Join(" | ", cells) + " |");
        }
        var verdicts = arms.Select(a => Grade(a.Agg["baseline"], a.Agg["skilledPlugin"]));
        _o.WriteLine("| **→ verdict** | " + string.Join(" | ", verdicts) + " |");
    }

    public void SourceDiff(IReadOnlyList<string> files)
    {
        var arms = files.Select(Loader.LoadArm).ToList();
        var agents = arms.FirstOrDefault(a => !a.IsReadme);
        var readme = arms.FirstOrDefault(a => a.IsReadme);
        if (agents is null || readme is null)
        {
            _o.WriteLine("source-diff needs one AGENTS.md dataset and one README dataset "
                + "(a path containing 'readme').");
            return;
        }
        var ag = agents.Agg["skilledPlugin"];
        var rd = readme.Agg["skilledPlugin"];
        var sn = agents.SkillName;
        if (!NoTitle)
            _o.WriteLine($"### Source-diff — {sn} · `{agents.Model}` · AGENTS.md benefit over README.md\n");
        var mpref = NoTitle ? $"`{agents.Model}` · " : "";
        _o.WriteLine($"_{mpref}Both surfaced via the grounding tool; baseline removed. Single column = "
            + "AGENTS.md change vs README.md (− = AGENTS cheaper on cost metrics, "
            + "+ on success/func, lower resourcefulness = AGENTS more self-sufficient). "
            + "The README is co-tested here as a usability artifact._\n");
        _o.WriteLine("| Metric | AGENTS.md − README.md |");
        _o.WriteLine("| --- | ---: |");
        foreach (var (label, _, diff) in Spec)
            _o.WriteLine($"| {label} | {diff(ag, rd)} |");
        _o.WriteLine($"\n> **Conclusion:** {Grade(rd, ag)} _(README arm is co-tested for usability, not a floor to beat)._");
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
                var gtok = Loader.GroundingTokens(sn);
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
        sb.Append(PadL(Str(r.Turns), 4)).Append(" | ");
        sb.Append(PadL(r.Di.ToString(Inv), 2)).Append(" | ");
        sb.Append(PadL(r.Mcp.ToString(Inv), 3)).Append(" | ");
        sb.Append(PadL(r.Cache.ToString(Inv), 5)).Append(" | ");
        sb.Append(PadL(r.Bash.ToString(Inv), 4));
        return sb.ToString();
    }

    private static string Str(int? v) => v?.ToString(Inv) ?? "?";
    private static string Pad(string s, int w) => s.Length >= w ? s : s + new string(' ', w - s.Length);
    private static string PadL(string s, int w) => s.Length >= w ? s : new string(' ', w - s.Length) + s;
}
