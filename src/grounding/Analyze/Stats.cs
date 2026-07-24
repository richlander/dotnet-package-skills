using System.Globalization;

namespace Grounding.Analyze;

// Minimal, dependency-free samplers for the quality-card bands pass. Deterministic (seeded) so a
// card is reproducible. Beta via two Gammas; Gamma via Marsaglia–Tsang with the α<1 boost so the
// Jeffreys Beta(½,·) prior is supported; Normal via Box–Muller.
internal sealed class Rng
{
    private readonly Random _r;
    private double? _spare;

    public Rng(int seed) => _r = new Random(seed);

    public double NextUniform() => _r.NextDouble();

    public double NextNormal(double mean = 0.0, double sd = 1.0)
    {
        if (_spare is { } s) { _spare = null; return mean + sd * s; }
        double u1, u2;
        do { u1 = _r.NextDouble(); } while (u1 <= 1e-12);
        u2 = _r.NextDouble();
        double mag = Math.Sqrt(-2.0 * Math.Log(u1));
        _spare = mag * Math.Sin(2.0 * Math.PI * u2);
        return mean + sd * (mag * Math.Cos(2.0 * Math.PI * u2));
    }

    // Marsaglia–Tsang gamma (shape k, scale 1). Handles k<1 via the boost k -> k+1 then U^(1/k).
    public double NextGamma(double shape)
    {
        if (shape < 1.0)
        {
            double u = _r.NextDouble();
            return NextGamma(shape + 1.0) * Math.Pow(u <= 1e-12 ? 1e-12 : u, 1.0 / shape);
        }
        double d = shape - 1.0 / 3.0;
        double c = 1.0 / Math.Sqrt(9.0 * d);
        while (true)
        {
            double x = NextNormal();
            double v = 1.0 + c * x;
            if (v <= 0) continue;
            v = v * v * v;
            double uu = _r.NextDouble();
            if (uu < 1.0 - 0.0331 * x * x * x * x) return d * v;
            if (Math.Log(uu) < 0.5 * x * x + d * (1.0 - v + Math.Log(v))) return d * v;
        }
    }

    public double NextBeta(double a, double b)
    {
        double x = NextGamma(a), y = NextGamma(b);
        return x / (x + y);
    }

    // Number of successes in k Bernoulli(p) trials (k is small — direct draws).
    public int NextBinomial(int k, double p)
    {
        int n = 0;
        for (int i = 0; i < k; i++) if (_r.NextDouble() < p) n++;
        return n;
    }
}

internal static class Stats
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static double Percentile(List<double> sorted, double q)
    {
        if (sorted.Count == 0) return double.NaN;
        if (sorted.Count == 1) return sorted[0];
        double idx = q * (sorted.Count - 1);
        int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
        double frac = idx - lo;
        return sorted[lo] + (sorted[hi] - sorted[lo]) * frac;
    }

    // Mean + [2.5%, 97.5%] credible/confidence interval from a sample.
    public static (double mean, double lo, double hi) MeanCI(List<double> sample)
    {
        if (sample.Count == 0) return (double.NaN, double.NaN, double.NaN);
        var s = sample.OrderBy(x => x).ToList();
        return (sample.Average(), Percentile(s, 0.025), Percentile(s, 0.975));
    }

    public static string Sig(double x, string fmt = "0.000") => (x >= 0 ? "+" : "") + x.ToString(fmt, Inv);
}
