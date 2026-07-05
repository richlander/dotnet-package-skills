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
    private static string RawCache(ArmAgg a) => $"{a.Cache} / {a.NugetWeb}";
    private static string RawToolSplit(ArmAgg a) => $"{a.Web}/{a.Bash}/{a.Other}";
    private static string RawIet(ArmAgg a) => F0(a.Iet);
    private static string RawSessionTurns(ArmAgg a) => F0(a.AllTurns);
    private static string RawOut(ArmAgg a) => $"{F0(a.Out)} ({F0(a.OutIetPct)}%)";
    private static string RawReadGrounding(ArmAgg a) => $"{F0(a.Activated * 100)}%";
    private static string RawToolTurnSecs(ArmAgg a) => $"{F0(a.ToolTurnSecs)}s ({F0(a.ToolTurnSecsPct)}%)";
    private static string RawToolTurnIet(ArmAgg a) => $"{F0(a.ToolTurnIetPct)}%";
    private static string RawToolCallTurns(ArmAgg a) => $"{F0(a.ToolTurns)} ({F0(a.ToolTurnPct)}%)";

    private static string DiffSuccess(ArmAgg n, ArmAgg o) => $"{SignedInt(n.Succ - o.Succ)} ({n.Succ}/{n.N})";
    private static string DiffFunc(ArmAgg n, ArmAgg o) => $"{SignedInt(n.Fp - o.Fp)} ({n.Fp}/{n.Ft})";
    private static string DiffCache(ArmAgg n, ArmAgg o) => $"{o.Cache}/{o.NugetWeb}\u2192{n.Cache}/{n.NugetWeb}";
    private static string DiffToolSplit(ArmAgg n, ArmAgg o) =>
        $"{o.Web}/{o.Bash}/{o.Other}\u2192{n.Web}/{n.Bash}/{n.Other}";
    private static string DiffIet(ArmAgg n, ArmAgg o) => SignedPct(Pct(n.Iet, o.Iet));
    private static string DiffSessionTurns(ArmAgg n, ArmAgg o) => $"{F0(o.AllTurns)}\u2192{F0(n.AllTurns)}";
    private static string DiffOut(ArmAgg n, ArmAgg o) => SignedPct(Pct(n.Out, o.Out));
    private static string DiffReadGrounding(ArmAgg n, ArmAgg o) =>
        $"{F0(o.Activated * 100)}%\u2192{F0(n.Activated * 100)}%";
    private static string DiffToolTurnSecs(ArmAgg n, ArmAgg o) =>
        $"{F0(o.ToolTurnSecs)}\u2192{F0(n.ToolTurnSecs)}s ({F0(o.ToolTurnSecsPct)}\u2192{F0(n.ToolTurnSecsPct)}%)";
    private static string DiffToolTurnIet(ArmAgg n, ArmAgg o) =>
        $"{F0(o.ToolTurnIetPct)}\u2192{F0(n.ToolTurnIetPct)}%";
    private static string DiffToolCallTurns(ArmAgg n, ArmAgg o) =>
        $"{F0(o.ToolTurns)}\u2192{F0(n.ToolTurns)} ({F0(o.ToolTurnPct)}\u2192{F0(n.ToolTurnPct)}%)";

    // The grounding doc's size (tokens loaded into the arm; baseline = 0), shown as size
    // context only. The doc's cost is a real cost and is NOT netted out — it is reported in
    // full by session IET below. (Netting raw DocTok out of weighted IET was dimensionally
    // unfaithful anyway: the doc is cache-read every turn, so its IET footprint != its token
    // count.)
    private static string RawDoc(ArmAgg a) => a.DocTok.ToString(Inv);
    private static string DiffDoc(ArmAgg n, ArmAgg o) => $"{o.DocTok}\u2192{n.DocTok}";

    private static readonly (string Label, Func<ArmAgg, string> Raw, Func<ArmAgg, ArmAgg, string> Diff)[] Spec =
    {
        ("tasks correct (+)",                  RawSuccess, DiffSuccess),
        ("func passed (assertions) (+)",       RawFunc,    DiffFunc),
        // Narrative: (1) all tool calls, (2) the subset (largely bash) that dug the nuget cache,
        // (3) the grounding meant to mitigate that, (4) the evidence.
        ("tool calls: web / bash / other (context)", RawToolSplit, DiffToolSplit),
        ("nuget archaeology: cache / nuget.org (-)", RawCache,  DiffCache),
        ("grounding load (tok) (context)",     RawDoc,     DiffDoc),
        ("read grounding (%)",                 RawReadGrounding, DiffReadGrounding),
        ("output tok (% of IET) (-)",          RawOut,     DiffOut),
        ("tool-call turns (% of total) (-)",    RawToolCallTurns, DiffToolCallTurns),
        ("tool-turn secs (% of turn time) (-)", RawToolTurnSecs, DiffToolTurnSecs),
        ("tool-turn IET (% of turn IET) (-)",  RawToolTurnIet,  DiffToolTurnIet),
        // Session summary (bottom line). `Session turns` doubles as the billable-request
        // count: the harness's premium-request "cost" is exactly 1 per turn (verified
        // 216/216), so a separate cost row would just restate turns — dropped. `Session IET`
        // is the real token-weighted cost.
        ("Session turns (-)",                  RawSessionTurns, DiffSessionTurns),
        ("Session IET (-)",                    RawIet,     DiffIet),
    };

    // ---- grading (Python _grade) -----------------------------------------

    // Verdict model: FAIL is the only correctness gate (grounding made the model answer
    // fewer scenarios correctly). The rest — archaeology, web, IET, output, cost, judge —
    // are SIGNALS that rank BETTER / NEUTRAL / WORSE; none of them flips the verdict alone.
    private static string Grade(ArmAgg b, ArmAgg g)
    {
        var iet = Pct(g.Iet, b.Iet);   // session IET (full token-weighted cost, doc included)
        var @out = Pct(g.Out, b.Out);
        var dsucc = g.Succ - b.Succ;
        int bArch = b.Arch, gArch = g.Arch;
        var tail = $"tasks correct {g.Succ}/{g.N} vs {b.Succ}/{b.N}, "
                 + $"resourcefulness {bArch}\u2192{gArch}, IET {SignedPct(iet)}";

        // FAIL: grounding regressed correctness — fewer scenarios answered correctly.
        if (dsucc < 0)
            return $"**FAIL** — fewer tasks correct ({tail})";

        // WORSE: real IET/output inflation (a harm signal), not a stray web call. (Premium-request
        // "cost" is 1:1 with turns, and IET is the token-weighted cost gate, so cost is not a
        // separate axis.)
        var worse = new List<string>();
        if (iet > IetHarmCapFrac * 100) worse.Add($"IET +{F0(iet)}%");
        if (@out > OutInflateFrac * 100) worse.Add($"output +{F0(@out)}%");
        if (worse.Count > 0)
            return $"**WORSE** — {string.Join(", ", worse)} ({tail})";

        // BETTER: solved more, eliminated archaeology, or materially cheaper (IET).
        if (dsucc > 0 || -iet >= IetWinFrac * 100 || (bArch > 0 && gArch == 0))
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
        _o.WriteLine($"_{mpref}Baseline (no grounding) vs `AGENTS.md`{tokNote}. Judge `{a.Judge}`. IET model {IetModels.CaptionFor(new[] { a.Model })}. Means across scenarios._\n");
        _o.WriteLine("| Metric (goal) | Baseline | AGENTS.md |");
        _o.WriteLine("| --- | ---: | ---: |");
        foreach (var (label, raw, _) in Spec)
            _o.WriteLine($"| {label} | {raw(b)} | {raw(g)} |");
        _o.WriteLine($"\n> **Conclusion:** {Grade(b, g)}.");
    }

    public void Card(IReadOnlyList<string> files)
    {
        var arms = files.Select(Loader.LoadArm).Where(a => !a.IsReadme && !a.IsSkill)
            .OrderBy(a => a.Tier == "mini" ? 0 : 1).ThenBy(a => a.Model, StringComparer.Ordinal).ToList();
        if (arms.Count == 0)
        {
            _o.WriteLine("--card needs at least one AGENTS.md dataset (non-'readme'/'skill' path)."); return;
        }
        var sn = arms[0].SkillName;
        var gtok = Loader.GroundingTokens(arms[0].SkillPath, sn);
        var tokNote = gtok is { } t ? $" (~{t} tok)" : "";
        if (!NoTitle) _o.WriteLine($"### Grounding eval — {sn}\n");
        _o.WriteLine($"_Each cell: baseline (no grounding) → `AGENTS.md`{tokNote}. Columns are models. Judge `{arms[0].Judge}`. IET model {IetModels.CaptionFor(arms.Select(a => a.Model))}. Means across scenarios._\n");
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
        var arms = files.Select(Loader.LoadArm).Where(a => !a.IsReadme && !a.IsSkill).ToList();
        if (arms.Count == 0) { _o.WriteLine("model-diff needs at least one AGENTS.md dataset."); return; }
        arms = arms
            .OrderBy(a => a.Tier == "mini" ? 0 : 1)
            .ThenBy(a => a.Model, StringComparer.Ordinal)
            .ToList();
        var sn = arms[0].SkillName;
        if (!NoTitle)
            _o.WriteLine($"### Model-diff — {sn} | AGENTS.md lift over baseline\n");
        _o.WriteLine($"_Each cell: `AGENTS.md` change vs that model's own baseline (count Δ; before→after for archaeology; % for IET/output/cost, − = cheaper). Columns are models. Judge `{arms[0].Judge}`. IET model {IetModels.CaptionFor(arms.Select(a => a.Model))}. Means across scenarios._\n");
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
                          Agents: g.FirstOrDefault(a => !a.IsReadme && !a.IsSkill),
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
        _o.WriteLine($"_Each cell: `AGENTS.md` − `README.md`, both via the grounding tool, baseline removed (− = AGENTS cheaper; + on success/func; lower archaeology = AGENTS more self-sufficient). Columns are models. Judge `{models[0].Agents!.Judge}`. IET model {IetModels.CaptionFor(models.Select(m => m.Model))}. Means across scenarios._\n");
        _o.WriteLine("| Metric (goal) | " + string.Join(" | ", models.Select(m => $"`{m.Model}`")) + " |");
        _o.WriteLine("| --- |" + string.Concat(Enumerable.Repeat(" ---: |", models.Count)));
        foreach (var (label, _, diff) in Spec)
            _o.WriteLine($"| {label} | " + string.Join(" | ", models.Select(m => diff(m.Agents!.Agg[Arm], m.Readme!.Agg[Arm]))) + " |");
        _o.WriteLine("| **verdict** | " + string.Join(" | ", models.Select(m => $"**{GradeLabel(m.Readme!.Agg[Arm], m.Agents!.Agg[Arm])}**")) + " |");
    }

    // AGENTS.md vs SKILL.md, per model: what the Textbook's extra tokens buy over the Missing
    // Manual. Each cell is SKILL − AGENTS (both via the grounding tool, baseline removed), so a
    // positive success/func Δ or lower archaeology is SKILL pulling ahead; − on IET/output/cost
    // means SKILL is cheaper (usually it is not — that is the token cost you are weighing).
    public void SkillDiff(IReadOnlyList<string> files)
    {
        var loaded = files.Select(Loader.LoadArm).ToList();
        var models = loaded.GroupBy(a => a.Model)
            .Select(g => (Model: g.Key,
                          Agents: g.FirstOrDefault(a => !a.IsReadme && !a.IsSkill),
                          Skill: g.FirstOrDefault(a => a.IsSkill)))
            .Where(x => x.Agents is not null && x.Skill is not null)
            .OrderBy(x => x.Agents!.Tier == "mini" ? 0 : 1).ThenBy(x => x.Model, StringComparer.Ordinal)
            .ToList();
        if (models.Count == 0)
        {
            _o.WriteLine("skill-diff needs an AGENTS.md + SKILL.md dataset per model (a path containing 'skill').");
            return;
        }
        var sn = models[0].Agents!.SkillName;
        if (!NoTitle) _o.WriteLine($"### SKILL.md over AGENTS.md — {sn}\n");
        _o.WriteLine($"_Each cell: `SKILL.md` − `AGENTS.md`, both via the grounding tool, baseline removed (+ on success/func = the Textbook wins more tasks; lower archaeology = more self-sufficient; % for IET/output/cost, − = SKILL cheaper — the extra tokens are the price). Columns are models. Judge `{models[0].Agents!.Judge}`. IET model {IetModels.CaptionFor(models.Select(m => m.Model))}. Means across scenarios._\n");
        _o.WriteLine("| Metric (goal) | " + string.Join(" | ", models.Select(m => $"`{m.Model}`")) + " |");
        _o.WriteLine("| --- |" + string.Concat(Enumerable.Repeat(" ---: |", models.Count)));
        foreach (var (label, _, diff) in Spec)
            _o.WriteLine($"| {label} | " + string.Join(" | ", models.Select(m => diff(m.Skill!.Agg[Arm], m.Agents!.Agg[Arm]))) + " |");
        _o.WriteLine("| **verdict** | " + string.Join(" | ", models.Select(m => $"**{GradeLabel(m.Agents!.Agg[Arm], m.Skill!.Agg[Arm])}**")) + " |");
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
            var ietModel = IetModels.For(d.Model);
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
                        var r = Loader.Row(Loader.ArmOf(sc, key), ietModel);
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
