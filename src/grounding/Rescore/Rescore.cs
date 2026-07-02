using System.Globalization;
using Grounding.Json;

namespace Grounding.Rescore;

// Re-score skill-validator results
// under the grounding-specific IET rubric.
internal static class Rescore
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const double CacheReadMult = 0.10;
    private const double CacheWriteMult = 1.25;
    private const double DefaultOutputMult = 5.0;
    private const double WQuality = 0.70;
    private const double WCost = 0.30;
    private const double HelpThreshold = 0.20;
    private const double HarmCostFrac = 0.10;

    private static double Clamp(double x, double lo = -1.0, double hi = 1.0) => Math.Max(lo, Math.Min(hi, x));

    private static double ArmIet(Metrics m, double wOut)
    {
        var freshIn = Math.Max(0, m.InputTokens - m.CacheReadTokens - m.CacheWriteTokens);
        return freshIn + m.CacheReadTokens * CacheReadMult + m.CacheWriteTokens * CacheWriteMult + m.OutputTokens * wOut;
    }

    private static double HaikuRatio(string model)
    {
        var s = model.ToLowerInvariant();
        if (s.Contains("opus")) return 15.0;
        if (s.Contains("sonnet")) return 3.0;
        if (s.Contains("haiku")) return 1.0;
        return 1.0;
    }

    private static double Q(Arm arm) => (arm.JudgeResult?.OverallScore ?? 0.0) / 5.0;

    private sealed record ArmScore(double Dq, double Iet, double CostReduction, double Score);

    private sealed class Run
    {
        public required string Model;
        public double BaseQ, BaseIet, HietRatio;
        public required ArmScore Gate;
        public double? Harness;
    }

    // ---- single-scenario rescore (single scenario) ----------------------------

    public static int Single(IReadOnlyList<string> specs, double wOut)
    {
        if (specs.Count == 0) { Console.Error.WriteLine("rescore: need model=path specs."); return 1; }

        var runs = new List<Run>();
        foreach (var spec in specs)
        {
            string model, path;
            var eq = spec.IndexOf('=');
            var colon = spec.IndexOf(':');
            if (eq >= 0) { model = spec[..eq]; path = spec[(eq + 1)..]; }
            else if (colon >= 0) { model = spec[..colon]; path = spec[(colon + 1)..]; }
            else { Console.Error.WriteLine($"Bad spec '{spec}'; use model=path"); return 1; }
            runs.Add(Analyze(model.Trim(), path.Trim(), wOut));
        }

        runs = runs.OrderBy(r => r.BaseQ).ThenBy(r => r.BaseIet).ToList();

        var line = new string('=', 100);
        Console.WriteLine(line);
        Console.WriteLine("GROUNDING RE-SCORE  (one scenario, varying agent model; judge fixed)");
        Console.WriteLine($"IET = fresh_in + 0.1*cacheRead + 1.25*cacheWrite + {wOut.ToString("0.######", Inv)}*output   (units: base-input-tokens)");
        Console.WriteLine("HIET = IET x input-price-vs-Haiku (Opus 15x / Sonnet 3x / Haiku 1x): dollar-comparable across tiers");
        Console.WriteLine("Our score = 0.70*dQuality + 0.30*costReduction(IET)");
        Console.WriteLine(line);
        var hdr = L("agent model", 20) + R("baseQ/5", 8) + R("gate dQ", 9) + R("base IET", 11)
                  + R("gate IET", 11) + R("gate HIET", 12) + R("costRed", 9) + R("OUR", 8);
        Console.WriteLine(hdr);
        Console.WriteLine(new string('-', hdr.Length));
        foreach (var r in runs)
        {
            var g = r.Gate;
            Console.WriteLine(
                L(r.Model, 20)
                + R((r.BaseQ * 5).ToString("F1", Inv), 8)
                + R(Pct(g.Dq), 9)
                + R(F0(r.BaseIet), 11)
                + R(F0(g.Iet), 11)
                + R(F0(g.Iet * r.HietRatio), 12)
                + R(Pct(g.CostReduction), 9)
                + R(Pct(g.Score), 8));
        }
        Console.WriteLine(new string('-', hdr.Length));
        Console.WriteLine("(IET in base-input-token units; HIET in Haiku-input-token equivalents; 'gate' = worse arm under OUR score)");
        Console.WriteLine();

        Console.WriteLine("CURVE  (weakest -> strongest by baseline quality):");
        foreach (var r in runs)
        {
            var g = r.Gate;
            Console.WriteLine($"  {L(r.Model, 20)} dQuality={R(Pct(g.Dq), 8)}  costReduction={R(Pct(g.CostReduction), 8)}  "
                + $"OUR={R(Pct(g.Score), 8)}  harness={R(Pct(r.Harness), 8)}");
        }
        Console.WriteLine();

        var weakest = runs[0];
        var strongest = runs[^1];
        var helpsWeak = weakest.Gate.Dq >= HelpThreshold;
        var sg = strongest.Gate;
        var harmsFrontier = sg.Dq <= 0 && sg.CostReduction <= -HarmCostFrac;
        var helpsAny = runs.Any(r => r.Gate.Dq >= HelpThreshold || r.Gate.CostReduction >= HarmCostFrac);

        Console.WriteLine("PARETO VERDICT (grounding is auto-installed & un-removable):");
        Console.WriteLine($"  weakest tier ({weakest.Model}): dQuality={Pct(weakest.Gate.Dq)}  -> materially helps? {PyBool(helpsWeak)}");
        Console.WriteLine($"  frontier tier ({strongest.Model}): dQuality={Pct(sg.Dq)}, costReduction={Pct(sg.CostReduction)} -> harms frontier? {PyBool(harmsFrontier)}");
        string verdict;
        if (helpsWeak && !harmsFrontier)
            verdict = "SHIP - helps the tier that needs it; no meaningful harm to the frontier.";
        else if (helpsWeak && harmsFrontier)
            verdict = "SHIP WITH CARE - helps weak tier but taxes the frontier; rely on retrieval to keep it out of frontier context.";
        else if (!helpsAny)
            verdict = "RIP OUT - helps no tier on quality or cost.";
        else
            verdict = "MARGINAL - re-examine scope / retrieval.";
        Console.WriteLine($"  => {verdict}");
        return 0;
    }

    private static Run Analyze(string model, string path, double wOut)
    {
        if (Directory.Exists(path)) path = Path.Combine(path, "results.json");
        var d = Grounding.Analyze.Loader.Parse(path);
        var s = d.Verdicts![0].Scenarios![0];
        var baseArm = s.Baseline!;
        var baseQ = Q(baseArm);
        var baseIet = ArmIet(baseArm.Metrics!, wOut);

        var order = new (string Name, Arm? Arm)[] { ("skilledIsolated", s.SkilledIsolated), ("skilledPlugin", s.SkilledPlugin) };
        ArmScore? gate = null;
        foreach (var (_, arm) in order)
        {
            if (arm is null) continue;
            var dq = Q(arm) - baseQ;
            var iet = ArmIet(arm.Metrics!, wOut);
            var costReduction = baseIet != 0 ? Clamp((baseIet - iet) / baseIet) : 0.0;
            var score = WQuality * Clamp(dq) + WCost * costReduction;
            var cand = new ArmScore(dq, iet, costReduction, score);
            if (gate is null || cand.Score < gate.Score) gate = cand; // first-min on ties
        }
        return new Run
        {
            Model = model,
            BaseQ = baseQ,
            BaseIet = baseIet,
            HietRatio = HaikuRatio(model),
            Gate = gate!,
            Harness = s.ImprovementScore,
        };
    }

    private static string Pct(double? x) =>
        x is { } v ? Signed1(v * 100) + "%" : "  n/a";

    private static string Signed1(double v) => (v < 0 ? "-" : "+") + Math.Abs(v).ToString("F1", Inv);

    // ---- batch rescore over .skill-validator-results (batch) -----

    private static readonly (string Key, string Val)[] Lib =
    {
        ("CommandLine", "SCL"), ("System.CommandLine", "SCL"), ("McMaster", "SCL"),
        ("Text.Json", "STJ"), ("Newtonsoft", "STJ"), ("Native AOT", "STJ"), ("AOT", "STJ"),
        ("Extensions.AI", "M.E.AI"),
    };

    private static string LibOf(string name)
    {
        foreach (var (k, v) in Lib) if (name.Contains(k)) return v;
        return "?";
    }

    private static string PctAll(double? x) => x is { } v ? Signed1(v * 100) + "%" : "   n/a";

    public static int All(double wOut)
    {
        var resultsRoot = DataCache.ResultsRoot();
        var rows = new List<(string Lib, string Scenario, string Agent, double BaseQ, double? Harness,
            double GateDq, double GateCred, double GateOur, double BestCred, double Biet, string Dir)>();

        if (Directory.Exists(resultsRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(resultsRoot).OrderBy(x => x, StringComparer.Ordinal))
            {
                var f = Path.Combine(dir, "results.json");
                if (!File.Exists(f)) continue;
                var j = Grounding.Analyze.Loader.Parse(f);
                var model = j.Model ?? "?";
                foreach (var v in j.Verdicts ?? new())
                {
                    foreach (var s in v.Scenarios ?? new())
                    {
                        var name = s.Name ?? s.ScenarioName ?? "";
                        var baseArm = s.Baseline;
                        if (baseArm is null) continue;
                        var bq = Q(baseArm);
                        var biet = ArmIet(baseArm.Metrics!, wOut);
                        var arms = new List<(double Dq, double Cr, double Our)>();
                        foreach (var arm in new[] { s.SkilledIsolated, s.SkilledPlugin })
                        {
                            if (arm is null) continue;
                            var dq = Q(arm) - bq;
                            var iet = ArmIet(arm.Metrics!, wOut);
                            var cr = biet != 0 ? Clamp((biet - iet) / biet) : 0.0;
                            arms.Add((dq, cr, WQuality * Clamp(dq) + WCost * cr));
                        }
                        if (arms.Count == 0) continue;
                        var gate = arms.Aggregate((a, b) => b.Our < a.Our ? b : a);
                        var best = arms.Aggregate((a, b) => b.Our > a.Our ? b : a);
                        rows.Add((LibOf(name), Trunc(name, 48), model.Replace("claude-", ""),
                            bq * 5, s.ImprovementScore, gate.Dq, gate.Cr, gate.Our, best.Cr, biet,
                            Path.GetFileName(dir)));
                    }
                }
            }
        }

        rows = rows
            .OrderBy(r => r.Lib, StringComparer.Ordinal)
            .ThenBy(r => r.Scenario, StringComparer.Ordinal)
            .ThenBy(r => r.Dir, StringComparer.Ordinal)
            .ToList();

        var hdr = L("lib", 7) + L("scenario", 50) + L("agent", 13) + R("baseQ", 6) + R("harness", 9)
                  + R("gate dQ", 9) + R("gate cRed", 10) + R("OUR", 8) + R("best cRed", 10);
        Console.WriteLine(hdr);
        Console.WriteLine(new string('-', hdr.Length));
        string? last = null;
        foreach (var r in rows)
        {
            if (last is not null && last != r.Lib) Console.WriteLine();
            last = r.Lib;
            Console.WriteLine(
                L(r.Lib, 7) + L(r.Scenario, 50) + L(r.Agent, 13)
                + R(r.BaseQ.ToString("F1", Inv), 6) + R(PctAll(r.Harness), 9)
                + R(PctAll(r.GateDq), 9) + R(PctAll(r.GateCred), 10)
                + R(PctAll(r.GateOur), 8) + R(PctAll(r.BestCred), 10));
        }
        return 0;
    }

    // ---- formatting helpers ----------------------------------------------

    private static string F0(double v) => v.ToString("F0", Inv);
    private static string PyBool(bool b) => b ? "True" : "False";
    private static string Trunc(string s, int n) => s.Length > n ? s[..n] : s;
    private static string L(string s, int w) => s.Length >= w ? s : s + new string(' ', w - s.Length);
    private static string R(string s, int w) => s.Length >= w ? s : new string(' ', w - s.Length) + s;
}
