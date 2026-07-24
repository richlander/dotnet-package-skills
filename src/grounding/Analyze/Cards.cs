using System.Globalization;
using System.Text;
using Grounding.Json;
using Markout;
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
    // The grounded primary is AGENTS.md or SKILL.md depending on the run (README is a separate
    // secondary arm). A run is homogeneous, so label from the arms: all-skill → SKILL.md,
    // any-skill (mixed, unusual) → generic, else AGENTS.md.
    private static string DocLabel(IReadOnlyList<LoadedArm> arms) =>
        arms.Count > 0 && arms.All(a => a.IsSkill) ? "SKILL.md"
        : arms.Any(a => a.IsSkill) ? "grounding doc"
        : "AGENTS.md";

    private static string RawFunc(ArmAgg a) => $"{a.Fp}/{a.Ft}";
    private static string RawCache(ArmAgg a) => $"{F0(a.Cache)} / {F0(a.NugetWeb)}";
    private static string RawToolSplit(ArmAgg a) => $"{F0(a.Web)}/{F0(a.Bash)}/{F0(a.Other)}";
    private static string RawIet(ArmAgg a) => F0(a.Iet);
    private static string RawSessionTurns(ArmAgg a) => F0(a.AllTurns);
    private static string RawSessionSecs(ArmAgg a) => $"{F0(a.Secs)}s";
    private static string RawOut(ArmAgg a) => $"{F0(a.Out)} ({F0(a.OutIetPct)}%)";
    private static string RawReadGrounding(ArmAgg a) => $"{F0(a.Activated * a.N)}/{a.N}";
    private static string RawToolTurnSecs(ArmAgg a) => $"{F0(a.ToolTurnSecs)}s ({F0(a.ToolTurnSecsPct)}%)";
    private static string RawToolTurnIet(ArmAgg a) => $"{F0(a.ToolTurnIetPct)}%";
    private static string RawToolCallTurns(ArmAgg a) => $"{F0(a.ToolTurns)} ({F0(a.ToolTurnPct)}%)";

    private static string DiffSuccess(ArmAgg n, ArmAgg o) => $"{SignedInt(n.Succ - o.Succ)} ({n.Succ}/{n.N})";
    private static string DiffFunc(ArmAgg n, ArmAgg o) => $"{SignedInt(n.Fp - o.Fp)} ({n.Fp}/{n.Ft})";
    private static string DiffCache(ArmAgg n, ArmAgg o) => $"{F0(o.Cache)}/{F0(o.NugetWeb)}\u2192{F0(n.Cache)}/{F0(n.NugetWeb)}";
    private static string DiffToolSplit(ArmAgg n, ArmAgg o) =>
        $"{F0(o.Web)}/{F0(o.Bash)}/{F0(o.Other)}\u2192{F0(n.Web)}/{F0(n.Bash)}/{F0(n.Other)}";
    private static string DiffIet(ArmAgg n, ArmAgg o) => $"{K(o.Iet)}\u2192{K(n.Iet)} ({SignedPct(Pct(n.Iet, o.Iet))})";
    // Grounding IET = the doc's carrying cost (baseline 0). Its "change" is expressed as a share of
    // the baseline total so the three IET rows add up: Total% = Grounding% + Work%.
    private static string RawGroundingIet(ArmAgg a) => F0(a.GroundingIet);
    private static string DiffGroundingIet(ArmAgg n, ArmAgg o) => $"{K(o.GroundingIet)}\u2192{K(n.GroundingIet)} ({SignedPct(o.Iet > 0 ? (double)n.GroundingIet / o.Iet * 100 : 0)})";
    private static string RawWorkIet(ArmAgg a) => F0(a.WorkIet);
    private static string DiffWorkIet(ArmAgg n, ArmAgg o) => $"{K(o.WorkIet)}\u2192{K(n.WorkIet)} ({SignedPct(Pct(n.WorkIet, o.WorkIet))})";
    private static string DiffSessionTurns(ArmAgg n, ArmAgg o) => $"{F0(o.AllTurns)}\u2192{F0(n.AllTurns)}";
    private static string DiffSessionSecs(ArmAgg n, ArmAgg o) => $"{F0(o.Secs)}\u2192{F0(n.Secs)}s ({SignedPct(Pct(n.Secs, o.Secs))})";
    private static string DiffOut(ArmAgg n, ArmAgg o) => SignedPct(Pct(n.Out, o.Out));
    private static string DiffReadGrounding(ArmAgg n, ArmAgg o) =>
        $"{F0(o.Activated * o.N)}/{o.N}\u2192{F0(n.Activated * n.N)}/{n.N}";
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

    // Skill coverage (plugin self-select): how many distinct shelf skills the arm actually pulled.
    // Baseline pulls none. A low count vs the shelf flags skills that earn no place (delete them).
    private static string RawSkillsUsed(ArmAgg a) =>
        a.SkillCounts.Count == 0 ? "\u2014" : a.SkillCounts.Count.ToString(Inv);
    private static string DiffSkillsUsed(ArmAgg n, ArmAgg o) => $"{o.SkillCounts.Count}\u2192{n.SkillCounts.Count}";

    // Per-skill pull breakdown for the note under the card: "markout×23 · conditional-composition×7 · …".
    private static string SkillBreakdown(ArmAgg a) =>
        a.SkillCounts.Count == 0 ? "none"
        : string.Join(" \u00b7 ", a.SkillCounts.OrderByDescending(k => k.Value)
            .ThenBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}\u00d7{k.Value}"));

    private static readonly (string Label, Func<ArmAgg, string> Raw, Func<ArmAgg, ArmAgg, string> Diff)[] Spec =
    {
        // Narrative headline (3 rows, same X/total format so the connection is obvious):
        // (1) did the agent answer correctly, (2) did it rely on the grounding (tasks that
        // invoked it), (3) did it fall back to archaeology instead.
        ("tasks correct (+)",                  RawSuccess, DiffSuccess),
        ("relied on grounding: tasks (+)",     RawReadGrounding, DiffReadGrounding),
        ("relied on archaeology, fallback: cache / nuget.org (-)", RawCache,  DiffCache),
        ("unique skills used (of shelf) (context)", RawSkillsUsed, DiffSkillsUsed),
        ("func passed (assertions) (+)",       RawFunc,    DiffFunc),
        ("tool calls: web / bash / other (context)", RawToolSplit, DiffToolSplit),
        ("grounding load (tok) (context)",     RawDoc,     DiffDoc),
        ("output tok (% of IET) (-)",          RawOut,     DiffOut),
        ("tool-call turns (% of total) (-)",    RawToolCallTurns, DiffToolCallTurns),
        ("tool-turn secs (% of turn time) (-)", RawToolTurnSecs, DiffToolTurnSecs),
        ("tool-turn IET (% of turn IET) (-)",  RawToolTurnIet,  DiffToolTurnIet),
        // Session summary (bottom line). `Session turns` doubles as the billable-request
        // count: the harness's premium-request "cost" is exactly 1 per turn (verified
        // 216/216), so a separate cost row would just restate turns — dropped. `Session IET`
        // is the real token-weighted cost.
        ("Session turns (-)",                  RawSessionTurns, DiffSessionTurns),
        ("Session wall-clock (end-to-end) (-)", RawSessionSecs, DiffSessionSecs),
        ("Total IET (-)",                      RawIet,          DiffIet),
        ("↳ Grounding IET (doc) (-)",          RawGroundingIet, DiffGroundingIet),
        ("↳ Work IET (agent) (-)",             RawWorkIet,      DiffWorkIet),
    };

    // ---- grading (Python _grade) -----------------------------------------

    // Verdict model — TWO ORTHOGONAL AXES (correctness is not the same question as efficiency):
    //   1. EFFICACY GATE  → PASS / FAIL  — did the doc answer 100% of its tier correctly?
    //      (the dotnet/skills philosophy: correct answers trump tokens; below 100% = unfinished.)
    //   2. EFFICIENCY      → BETTER / NEUTRAL / WORSE — independent of the gate, rank the SIGNALS
    //      (archaeology, work IET, output). A doc can FAIL the gate yet still be BETTER on
    //      efficiency (e.g. haiku: more correct + cheaper, but not yet 100%) — the old single
    //      verdict hid this by withholding the efficiency label whenever the gate failed.
    //      BUT a correctness REGRESSION (fewer tasks correct than baseline) forces WORSE: a cheaper
    //      arm that answers fewer questions is a harm, never BETTER (correct answers trump tokens).

    // EFFICACY GATE: did the grounded arm answer 100% of its tier correctly?
    private static string GateLabel(ArmAgg b, ArmAgg g) => g.Succ >= g.N ? "PASS" : "FAIL";

    // EFFICIENCY: rank archaeology / work IET / output, independent of the gate.
    private static string EffLabel(ArmAgg b, ArmAgg g)
    {
        // CORRECTNESS REGRESSION DOMINATES: if the grounded arm answers FEWER tasks correctly than
        // baseline, it is WORSE no matter how much IET it saves — cheaper-but-wrong is never BETTER
        // (dotnet/skills philosophy: correct answers trump tokens). An IET win cannot mask lost answers.
        if (g.Succ - b.Succ < 0) return "WORSE";
        var iet = Pct(g.WorkIet, b.WorkIet);   // WORK IET — doc carrying-cost netted out (the agent's effort)
        var @out = Pct(g.Out, b.Out);
        // WORSE: real IET/output inflation (a harm signal).
        if (iet > IetHarmCapFrac * 100 || @out > OutInflateFrac * 100) return "WORSE";
        // BETTER: eliminated archaeology, or materially cheaper (work IET), or more tasks correct.
        if (g.Succ - b.Succ > 0 || -iet >= IetWinFrac * 100 || (b.Arch >= 0.5 && g.Arch < 0.5)) return "BETTER";
        return "NEUTRAL";
    }

    private static string Grade(ArmAgg b, ArmAgg g)
    {
        var iet = Pct(g.WorkIet, b.WorkIet);
        var gate = GateLabel(b, g);
        var eff = EffLabel(b, g);
        var gateWhy = gate == "PASS" ? "100% correct" : $"{g.Succ}/{g.N} correct{(g.Succ - b.Succ < 0 ? ", regressed vs baseline" : "")}";
        var tail = $"tasks correct {g.Succ}/{g.N} vs {b.Succ}/{b.N}, "
                 + $"resourcefulness {F0(b.Arch)}\u2192{F0(g.Arch)}, work IET {SignedPct(iet)}";
        return $"**{gate}** ({gateWhy}) / **{eff}** — {tail}";
    }

    // Combined two-axis label for verdict cells, e.g. "PASS / BETTER" or "FAIL / BETTER".
    private static string GradeLabel(ArmAgg b, ArmAgg g) => $"{GateLabel(b, g)} / {EffLabel(b, g)}";

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
        var docLabel = a.IsSkill ? "SKILL.md" : "AGENTS.md";
        _o.WriteLine($"_{mpref}Baseline (no grounding) vs `{docLabel}`{tokNote}. Judge `{a.Judge}`. IET model {IetModels.CaptionFor(new[] { a.Model })}. Means across scenarios._\n");
        _o.WriteLine($"| Metric (goal) | Baseline | {docLabel} |");
        _o.WriteLine("| --- | ---: | ---: |");
        foreach (var (label, raw, _) in Spec)
        {
            if (label.StartsWith("Total IET", StringComparison.Ordinal))
                _o.WriteLine($"| {label} | {raw(b)} | {raw(g)} ({SignedPct(Pct(g.Iet, b.Iet))}) |");
            else
                _o.WriteLine($"| {label} | {raw(b)} | {raw(g)} |");
        }
        _o.WriteLine($"\n> **Conclusion:** {Grade(b, g)}.");
    }

    public void Card(IReadOnlyList<string> files)
    {
        var arms = files.Select(Loader.LoadArm).Where(a => !a.IsReadme)
            .OrderBy(a => a.Tier == "mini" ? 0 : 1).ThenBy(a => a.Model, StringComparer.Ordinal).ToList();
        if (arms.Count == 0)
        {
            _o.WriteLine("--card needs at least one grounded dataset (AGENTS.md or SKILL.md; non-'readme' path)."); return;
        }
        var docLabel = DocLabel(arms);
        var sn = arms[0].SkillName;
        var gtok = Loader.GroundingTokens(arms[0].SkillPath, sn);
        var tokNote = gtok is { } t ? $" (~{t} tok)" : "";
        if (!NoTitle) _o.WriteLine($"### Grounding eval — {sn}\n");
        _o.WriteLine($"_Each cell: baseline (no grounding) → `{docLabel}`{tokNote}. Columns are models. Judge `{arms[0].Judge}`. IET model {IetModels.CaptionFor(arms.Select(a => a.Model))}. Means across scenarios._\n");
        _o.WriteLine("| Metric (goal) | " + string.Join(" | ", arms.Select(a => $"`{a.Model}`")) + " |");
        _o.WriteLine("| --- |" + string.Concat(Enumerable.Repeat(" ---: |", arms.Count)));

        // Per-arm LIET summary (floor-anchored IET/duration per correct answer + target-skill hit),
        // computed once and reused so the card and the SVG report the SAME numbers.
        var ls = arms.ToDictionary(a => a, a => Liet.Summarize(a.Path, Arm));

        // A grounded "base → grounded" cell built from an ArmAgg raw formatter.
        string Pair(LoadedArm a, Func<ArmAgg, string> raw) => $"{raw(a.Agg["baseline"])} → {raw(a.Agg[Arm])}";

        // Rows in NARRATIVE order — five acts, each cost act following the same total → decomposition
        // → levelized shape, grouped by currency (tokens / turns / wall-clock):
        //   ① OUTCOME    — did the skills produce correct work? (tasks correct ← func passed proves it)
        //   ② MECHANISM  — did it lean on the skills or fall back to digging (archaeology)?
        //   ③ TURNS      — billable requests (total → tool-call share)
        //   ④ WALL-CLOCK — duration; SYMMETRIC with ⑤: a raw Total (+ tool share) then a floor-anchored
        //                   per-correct-answer hero (+ efficiency detail + Floor). Machine-dependent.
        //   ⑤ TOKEN COST — IET, the normative metric and the punchline; SYMMETRIC with ④: a raw Total
        //                   (+ doc/work/skill-load/output/tool decomposition) then the floor-anchored
        //                   per-correct-answer hero (+ efficiency detail + Floor). Last: it's what we sell.
        // Parent rows carry a Session/Total; the `↳` children decompose, drive, or prove that parent.
        var rows = new (string Label, Func<LoadedArm, string> Cell)[]
        {
            // ① OUTCOME
            ("tasks correct (+)",                          a => Pair(a, RawSuccess)),
            ("↳ func passed (assertions) (+)",             a => Pair(a, RawFunc)),
            // ② MECHANISM — skills vs. archaeology
            ("relied on skills: tasks (+)",                a => Pair(a, RawReadGrounding)),
            ("↳ expected skill pulled (target) (context)", a => ls[a].HasData && ls[a].TargetTotal > 0 ? $"{ls[a].TargetHits}/{ls[a].TargetTotal}" : "—"),
            ("↳ unique skills used (of shelf) (context)",  a => Pair(a, RawSkillsUsed)),
            ("relied on archaeology, fallback: cache / nuget.org (-)", a => Pair(a, RawCache)),
            ("↳ tool calls: web / bash / other (context)", a => Pair(a, RawToolSplit)),
            // ③ TURNS
            ("Session turns (-)",                          a => Pair(a, RawSessionTurns)),
            ("↳ tool-call turns (% of total) (-)",         a => Pair(a, RawToolCallTurns)),
            // ④ WALL-CLOCK (duration) — mirrors the IET section: a raw Total with a decomposition, then
            //    a floor-anchored per-correct-answer hero with its efficiency detail + Floor.
            ("Total duration (-)",                         a => $"{RawSessionSecs(a.Agg["baseline"])} → {RawSessionSecs(a.Agg[Arm])} ({SignedPct(Pct(a.Agg[Arm].Secs, a.Agg["baseline"].Secs))})"),
            ("↳ tool-turn secs (% of turn time) (-)",      a => Pair(a, RawToolTurnSecs)),
            ("Duration per correct answer (-)",            a => ls[a].HasData ? ls[a].DurDelta : "—"),
            ("↳ efficiency: baseline-doable (-)",          a => ls[a].HasData ? $"{ls[a].BaseDur} → {ls[a].AgDur} (Δ {ls[a].DurDelta})" : "—"),
            ("↳ Floor (context)",                          a => ls[a].HasData ? ls[a].FloorDur : "—"),
            // ⑤ TOKEN COST (IET) — the punchline
            ("Total IET (-)",                              a => $"{RawIet(a.Agg["baseline"])} → {RawIet(a.Agg[Arm])} ({SignedPct(Pct(a.Agg[Arm].Iet, a.Agg["baseline"].Iet))})"),
            ("↳ Grounding IET (doc) (-)",                  a => Pair(a, RawGroundingIet)),
            ("↳ Work IET (agent) (-)",                     a => Pair(a, RawWorkIet)),
            ("↳ skill load (tok) (context)",               a => Pair(a, RawDoc)),
            ("↳ output tok (% of IET) (-)",                a => Pair(a, RawOut)),
            ("↳ tool-turn IET (% of turn IET) (-)",        a => Pair(a, RawToolTurnIet)),
            ("IET per correct answer (-)",                 a => ls[a].HasData ? ls[a].LietDelta : "—"),
            ("↳ efficiency: baseline-doable (-)",          a => ls[a].HasData ? $"{ls[a].BaseLiet} → {ls[a].AgLiet} (Δ {ls[a].LietDelta})" : "—"),
            ("↳ Floor (context)",                          a => ls[a].HasData ? ls[a].Floor : "—"),
        };
        foreach (var (label, cell) in rows)
            _o.WriteLine($"| {label} | " + string.Join(" | ", arms.Select(cell)) + " |");
        _o.WriteLine("| **verdict** | " + string.Join(" | ", arms.Select(a => $"**{GradeLabel(a.Agg["baseline"], a.Agg[Arm])}**")) + " |");
        _o.WriteLine("\n_Two axes. **Gate** (correctness): **PASS** = 100% of tier correct, **FAIL** = below the gate. "
            + "**Efficiency** (independent of the gate): **BETTER** = more tasks correct / archaeology→0 / work IET cut ≥20%; "
            + "**WORSE** = fewer tasks correct than baseline, or work IET / output inflated ≥20%; **NEUTRAL** = held. "
            + "A correctness regression forces WORSE (cheaper-but-wrong is never better); a doc can FAIL the gate yet be BETTER on efficiency._\n");
        _o.WriteLine("> Note: even ungrounded, the baseline self-grounds from the restored NuGet cache "
            + "(README/AGENTS are packed in the nupkg) and the open web — so its resourcefulness count is a "
            + "**lower bound** and grounding's advantage is understated.\n");
        if (arms.Any(a => a.Agg[Arm].SkillCounts.Count > 0))
            _o.WriteLine("> **Skills pulled** (self-select from shelf, ×scenarios): "
                + string.Join(" \u2014 ", arms.Select(a => $"`{a.Model}` {SkillBreakdown(a.Agg[Arm])}"))
                + ". A shelf skill pulled ×0\u20131 earns no place (delete it).\n");
    }

    public void ModelDiff(IReadOnlyList<string> files)
    {
        var arms = files.Select(Loader.LoadArm).Where(a => !a.IsReadme).ToList();
        if (arms.Count == 0) { _o.WriteLine("model-diff needs at least one grounded dataset (AGENTS.md or SKILL.md)."); return; }
        arms = arms
            .OrderBy(a => a.Tier == "mini" ? 0 : 1)
            .ThenBy(a => a.Model, StringComparer.Ordinal)
            .ToList();
        var docLabel = DocLabel(arms);
        var sn = arms[0].SkillName;
        if (!NoTitle)
            _o.WriteLine($"### Model-diff — {sn} | {docLabel} lift over baseline\n");
        _o.WriteLine($"_Each cell: `{docLabel}` change vs that model's own baseline (count Δ; before→after for archaeology; % for IET/output/cost, − = cheaper). Columns are models. Judge `{arms[0].Judge}`. IET model {IetModels.CaptionFor(arms.Select(a => a.Model))}. Means across scenarios._\n");
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

    // ---- document H2H over the answerable space (the LIET insight) -------------------------
    //
    // Each table lists every QUESTION at least one arm ATTEMPTED — so the table is the whole session,
    // including questions all arms got wrong. Every attempted cell shows its per-question IET with a
    // `(true/false)` correctness tag (a wrong answer still costs IET); `—` = the question was not in
    // that arm's dataset. The subset every arm answered is the efficiency-comparable set (mean-IET
    // footer); reach shows correctness capability; total IET is the per-arm session sum.
    // Two tables per model:
    //   1. baseline / README / AGENTS — does the Missing Manual pay its way over the Brochure.
    //   2. baseline / AGENTS  / SKILL — what the Textbook's extra tokens buy over the Missing Manual.
    // Secondary views: `--card` per document (baseline vs each over the full ladder) and `--view liet`
    // (the per-rung curve, all arms).
    public void H2H(IReadOnlyList<string> files)
    {
        var parsed = new List<(string file, ResultsFile d)>();
        foreach (var f in files.Distinct())
        {
            try { parsed.Add((f, Loader.Parse(f))); }
            catch (Exception e) { _o.WriteLine($"!! {f}: {e.Message}"); }
        }
        var groups = parsed.GroupBy(x => (Model: x.d.Model ?? "?", Unit: UnitOf(x.d)))
            .OrderBy(g => Metrics.Tier(g.Key.Model) == "mini" ? 0 : 1)
            .ThenBy(g => g.Key.Unit, StringComparer.Ordinal).ThenBy(g => g.Key.Model, StringComparer.Ordinal);
        foreach (var g in groups)
        {
            var (model, unit) = g.Key;
            var iet = IetModels.For(model);
            var items = g.ToList();
            // Exactly one dataset per doc type within a (model, unit) group; grouping by unit
            // prevents silently pairing AGENTS from one unit with README/SKILL from another, and
            // >1 match of a type is ambiguous — skip that type rather than pick an arbitrary one.
            ResultsFile? Pick(string kind, Func<string, bool> pred)
            {
                var m = items.Where(x => pred(System.IO.Path.GetFileName(x.file).ToLowerInvariant()))
                             .Select(x => x.d).ToList();
                if (m.Count > 1) { _o.WriteLine($"_(h2h: {m.Count} {kind} datasets for {unit}/`{model}` — ambiguous, skipping {kind}.)_\n"); return null; }
                return m.FirstOrDefault();
            }
            var agents = Pick("AGENTS", n => !n.Contains("readme") && !n.Contains("skill"));
            var readme = Pick("README", n => n.Contains("readme"));
            var skill = Pick("SKILL", n => n.Contains("skill"));
            if (agents is null)
            {
                _o.WriteLine($"_(h2h: no AGENTS dataset for {unit}/`{model}` — need a path without 'readme'/'skill'.)_\n");
                continue;
            }
            if (!NoTitle)
            {
                var display = (agents.Verdicts is { Count: > 0 } ? agents.Verdicts[0].SkillName : null) ?? unit;
                _o.WriteLine($"### Document H2H (answerable questions) — {display} | `{model}`\n");
            }
            if (readme is not null)
                EmitH2H("Missing Manual vs Brochure — does `AGENTS.md` pay its way over `README.md`", model, iet,
                    ("baseline", agents, "baseline"), ("README.md", readme, Arm), ("AGENTS.md", agents, Arm));
            if (skill is not null)
                EmitH2H("Textbook premium — what `SKILL.md`'s extra tokens buy over `AGENTS.md`", model, iet,
                    ("baseline", agents, "baseline"), ("AGENTS.md", agents, Arm), ("SKILL.md", skill, Arm));
            if (readme is null && skill is null)
                _o.WriteLine("_(h2h needs a README (`*readme*`) and/or SKILL (`*skill*`) dataset beside the AGENTS dataset.)_\n");
        }
    }

    private void EmitH2H(string title, string model, IetScheme iet,
        params (string label, ResultsFile ds, string armKey)[] cols)
    {
        // Row set = every QUESTION at least one arm ATTEMPTED (present) across all column datasets —
        // so the table is the whole session, including questions all arms got wrong (all `(false)`).
        var names = cols.SelectMany(c => ScenarioShorts(c.ds)).Distinct().ToList();
        var rows = new List<(string name, (bool present, bool passed, double iet)[] cells)>();
        foreach (var name in names)
        {
            var cells = cols.Select(c => CellAt(c.ds, c.armKey, name, iet)).ToArray();
            if (cells.Any(x => x.present)) rows.Add((name, cells));
        }
        _o.WriteLine($"#### {title}\n");
        if (rows.Count == 0) { _o.WriteLine("_No question attempted by any arm._\n"); return; }
        int allCorrect = rows.Count(r => r.cells.All(x => x.passed));
        _o.WriteLine($"_{rows.Count} question(s) attempted ({allCorrect} answered by all — the efficiency-comparable "
            + $"set). Cell = per-question IET with `(true/false)` = whether that arm answered correctly "
            + $"(a wrong answer still costs IET); `—` = not attempted. IET model {IetModels.CaptionFor(new[] { model })}._\n");
        _o.WriteLine("| question | " + string.Join(" | ", cols.Select(c => $"`{c.label}`")) + " |");
        _o.WriteLine("| --- |" + string.Concat(Enumerable.Repeat(" ---: |", cols.Length)));
        foreach (var (name, cells) in rows)
            _o.WriteLine($"| {name} | " + string.Join(" | ", cells.Select(Cell)) + " |");
        // Footer: reach per arm (capability), mean IET on the all-correct set (efficiency), and the
        // session total — the sum of each arm's per-question IET over every question it attempted.
        _o.WriteLine("| **reach** (answered) | " + string.Join(" | ",
            Enumerable.Range(0, cols.Length).Select(i => $"{rows.Count(r => r.cells[i].passed)}/{rows.Count}")) + " |");
        var allSet = rows.Where(r => r.cells.All(x => x.passed)).ToList();
        if (allSet.Count > 0 && cols.Length >= 2)
        {
            string Mean(int i) => K(allSet.Average(r => r.cells[i].iet));
            _o.WriteLine("| **mean IET** (all-correct set) | " + string.Join(" | ",
                cols.Select((c, i) => Mean(i))) + " |");
        }
        _o.WriteLine("| **total IET** (session) | " + string.Join(" | ",
            Enumerable.Range(0, cols.Length).Select(i => K(rows.Where(r => r.cells[i].present).Sum(r => r.cells[i].iet)))) + " |");
        _o.WriteLine();
    }

    private static string Cell((bool present, bool passed, double iet) x) =>
        x.present ? $"{K(x.iet)} ({(x.passed ? "true" : "false")})" : "—";

    private static string K(double v) => v >= 1000 ? $"{(v / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)}k"
        : v.ToString("0", CultureInfo.InvariantCulture);

    private static List<string> ScenarioShorts(ResultsFile d) =>
        ((d.Verdicts is { Count: > 0 } ? d.Verdicts[0].Scenarios : null) ?? new()).Select(Short).ToList();

    private static string Short(Scenario s) => (s.ScenarioName ?? "").Split(':')[0].Trim();

    // Stable unit identity for grouping. Prefer skillPath (the grounding/<unit> dir — shared across
    // a unit's readme/agents/skill runs, distinct between units) over skillName, which is NOT unique
    // (e.g. markout and markout-013 both use `name: markout`; see Loader.cs). Falls back to name.
    private static string UnitOf(ResultsFile d)
    {
        var v = d.Verdicts is { Count: > 0 } ? d.Verdicts[0] : null;
        if (v is null) return "?";
        return !string.IsNullOrEmpty(v.SkillPath) ? v.SkillPath!.Replace('\\', '/').TrimEnd('/') : (v.SkillName ?? "?");
    }

    private static (bool present, bool passed, double iet) CellAt(ResultsFile d, string armKey, string name, IetScheme iet)
    {
        var sc = ((d.Verdicts is { Count: > 0 } ? d.Verdicts[0].Scenarios : null) ?? new())
            .FirstOrDefault(s => Short(s) == name);
        var r = sc is null ? null : Loader.Row(Loader.ArmOf(sc, armKey), iet);
        if (r is null) return (false, false, 0);
        return (true, r.Ft > 0 && r.Fp == r.Ft, r.Iet);
    }

    // ---- declarative card (Markout composite cells) ----------------------

    // The quality card rendered from a single declarative model (QualityCard) of Markout
    // composite shapes. One declaration → the dense Markdown card AND, with --jsonl, the
    // decomposed typed rows. Single model (baseline → grounded); the multi-model wide table
    // is a separate layout (see Card).
    public void DocCard(IReadOnlyList<string> files, bool jsonl)
    {
        // Multiple models → the multi-model pivot (rows = metrics, columns = models). A single model
        // keeps the dense single-model card below.
        var arms = files.Select(Loader.LoadArm).Where(x => !x.IsReadme)
            .OrderBy(x => x.Tier == "mini" ? 0 : 1).ThenBy(x => x.Model, StringComparer.Ordinal).ToList();
        if (arms.Count == 0)
        {
            _o.WriteLine("doc-card needs at least one grounded dataset (AGENTS.md or SKILL.md; non-'readme' path).");
            return;
        }
        if (arms.Count > 1)
        {
            // The pivot keys columns by model, so duplicate models would collide into one column and
            // silently drop an arm. Require one dataset per distinct model.
            if (arms.Select(x => x.Model).Distinct(StringComparer.Ordinal).Count() != arms.Count)
            {
                _o.WriteLine("doc-card multi-model needs one dataset per distinct model (duplicate models supplied).");
                return;
            }
            // The card is one grounding unit compared across models; the header/token note come from
            // arms[0], so mixing units would mislabel the card and combine unrelated metrics.
            if (arms.Select(x => x.SkillName).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                _o.WriteLine("doc-card multi-model needs all datasets from the same grounding unit (mixed units supplied).");
                return;
            }
            DocCardMultiModel(arms, jsonl);
            return;
        }

        // Exactly one grounded arm (input may also include a README/SKILL dataset that sorts ahead of it).
        var a = arms[0];
        var b = a.Agg["baseline"];
        var g = a.Agg[Arm];
        var card = QualityCard.Build(b, g, a.Iet, GradeLabel(b, g));
        var gtok = Loader.GroundingTokens(a.SkillPath, a.SkillName);
        var tokNote = gtok is { } t ? $" (~{t} tok, via grounding tool)" : "";
        var docLabel = a.IsSkill ? "SKILL.md" : "AGENTS.md";
        if (!NoTitle) _o.WriteLine($"### Grounding eval — {a.SkillName} | `{a.Model}`\n");
        _o.WriteLine($"_Baseline (no grounding) → `{docLabel}`{tokNote}. Judge `{a.Judge}`. "
            + $"IET model {IetModels.CaptionFor(new[] { a.Model })}. Means across scenarios._\n");
        _o.Write(MarkoutSerializer.Serialize(card, QualityCardContext.Default));
        _o.WriteLine($"\n> **Conclusion:** {Grade(b, g)}.");
        if (jsonl)
        {
            _o.WriteLine("\n_Same model, decomposed to typed JSONL rows:_\n");
            _o.WriteLine("```jsonl");
            MarkoutSerializer.Serialize(card, _o, new TableFormatter(), QualityCardContext.Default,
                new MarkoutWriterOptions { TableMode = MarkoutTableMode.Jsonl, JsonTypedValues = true, OmitEmptyJsonFields = true });
            _o.WriteLine("```");
        }
    }

    // The multi-model quality card via Markout 0.17.0 multi-source rows: models pivot into columns
    // (mini-tier first), each cell a baseline → grounded Change<Shape>, verdict as GateStatus. One
    // declarative model renders the dense Markdown card and, with --jsonl, the decomposed typed rows.
    private void DocCardMultiModel(IReadOnlyList<LoadedArm> arms, bool jsonl)
    {
        var models = arms
            .Select(a => (a.Model, B: a.Agg["baseline"], G: a.Agg[Arm], Grade: GradeLabel(a.Agg["baseline"], a.Agg[Arm])))
            .ToList();
        var card = MultiModelCard.Build(models);

        var sn = arms[0].SkillName;
        var gtok = Loader.GroundingTokens(arms[0].SkillPath, sn);
        var tokNote = gtok is { } t ? $" (~{t} tok)" : "";
        var docLabel = DocLabel(arms);
        if (!NoTitle) _o.WriteLine($"### Grounding eval — {sn}\n");
        _o.WriteLine($"_Each cell: baseline (no grounding) → `{docLabel}`{tokNote}. Columns are models. "
            + $"Judge `{arms[0].Judge}`. IET model {IetModels.CaptionFor(arms.Select(a => a.Model))}. Means across scenarios._\n");
        _o.Write(MarkoutSerializer.Serialize(card, MultiModelCardContext.Default));
        if (jsonl)
        {
            _o.WriteLine("\n_Same card, decomposed to typed JSONL rows (one per metric; roles are models):_\n");
            _o.WriteLine("```jsonl");
            MarkoutSerializer.Serialize(card, _o, new TableFormatter(), MultiModelCardContext.Default,
                new MarkoutWriterOptions { TableMode = MarkoutTableMode.Jsonl, JsonTypedValues = true, OmitEmptyJsonFields = true });
            _o.WriteLine("```");
        }
    }

    // ---- raw per-scenario table (Python main) ----------------------------

    private const string Hdr =
        "scenario                     | arm      | qual | func |     tok |    iet | cost | secs \u2016 web | tools | turn | di | mcp | cache | bash";
    private const string Grp =
        "                                                 <<<<<<<<<< NORMATIVE METRICS         \u2016 INFORMATIVE SIGNALS >>>>>>>>>>";

    // Smell test — a single-arm, unjudged "finger in the wind" over the self-selecting shelf
    // (skilledPlugin). No baseline column, no judge, no verdict: just the objective behavioural
    // signals a maintainer eyeballs after editing a shelf — did the skills activate, did the agent
    // avoid archaeology (cache/web digging), and did it stay cheap (turns, IET). Works on any
    // dataset, but is meant for the cheap `run --no-judge` output. Arm defaults to skilledPlugin
    // (the whole-shelf self-select), overridable via GROUNDING_CARD_ARM.
    public void SmellCard(IReadOnlyList<string> files)
    {
        var arm = Environment.GetEnvironmentVariable("GROUNDING_CARD_ARM") is { Length: > 0 } v ? v : "skilledPlugin";
        var arms = files.Select(Loader.LoadAny).Where(a => !a.IsReadme && a.Agg.ContainsKey(arm))
            .OrderBy(a => a.Tier == "mini" ? 0 : 1).ThenBy(a => a.Model, StringComparer.Ordinal).ToList();
        if (arms.Count == 0)
        {
            _o.WriteLine($"smell needs at least one dataset carrying the `{arm}` arm."); return;
        }
        var sn = arms[0].SkillName;
        var n = arms[0].Agg[arm].N;
        if (!NoTitle) _o.WriteLine($"### Smell test — {sn} (unjudged)\n");
        _o.WriteLine($"_Self-selecting shelf arm (`{arm}`) only — no baseline, no judge. IET model "
            + $"{IetModels.CaptionFor(arms.Select(a => a.Model))}. Means across {n} scenarios. Finger in the wind: "
            + "did the shelf activate, avoid archaeology, and stay cheap?_\n");
        _o.WriteLine("| Signal (goal) | " + string.Join(" | ", arms.Select(a => $"`{a.Model}`")) + " |");
        _o.WriteLine("| --- |" + string.Concat(Enumerable.Repeat(" ---: |", arms.Count)));
        (string Label, Func<ArmAgg, string> Raw)[] smell =
        {
            ("tasks correct (+)",                                    RawSuccess),
            ("relied on grounding: tasks (+)",                       RawReadGrounding),
            ("archaeology: cache / nuget.org (-)",                   RawCache),
            ("unique skills used (of shelf) (context)",              RawSkillsUsed),
            ("tool calls: web / bash / other (context)",             RawToolSplit),
            ("output tok (% of IET) (-)",                            RawOut),
            ("tool-call turns (% of total) (-)",                     RawToolCallTurns),
            ("session turns (-)",                                    RawSessionTurns),
            ("Total IET (-)",                                        RawIet),
        };
        foreach (var (label, raw) in smell)
            _o.WriteLine($"| {label} | " + string.Join(" | ", arms.Select(a => raw(a.Agg[arm]))) + " |");
        if (arms.Any(a => a.Agg[arm].SkillCounts.Count > 0))
            _o.WriteLine("\n> **Skills pulled** (self-select from shelf, ×scenarios): "
                + string.Join(" \u2014 ", arms.Select(a => $"`{a.Model}` {SkillBreakdown(a.Agg[arm])}"))
                + ". A shelf skill pulled ×0\u20131 earns no place (delete it).\n");
    }

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
        var web = $"{F0(r.Web)}{(r.WebUsed ? "Y" : ".")}";
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
        sb.Append(PadL(Str(r.Turns), 4)).Append(" | ");        sb.Append(PadL(F0(r.Di), 2)).Append(" | ");
        sb.Append(PadL(F0(r.Mcp), 3)).Append(" | ");
        sb.Append(PadL(F0(r.Cache), 5)).Append(" | ");
        sb.Append(PadL(F0(r.Bash), 4));
        return sb.ToString();
    }

    private static string Str(int? v) => v?.ToString(Inv) ?? "?";
    private static string Str(double? v) => v?.ToString(Inv) ?? "?";
    private static string Pad(string s, int w) => s.Length >= w ? s : s + new string(' ', w - s.Length);
    private static string PadL(string s, int w) => s.Length >= w ? s : new string(' ', w - s.Length) + s;
}
