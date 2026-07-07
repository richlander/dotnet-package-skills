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
    private static string RawFunc(ArmAgg a) => $"{a.Fp}/{a.Ft}";
    private static string RawCache(ArmAgg a) => $"{F0(a.Cache)} / {F0(a.NugetWeb)}";
    private static string RawToolSplit(ArmAgg a) => $"{F0(a.Web)}/{F0(a.Bash)}/{F0(a.Other)}";
    private static string RawIet(ArmAgg a) => F0(a.Iet);
    private static string RawSessionTurns(ArmAgg a) => F0(a.AllTurns);
    private static string RawOut(ArmAgg a) => $"{F0(a.Out)} ({F0(a.OutIetPct)}%)";
    private static string RawReadGrounding(ArmAgg a) => $"{F0(a.Activated * 100)}%";
    private static string RawToolTurnSecs(ArmAgg a) => $"{F0(a.ToolTurnSecs)}s ({F0(a.ToolTurnSecsPct)}%)";
    private static string RawToolTurnIet(ArmAgg a) => $"{F0(a.ToolTurnIetPct)}%";
    private static string RawToolCallTurns(ArmAgg a) => $"{F0(a.ToolTurns)} ({F0(a.ToolTurnPct)}%)";

    private static string DiffSuccess(ArmAgg n, ArmAgg o) => $"{SignedInt(n.Succ - o.Succ)} ({n.Succ}/{n.N})";
    private static string DiffFunc(ArmAgg n, ArmAgg o) => $"{SignedInt(n.Fp - o.Fp)} ({n.Fp}/{n.Ft})";
    private static string DiffCache(ArmAgg n, ArmAgg o) => $"{F0(o.Cache)}/{F0(o.NugetWeb)}\u2192{F0(n.Cache)}/{F0(n.NugetWeb)}";
    private static string DiffToolSplit(ArmAgg n, ArmAgg o) =>
        $"{F0(o.Web)}/{F0(o.Bash)}/{F0(o.Other)}\u2192{F0(n.Web)}/{F0(n.Bash)}/{F0(n.Other)}";
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
        double bArch = b.Arch, gArch = g.Arch;
        var tail = $"tasks correct {g.Succ}/{g.N} vs {b.Succ}/{b.N}, "
                 + $"resourcefulness {F0(bArch)}\u2192{F0(gArch)}, IET {SignedPct(iet)}";

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
        if (dsucc > 0 || -iet >= IetWinFrac * 100 || (bArch >= 0.5 && gArch < 0.5))
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

    // ---- document H2H over the answerable space (the LIET insight) -------------------------
    //
    // Each table lists the QUESTIONS at least one arm answered correctly (the union — so you can see
    // the space where some arm could NOT answer, the ✗ cells: unlocks and regressions, not just the
    // all-correct region). Failed arms show ✗ and are never extrapolated (docs/liet.md). The subset
    // every arm answered is the efficiency-comparable set (mean-IET footer); reach shows capability.
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
        // Row set = the QUESTIONS at least one arm answered correctly (union), across ALL column
        // datasets — a question present only in the README/SKILL run still counts.
        var names = cols.SelectMany(c => ScenarioShorts(c.ds)).Distinct().ToList();
        var rows = new List<(string name, (bool present, bool passed, double iet)[] cells)>();
        foreach (var name in names)
        {
            var cells = cols.Select(c => CellAt(c.ds, c.armKey, name, iet)).ToArray();
            if (cells.Any(x => x.passed)) rows.Add((name, cells));
        }
        _o.WriteLine($"#### {title}\n");
        if (rows.Count == 0) { _o.WriteLine("_No question answered by any arm._\n"); return; }
        int allCorrect = rows.Count(r => r.cells.All(x => x.passed));
        _o.WriteLine($"_{rows.Count} question(s) answered by ≥1 arm ({allCorrect} by all — the efficiency-comparable "
            + $"set). Cell = per-question IET; `✗` = that arm did not answer (not extrapolated). "
            + $"IET model {IetModels.CaptionFor(new[] { model })}._\n");
        _o.WriteLine("| question | " + string.Join(" | ", cols.Select(c => $"`{c.label}`")) + " |");
        _o.WriteLine("| --- |" + string.Concat(Enumerable.Repeat(" ---: |", cols.Length)));
        foreach (var (name, cells) in rows)
            _o.WriteLine($"| {name} | " + string.Join(" | ", cells.Select(Cell)) + " |");
        // Footer: reach per arm (capability), mean IET on the all-correct set (efficiency), and the
        // session total — the sum of each arm's per-question IET over the questions it answered.
        _o.WriteLine("| **reach** (answered) | " + string.Join(" | ",
            Enumerable.Range(0, cols.Length).Select(i => $"{rows.Count(r => r.cells[i].passed)}/{rows.Count}")) + " |");
        var allSet = rows.Where(r => r.cells.All(x => x.passed)).ToList();
        if (allSet.Count > 0 && cols.Length >= 2)
        {
            string Mean(int i) => K(allSet.Average(r => r.cells[i].iet));
            _o.WriteLine("| **mean IET** (all-correct set) | " + string.Join(" | ",
                cols.Select((c, i) => Mean(i))) + " |");
        }
        _o.WriteLine("| **total IET** (answered) | " + string.Join(" | ",
            Enumerable.Range(0, cols.Length).Select(i => K(rows.Where(r => r.cells[i].passed).Sum(r => r.cells[i].iet)))) + " |");
        _o.WriteLine();
    }

    private static string Cell((bool present, bool passed, double iet) x) =>
        x.passed ? K(x.iet) : (x.present ? "✗" : "—");

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
        var arms = files.Select(Loader.LoadArm).Where(x => !x.IsReadme && !x.IsSkill)
            .OrderBy(x => x.Tier == "mini" ? 0 : 1).ThenBy(x => x.Model, StringComparer.Ordinal).ToList();
        if (arms.Count == 0)
        {
            _o.WriteLine("doc-card needs at least one AGENTS.md dataset (non-'readme'/'skill' path).");
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

        // Exactly one AGENTS arm (input may also include a README/SKILL dataset that sorts ahead of it).
        var a = arms[0];
        var b = a.Agg["baseline"];
        var g = a.Agg[Arm];
        var card = QualityCard.Build(b, g, a.Iet, GradeLabel(b, g));
        var gtok = Loader.GroundingTokens(a.SkillPath, a.SkillName);
        var tokNote = gtok is { } t ? $" (~{t} tok, via grounding tool)" : "";
        if (!NoTitle) _o.WriteLine($"### Grounding eval — {a.SkillName} | `{a.Model}`\n");
        _o.WriteLine($"_Baseline (no grounding) → `AGENTS.md`{tokNote}. Judge `{a.Judge}`. "
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
        if (!NoTitle) _o.WriteLine($"### Grounding eval — {sn}\n");
        _o.WriteLine($"_Each cell: baseline (no grounding) → `AGENTS.md`{tokNote}. Columns are models. "
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
