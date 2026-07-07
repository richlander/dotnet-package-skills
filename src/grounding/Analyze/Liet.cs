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
        public Point Base = new(), Ag = new(), Oracle = new();
        public double? Ceiling;  // competitor envelope for AGENTS.md (min IET of other passing arms)
        public string Region = "";
    }

    private static bool Correct(ArmRow? r) => r is { Ft: > 0 } && r.Fp == r.Ft;

    public void Render(IReadOnlyList<string> files, string? svgPath)
    {
        foreach (var f in files.Distinct().OrderBy(x => x, StringComparer.Ordinal))
        {
            ResultsFile d;
            try { d = Loader.Parse(f); }
            catch (Exception e) { _o.WriteLine($"!! {f}: {e.Message}"); continue; }
            var iet = IetModels.For(d.Model);
            int vi = 0;
            foreach (var v in d.Verdicts ?? new())
            {
                var rungs = BuildRungs(v, iet);
                if (rungs.Count == 0) { vi++; continue; }
                var unit = v.SkillName ?? "?";
                EmitTable(rungs, unit, d.Model ?? "?", d.JudgeModel);
                if (svgPath is { Length: > 0 })
                {
                    var multi = files.Count > 1 || (d.Verdicts?.Count ?? 0) > 1;
                    var outPath = multi ? SvgVariant(svgPath, f, unit, d.Model, vi) : svgPath;
                    File.WriteAllText(outPath, BuildSvg(rungs, unit, d.Model ?? "?"));
                    _o.WriteLine($"\n_LIET curve written to `{outPath}`._");
                }
                vi++;
            }
        }
    }

    private static List<Rung> BuildRungs(Verdict v, IetScheme iet)
    {
        var rungs = new List<Rung>();
        int i = 0;
        foreach (var sc in v.Scenarios ?? new())
        {
            var b = Loader.Row(Loader.ArmOf(sc, "baseline"), iet);
            var a = Loader.Row(Loader.ArmOf(sc, "skilledIsolated"), iet);
            // NOTE: skilledPlugin is a DELIVERY variant of the same AGENTS.md (whole-shelf
            // self-select), NOT a SKILL.md oracle — so it is deliberately NOT read here. The oracle
            // is a `--source skill` run, overlaid separately when available. Without it the ceiling
            // is baseline alone: "grounding must beat the model knowing nothing".
            var r = new Rung
            {
                Name = (sc.ScenarioName ?? "").Split(':')[0].Trim(),
                Index = i++,
                Base = new Point { Present = b is not null, Passed = Correct(b), Iet = b?.Iet ?? 0 },
                Ag = new Point { Present = a is not null, Passed = Correct(a), Iet = a?.Iet ?? 0 },
                Oracle = new Point(),
            };
            // Competitor envelope for AGENTS.md = min IET of the OTHER arms that passed.
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

    private void EmitTable(List<Rung> rungs, string unit, string model, string? judge)
    {
        if (!NoTitle) _o.WriteLine($"### LIET curve — {unit} | `{model}`\n");
        _o.WriteLine($"_Per-rung IET (levelized) by arm; difficulty = authored rung order. `✗` = arm failed "
            + $"(not plotted, not extrapolated). Ceiling = min IET of passing competitors — the max price "
            + $"`AGENTS.md` may pay. Judge `{judge ?? "?"}`. IET model {IetModels.CaptionFor(new[] { model })}._\n");
        _o.WriteLine("| rung | baseline | `AGENTS.md` | oracle | ceiling | region |");
        _o.WriteLine("| --- | ---: | ---: | ---: | ---: | --- |");
        foreach (var r in rungs)
        {
            _o.WriteLine($"| {r.Name} | {Cell(r.Base)} | {AgCell(r)} | {Cell(r.Oracle)} "
                + $"| {(r.Ceiling is { } c ? K(c) : "—")} | {RegionTag(r.Region)} |");
        }
        EmitScalars(rungs);
        _o.WriteLine();
    }

    private static string Cell(Point p) => !p.Present ? "—" : p.Passed ? K(p.Iet) : "✗";

    private static string AgCell(Rung r)
    {
        if (!r.Ag.Present) return "—";
        if (!r.Ag.Passed) return "✗";
        var pays = r.Ceiling is { } c ? (r.Ag.Iet <= c ? " ✓" : " ⚠") : "";
        return K(r.Ag.Iet) + pays;
    }

    private static string RegionTag(string region) => region switch
    {
        "win" => "**win** (under ceiling)",
        "harm" => "harm (over ceiling)",
        "unlock" => "unlock (baseline ✗)",
        "regression" => "**regression** (harm veto)",
        "ceiling" => "ceiling only (AGENTS ✗)",
        "unreached" => "unreached",
        _ => region,
    };

    // Honest scalar reductions of the curve (all difficulty-aware; never a cross-difficulty mean).
    private void EmitScalars(List<Rung> rungs)
    {
        var shared = rungs.Where(r => r.Base.Passed && r.Ag.Passed).ToList();
        var sb = new StringBuilder("\n");

        // Value delivered on the shared region: baseline − AGENTS, summed and per-rung.
        if (shared.Count > 0)
        {
            double val = shared.Sum(r => r.Base.Iet - r.Ag.Iet);
            double ratio = shared.Sum(r => r.Base.Iet) / Math.Max(1, shared.Sum(r => r.Ag.Iet));
            sb.Append($"- **Value delivered** (shared region, {shared.Count} rung(s)): baseline − AGENTS = {K(val)} IET "
                + $"(baseline {ratio.ToString("0.0", Inv)}× AGENTS).\n");
            // Shared-region slope: mean per-rung step for each arm across the shared rungs (efficiency).
            if (shared.Count >= 2)
            {
                double bs = Slope(shared.Select(r => r.Base.Iet)), as_ = Slope(shared.Select(r => r.Ag.Iet));
                sb.Append($"- **Shared-region slope** (efficiency): baseline {Signed(bs)}/rung vs AGENTS {Signed(as_)}/rung.\n");
            }
        }

        // Unlock: rungs AGENTS answered that baseline did not.
        var unlock = rungs.Where(r => r.Ag.Passed && !r.Base.Passed).Select(r => r.Name).ToList();
        if (unlock.Count > 0) sb.Append($"- **Unlocked** (baseline ✗, AGENTS ✓): {string.Join(", ", unlock)}.\n");

        // Regressions (harm veto).
        var regr = rungs.Where(r => r.Region == "regression").Select(r => r.Name).ToList();
        if (regr.Count > 0) sb.Append($"- **Regressions** (baseline ✓, AGENTS ✗ — harm veto): {string.Join(", ", regr)}.\n");

        // Harm region (AGENTS over the ceiling on rungs a competitor is cheaper).
        var harm = rungs.Where(r => r.Region == "harm").Select(r => r.Name).ToList();
        if (harm.Count > 0) sb.Append($"- **Harm region** (AGENTS over ceiling): {string.Join(", ", harm)}.\n");

        // Divergence rung (knee): the shared rung with the largest upward step in the AGENTS curve.
        var knee = Knee(rungs.Where(r => r.Ag.Passed).ToList());
        if (knee is not null) sb.Append($"- **Divergence rung** (AGENTS knee): {knee}.\n");

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

    private static string BuildSvg(List<Rung> rungs, string unit, string model)
    {
        const int W = 760, H = 480, L = 100, R = 664, T = 64, B = 380; // plot box
        double maxIet = rungs.SelectMany(r => new[]
        {
            r.Base.Passed ? r.Base.Iet : 0, r.Ag.Passed ? r.Ag.Iet : 0,
            r.Oracle.Passed ? r.Oracle.Iet : 0, r.Ceiling ?? 0,
        }).DefaultIfEmpty(1).Max();
        maxIet = Math.Max(maxIet, 1);
        int n = rungs.Count;
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
        sb.Append($"  <text x=\"{(L + R) / 2}\" y=\"430\" text-anchor=\"middle\" font-size=\"12\" fill=\"#334155\">rung (difficulty) →</text>\n");
        sb.Append($"  <text x=\"52\" y=\"222\" text-anchor=\"middle\" font-size=\"12\" fill=\"#334155\" transform=\"rotate(-90 52 222)\">IET (per correct answer) →</text>\n");
        // rung ticks
        sb.Append("  <g font-size=\"11\" fill=\"#64748b\" text-anchor=\"middle\">\n");
        for (int i = 0; i < n; i++) sb.Append($"    <text x=\"{N(X(i))}\" y=\"{B + 18}\">{Esc(rungs[i].Name)}</text>\n");
        sb.Append("  </g>\n");
        // ceilings on rungs where AGENTS failed (max price of generalization)
        foreach (var r in rungs.Where(r => !r.Ag.Passed && r.Ceiling is not null))
        {
            double y = Y(r.Ceiling!.Value), x = X(r.Index);
            sb.Append($"  <line x1=\"{N(x - 26)}\" y1=\"{N(y)}\" x2=\"{N(x + 26)}\" y2=\"{N(y)}\" stroke=\"#b45309\" stroke-width=\"2\" stroke-dasharray=\"5 4\"/>\n");
            sb.Append($"  <text x=\"{N(x)}\" y=\"{B + 32}\" text-anchor=\"middle\" font-size=\"9.5\" font-weight=\"700\" fill=\"#1d4ed8\">AGENTS ✗</text>\n");
        }
        // series
        sb.Append(Series(rungs, p => p.Oracle, "#d97706", "SKILL.md (oracle)", X, Y, false));
        sb.Append(Series(rungs, p => p.Base, "#dc2626", "baseline", X, Y, false));
        sb.Append(Series(rungs, p => p.Ag, "#2563eb", "AGENTS.md", X, Y, true));
        // legend
        sb.Append("  <g font-size=\"10.5\" fill=\"#475569\">\n");
        sb.Append($"    <circle cx=\"112\" cy=\"456\" r=\"4\" fill=\"none\" stroke=\"#2563eb\" stroke-width=\"2\"/><text x=\"122\" y=\"460\">AGENTS over ceiling (harm)</text>\n");
        sb.Append($"    <circle cx=\"290\" cy=\"456\" r=\"4\" fill=\"#2563eb\"/><text x=\"300\" y=\"460\">AGENTS under ceiling (pays its way)</text>\n");
        sb.Append($"    <text x=\"540\" y=\"460\" font-style=\"italic\">failed rungs not plotted</text>\n");
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
    private static string Signed(double v) => (v >= 0 ? "+" : "") + K(v);
    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // Unique, filename-safe SVG path per (source file, verdict) so multiple curves never collide or
    // escape the target directory (model ids like "openai/gpt-5" contain separators).
    private static string SvgVariant(string basePath, string sourceFile, string unit, string? model, int verdictIndex)
    {
        var dir = Path.GetDirectoryName(basePath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(basePath);
        var ext = Path.GetExtension(basePath);
        var src = Path.GetFileNameWithoutExtension(sourceFile);
        var tag = Sanitize($"{src}.{unit}.{model}") + (verdictIndex > 0 ? $".v{verdictIndex}" : "");
        return Path.Combine(dir, $"{stem}.{tag}{ext}");
    }

    private static string Sanitize(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var chars = s.Select(c => bad.Contains(c) || c is '/' or '\\' or ' ' ? '-' : c).ToArray();
        return new string(chars);
    }
}
