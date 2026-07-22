using System.Globalization;
using System.Text;
using Grounding.Json;

namespace Grounding.Analyze;

// The Levelized-IET (LIET) curve — see docs/liet.md.
//
// Per-rung IET, one series per arm (baseline / AGENTS.md / oracle), difficulty on the x-axis
// (authored rung order). The rules the renderer enforces:
//   * A rung shows a value for an arm ONLY where that arm answered correctly (all functional
//     assertions pass). A failed arm is NOT plotted and NOT extrapolated.
//   * For every rung, the competitor envelope = min(IET of the OTHER arms that passed). That is the
//     ceiling AGENTS.md must stay UNDER — the maximum price of generalization (a ceiling, not a
//     floor). Under it: AGENTS.md pays its way. Over it (or absent): ship the cheaper competitor.
internal sealed class Liet
{
    private readonly TextWriter _o;
    public bool NoTitle;
    public bool OracleFromPlugin;   // opt-in: read skilledPlugin as the SKILL.md oracle
    // The grounded arm to plot as the primary (blue) curve. Mirrors the card's GROUNDING_CARD_ARM so
    // `--view liet` and `--view card` always describe the SAME arm. Default skilledIsolated (the clean
    // single-skill content measure); set skilledPlugin for the realistic whole-shelf pull delivery.
    public string GroundArm { get; init; } =
        Environment.GetEnvironmentVariable("GROUNDING_CARD_ARM") is { Length: > 0 } v ? v : "skilledIsolated";
    public Liet(TextWriter o) => _o = o;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed class Point
    {
        public double Iet;
        public bool Passed;      // arm answered correctly (all functional assertions pass)
        public bool Present;     // arm exists in the dataset for this rung
        public double Arch;      // archaeology = external digging (nuget cache + nuget.org + web tool calls)
        public double Secs;      // end-to-end wall-clock seconds (spent regardless of correctness)
    }

    private sealed class Rung
    {
        public string Name = "";
        public int Index;
        public Point Base = new(), Ag = new(), Oracle = new(), Readme = new();
        public double? Ceiling;  // competitor envelope for AGENTS.md (min IET of other passing arms)
        public string Region = "";
        public List<string> Skills = new();  // skills the grounded arm pulled for this rung (plugin self-select)
        public string? Expected;             // methodology prior: the ONE target skill for this rung (eval.yaml)
    }

    private static bool Correct(ArmRow? r) => r is { Ft: > 0 } && r.Fp == r.Ft;

    // Archaeology = "went outside the grounding": nuget-cache decompile + nuget.org + open-web tool
    // calls. This is the effort the grounding is meant to delete; plotted for every present arm
    // (success or failure — digging happens regardless of whether the answer landed).
    private static double Arch(ArmRow? r) => r is null ? 0 : r.Cache + r.NugetWeb + r.Web;

    public void Render(IReadOnlyList<string> files, string? svgPath)
    {
        var parsed = new List<(string path, ResultsFile d)>();
        foreach (var f in files.Distinct())
        {
            try { parsed.Add((f, Loader.Parse(f))); }
            catch (Exception e) { _o.WriteLine($"!! {f}: {e.Message}"); }
        }
        // A dataset whose file name contains "readme" is the packed-README arm; fold it into the
        // matching primary (AGENTS/skill) curve as a third series rather than plotting it separately.
        static bool IsReadme(string p) => System.IO.Path.GetFileName(p).ToLowerInvariant().Contains("readme");
        var readmeFiles = parsed.Where(p => IsReadme(p.path)).ToList();
        var primary = parsed.Where(p => !IsReadme(p.path)).ToList();
        if (primary.Count == 0) primary = parsed;   // all-readme input: fall back to plotting them

        // Global skill-id map across ALL rendered datasets so an id means the SAME skill in every
        // card (M = base skill; domain skills alphabetical → 1..N over the union of everything
        // pulled). Render models together so opus and haiku legends are directly comparable.
        var allSkills = new HashSet<string>(StringComparer.Ordinal);
        string baseSkill = "";
        foreach (var (_, d0) in primary)
            foreach (var v0 in d0.Verdicts ?? new())
            {
                if (baseSkill.Length == 0) baseSkill = v0.SkillName ?? "";
                foreach (var sc0 in v0.Scenarios ?? new())
                {
                    foreach (var s0 in Loader.DetectedSkillsOf(sc0, GroundArm)) allSkills.Add(s0);
                    // Include the methodology target so an expected-but-never-pulled skill (an
                    // under-fire gap) still gets a stable global id and shows in the legend.
                    if (!string.IsNullOrWhiteSpace(sc0.ExpectedSkill)) allSkills.Add(sc0.ExpectedSkill.Trim());
                }
            }
        var skillIds = BuildGlobalIds(allSkills, baseSkill);

        foreach (var (f, d) in primary.OrderBy(x => x.path, StringComparer.Ordinal))
        {
            var iet = IetModels.For(d.Model);
            var rf = readmeFiles.FirstOrDefault(r => r.d.Model == d.Model);
            var readmeMap = rf.d is not null ? BuildReadmeMap(rf.d, iet) : null;
            int vi = 0;
            foreach (var v in d.Verdicts ?? new())
            {
                var rungs = BuildRungs(v, iet, OracleFromPlugin, GroundArm, readmeMap);
                if (rungs.Count == 0) { vi++; continue; }
                // Levelize: order rungs by MEASURED difficulty — the LCOE-faithful x-axis — not
                // authored order. Baseline alone doesn't cover the whole range (it can't answer the
                // hardest rungs), so we sort in three tiers so both curves read left-to-right easy→hard:
                //   0. baseline-correct rungs, easiest first (by baseline IET)
                //   1. grounded-only unlocks (baseline ✗, SKILL ✓), easiest first (by grounded IET)
                //   2. rungs neither arm answers (SKILL ✗), lowest grounded IET first
                // Within tiers 1–2 there is no baseline IET, so the grounded arm's IET defines difficulty.
                rungs = rungs
                    .OrderBy(r => r.Base.Passed ? 0 : (r.Ag.Passed ? 1 : 2))
                    .ThenBy(r => r.Base.Passed ? r.Base.Iet : r.Ag.Iet)
                    .ThenBy(r => r.Index).ToList();
                for (int k = 0; k < rungs.Count; k++) rungs[k].Index = k;
                var unit = v.SkillName ?? "?";
                // Label the grounded arm from the dataset file name (same heuristic as the card's
                // DocLabel): a "skill" dataset ships SKILL.md, otherwise AGENTS.md.
                var docLabel = System.IO.Path.GetFileName(f).ToLowerInvariant().Contains("skill") ? "SKILL.md" : "AGENTS.md";
                EmitTable(rungs, unit, d.Model ?? "?", d.JudgeModel, docLabel, skillIds);
                if (svgPath is { Length: > 0 })
                {
                    var multi = primary.Count > 1 || (d.Verdicts?.Count ?? 0) > 1;
                    var outPath = multi ? SvgVariant(svgPath, f, unit, d.Model, vi) : svgPath;
                    File.WriteAllText(outPath, BuildSvg(rungs, unit, d.Model ?? "?", docLabel, skillIds));
                    _o.WriteLine($"\n_LIET curve written to `{outPath}`._");
                    // Companion archaeology chart: same levelized x-axis, external-digging on y.
                    var archPath = ArchVariant(outPath);
                    File.WriteAllText(archPath, BuildArchSvg(rungs, unit, d.Model ?? "?", docLabel));
                    _o.WriteLine($"_Archaeology curve written to `{archPath}`._");
                }
                vi++;
            }
        }
    }

    // README grounding = the readme dataset's `skilledIsolated` arm, keyed by rung name.
    private static Dictionary<string, Point> BuildReadmeMap(ResultsFile d, IetScheme iet)
    {
        var map = new Dictionary<string, Point>();
        foreach (var v in d.Verdicts ?? new())
            foreach (var sc in v.Scenarios ?? new())
            {
                var a = Loader.Row(Loader.ArmOf(sc, "skilledIsolated"), iet);
                map[(sc.ScenarioName ?? "").Split(':')[0].Trim()] =
                    new Point { Present = a is not null, Passed = Correct(a), Iet = a?.Iet ?? 0 };
            }
        return map;
    }

    private static List<Rung> BuildRungs(Verdict v, IetScheme iet, bool oracleFromPlugin, string groundArm,
        Dictionary<string, Point>? readmeMap = null)
    {
        var rungs = new List<Rung>();
        int i = 0;
        foreach (var sc in v.Scenarios ?? new())
        {
            var b = Loader.Row(Loader.ArmOf(sc, "baseline"), iet);
            var a = Loader.Row(Loader.ArmOf(sc, groundArm), iet);
            // skilledPlugin is a DELIVERY variant of the same grounding doc (whole-shelf self-select).
            // It is read as the oracle ONLY when --oracle-from-plugin is set (and it is not already the
            // primary arm). Otherwise the ceiling is baseline alone: "grounding must beat knowing nothing".
            var o = oracleFromPlugin && groundArm != "skilledPlugin" ? Loader.Row(Loader.ArmOf(sc, "skilledPlugin"), iet) : null;
            var r = new Rung
            {
                Name = (sc.ScenarioName ?? "").Split(':')[0].Trim(),
                Index = i++,
                Base = new Point { Present = b is not null, Passed = Correct(b), Iet = b?.Iet ?? 0, Arch = Arch(b), Secs = b?.Secs ?? 0 },
                Ag = new Point { Present = a is not null, Passed = Correct(a), Iet = a?.Iet ?? 0, Arch = Arch(a), Secs = a?.Secs ?? 0 },
                Oracle = new Point { Present = o is not null, Passed = Correct(o), Iet = o?.Iet ?? 0, Arch = Arch(o), Secs = o?.Secs ?? 0 },
            };
            if (readmeMap is not null && readmeMap.TryGetValue(r.Name, out var rm)) r.Readme = rm;
            r.Skills = Loader.DetectedSkillsOf(sc, groundArm).Distinct().ToList();
            r.Expected = string.IsNullOrWhiteSpace(sc.ExpectedSkill) ? null : sc.ExpectedSkill.Trim();
            // Competitor envelope for AGENTS.md = min IET of baseline + oracle that passed. README is
            // PLOTTED as a reference series but kept OUT of the ceiling: where README≈AGENTS (common),
            // per-rung README-vs-AGENTS differences are n=1 noise and would flip win/harm spuriously.
            var comp = new List<double>();
            if (r.Base.Passed) comp.Add(r.Base.Iet);
            if (r.Oracle.Passed) comp.Add(r.Oracle.Iet);
            r.Ceiling = comp.Count > 0 ? comp.Min() : (double?)null;
            r.Region = Classify(r);
            rungs.Add(r);
        }
        return rungs;
    }

    // Region precedence (see docs/liet.md "Regions"):
    private static string Classify(Rung r)
    {
        if (!r.Ag.Passed)
        {
            if (r.Base.Passed) return "regression";            // baseline ok, AGENTS wrong — harm veto
            if (r.Ceiling is not null) return "ceiling";       // AGENTS absent; oracle sets the max price
            return "unreached";                                // nobody answered
        }
        if (!r.Base.Passed) return "unlock";                   // AGENTS climbs where baseline can't
        // both baseline and AGENTS passed — efficiency region
        return r.Ceiling is { } c && r.Ag.Iet <= c ? "win" : "harm";
    }

    // ---- table ----------------------------------------------------------------------------------

    private void EmitTable(List<Rung> rungs, string unit, string model, string? judge, string docLabel,
        Dictionary<string, string> ids)
    {
        var ds = docLabel.Replace(".md", "");
        if (!NoTitle) _o.WriteLine($"### LIET curve — {unit} | `{model}`\n");
        _o.WriteLine($"_Per-rung IET (levelized) by arm; difficulty = measured baseline IET where the baseline "
            + $"answers, else grounded IET (three-tier: baseline-correct, then grounded-only unlocks, then neither). "
            + $"`✗` = arm failed "
            + $"(not plotted, not extrapolated). Ceiling = min IET of passing competitors — the max price "
            + $"`{docLabel}` may pay. Judge `{judge ?? "?"}`. IET model {IetModels.CaptionFor(new[] { model })}._\n");
        bool hasReadme = rungs.Any(r => r.Readme.Present);
        var rHdr = hasReadme ? " `README.md` |" : "";
        var rSep = hasReadme ? " ---: |" : "";
        var legend = CardLegend(rungs, ids);
        bool hasSkills = legend.Count > 0;
        var sHdr = hasSkills ? " skills |" : "";
        var sSep = hasSkills ? " --- |" : "";
        _o.WriteLine($"| rung | baseline |{rHdr} `{docLabel}` | oracle | ceiling | region |{sHdr}");
        _o.WriteLine($"| --- | ---: |{rSep} ---: | ---: | ---: | --- |{sSep}");
        foreach (var r in rungs)
        {
            var rCell = hasReadme ? $" {Cell(r.Readme)} |" : "";
            var sCell = hasSkills ? $" {SkillCell(r, ids)} |" : "";
            _o.WriteLine($"| {r.Name} | {Cell(r.Base)} |{rCell} {AgCell(r)} | {Cell(r.Oracle)} "
                + $"| {(r.Ceiling is { } c ? K(c) : "—")} | {RegionTag(r.Region, ds)} |{sCell}");
        }
        if (hasSkills)
        {
            var leg = string.Join(" · ", legend.Select(l => $"`{l.id}`={l.name} ({LegendCount(l.id, l.expected, l.pulled)})"));
            _o.WriteLine($"\n_Skills pulled (self-select from shelf) vs the methodology prior: {leg}. "
                + $"`exp` = tasks that target the skill; `pull` = tasks that actually pulled it. "
                + $"exp ×0 & pull ≤1 ⇒ deletion candidate; exp high & pull low ⇒ under-fire (discovery gap, keep)._");
        }
        var scr = Score(rungs);
        var fm = FloorMetric(rungs);
        var th = TargetHit(rungs);
        var hit = th.total > 0 ? $" · target-skill hit {th.hits}/{th.total}" : "";
        _o.WriteLine($"\n**Score:** correct {scr.baseCorrect}→{scr.agCorrect}/{scr.total} · "
            + $"IET per baseline-correct answer (over floor {K(fm.floor)}, all 24 incl. waste) "
            + $"{K(fm.basePerCorrect)}→{K(fm.agPerCorrect)} (Δ {SignedK(fm.agPerCorrect - fm.basePerCorrect)}/answer) · "
            + $"archaeology {N(scr.baseArch)}→{N(scr.agArch)} calls ({PctStr(scr.baseArch, scr.agArch)}){hit}.");
        EmitScalars(rungs, ds);
        EmitInvestigate(rungs, ds);
        _o.WriteLine();
    }

    // Ranked worklist of rungs where the grounded arm is WORSE than baseline, so a skill author knows
    // exactly where to look and in what order. Primary signal = archaeology (the skill was supposed to
    // delete external digging; a positive delta means it dug MORE despite the skill). Correctness
    // regressions (baseline ✓, grounded ✗) always surface, and per-rung IET Δ is shown for context.
    private void EmitInvestigate(List<Rung> rungs, string ds)
    {
        var rows = rungs
            .Select(r => new
            {
                r.Name,
                ArchDelta = r.Ag.Arch - r.Base.Arch,          // + = grounded dug more (worse)
                r.Base, r.Ag,
                Regression = r.Base.Passed && !r.Ag.Passed,   // baseline answered, grounded did not
                IetDelta = r.Base.Passed && r.Ag.Passed ? r.Ag.Iet - r.Base.Iet : (double?)null,
            })
            // Worth investigating: dug more than baseline, OR a correctness regression. Exclude
            // UNLOCKS (baseline ✗, grounded ✓) — the skill answered where baseline couldn't, so a
            // little extra digging there is the skill doing BETTER, not worse.
            .Where(x => (x.ArchDelta > 0.05 || x.Regression) && !(!x.Base.Passed && x.Ag.Passed))
            // Regressions first, then by how much MORE the grounded arm dug (largest gap on top).
            .OrderByDescending(x => x.Regression)
            .ThenByDescending(x => x.ArchDelta)
            .ToList();
        if (rows.Count == 0) return;

        _o.WriteLine($"\n**Investigate** ({ds} worse than baseline — ranked by archaeology gap; regressions first):");
        _o.WriteLine("\n| # | rung | correctness | archaeology (base→{0}) | Δ arch | Δ IET |".Replace("{0}", ds));
        _o.WriteLine("| --- | --- | --- | ---: | ---: | ---: |");
        int i = 1;
        foreach (var x in rows)
        {
            var correctness = x.Regression ? "**regression** (base ✓, ✗)"
                : !x.Ag.Passed ? "both ✗"
                : !x.Base.Passed ? "unlock (base ✗, ✓)"
                : "both ✓";
            var idelta = x.IetDelta is { } id ? SignedK(id) : "—";
            _o.WriteLine($"| {i++} | {x.Name} | {correctness} | {N(x.Base.Arch)}→{N(x.Ag.Arch)} "
                + $"| {(x.ArchDelta > 0.05 ? "+" + N(x.ArchDelta) : "—")} | {idelta} |");
        }
    }

    // Global skill-id map: `M` = base skill, domain skills alphabetical → 1..N over the union of ALL
    // rendered datasets. Alphabetical (not by count) and shared across cards so an id means the same
    // skill in every table/image — opus and haiku legends line up.
    private static Dictionary<string, string> BuildGlobalIds(IEnumerable<string> allNames, string baseName)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = new HashSet<string>(allNames, StringComparer.Ordinal);
        if (names.Remove(baseName) || !string.IsNullOrEmpty(baseName)) ids[baseName] = "M";
        int n = 1;
        foreach (var name in names.OrderBy(k => k, StringComparer.Ordinal)) ids[name] = (n++).ToString();
        return ids;
    }

    // This card's legend: every skill either PULLED in these rungs or EXPECTED by the methodology
    // (union), with this card's pull count and the prior's expected count, ordered by the shared
    // global id (M first, then 1..N). Expected-but-unpulled skills (pull ×0) surface under-fire gaps.
    private static List<(string id, string name, int expected, int pulled)> CardLegend(List<Rung> rungs, Dictionary<string, string> ids)
    {
        var pulled = new Dictionary<string, int>(StringComparer.Ordinal);
        var expected = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rungs)
        {
            foreach (var s in r.Skills)
                pulled[s] = pulled.TryGetValue(s, out var c) ? c + 1 : 1;
            if (r.Expected is { Length: > 0 } e)
                expected[e] = expected.TryGetValue(e, out var ec) ? ec + 1 : 1;
        }
        var keys = new HashSet<string>(pulled.Keys, StringComparer.Ordinal);
        keys.UnionWith(expected.Keys);
        return keys.Select(k => (
                id: ids.TryGetValue(k, out var i) ? i : "?",
                name: k,
                expected: expected.TryGetValue(k, out var ec) ? ec : 0,
                pulled: pulled.TryGetValue(k, out var pc) ? pc : 0))
            .OrderBy(t => t.id == "M" ? "" : t.id, StringComparer.Ordinal).ToList();
    }

    // Legend count suffix. M is the base skill (expected everywhere → show pull only). Domain skills
    // show the prior's expected count next to the actual pull count so over/under-fire is visible.
    private static string LegendCount(string id, int expected, int pulled) =>
        id == "M" ? $"\u00d7{pulled}"
        : expected > 0 ? $"exp \u00d7{expected} \u00b7 pull \u00d7{pulled}"
        : $"pull \u00d7{pulled}";

    // Target-skill hit rate: rungs whose ONE expected skill was among the pulled set. Basics target
    // the base skill (M). Returns (hits, tasksWithAPrior); empty when no prior is plumbed.
    private static (int hits, int total) TargetHit(List<Rung> rungs)
    {
        int hits = 0, total = 0;
        foreach (var r in rungs.Where(r => r.Expected is { Length: > 0 }))
        {
            total++;
            if (r.Skills.Contains(r.Expected!, StringComparer.Ordinal)) hits++;
        }
        return (hits, total);
    }

    // Rung's pulled skills as id tokens (M first, then numeric), e.g. "M 3" or "—" if none pulled.
    private static string SkillCell(Rung r, Dictionary<string, string> ids)
    {
        if (r.Skills.Count == 0) return "—";
        var tokens = r.Skills
            .Select(s => ids.TryGetValue(s, out var id) ? id : "?")
            .OrderBy(t => t == "M" ? "" : t, StringComparer.Ordinal);
        return string.Join(" ", tokens);
    }

    private static string Cell(Point p) => !p.Present ? "—" : p.Passed ? K(p.Iet) : "✗";

    private static string AgCell(Rung r)
    {
        if (!r.Ag.Present) return "—";
        if (!r.Ag.Passed) return "✗";
        var pays = r.Ceiling is { } c ? (r.Ag.Iet <= c ? " ✓" : " ⚠") : "";
        return K(r.Ag.Iet) + pays;
    }

    private static string RegionTag(string region, string ds) => region switch
    {
        "win" => "**win** (under ceiling)",
        "harm" => "harm (over ceiling)",
        "unlock" => "unlock (baseline ✗)",
        "regression" => "**regression** (harm veto)",
        "ceiling" => $"ceiling only ({ds} ✗)",
        "unreached" => "unreached",
        _ => region,
    };

    // Honest scalar reductions of the curve (all difficulty-aware; never a cross-difficulty mean).
    private void EmitScalars(List<Rung> rungs, string ds)
    {
        var shared = rungs.Where(r => r.Base.Passed && r.Ag.Passed).ToList();
        var sb = new StringBuilder("\n");

        // Value delivered on the shared region: baseline − grounded, summed and per-rung.
        if (shared.Count > 0)
        {
            double val = shared.Sum(r => r.Base.Iet - r.Ag.Iet);
            double ratio = shared.Sum(r => r.Base.Iet) / Math.Max(1, shared.Sum(r => r.Ag.Iet));
            sb.Append($"- **Value delivered** (shared region, {shared.Count} rung(s)): baseline − {ds} = {K(val)} IET "
                + $"(baseline {ratio.ToString("0.0", Inv)}× {ds}).\n");
            // Shared-region slope: mean per-rung step for each arm across the shared rungs (efficiency).
            if (shared.Count >= 2)
            {
                double bs = Slope(shared.Select(r => r.Base.Iet)), as_ = Slope(shared.Select(r => r.Ag.Iet));
                sb.Append($"- **Shared-region slope** (efficiency): baseline {Signed(bs)}/rung vs {ds} {Signed(as_)}/rung.\n");
            }
        }

        // Unlock: rungs the grounded arm answered that baseline did not.
        var unlock = rungs.Where(r => r.Ag.Passed && !r.Base.Passed).Select(r => r.Name).ToList();
        if (unlock.Count > 0) sb.Append($"- **Unlocked** (baseline ✗, {ds} ✓): {string.Join(", ", unlock)}.\n");

        // Regressions (harm veto).
        var regr = rungs.Where(r => r.Region == "regression").Select(r => r.Name).ToList();
        if (regr.Count > 0) sb.Append($"- **Regressions** (baseline ✓, {ds} ✗ — harm veto): {string.Join(", ", regr)}.\n");

        // Harm region (grounded arm over the ceiling on rungs a competitor is cheaper).
        var harm = rungs.Where(r => r.Region == "harm").Select(r => r.Name).ToList();
        if (harm.Count > 0) sb.Append($"- **Harm region** ({ds} over ceiling): {string.Join(", ", harm)}.\n");

        // Divergence rung (knee): the shared rung with the largest upward step in the grounded curve.
        var knee = Knee(rungs.Where(r => r.Ag.Passed).ToList());
        if (knee is not null) sb.Append($"- **Divergence rung** ({ds} knee): {knee}.\n");

        // Handoff rung: where the binding ceiling switches from baseline to oracle.
        var handoff = Handoff(rungs);
        if (handoff is not null) sb.Append($"- **Handoff rung** (ceiling baseline→oracle): {handoff}.\n");

        _o.Write(sb.ToString());
    }

    private static double Slope(IEnumerable<double> ys)
    {
        var a = ys.ToList();
        return a.Count < 2 ? 0 : (a[^1] - a[0]) / (a.Count - 1);
    }

    private static string? Knee(List<Rung> agPassed)
    {
        string? knee = null; double best = double.NegativeInfinity;
        for (int i = 1; i < agPassed.Count; i++)
        {
            double step = agPassed[i].Ag.Iet - agPassed[i - 1].Ag.Iet;
            if (step > best) { best = step; knee = agPassed[i].Name; }
        }
        return best > 0 ? knee : null;
    }

    private static string? Handoff(List<Rung> rungs)
    {
        // First rung where baseline stops being the (passing) ceiling and the oracle takes over.
        bool seenBaselineCeil = false;
        foreach (var r in rungs)
        {
            bool baseIsCeil = r.Base.Passed && (!r.Oracle.Passed || r.Base.Iet <= r.Oracle.Iet);
            bool oracleIsCeil = r.Oracle.Passed && (!r.Base.Passed || r.Oracle.Iet < r.Base.Iet);
            if (baseIsCeil) seenBaselineCeil = true;
            else if (oracleIsCeil && seenBaselineCeil) return r.Name;
        }
        return null;
    }

    // ---- SVG ------------------------------------------------------------------------------------

    private static string BuildSvg(List<Rung> rungs, string unit, string model, string docLabel,
        Dictionary<string, string> skillIds)
    {
        const int W = 760, L = 100, R = 664, T = 64, B = 380; // plot box
        var skillLegend = CardLegend(rungs, skillIds);
        // Footer stacks under the plot: circle legend, then a vertical bulleted skill list, then a
        // bulleted Metrics block. Grow the canvas to fit both variable-length lists.
        int footerBullets = skillLegend.Count;
        var thh = TargetHit(rungs);
        int metricsBullets = 4 + (thh.total > 0 ? 1 : 0);
        int metricsHeadY = footerBullets > 0 ? B + 104 + 14 + footerBullets * 14 + 6 : B + 108;
        int H = metricsHeadY + 14 + metricsBullets * 14 + 12; // Metrics heading + bullets + bottom margin
        double maxIet = rungs.SelectMany(r => new[]
        {
            r.Base.Passed ? r.Base.Iet : 0, r.Ag.Passed ? r.Ag.Iet : 0,
            r.Readme.Passed ? r.Readme.Iet : 0,
            r.Oracle.Passed ? r.Oracle.Iet : 0, r.Ceiling ?? 0,
        }).DefaultIfEmpty(1).Max();
        maxIet = Math.Max(maxIet, 1);
        int n = rungs.Count;
        double yStep = NiceStep(maxIet / 4.0);
        maxIet = Math.Ceiling(maxIet / yStep) * yStep;   // snap the top to a round gridline value
        double X(int i) => n <= 1 ? (L + R) / 2.0 : L + (R - L) * i / (n - 1);
        double Y(double v) => B - (B - T) * (v / maxIet);
        // Secondary axis: end-to-end wall-clock seconds. Plotted for EVERY present rung (answered or
        // not) — wall-time is spent regardless of correctness — so these lines span the full x-range,
        // unlike the per-correct IET curves. Kept visually subordinate (thin, dashed, muted) against a
        // right axis; where two axes cross is arbitrary, so correctness stays the story.
        double maxSecs = rungs.SelectMany(r => new[] { r.Base.Present ? r.Base.Secs : 0, r.Ag.Present ? r.Ag.Secs : 0 })
            .DefaultIfEmpty(1).Max();
        maxSecs = Math.Max(maxSecs, 1);
        double secStep = NiceStep(maxSecs / 4.0);
        maxSecs = Math.Ceiling(maxSecs / secStep) * secStep;
        double Ysec(double v) => B - (B - T) * (v / maxSecs);

        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {W} {H}\" xmlns=\"http://www.w3.org/2000/svg\" font-family=\"ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, sans-serif\">\n");
        sb.Append($"  <rect width=\"{W}\" height=\"{H}\" fill=\"#ffffff\"/>\n");
        sb.Append($"  <text x=\"{(L + R) / 2}\" y=\"30\" text-anchor=\"middle\" font-size=\"17\" font-weight=\"700\" fill=\"#0f172a\">LIET curve — {Esc(unit)} ({Esc(model)})</text>\n");
        // axes
        sb.Append($"  <line x1=\"{L}\" y1=\"{B}\" x2=\"{R}\" y2=\"{B}\" stroke=\"#334155\" stroke-width=\"1.5\"/>\n");
        sb.Append($"  <line x1=\"{L}\" y1=\"{B}\" x2=\"{L}\" y2=\"{T}\" stroke=\"#334155\" stroke-width=\"1.5\"/>\n");
        // y-axis scale: round gridlines + numeric labels (IET in tokens, `k` = thousands) so the
        // curve magnitude is readable and the arms are directly comparable in absolute cost.
        for (double gv = 0; gv <= maxIet + yStep * 0.01; gv += yStep)
        {
            double gy = Y(gv);
            if (gv > 0) sb.Append($"  <line x1=\"{L}\" y1=\"{N(gy)}\" x2=\"{R}\" y2=\"{N(gy)}\" stroke=\"#e2e8f0\" stroke-width=\"1\"/>\n");
            sb.Append($"  <text x=\"{L - 8}\" y=\"{N(gy + 4)}\" text-anchor=\"end\" font-size=\"10.5\" fill=\"#64748b\">{K(gv)}</text>\n");
        }
        sb.Append($"  <text x=\"{(L + R) / 2}\" y=\"438\" text-anchor=\"middle\" font-size=\"12\" fill=\"#334155\">rung (difficulty) →</text>\n");
        sb.Append($"  <text x=\"46\" y=\"222\" text-anchor=\"middle\" font-size=\"12\" fill=\"#334155\" transform=\"rotate(-90 46 222)\">IET per correct answer (tokens) →</text>\n");
        // right y-axis: wall-clock seconds (secondary, subordinate axis for the duration overlay).
        sb.Append($"  <line x1=\"{R}\" y1=\"{B}\" x2=\"{R}\" y2=\"{T}\" stroke=\"#94a3b8\" stroke-width=\"1.2\"/>\n");
        for (double gv = secStep; gv <= maxSecs + secStep * 0.01; gv += secStep)
        {
            double gy = Ysec(gv);
            sb.Append($"  <line x1=\"{R}\" y1=\"{N(gy)}\" x2=\"{R + 4}\" y2=\"{N(gy)}\" stroke=\"#94a3b8\" stroke-width=\"1\"/>\n");
            sb.Append($"  <text x=\"{R + 7}\" y=\"{N(gy + 4)}\" text-anchor=\"start\" font-size=\"10\" fill=\"#94a3b8\">{N(gv)}s</text>\n");
        }
        sb.Append($"  <text x=\"{R + 44}\" y=\"222\" text-anchor=\"middle\" font-size=\"10.5\" fill=\"#94a3b8\" transform=\"rotate(-90 {R + 44} 222)\">wall-clock per question (s), all rungs →</text>\n");
        // Floor reference: the LIET metric's zero-point (mean levelized IET of the baseline's correct
        // basics). A light dashed green line so each rung's cost reads as height OVER the floor.
        {
            double floorV = FloorMetric(rungs).floor;
            if (floorV > 0)
            {
                double fY = Y(floorV);
                sb.Append($"  <line x1=\"{L}\" y1=\"{N(fY)}\" x2=\"{R}\" y2=\"{N(fY)}\" stroke=\"#22c55e\" stroke-width=\"1.4\" stroke-dasharray=\"6 5\" opacity=\"0.7\"/>\n");
                sb.Append($"  <text x=\"{L + 6}\" y=\"{N(fY - 4)}\" font-size=\"10\" fill=\"#16a34a\">floor {K(floorV)}</text>\n");
            }
        }
        // arms legend (top-left, inside plot): one line swatch per plotted arm so the baseline,
        // README and oracle curves are named, not just the AGENTS.md dots explained below.
        {
            double lx = L + 12, ly = T + 8; int row = 0;
            bool hasOracle = rungs.Any(r => r.Oracle.Passed), hasReadme = rungs.Any(r => r.Readme.Present);
            sb.Append("  <g font-size=\"11\" fill=\"#475569\">\n");
            void Swatch(string color, string label)
            {
                double y = ly + row++ * 16;
                sb.Append($"    <line x1=\"{N(lx)}\" y1=\"{N(y)}\" x2=\"{N(lx + 22)}\" y2=\"{N(y)}\" stroke=\"{color}\" stroke-width=\"2.5\"/><text x=\"{N(lx + 28)}\" y=\"{N(y + 4)}\">{Esc(label)}</text>\n");
            }
            Swatch("#dc2626", "baseline (archaeology only)");
            if (hasReadme) Swatch("#7c3aed", "README.md (packed)");
            Swatch("#2563eb", docLabel);
            if (hasOracle) Swatch("#d97706", "SKILL.md (oracle)");
            // duration overlay swatches (secondary right axis; dashed to match the muted lines)
            void DashSwatch(string color, string label)
            {
                double y = ly + row++ * 16;
                sb.Append($"    <line x1=\"{N(lx)}\" y1=\"{N(y)}\" x2=\"{N(lx + 22)}\" y2=\"{N(y)}\" stroke=\"{color}\" stroke-width=\"1.6\" stroke-dasharray=\"4 3\"/><text x=\"{N(lx + 28)}\" y=\"{N(y + 4)}\" font-style=\"italic\">{Esc(label)} · s, right axis</text>\n");
            }
            DashSwatch("#c4a484", "baseline time");
            DashSwatch("#5eaaa8", "grounded time");
            sb.Append("  </g>\n");
        }
        // rung ticks
        sb.Append("  <g font-size=\"11\" fill=\"#64748b\" text-anchor=\"middle\">\n");
        for (int i = 0; i < n; i++) sb.Append($"    <text x=\"{N(X(i))}\" y=\"{B + 18}\">{Esc(ShortRung(rungs[i].Name))}</text>\n");
        sb.Append("  </g>\n");
        // ceilings on rungs where AGENTS failed (max price of generalization): a dashed ceiling
        // line with a green "pays its way" zone under it and a red "ship SKILL.md" zone over it.
        foreach (var r in rungs.Where(r => !r.Ag.Passed && r.Ceiling is not null))
        {
            double y = Y(r.Ceiling!.Value), x = X(r.Index); const double hw = 28;
            sb.Append($"  <rect x=\"{N(x - hw)}\" y=\"{N(y)}\" width=\"{N(2 * hw)}\" height=\"{N(B - y)}\" fill=\"#bbf7d0\" opacity=\"0.4\"/>\n");
            sb.Append($"  <rect x=\"{N(x - hw)}\" y=\"{T}\" width=\"{N(2 * hw)}\" height=\"{N(y - T)}\" fill=\"#fecaca\" opacity=\"0.4\"/>\n");
            sb.Append($"  <line x1=\"{N(x - hw)}\" y1=\"{N(y)}\" x2=\"{N(x + hw)}\" y2=\"{N(y)}\" stroke=\"#b45309\" stroke-width=\"2\" stroke-dasharray=\"5 4\"/>\n");
            sb.Append($"  <text x=\"{N(x)}\" y=\"{B + 44}\" text-anchor=\"middle\" font-size=\"9.5\" font-weight=\"700\" fill=\"#1d4ed8\">{Esc(docLabel.Replace(".md", ""))} ✗</text>\n");
        }
        // duration overlay (secondary axis): baseline + grounded wall-clock, muted/dashed, no dots,
        // spanning every rung (including unanswered). Drawn UNDER the IET series so correctness leads.
        sb.Append(DurSeries(rungs, r => r.Base, "#c4a484", "baseline time", X, Ysec));
        sb.Append(DurSeries(rungs, r => r.Ag,   "#5eaaa8", docLabel.Replace(".md", "") + " time", X, Ysec));
        // series
        sb.Append(Series(rungs, p => p.Oracle, "#d97706", "SKILL.md (oracle)", X, Y, false));
        sb.Append(Series(rungs, p => p.Base, "#dc2626", "baseline", X, Y, false));
        sb.Append(Series(rungs, p => p.Readme, "#7c3aed", "README.md", X, Y, false));
        sb.Append(Series(rungs, p => p.Ag, "#2563eb", docLabel, X, Y, true));
        // skill ids as a small sub-label under each plotted SKILL.md dot (which skills this rung pulled).
        if (skillLegend.Count > 0)
        {
            sb.Append("  <g font-size=\"8\" font-weight=\"700\" fill=\"#0891b2\" text-anchor=\"middle\">\n");
            foreach (var r in rungs.Where(r => r.Ag.Passed))
            {
                var cell = SkillCell(r, skillIds);
                if (cell != "—") sb.Append($"    <text x=\"{N(X(r.Index))}\" y=\"{N(Y(r.Ag.Iet) + 13)}\">{Esc(cell)}</text>\n");
            }
            sb.Append("  </g>\n");
        }
        // footer legend — circle legend row, then a vertical bulleted skill list beneath it so the
        // long skill names never run off the canvas.
        sb.Append("  <g font-size=\"10.5\" fill=\"#475569\">\n");
        sb.Append($"    <circle cx=\"112\" cy=\"{B + 82}\" r=\"4\" fill=\"none\" stroke=\"#2563eb\" stroke-width=\"2\"/><text x=\"122\" y=\"{B + 86}\">{Esc(docLabel.Replace(".md", ""))} over ceiling (harm)</text>\n");
        sb.Append($"    <circle cx=\"290\" cy=\"{B + 82}\" r=\"4\" fill=\"#2563eb\"/><text x=\"300\" y=\"{B + 86}\">{Esc(docLabel.Replace(".md", ""))} under ceiling (pays its way)</text>\n");
        sb.Append($"    <text x=\"540\" y=\"{B + 86}\" font-style=\"italic\">failed rungs not plotted</text>\n");
        sb.Append("  </g>\n");
        int metricsY = B + 108;
        if (skillLegend.Count > 0)
        {
            int hy = B + 104;
            sb.Append($"  <text x=\"{L + 12}\" y=\"{hy}\" font-size=\"9.5\" font-weight=\"700\" fill=\"#0891b2\">skills pulled (id under each {Esc(docLabel.Replace(".md", ""))} dot):</text>\n");
            sb.Append("  <g font-size=\"9.5\" fill=\"#0891b2\">\n");
            for (int i = 0; i < skillLegend.Count; i++)
            {
                var l = skillLegend[i];
                sb.Append($"    <text x=\"{L + 20}\" y=\"{hy + 14 + i * 14}\">\u2022 <tspan font-weight=\"700\">{Esc(l.id)}</tspan> = {Esc(l.name)} ({Esc(LegendCount(l.id, l.expected, l.pulled))})</text>\n");
            }
            sb.Append("  </g>\n");
            metricsY = hy + 14 + skillLegend.Count * 14 + 6;
        }
        var sc = Score(rungs);
        var fm = FloorMetric(rungs);
        var dm = FloorMetric(rungs, p => p.Secs);   // identical levelization on wall-clock duration
        var th = TargetHit(rungs);
        var metrics = new List<string>
        {
            $"Correct answers: {sc.baseCorrect}\u2192{sc.agCorrect}/{sc.total}",
            $"Floor LIET: {K(fm.floor)}",
            $"LIET: {K(fm.basePerCorrect)}\u2192{K(fm.agPerCorrect)} (\u0394 {SignedK(fm.agPerCorrect - fm.basePerCorrect)})",
            $"Levelized duration: {Secs(dm.basePerCorrect)}\u2192{Secs(dm.agPerCorrect)} (\u0394 {SignedSecs(dm.agPerCorrect - dm.basePerCorrect)})",
            $"Archaeology operations: {N(sc.baseArch)}\u2192{N(sc.agArch)} ({PctStr(sc.baseArch, sc.agArch)})",
        };
        if (th.total > 0) metrics.Add($"Expected skill pulled: {th.hits}/{th.total}");
        sb.Append($"  <text x=\"{L + 12}\" y=\"{metricsY}\" font-size=\"9.5\" font-weight=\"700\" fill=\"#334155\">Metrics:</text>\n");
        sb.Append("  <g font-size=\"9.5\" fill=\"#334155\">\n");
        for (int i = 0; i < metrics.Count; i++)
            sb.Append($"    <text x=\"{L + 20}\" y=\"{metricsY + 14 + i * 14}\">\u2022 {Esc(metrics[i])}</text>\n");
        sb.Append("  </g>\n");
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    // Duration overlay line for the secondary (wall-clock) axis: connects EVERY present rung in order
    // (answered or not — wall-time is spent regardless), thin + dashed + muted, no markers. A
    // subordinate companion to the per-correct IET curve.
    private static string DurSeries(List<Rung> rungs, Func<Rung, Point> sel, string color, string label,
        Func<int, double> X, Func<double, double> Ysec)
    {
        var pts = rungs.Where(r => sel(r).Present).Select(r => (x: X(r.Index), y: Ysec(sel(r).Secs), passed: sel(r).Passed)).ToList();
        if (pts.Count < 2) return "";
        var sb = new StringBuilder();
        sb.Append($"  <polyline points=\"{string.Join(" ", pts.Select(p => $"{N(p.x)},{N(p.y)}"))}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.6\" stroke-dasharray=\"4 3\" opacity=\"0.85\"/>\n");
        // node dots so per-rung values read even across straight multi-rung runs. Match the big
        // markers' convention: filled = this arm PASSED the rung, open = failed (duration is spent
        // either way). Small + muted so they stay subordinate to the IET/archaeology markers.
        foreach (var p in pts)
            sb.Append(p.passed
                ? $"  <circle cx=\"{N(p.x)}\" cy=\"{N(p.y)}\" r=\"2\" fill=\"{color}\" opacity=\"0.85\"/>\n"
                : $"  <circle cx=\"{N(p.x)}\" cy=\"{N(p.y)}\" r=\"2\" fill=\"white\" stroke=\"{color}\" stroke-width=\"1.2\" opacity=\"0.85\"/>\n");
        var last = pts[^1];
        sb.Append($"  <text x=\"{N(last.x - 4)}\" y=\"{N(last.y - 6)}\" text-anchor=\"end\" font-size=\"10\" font-style=\"italic\" fill=\"{color}\">{Esc(label)}</text>\n");
        return sb.ToString();
    }

    private static string Series(List<Rung> rungs, Func<Rung, Point> sel, string color, string label,
        Func<int, double> X, Func<double, double> Y, bool isAgents)
    {
        var pts = rungs.Where(r => sel(r).Passed).Select(r => (x: X(r.Index), y: Y(sel(r).Iet), r)).ToList();
        if (pts.Count == 0) return "";
        var sb = new StringBuilder();
        // Connect only CONSECUTIVE passed rungs. A gap (an interior failed rung) breaks the line so
        // no solid segment crosses a difficulty where this arm produced no correct answer — the
        // failed rung is neither plotted nor implied (docs/liet.md).
        var segment = new List<(double x, double y)>();
        int? prevIndex = null;
        void Flush()
        {
            if (segment.Count >= 2)
                sb.Append($"  <polyline points=\"{string.Join(" ", segment.Select(p => $"{N(p.x)},{N(p.y)}"))}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2.5\"/>\n");
            segment.Clear();
        }
        foreach (var p in pts)
        {
            if (prevIndex is { } pi && p.r.Index != pi + 1) Flush();
            segment.Add((p.x, p.y));
            prevIndex = p.r.Index;
        }
        Flush();
        // markers
        foreach (var p in pts)
        {
            if (isAgents)
            {
                bool over = sel(p.r).Iet > (p.r.Ceiling ?? double.PositiveInfinity);
                sb.Append(over
                    ? $"  <circle cx=\"{N(p.x)}\" cy=\"{N(p.y)}\" r=\"4\" fill=\"#ffffff\" stroke=\"{color}\" stroke-width=\"2\"/>\n"
                    : $"  <circle cx=\"{N(p.x)}\" cy=\"{N(p.y)}\" r=\"4\" fill=\"{color}\"/>\n");
            }
            else sb.Append($"  <circle cx=\"{N(p.x)}\" cy=\"{N(p.y)}\" r=\"3.5\" fill=\"{color}\"/>\n");
        }
        // label near the last point
        var last = pts[^1];
        sb.Append($"  <text x=\"{N(last.x - 4)}\" y=\"{N(last.y - 8)}\" text-anchor=\"end\" font-size=\"11.5\" font-weight=\"700\" fill=\"{color}\">{Esc(label)}</text>\n");
        return sb.ToString();
    }

    // Companion archaeology chart: same levelized x-axis as the LIET curve, but y = external digging
    // (nuget-cache + nuget.org + open-web tool calls). Plotted for EVERY present arm (success OR
    // failure — digging happens either way), so the story is "grounding collapses archaeology to ~0".
    private static string BuildArchSvg(List<Rung> rungs, string unit, string model, string docLabel)
    {
        const int W = 760, H = 520, L = 100, R = 664, T = 64, B = 380;
        double maxArch = rungs.SelectMany(r => new[] { r.Base.Present ? r.Base.Arch : 0, r.Ag.Present ? r.Ag.Arch : 0 })
            .DefaultIfEmpty(1).Max();
        maxArch = Math.Max(maxArch, 1);
        int n = rungs.Count;
        double yStep = NiceStep(maxArch / 4.0);
        maxArch = Math.Ceiling(maxArch / yStep) * yStep;
        double X(int i) => n <= 1 ? (L + R) / 2.0 : L + (R - L) * i / (n - 1);
        double Y(double v) => B - (B - T) * (v / maxArch);
        // secondary axis: wall-clock seconds (same overlay as the LIET chart) — to eyeball whether
        // archaeology (external digging) tracks duration.
        double maxSecs = rungs.SelectMany(r => new[] { r.Base.Present ? r.Base.Secs : 0, r.Ag.Present ? r.Ag.Secs : 0 })
            .DefaultIfEmpty(1).Max();
        maxSecs = Math.Max(maxSecs, 1);
        double secStep = NiceStep(maxSecs / 4.0);
        maxSecs = Math.Ceiling(maxSecs / secStep) * secStep;
        double Ysec(double v) => B - (B - T) * (v / maxSecs);
        // The wall-clock duration overlay on the arch chart is an opt-in diagnostic (does archaeology
        // track duration?); default OFF so the published arch charts stay archaeology-only.
        bool durOverlay = Environment.GetEnvironmentVariable("GROUNDING_ARCH_DURATION_OVERLAY") is { Length: > 0 };

        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {W} {H}\" xmlns=\"http://www.w3.org/2000/svg\" font-family=\"ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, sans-serif\">\n");
        sb.Append($"  <rect width=\"{W}\" height=\"{H}\" fill=\"#ffffff\"/>\n");
        sb.Append($"  <text x=\"{(L + R) / 2}\" y=\"30\" text-anchor=\"middle\" font-size=\"17\" font-weight=\"700\" fill=\"#0f172a\">Archaeology — {Esc(unit)} ({Esc(model)})</text>\n");
        sb.Append($"  <line x1=\"{L}\" y1=\"{B}\" x2=\"{R}\" y2=\"{B}\" stroke=\"#334155\" stroke-width=\"1.5\"/>\n");
        sb.Append($"  <line x1=\"{L}\" y1=\"{B}\" x2=\"{L}\" y2=\"{T}\" stroke=\"#334155\" stroke-width=\"1.5\"/>\n");
        for (double gv = 0; gv <= maxArch + yStep * 0.01; gv += yStep)
        {
            double gy = Y(gv);
            if (gv > 0) sb.Append($"  <line x1=\"{L}\" y1=\"{N(gy)}\" x2=\"{R}\" y2=\"{N(gy)}\" stroke=\"#e2e8f0\" stroke-width=\"1\"/>\n");
            sb.Append($"  <text x=\"{L - 8}\" y=\"{N(gy + 4)}\" text-anchor=\"end\" font-size=\"10.5\" fill=\"#64748b\">{K(gv)}</text>\n");
        }
        sb.Append($"  <text x=\"{(L + R) / 2}\" y=\"430\" text-anchor=\"middle\" font-size=\"12\" fill=\"#334155\">rung (difficulty) →</text>\n");
        sb.Append($"  <text x=\"46\" y=\"222\" text-anchor=\"middle\" font-size=\"12\" fill=\"#334155\" transform=\"rotate(-90 46 222)\">archaeology tool calls →</text>\n");
        // right y-axis: wall-clock seconds (secondary, for the duration overlay).
        if (durOverlay)
        {
            sb.Append($"  <line x1=\"{R}\" y1=\"{B}\" x2=\"{R}\" y2=\"{T}\" stroke=\"#94a3b8\" stroke-width=\"1.2\"/>\n");
            for (double gv = secStep; gv <= maxSecs + secStep * 0.01; gv += secStep)
            {
                double gy = Ysec(gv);
                sb.Append($"  <line x1=\"{R}\" y1=\"{N(gy)}\" x2=\"{R + 4}\" y2=\"{N(gy)}\" stroke=\"#94a3b8\" stroke-width=\"1\"/>\n");
                sb.Append($"  <text x=\"{R + 7}\" y=\"{N(gy + 4)}\" text-anchor=\"start\" font-size=\"10\" fill=\"#94a3b8\">{N(gv)}s</text>\n");
            }
            sb.Append($"  <text x=\"{R + 44}\" y=\"222\" text-anchor=\"middle\" font-size=\"10.5\" fill=\"#94a3b8\" transform=\"rotate(-90 {R + 44} 222)\">wall-clock per question (s), all rungs →</text>\n");
        }
        // rung ticks
        sb.Append("  <g font-size=\"11\" fill=\"#64748b\" text-anchor=\"middle\">\n");
        for (int i = 0; i < n; i++) sb.Append($"    <text x=\"{N(X(i))}\" y=\"{B + 18}\">{Esc(ShortRung(rungs[i].Name))}</text>\n");
        sb.Append("  </g>\n");
        // duration overlay (secondary axis), drawn UNDER the archaeology series so digging leads.
        if (durOverlay)
        {
            sb.Append(DurSeries(rungs, r => r.Base, "#c4a484", "baseline time", X, Ysec));
            sb.Append(DurSeries(rungs, r => r.Ag,   "#5eaaa8", docLabel.Replace(".md", "") + " time", X, Ysec));
        }
        // series (every PRESENT arm, connected in rung order — archaeology is effort, not success)
        sb.Append(ArchSeries(rungs, p => p.Base, "#dc2626", false, X, Y));
        sb.Append(ArchSeries(rungs, p => p.Ag, "#2563eb", true, X, Y));
        // arm legend + marker key (open = failed the task, closed = passed; superscript = skills pulled)
        sb.Append("  <g font-size=\"11\" fill=\"#475569\">\n");
        sb.Append($"    <line x1=\"{L + 12}\" y1=\"{T + 8}\" x2=\"{L + 34}\" y2=\"{T + 8}\" stroke=\"#dc2626\" stroke-width=\"2.5\"/><text x=\"{L + 40}\" y=\"{T + 12}\">baseline (archaeology only)</text>\n");
        sb.Append($"    <line x1=\"{L + 12}\" y1=\"{T + 24}\" x2=\"{L + 34}\" y2=\"{T + 24}\" stroke=\"#2563eb\" stroke-width=\"2.5\"/><text x=\"{L + 40}\" y=\"{T + 28}\">{Esc(docLabel)}</text>\n");
        if (durOverlay)
        {
            sb.Append($"    <line x1=\"{L + 12}\" y1=\"{T + 40}\" x2=\"{L + 34}\" y2=\"{T + 40}\" stroke=\"#c4a484\" stroke-width=\"1.6\" stroke-dasharray=\"4 3\"/><text x=\"{L + 40}\" y=\"{T + 44}\" font-style=\"italic\">baseline time · s, right axis</text>\n");
            sb.Append($"    <line x1=\"{L + 12}\" y1=\"{T + 56}\" x2=\"{L + 34}\" y2=\"{T + 56}\" stroke=\"#5eaaa8\" stroke-width=\"1.6\" stroke-dasharray=\"4 3\"/><text x=\"{L + 40}\" y=\"{T + 60}\" font-style=\"italic\">grounded time · s, right axis</text>\n");
        }
        sb.Append("  </g>\n");
        sb.Append("  <g font-size=\"10.5\" fill=\"#475569\">\n");
        sb.Append($"    <circle cx=\"{L + 20}\" cy=\"{B + 62}\" r=\"3.8\" fill=\"#64748b\"/><text x=\"{L + 30}\" y=\"{B + 66}\">passed</text>\n");
        sb.Append($"    <circle cx=\"{L + 96}\" cy=\"{B + 62}\" r=\"3.8\" fill=\"#ffffff\" stroke=\"#64748b\" stroke-width=\"2\"/><text x=\"{L + 106}\" y=\"{B + 66}\">failed</text>\n");
        sb.Append($"    <text x=\"{L + 168}\" y=\"{B + 66}\"><tspan font-weight=\"700\" fill=\"#0891b2\">ⁿ</tspan> = skills pulled on the {Esc(docLabel.Replace(".md", ""))} line</text>\n");
        sb.Append("  </g>\n");
        var sc = Score(rungs);
        var metrics = new[]
        {
            $"Archaeology operations: {N(sc.baseArch)}\u2192{N(sc.agArch)} ({PctStr(sc.baseArch, sc.agArch)})",
            $"Correct answers: {sc.baseCorrect}\u2192{sc.agCorrect}/{sc.total}",
        };
        sb.Append($"  <text x=\"{L + 20}\" y=\"{B + 86}\" font-size=\"9.5\" font-weight=\"700\" fill=\"#334155\">Metrics:</text>\n");
        sb.Append("  <g font-size=\"9.5\" fill=\"#334155\">\n");
        for (int i = 0; i < metrics.Length; i++)
            sb.Append($"    <text x=\"{L + 28}\" y=\"{B + 100 + i * 14}\">\u2022 {Esc(metrics[i])}</text>\n");
        sb.Append("  </g>\n");
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    private static string ArchSeries(List<Rung> rungs, Func<Rung, Point> sel, string color, bool skilled,
        Func<int, double> X, Func<double, double> Y)
    {
        var pres = rungs.Where(r => sel(r).Present).ToList();
        if (pres.Count == 0) return "";
        var sb = new StringBuilder();
        sb.Append($"  <polyline points=\"{string.Join(" ", pres.Select(r => $"{N(X(r.Index))},{N(Y(sel(r).Arch))}"))}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2.5\"/>\n");
        foreach (var r in pres)
        {
            double x = X(r.Index), y = Y(sel(r).Arch);
            // open circle = this arm FAILED the task at this rung, closed = passed. Tells whether the
            // digging paid off (baseline can dig a lot and still fail; SKILL can pass with ~0 digging).
            sb.Append(sel(r).Passed
                ? $"  <circle cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"3.8\" fill=\"{color}\"/>\n"
                : $"  <circle cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"3.8\" fill=\"#ffffff\" stroke=\"{color}\" stroke-width=\"2\"/>\n");
            // superscript on the SKILL line: how many skills it pulled here (how the grounded side wins).
            if (skilled && r.Skills.Count > 0)
                sb.Append($"  <text x=\"{N(x + 5)}\" y=\"{N(y - 5)}\" font-size=\"8\" font-weight=\"700\" fill=\"#0891b2\">{r.Skills.Count}</text>\n");
        }
        return sb.ToString();
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static string K(double v) => Math.Abs(v) >= 1000 ? $"{(v / 1000.0).ToString("0.#", Inv)}k" : v.ToString("0", Inv);
    private static string PctStr(double from, double to)   // signed % change (− = fewer/cheaper)
    {
        if (from <= 0) return "n/a";
        double p = (to - from) / from * 100;
        if (Math.Round(p) == 0) return "0%";
        return (p >= 0 ? "+" : "") + p.ToString("0", Inv) + "%";
    }
    // Headline score for a chart/table: correctness (baseline→grounded) + the chart's key metric.
    private static (int baseCorrect, int agCorrect, int total, double baseArch, double agArch,
        double sharedBaseIet, double sharedAgIet) Score(List<Rung> rungs)
    {
        var sh = rungs.Where(r => r.Base.Passed && r.Ag.Passed).ToList();
        return (rungs.Count(r => r.Base.Passed), rungs.Count(r => r.Ag.Passed), rungs.Count,
            rungs.Where(r => r.Base.Present).Sum(r => r.Base.Arch),
            rungs.Where(r => r.Ag.Present).Sum(r => r.Ag.Arch),
            sh.Sum(r => r.Base.Iet), sh.Sum(r => r.Ag.Iet));
    }
    // Floor-anchored efficiency: one COMMON floor F = mean levelized IET of the 6 CHEAPEST
    // baseline-correct rungs (the first 6 points on the LIET slope). Both arms are charged their
    // total IET-over-F across ALL 24 tasks (including wasted IET on tasks they FAILED — this
    // penalizes hedging), then normalized by a FIXED denominator = the baseline's correct count (so a
    // skill can't dilute its cost by unlocking cheap tasks). The 6-cheapest anchor (vs nominal
    // "basics") keeps the floor at the true bottom of the curve: a task that is nominally basic but
    // expensive for baseline (archaeology tax) is not in the cheapest 6 and cannot inflate the zero-
    // point. F cancels in the arm DELTA (both arms run all 24), so the anchor sets absolute scale, not
    // the story. Returns per-correct-baseline-answer premium for each arm. Precedent: cost-per-
    // successful-outcome / cost-effectiveness ratio (cost per approved drug incl. failures).
    private static (double floor, int nBase, double basePerCorrect, double agPerCorrect) FloorMetric(List<Rung> rungs, Func<Point, double>? metric = null)
    {
        // metric selector defaults to IET (LIET); pass p => p.Secs for the identical levelization on
        // wall-clock duration ("levelized duration"). Floor = the 6 cheapest baseline-correct rungs
        // BY THIS METRIC; the delta cancels the floor, so only the arm difference is the story.
        var m = metric ?? (p => p.Iet);
        var basePassedBasics = rungs.Where(r => r.Base.Passed).OrderBy(r => m(r.Base))
            .Take(6).Select(r => m(r.Base)).ToList();
        double f = basePassedBasics.Count > 0 ? basePassedBasics.Average() : 0;
        int nBase = rungs.Count(r => r.Base.Passed);
        double baseTotal = rungs.Where(r => r.Base.Present).Sum(r => m(r.Base) - f);
        double agTotal = rungs.Where(r => r.Ag.Present).Sum(r => m(r.Ag) - f);
        return (f, nBase, nBase > 0 ? baseTotal / nBase : 0, nBase > 0 ? agTotal / nBase : 0);
    }
    private static string N(double v) => v.ToString("0.#", Inv);   // invariant SVG coordinate
    private static string SignedK(double v) => (v >= 0 ? "+" : "−") + K(Math.Abs(v));  // signed IET delta
    private static string Secs(double v) => v.ToString("0.#", Inv) + "s";               // wall-clock seconds
    private static string SignedSecs(double v) => (v >= 0 ? "+" : "−") + Secs(Math.Abs(v)); // signed duration delta
    private static double NiceStep(double raw)                     // round axis step: 1/2/2.5/5 × 10ⁿ
    {
        if (raw <= 0 || double.IsNaN(raw)) return 1;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw))), norm = raw / mag;
        double nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 2.5 ? 2.5 : norm <= 5 ? 5 : 10;
        return nice * mag;
    }
    private static string Signed(double v) => (v >= 0 ? "+" : "") + K(v);
    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // Rung tick label for charts: drop the alpha prefix so "CT24" reads as a compact "24" on a
    // crowded 24-rung x-axis (keeps the original if there is no trailing number).
    private static string ShortRung(string name)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(name, "^[A-Za-z]+", "");
        return s.Length > 0 ? s : name;
    }

    // Companion archaeology SVG path: insert "-arch" before the extension of the LIET SVG path.
    private static string ArchVariant(string lietPath)
    {
        var dir = Path.GetDirectoryName(lietPath) ?? ".";
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(lietPath) + "-arch" + Path.GetExtension(lietPath));
    }

    // Unique, filename-safe SVG path per (source file, verdict) so multiple curves never collide or
    // escape the target directory (model ids like "openai/gpt-5" contain separators). A short hash of
    // the FULL source path guards the common case of many inputs all named `results.json`.
    private static string SvgVariant(string basePath, string sourceFile, string unit, string? model, int verdictIndex)
    {
        var dir = Path.GetDirectoryName(basePath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);
        var src = Path.GetFileNameWithoutExtension(sourceFile);
        var h = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(sourceFile))))[..6].ToLowerInvariant();
        var tag = Sanitize($"{src}.{unit}.{model}.{h}") + (verdictIndex > 0 ? $".v{verdictIndex}" : "");
        return Path.Combine(dir, $"{stem}.{tag}{ext}");
    }

    private static string Sanitize(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var chars = s.Select(c => bad.Contains(c) || c is '/' or '\\' or ' ' ? '-' : c).ToArray();
        return new string(chars);
    }
}
