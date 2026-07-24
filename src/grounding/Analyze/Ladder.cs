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

        // Bands (beta-binomial posterior + one nested finite-suite bootstrap) only when per-run capture
        // exists — a k=1 proxy carries no within-task spread to resample.
        var bands = capture ? ComputeBands(rows) : null;

        Scoreboard(rows);
        Axis1(rows, bands);
        Axis2(rows, iet, bands);
        Claims(rows);
        Gate(rows, bands);
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
    private void Axis1(List<TaskRow> rows, Bands? bands)
    {
        double pb = rows.Average(r => r.B.P);
        double pg = rows.Average(r => r.G.P);
        var both = rows.Where(r => r.B.Productive && r.G.Productive).ToList();
        double dpBoth = both.Count > 0 ? both.Average(r => r.G.P - r.B.P) : double.NaN;
        _o.WriteLine("### Axis 1 — return (yield pˣ = Kˣ/k)\n");
        _o.WriteLine("| quantity | goal | baseline | grounded | Δ | 95% CrI |");
        _o.WriteLine("|----------|------|----------|----------|---|---------|");
        _o.WriteLine($"| mean yield P (all tasks) | ↑ | {pb:0.000} | {pg:0.000} | {Signed(pg - pb)} | descriptive |");
        _o.WriteLine($"| ΔP\\|both (C2 estimand, both-productive) | ↑ | — | — | {(double.IsNaN(dpBoth) ? "n/a" : Signed(dpBoth))} | {CiSigned(bands?.DpBoth)} |");
        _o.WriteLine("\n_ΔP over all tasks mixes C1 unlocks into C2 and is descriptive only (its beta-binomial band " +
                     "would shrink the 0/5→5/5 unlocks hard — a conservative attenuation, but not the certified quantity); " +
                     "ΔP\\|both is the banded reliability estimand (conditional on joint productivity; the excluded cells " +
                     "are owned by C1 and the gate)._");
        if (bands is { } bn)
        {
            _o.WriteLine($"\n_Bands: beta-binomial posterior (uniform Beta(1,1) prior), one nested finite-suite bootstrap " +
                         $"(B={BootIters}, tasks fixed, S\\* recomputed per iteration). Jeffreys Beta(½,½) sensitivity on " +
                         $"ΔP\\|both: {CiSigned(bn.DpBothJeffreys)} — {PriorReadout(bn.DpBoth, bn.DpBothJeffreys)}_");
        }
        _o.WriteLine();
    }

    // Compare the uniform vs Jeffreys ΔP|both bands on whether each excludes zero — the model's
    // "is the verdict about the data or the prior?" read.
    private static string PriorReadout(Ci uni, Ci jef)
    {
        bool uZ = ExcludesZero(uni), jZ = ExcludesZero(jef);
        if (uZ && jZ && Math.Sign(uni.Lo) == Math.Sign(jef.Lo))
            return "same-sign and excludes zero under both priors — the verdict is about the data, not the prior.";
        if (uZ != jZ)
            return "⚠ prior-sensitive — one prior's CrI excludes zero and the other includes it; treat C2 as prior-dependent (expected when an arm sits near the ceiling).";
        return "both priors' CrI include zero — C2 reliability is not established on this suite (expected when baseline is already near-ceiling; the win is on cost, not reliability).";
    }

    private static bool ExcludesZero(Ci c) => (c.Lo > 0 && c.Hi > 0) || (c.Lo < 0 && c.Hi < 0);

    // ---- Axis 2: cost (levelized, paired on S) -------------------------------
    private void Axis2(List<TaskRow> rows, IetScheme iet, Bands? bands)
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

        bool notEstimable = bands is { } b0 && b0.EstimabilityRate < 0.95;
        _o.WriteLine("| quantity | goal | value | 95% CrI |");
        _o.WriteLine("|----------|------|-------|---------|");
        _o.WriteLine($"| geo-mean ratio rᵢ = Lᵍ/Lᵇ (typical multiplier) | ↓ | {Mult(geo)} | {(notEstimable ? "not estimable" : CiMult(bands?.Geo))} |");
        _o.WriteLine($"| pooled ΣLᵍ/ΣLᵇ (Simpson guard) | ↓ | {Mult(pooled)} | — |");
        _o.WriteLine($"| Total IET on S (median-delivered) | ↓ | {K(totB)} → {K(totG)} ({SignedPct(totB > 0 ? (totG - totB) / totB * 100 : 0)}) | — |");
        if (geo > 0 && pooled > 0 && Math.Sign(Math.Log(geo)) != Math.Sign(Math.Log(pooled)))
            _o.WriteLine("\n> ⚠ Simpson flag: geo-mean and pooled ratios disagree in direction — a size-mix effect. Trust the paired geo-mean.");
        if (bands is { } bn)
        {
            _o.WriteLine($"\n_Geo-mean band is paired inside the same nested bootstrap (joint (delivered, cost) redraw; " +
                         $"S\\* recomputed per iteration). Estimability rate {bn.EstimabilityRate:0.0%}" +
                         (notEstimable ? " < 95% → Axis 2 reported not estimable rather than banding a biased subset." : ".") + "_");
        }
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
    private void Gate(List<TaskRow> rows, Bands? bands)
    {
        var regressions = rows.Where(r => r.G.P < r.B.P).ToList();
        double lossMass = rows.Sum(r => Math.Max(r.B.P - r.G.P, 0.0));       // Σ max(−Δpᵢ, 0) over all N
        _o.WriteLine("### Hard gate — do-no-harm (points-first)\n");
        _o.WriteLine($"- Suite loss mass Σ max(−Δpᵢ, 0) over all {rows.Count} tasks (↓ lower better, 0 = clean): **{lossMass:0.000}**");
        if (bands is { } bn)
        {
            bool trips = lossMass > bn.LossNull95;
            _o.WriteLine($"- Null-calibrated threshold (finite-suite null p̃ᵢ=(Kᵇ+Kᵍ)/2k, 95th pct of B={NullIters}): " +
                         $"**{bn.LossNull95:0.000}** → gate **{(trips ? "TRIPS ⛔" : "clean ✅")}** " +
                         $"(loss mass {(trips ? ">" : "≤")} null 95th).");
        }
        if (regressions.Count == 0)
            _o.WriteLine("- No per-task yield regressions.");
        else
        {
            _o.WriteLine($"- Per-task yield regressions ({regressions.Count}):");
            foreach (var r in regressions.OrderByDescending(r => r.B.P - r.G.P))
                _o.WriteLine($"  - {r.Name}: pᵇ={r.B.P:0.00} → pᵍ={r.G.P:0.00} (Δ {Signed(r.G.P - r.B.P)})");
        }
        _o.WriteLine("\n_Loss mass is truncation-biased (≥0 under the null), so the gate trips on the " +
                     "null-calibrated threshold, not on literal zero._");
    }

    // ---- bands: beta-binomial posterior + one nested finite-suite bootstrap ---
    private const int BootIters = 4000;
    private const int NullIters = 4000;
    private const int Seed = 12345;

    private sealed record Ci(double Lo, double Hi);

    private sealed record Bands(
        Ci DpAll, Ci DpBoth, Ci DpBothJeffreys,
        Ci Geo, double EstimabilityRate,
        double LossNull95);

    // Per-arm log-cost model for a task: lognormal(μ, σ) fit on all-run IET (all k runs' cost sit in
    // the levelized numerator — the retry tax), with a pooled per-arm σ fallback for σ=0 / single-run.
    private static (double mu, double sig) CostModel(ArmTask a, double pooledSig)
    {
        var logs = a.AllIet.Where(x => x > 0).Select(x => (double)Math.Log(x)).ToList();
        if (logs.Count == 0) return (double.NaN, pooledSig);
        double mu = logs.Average();
        double sig = logs.Count >= 2 ? Sd(logs) : pooledSig;
        if (double.IsNaN(sig) || sig <= 0) sig = pooledSig;
        return (mu, sig);
    }

    private static double PooledSigma(IEnumerable<ArmTask> arms)
    {
        var res = new List<double>();
        foreach (var a in arms)
        {
            var logs = a.AllIet.Where(x => x > 0).Select(x => (double)Math.Log(x)).ToList();
            if (logs.Count < 2) continue;
            double m = logs.Average();
            res.AddRange(logs.Select(l => l - m));
        }
        double s = Sd(res);
        return double.IsNaN(s) || s <= 0 ? 0.3 : s;   // conservative default when the suite is degenerate
    }

    private static Bands ComputeBands(List<TaskRow> rows)
    {
        int n = rows.Count;
        var rng = new Rng(Seed);
        double poolB = PooledSigma(rows.Select(r => r.B));
        double poolG = PooledSigma(rows.Select(r => r.G));
        var bc = rows.Select(r => CostModel(r.B, poolB)).ToArray();
        var gc = rows.Select(r => CostModel(r.G, poolG)).ToArray();

        var dpAll = new List<double>(BootIters);
        var dpBoth = new List<double>(BootIters);
        var geo = new List<double>(BootIters);
        int nonEstimable = 0;

        // Primary pass: uniform Beta(1,1) prior.
        BootstrapPass(rows, rng, bc, gc, 1.0, dpAll, dpBoth, geo, ref nonEstimable);

        // Jeffreys Beta(½,½) sensitivity — ΔP|both only.
        var dpBothJ = new List<double>(BootIters);
        var dpAllJ = new List<double>(BootIters);
        var geoJ = new List<double>(BootIters);
        int junk = 0;
        BootstrapPass(rows, rng, bc, gc, 0.5, dpAllJ, dpBothJ, geoJ, ref junk);

        // Null-calibrated loss-mass gate: both arms redrawn from pooled p̃ᵢ, finite-suite.
        var lossNull = new List<double>(NullIters);
        for (int b = 0; b < NullIters; b++)
        {
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                int kb = rows[i].B.k, kg = rows[i].G.k;
                double pt = (rows[i].B.Deliv + rows[i].G.Deliv) / (double)(kb + kg);
                int Kb = rng.NextBinomial(kb, pt);
                int Kg = rng.NextBinomial(kg, pt);
                double dp = (double)Kg / kg - (double)Kb / kb;
                sum += Math.Max(-dp, 0);
            }
            lossNull.Add(sum);
        }
        lossNull.Sort();

        double estRate = (double)(BootIters - nonEstimable) / BootIters;
        return new Bands(
            Ci95(dpAll), Ci95(dpBoth), Ci95(dpBothJ),
            Ci95Mult(geo), estRate,
            Stats.Percentile(lossNull, 0.95));
    }

    // One bootstrap pass. priorAB is the Beta prior weight (1.0 uniform, 0.5 Jeffreys); per task draw
    // posterior rates, binomial K*, joint lognormal costs; collect ΔP, ΔP|both and geo over S* (S*
    // recomputed each iteration — a task with K*=0 for either arm leaves S* that iteration).
    private static void BootstrapPass(
        List<TaskRow> rows, Rng rng, (double mu, double sig)[] bc, (double mu, double sig)[] gc,
        double priorAB, List<double> dpAll, List<double> dpBoth, List<double> geo, ref int nonEstimable)
    {
        int n = rows.Count;
        for (int b = 0; b < BootIters; b++)
        {
            double sumDpAll = 0, sumDpBoth = 0, sumLnR = 0;
            int cntBoth = 0;
            for (int i = 0; i < n; i++)
            {
                int kb = rows[i].B.k, kg = rows[i].G.k;
                double pb = rng.NextBeta(priorAB + rows[i].B.Deliv, priorAB + kb - rows[i].B.Deliv);
                double pg = rng.NextBeta(priorAB + rows[i].G.Deliv, priorAB + kg - rows[i].G.Deliv);
                sumDpAll += pg - pb;

                int Kb = rng.NextBinomial(kb, pb);
                int Kg = rng.NextBinomial(kg, pg);
                if (Kb >= 1 && Kg >= 1)
                {
                    double cb = 0, cg = 0;
                    for (int r = 0; r < kb; r++) cb += Math.Exp(rng.NextNormal(bc[i].mu, bc[i].sig));
                    for (int r = 0; r < kg; r++) cg += Math.Exp(rng.NextNormal(gc[i].mu, gc[i].sig));
                    double lb = cb / Kb, lg = cg / Kg;
                    if (lb > 0 && lg > 0) { sumLnR += Math.Log(lg / lb); }
                    sumDpBoth += pg - pb;
                    cntBoth++;
                }
            }
            dpAll.Add(sumDpAll / n);
            if (cntBoth > 0) { dpBoth.Add(sumDpBoth / cntBoth); geo.Add(Math.Exp(sumLnR / cntBoth)); }
            else nonEstimable++;
        }
    }

    private static Ci Ci95(List<double> xs)
    {
        var (_, lo, hi) = Stats.MeanCI(xs);
        return new Ci(lo, hi);
    }
    private static Ci Ci95Mult(List<double> xs) => Ci95(xs);   // same percentiles; formatted as ×

    private static string CiSigned(Ci? c) => c is { } v ? $"[{Signed(v.Lo)}, {Signed(v.Hi)}]" : "—";
    private static string CiMult(Ci? c) => c is { } v ? $"[{Mult(v.Lo)}, {Mult(v.Hi)}]" : "—";

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
