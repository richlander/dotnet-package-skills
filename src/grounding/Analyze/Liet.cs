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
    }

    private sealed class Rung
    {
        public string Name = "";
        public int Index;
        public Point Base = new(), Ag = new(), Oracle = new(), Readme = new();
        public double? Ceiling;  // competitor envelope for AGENTS.md (min IET of other passing arms)
        public string Region = "";
        public List<string> Skills = new();  // skills the grounded arm pulled for this rung (plugin self-select)
    }

    private static bool Correct(ArmRow? r) => r is { Ft: > 0 } && r.Fp == r.Ft;

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
                // Levelize: order rungs by MEASURED difficulty (baseline IET) — the LCOE-faithful
                // x-axis — not authored order. Rungs the baseline could not answer are the hardest
                // and sort to the end (their AGENTS/oracle 'unlock' points plot at the right).
                rungs = rungs.OrderBy(r => r.Base.Passed ? r.Base.Iet : double.PositiveInfinity)
                             .ThenBy(r => r.Index).ToList();
                for (int k = 0; k < rungs.Count; k++) rungs[k].Index = k;
                var unit = v.SkillName ?? "?";
                // Label the grounded arm from the dataset file name (same heuristic as the card's
                // DocLabel): a "skill" dataset ships SKILL.md, otherwise AGENTS.md.
                var docLabel = System.IO.Path.GetFileName(f).ToLowerInvariant().Contains("skill") ? "SKILL.md" : "AGENTS.md";
                EmitTable(rungs, unit, d.Model ?? "?", d.JudgeModel, docLabel);
                if (svgPath is { Length: > 0 })
                {
                    var multi = primary.Count > 1 || (d.Verdicts?.Count ?? 0) > 1;
                    var outPath = multi ? SvgVariant(svgPath, f, unit, d.Model, vi) : svgPath;
                    File.WriteAllText(outPath, BuildSvg(rungs, unit, d.Model ?? "?", docLabel));
                    _o.WriteLine($"\n_LIET curve written to `{outPath}`._");
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
                Base = new Point { Present = b is not null, Passed = Correct(b), Iet = b?.Iet ?? 0 },
                Ag = new Point { Present = a is not null, Passed = Correct(a), Iet = a?.Iet ?? 0 },
                Oracle = new Point { Present = o is not null, Passed = Correct(o), Iet = o?.Iet ?? 0 },
            };
            if (readmeMap is not null && readmeMap.TryGetValue(r.Name, out var rm)) r.Readme = rm;
            r.Skills = Loader.DetectedSkillsOf(sc, groundArm).Distinct().ToList();
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

    private void EmitTable(List<Rung> rungs, string unit, string model, string? judge, string docLabel)
    {
        var ds = docLabel.Replace(".md", "");
        if (!NoTitle) _o.WriteLine($"### LIET curve — {unit} | `{model}`\n");
        _o.WriteLine($"_Per-rung IET (levelized) by arm; difficulty = measured baseline IET (levelized). `✗` = arm failed "
            + $"(not plotted, not extrapolated). Ceiling = min IET of passing competitors — the max price "
            + $"`{docLabel}` may pay. Judge `{judge ?? "?"}`. IET model {IetModels.CaptionFor(new[] { model })}._\n");
        bool hasReadme = rungs.Any(r => r.Readme.Present);
        var rHdr = hasReadme ? " `README.md` |" : "";
        var rSep = hasReadme ? " ---: |" : "";
        var (ids, legend) = BuildSkillIds(rungs, unit);
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
            var leg = string.Join(" · ", legend.Select(l => $"`{l.id}`={l.name} (×{l.count})"));
            _o.WriteLine($"\n_Skills pulled (self-select from shelf): {leg}. "
                + $"{legend.Count} distinct — all earn their place; a skill pulled ×0–1 is a deletion candidate._");
        }
        EmitScalars(rungs, ds);
        _o.WriteLine();
    }

    // Stable skill ids for the LIET table/image: `M` = base skill, domain skills alphabetical → 1..N.
    // Alphabetical (not by count) keeps the legend identical across models for side-by-side reading.
    private static (Dictionary<string, string> ids, List<(string id, string name, int count)> legend)
        BuildSkillIds(List<Rung> rungs, string baseName)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rungs)
            foreach (var s in r.Skills)
                counts[s] = counts.TryGetValue(s, out var c) ? c + 1 : 1;
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var legend = new List<(string, string, int)>();
        if (counts.TryGetValue(baseName, out var bc)) { ids[baseName] = "M"; legend.Add(("M", baseName, bc)); }
        int n = 1;
        foreach (var name in counts.Keys.Where(k => k != baseName).OrderBy(k => k, StringComparer.Ordinal))
        {
            var id = n++.ToString();
            ids[name] = id;
            legend.Add((id, name, counts[name]));
        }
        return (ids, legend);
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

    private static string BuildSvg(List<Rung> rungs, string unit, string model, string docLabel)
    {
        const int W = 760, H = 500, L = 100, R = 664, T = 64, B = 380; // plot box
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

        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {W} {H}\" xmlns=\"http://www.w3.org/2000/svg\" font-family=\"ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, sans-serif\">\n");
        sb.Append($"  <rect width=\"{W}\" height=\"{H}\" fill=\"#ffffff\"/>\n");
        sb.Append($"  <text x=\"{(L + R) / 2}\" y=\"30\" text-anchor=\"middle\" font-size=\"17\" font-weight=\"700\" fill=\"#0f172a\">LIET curve — {Esc(unit)} ({Esc(model)})</text>\n");
        sb.Append($"  <text x=\"{(L + R) / 2}\" y=\"49\" text-anchor=\"middle\" font-size=\"12\" fill=\"#64748b\">IET per correctly-answered rung. Failed rungs are not plotted; the ceiling is the max price to beat.</text>\n");
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
            sb.Append("  </g>\n");
        }
        // rung ticks
        sb.Append("  <g font-size=\"11\" fill=\"#64748b\" text-anchor=\"middle\">\n");
        for (int i = 0; i < n; i++) sb.Append($"    <text x=\"{N(X(i))}\" y=\"{B + 18}\">{Esc(rungs[i].Name)}</text>\n");
        sb.Append("  </g>\n");
        // skill-id tick row: which skills the grounded arm pulled per rung (M = base, 1..N domain).
        var (skillIds, skillLegend) = BuildSkillIds(rungs, unit);
        if (skillLegend.Count > 0)
        {
            sb.Append("  <g font-size=\"9\" font-weight=\"700\" fill=\"#0891b2\" text-anchor=\"middle\">\n");
            for (int i = 0; i < n; i++)
            {
                var cell = SkillCell(rungs[i], skillIds);
                if (cell != "—") sb.Append($"    <text x=\"{N(X(i))}\" y=\"{B + 31}\">{Esc(cell)}</text>\n");
            }
            sb.Append("  </g>\n");
            var leg = string.Join("   ", skillLegend.Select(l => $"{l.id}={l.name}"));
            sb.Append($"  <text x=\"{(L + R) / 2}\" y=\"456\" text-anchor=\"middle\" font-size=\"9.5\" fill=\"#0891b2\">skills pulled — {Esc(leg)}</text>\n");
        }
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
        // series
        sb.Append(Series(rungs, p => p.Oracle, "#d97706", "SKILL.md (oracle)", X, Y, false));
        sb.Append(Series(rungs, p => p.Base, "#dc2626", "baseline", X, Y, false));
        sb.Append(Series(rungs, p => p.Readme, "#7c3aed", "README.md", X, Y, false));
        sb.Append(Series(rungs, p => p.Ag, "#2563eb", docLabel, X, Y, true));
        // legend
        sb.Append("  <g font-size=\"10.5\" fill=\"#475569\">\n");
        sb.Append($"    <circle cx=\"112\" cy=\"474\" r=\"4\" fill=\"none\" stroke=\"#2563eb\" stroke-width=\"2\"/><text x=\"122\" y=\"478\">{Esc(docLabel.Replace(".md", ""))} over ceiling (harm)</text>\n");
        sb.Append($"    <circle cx=\"290\" cy=\"474\" r=\"4\" fill=\"#2563eb\"/><text x=\"300\" y=\"478\">{Esc(docLabel.Replace(".md", ""))} under ceiling (pays its way)</text>\n");
        sb.Append($"    <text x=\"540\" y=\"478\" font-style=\"italic\">failed rungs not plotted</text>\n");
        sb.Append("  </g>\n</svg>\n");
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

    // ---- helpers --------------------------------------------------------------------------------

    private static string K(double v) => v >= 1000 ? $"{(v / 1000.0).ToString("0.#", Inv)}k" : v.ToString("0", Inv);
    private static string N(double v) => v.ToString("0.#", Inv);   // invariant SVG coordinate
    private static double NiceStep(double raw)                     // round axis step: 1/2/2.5/5 × 10ⁿ
    {
        if (raw <= 0 || double.IsNaN(raw)) return 1;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw))), norm = raw / mag;
        double nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 2.5 ? 2.5 : norm <= 5 ? 5 : 10;
        return nice * mag;
    }
    private static string Signed(double v) => (v >= 0 ? "+" : "") + K(v);
    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

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
