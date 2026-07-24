using System.Globalization;
using Grounding.Json;

namespace Grounding.Analyze;

// The graded-yield quality card (docs/quality-card-model.md), points-first.
//
// Consumes the harness per-run capture (metrics.perRun[]) to score the Fails → Satisfies → Delivers
// ladder per task, then renders the ratified two-axis model:
//   Axis 1 (return):   yield pˣ = Kˣ/k;  ΔP = Pᵍ − Pᵇ;  ΔP|both on the both-productive set.
//   Axis 2 (cost):     levelized Lˣ = Σcost / Kˣ on the shared set S, compared paired via the
//                      geometric mean of ratios rᵢ = Lᵍ/Lᵇ (Simpson guard = pooled cross-check).
//   Claims:            C1 capability (grounded-only unlocks), C2 reliability (ΔP|both),
//                      C3 efficiency (geo-mean ratio), C4 fidelity (Delivers rate), C5 σ_g/σ_b.
//   Hard gate:         per-task material regression OR suite loss-mass Σ max(−Δpᵢ, 0).
//
// Stage 1: Delivers == Satisfies (the model's proxy) until the delivers-tier assertions land, so
// C4 is 1.0 by construction and is labelled as not-yet-measured. Bands (beta-binomial posterior +
// nested bootstrap) are a deferred second pass; this cut reports point estimates only.
internal sealed class Ladder
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly TextWriter _o;
    public bool NoTitle;

    // Grounded arm to grade against baseline. Holistic datasets carry the plugin arm; per-skill the
    // isolated arm. Default prefers whichever actually carries per-run data; override with the env.
    private static readonly string? ArmOverride =
        Environment.GetEnvironmentVariable("GROUNDING_CARD_ARM") is { Length: > 0 } v ? v : null;

    public Ladder(TextWriter o) => _o = o;

    // One arm's outcome on one task: k runs, of which Sat satisfy and K deliver; per-run IET/cost.
    private sealed record ArmTask(int k, int Sat, int Deliv, List<long> AllIet, List<long> DelivIet)
    {
        public double P => k > 0 ? (double)Deliv / k : 0.0;                 // yield
        public bool Productive => Deliv >= 1;                                // K ≥ 1
        public double SumAllIet => AllIet.Count > 0 ? AllIet.Sum() : 0.0;
        public double Levelized => Deliv >= 1 ? SumAllIet / Deliv : double.NaN; // Lˣ = Σcost/Kˣ
        public double MedianDelivIet => Median(DelivIet);
    }

    private sealed record TaskRow(string Name, ArmTask B, ArmTask G);

    public void Render(IReadOnlyList<string> files)
    {
        if (files.Count == 0) { Console.Error.WriteLine("ladder: no input files."); return; }
        foreach (var path in files)
            RenderOne(path);
    }

    private void RenderOne(string path)
    {
        var d = Loader.Parse(path);
        var model = d.Model ?? "?";
        var iet = IetModels.For(model);
        var verdict = d.Verdicts is { Count: > 0 } ? d.Verdicts[0] : new Verdict();
        var scenarios = verdict.Scenarios ?? new();

        var groundedKey = PickGroundedArm(scenarios);
        var rows = new List<TaskRow>();
        foreach (var sc in scenarios)
        {
            var b = ArmTaskOf(Loader.ArmOf(sc, "baseline"), iet);
            var g = ArmTaskOf(Loader.ArmOf(sc, groundedKey), iet);
            if (b is null || g is null) continue;
            rows.Add(new TaskRow(ShortName(sc.ScenarioName ?? sc.Name ?? "?"), b, g));
        }

        if (!NoTitle)
            _o.WriteLine($"===== graded-yield ladder — {verdict.SkillName ?? "?"} · {model} · " +
                         $"baseline vs {groundedKey} · k={KOf(rows)} · {rows.Count} task(s) =====\n");

        if (rows.Count == 0) { _o.WriteLine("_no comparable tasks (no per-run data)._"); return; }

        var capture = rows.Any(r => r.B.k > 1 || r.G.k > 1);
        if (!capture)
            _o.WriteLine("> ⚠ no per-run capture in this dataset — yields are a k=1 binary proxy " +
                         "(Satisfies from the averaged run). Re-run with the per-run-capture harness for graded K/k.\n");

        _o.WriteLine("_Goal column: ↑ higher is better · ↓ lower is better · · context (no direction)._\n");

        Scoreboard(rows);
        Axis1(rows);
        Axis2(rows, iet);
        Claims(rows);
        Gate(rows);
        _o.WriteLine();
    }

    // ---- coverage scoreboard (rows-not-nets) --------------------------------
    private void Scoreboard(List<TaskRow> rows)
    {
        int both = rows.Count(r => r.B.Productive && r.G.Productive);
        int gOnly = rows.Count(r => !r.B.Productive && r.G.Productive);   // C1 capability unlock
        int bOnly = rows.Count(r => r.B.Productive && !r.G.Productive);   // regression candidate
        int neither = rows.Count(r => !r.B.Productive && !r.G.Productive);
        _o.WriteLine("### Coverage scoreboard (K ≥ 1 productive)\n");
        _o.WriteLine("| cell | tasks | goal | meaning |");
        _o.WriteLine("|------|-------|------|---------|");
        _o.WriteLine($"| both productive (S) | {both} | · | the efficiency-comparable shared set |");
        _o.WriteLine($"| grounded-only | {gOnly} | ↑ | C1 capability unlocks (descriptive) |");
        _o.WriteLine($"| baseline-only | {bOnly} | ↓ | regression candidates (hard gate) |");
        _o.WriteLine($"| neither | {neither} | · | out of reach for both |");
        _o.WriteLine();
    }

    // ---- Axis 1: return -----------------------------------------------------
    private void Axis1(List<TaskRow> rows)
    {
        double pb = rows.Average(r => r.B.P);
        double pg = rows.Average(r => r.G.P);
        var both = rows.Where(r => r.B.Productive && r.G.Productive).ToList();
        double dpBoth = both.Count > 0 ? both.Average(r => r.G.P - r.B.P) : double.NaN;
        _o.WriteLine("### Axis 1 — return (yield pˣ = Kˣ/k)\n");
        _o.WriteLine("| quantity | goal | baseline | grounded | Δ |");
        _o.WriteLine("|----------|------|----------|----------|---|");
        _o.WriteLine($"| mean yield P (all tasks) | ↑ | {pb:0.000} | {pg:0.000} | {Signed(pg - pb)} |");
        _o.WriteLine($"| ΔP\\|both (C2 estimand, both-productive) | ↑ | — | — | {(double.IsNaN(dpBoth) ? "n/a" : Signed(dpBoth))} |");
        _o.WriteLine("\n_ΔP over all tasks mixes C1 unlocks into C2; ΔP\\|both is the reliability quantity " +
                     "(conditional on joint productivity; the excluded cells are owned by C1 and the gate)._\n");
    }

    // ---- Axis 2: cost (levelized, paired on S) -------------------------------
    private void Axis2(List<TaskRow> rows, IetScheme iet)
    {
        var s = rows.Where(r => r.B.Productive && r.G.Productive).ToList();
        _o.WriteLine("### Axis 2 — cost (levelized Lˣ = Σ IET / Kˣ, paired on S)\n");
        if (s.Count == 0) { _o.WriteLine("_shared set S is empty — no cost comparison._\n"); return; }
        if (s.Count < 8) _o.WriteLine($"> ⚠ thin S (|S| = {s.Count} < 8): the cost ratio is under-powered.\n");

        var ratios = s.Select(r => r.G.Levelized / r.B.Levelized).Where(x => x > 0 && !double.IsNaN(x)).ToList();
        double geo = ratios.Count > 0 ? Math.Exp(ratios.Average(Math.Log)) : double.NaN;
        double pooled = s.Sum(r => r.G.Levelized) / s.Sum(r => r.B.Levelized);   // Simpson cross-check

        double totB = s.Sum(r => r.B.MedianDelivIet);
        double totG = s.Sum(r => r.G.MedianDelivIet);

        _o.WriteLine("| quantity | goal | value |");
        _o.WriteLine("|----------|------|-------|");
        _o.WriteLine($"| geo-mean ratio rᵢ = Lᵍ/Lᵇ (typical multiplier) | ↓ | {Mult(geo)} |");
        _o.WriteLine($"| pooled ΣLᵍ/ΣLᵇ (Simpson guard) | ↓ | {Mult(pooled)} |");
        _o.WriteLine($"| Total IET on S (median-delivered) | ↓ | {K(totB)} → {K(totG)} ({SignedPct(totB > 0 ? (totG - totB) / totB * 100 : 0)}) |");
        if (geo > 0 && pooled > 0 && Math.Sign(Math.Log(geo)) != Math.Sign(Math.Log(pooled)))
            _o.WriteLine("\n> ⚠ Simpson flag: geo-mean and pooled ratios disagree in direction — a size-mix effect. Trust the paired geo-mean.");
        _o.WriteLine("\n_Total (arithmetic median) and the levelized ratio answer different questions and will not match._\n");
    }

    // ---- claims table -------------------------------------------------------
    private void Claims(List<TaskRow> rows)
    {
        var working = rows.Where(r => r.G.Sat >= 1 || r.G.Deliv >= 1).ToList();
        double c4 = working.Count > 0
            ? working.Average(r => (r.G.Sat + r.G.Deliv) > 0 ? (double)r.G.Deliv / Math.Max(r.G.Sat, r.G.Deliv) : 0.0)
            : double.NaN;

        // C5: σ of log(IET) on Delivered runs only, pooled across tasks, per arm.
        var gLogs = rows.SelectMany(r => r.G.DelivIet).Where(x => x > 0).Select(x => Math.Log(x)).ToList();
        var bLogs = rows.SelectMany(r => r.B.DelivIet).Where(x => x > 0).Select(x => Math.Log(x)).ToList();
        double sg = Sd(gLogs), sb = Sd(bLogs);
        double c5 = sb > 0 ? sg / sb : double.NaN;

        _o.WriteLine("### Claims (point estimates)\n");
        _o.WriteLine("| claim | goal | value | strength |");
        _o.WriteLine("|-------|------|-------|----------|");
        _o.WriteLine($"| C4 fidelity (Delivers rate over working tasks) | ↑ | {(double.IsNaN(c4) ? "n/a" : c4.ToString("0.00", Inv))} | not-yet-measured (Stage 1: Delivers≡Satisfies) |");
        _o.WriteLine($"| C5 predictability (σ_g/σ_b, delivered log-IET) | ↓ | {(double.IsNaN(c5) ? "n/a" : c5.ToString("0.00", Inv))} | memo |");
        _o.WriteLine();
    }

    // ---- hard gate (do-no-harm) --------------------------------------------
    private void Gate(List<TaskRow> rows)
    {
        var regressions = rows.Where(r => r.G.P < r.B.P).ToList();
        double lossMass = rows.Sum(r => Math.Max(r.B.P - r.G.P, 0.0));       // Σ max(−Δpᵢ, 0) over all N
        _o.WriteLine("### Hard gate — do-no-harm (points-first)\n");
        _o.WriteLine($"- Suite loss mass Σ max(−Δpᵢ, 0) over all {rows.Count} tasks (↓ lower better, 0 = clean): **{lossMass:0.000}**");
        if (regressions.Count == 0)
            _o.WriteLine("- No per-task yield regressions.");
        else
        {
            _o.WriteLine($"- Per-task yield regressions ({regressions.Count}):");
            foreach (var r in regressions.OrderByDescending(r => r.B.P - r.G.P))
                _o.WriteLine($"  - {r.Name}: pᵇ={r.B.P:0.00} → pᵍ={r.G.P:0.00} (Δ {Signed(r.G.P - r.B.P)})");
        }
        _o.WriteLine("\n_Materiality (margin + band-excludes-zero) is deferred to the bands pass; " +
                     "this cut trips on raw point Δp only._");
    }

    // ---- construction from per-run capture ----------------------------------
    private static ArmTask? ArmTaskOf(Arm? arm, IetScheme iet)
    {
        var row = Loader.Row(arm, iet);
        if (row is null) return null;
        if (row.PerRun is { Count: > 0 } pr)
        {
            int sat = pr.Count(o => o.Satisfies);
            int del = pr.Count(o => o.Delivers);
            var all = pr.Select(o => o.Iet).ToList();
            var deliv = pr.Where(o => o.Delivers).Select(o => o.Iet).ToList();
            return new ArmTask(pr.Count, sat, del, all, deliv);
        }
        // Fallback: no per-run capture — treat the averaged arm as a single k=1 outcome.
        bool ok = row.Ft > 0 && row.Fp == row.Ft;
        var one = new List<long> { row.Iet };
        return new ArmTask(1, ok ? 1 : 0, ok ? 1 : 0, one, ok ? one : new List<long>());
    }

    private static string PickGroundedArm(List<Scenario> scenarios)
    {
        if (ArmOverride is { } o) return o;
        // Prefer the arm that carries real per-run data (non-empty plugin in holistic runs).
        bool pluginHasData = scenarios.Any(sc => sc.SkilledPlugin?.Metrics?.PerRun is { Count: > 0 })
                             || scenarios.Any(sc => (sc.SkilledPlugin?.Metrics?.InputTokens ?? 0) > 0);
        return pluginHasData ? "skilledPlugin" : "skilledIsolated";
    }

    // ---- helpers ------------------------------------------------------------
    private static int KOf(List<TaskRow> rows) => rows.Count == 0 ? 0 : rows.Max(r => Math.Max(r.B.k, r.G.k));

    private static double Median(List<long> xs)
    {
        if (xs.Count == 0) return double.NaN;
        var s = xs.OrderBy(x => x).ToList();
        int m = s.Count / 2;
        return s.Count % 2 == 1 ? s[m] : (s[m - 1] + s[m]) / 2.0;
    }

    private static double Sd(List<double> xs)
    {
        if (xs.Count < 2) return double.NaN;
        double mean = xs.Average();
        return Math.Sqrt(xs.Sum(x => (x - mean) * (x - mean)) / (xs.Count - 1));
    }

    private static string Signed(double x) => (x >= 0 ? "+" : "") + x.ToString("0.000", Inv);
    private static string Mult(double x) => double.IsNaN(x) ? "n/a" : $"×{x.ToString("0.00", Inv)}";
    private static string SignedPct(double p) => (p >= 0 ? "+" : "") + p.ToString("0", Inv) + "%";
    private static string K(double v) => Math.Abs(v) >= 1000 ? (v / 1000.0).ToString("0.0", Inv) + "k" : v.ToString("0", Inv);

    private static string ShortName(string full)
    {
        int c = full.IndexOf(':');
        return c > 0 ? full[..c].Trim() : full.Trim();
    }
}
