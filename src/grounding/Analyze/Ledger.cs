using System.Text.Json;
using System.Text.RegularExpressions;
using Grounding.Json;

namespace Grounding.Analyze;

// The content ledger — attribute AGENTS.md blocks to the FUNCTIONAL ASSERTIONS they cover, using
// the per-assertion baseline↔grounded diff as evidence. A filter over existing data (no re-run).
//
// Assertions are the precise, declared unit (not noisy dig-subjects). Per question each assertion
// aligns by index across arms, so we read the diff directly:
//   type 2  file_contains  -> value is the required API id (Metric, MarkoutSerializer.Serialize) —
//                             the GOLD attribution signal: it tests "did the code use API X", and X
//                             maps to the block documenting X.
//   type 9  run_command    -> the correctness oracle (build + expected-output regex); sets task pass.
//   type 11 reject_tools   -> archaeology guard; a fail→pass flip = grounding removed the web/dig.
//
// Per assertion × {covered by a block?} × {diff}: covered+flip = load-bearing; covered+still-fail =
// present-but-ineffective (salience); uncovered+still-fail = missing content; covered+both-pass =
// redundant for this model; uncovered+flip = win not from the doc. Block with no covered assertion =
// ORPHAN (nothing tests it → cut or grow a rung). Attribution is a curated-to-curated lexical join.
internal sealed partial class Ledger
{
    private readonly TextWriter _o;
    public Ledger(TextWriter? o = null) => _o = o ?? Console.Out;

    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9]{3,}")]
    private static partial Regex Word();
    [GeneratedRegex(@"^\s*([A-Za-z]+)\d")]
    private static partial Regex TierRx();

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "http","https","www","github","nuget","dotnet","microsoft","packages","package","json",
        "value","values","type","types","name","names","class","public","string","list","true","false",
        "code","file","files","path","test","build","program","console","system","text","using","with",
        "markdown","table","tables","field","fields","property","properties","example","default","render",
    };

    private sealed class Block
    {
        public string Title = "";
        public HashSet<string> Terms = new(StringComparer.OrdinalIgnoreCase);
        public int Flips, BothPass, StillFail;              // covered assertions by outcome
        public int FlipHeld, FlipAuth;                       // flips on CT(held-out) vs MM(authoring)
        public readonly HashSet<string> Via = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Rungs = new();       // scenarios touched
    }

    private sealed class Asrt
    {
        public string Kind = "";                             // api | output | build | reject
        public HashSet<string> Targets = new(StringComparer.OrdinalIgnoreCase);
        public bool BasePass, GrndPass;
        public bool Flip => !BasePass && GrndPass;
        public bool StillFail => !GrndPass;
        public bool BothPass => BasePass && GrndPass;
        public string? RejectTool;
    }

    private sealed class Scen
    {
        public string Short = "", Tier = "";
        public List<Asrt> Asserts = new();
        public bool BaseTaskPass, GrndTaskPass;
        public double BaseIet, GrndIet, BaseArch, GrndArch;
        public string Case = "";                             // redundant | efficiency | correctness | regressed
    }

    public int Run(IReadOnlyList<string> files, string? docOverride, double ietPremium, int minOverlap)
    {
        foreach (var f in files)
        {
            var d = Loader.Parse(f);
            var model = d.Model ?? "?";
            var iet = IetModels.For(model);
            var v = d.Verdicts is { Count: > 0 } ? d.Verdicts[0] : new Verdict();
            var docPath = ResolveDoc(docOverride, v.SkillPath, v.SkillName);
            if (docPath is null) { _o.WriteLine($"ledger: could not resolve AGENTS.md for {Path.GetFileName(f)}."); continue; }
            var blocks = Segment(File.ReadAllText(docPath));
            var distinctive = Distinctive(blocks);
            var scens = (v.Scenarios ?? new()).Select(s => BuildScen(s, iet, ietPremium)).Where(s => s is not null).Select(s => s!).ToList();
            Attribute(blocks, scens, distinctive, minOverlap);
            Emit(Path.GetFileName(docPath), model, blocks, scens);
        }
        return 0;
    }

    // ---- doc segmentation --------------------------------------------------
    private static string? ResolveDoc(string? over, string? skillPath, string? skillName)
    {
        if (!string.IsNullOrWhiteSpace(over) && File.Exists(over)) return over;
        var root = RepoRoot.Find();
        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(skillPath))
            dirs.Add(Path.IsPathRooted(skillPath) || root is null ? skillPath! : Path.Combine(root, skillPath!));
        if (root is not null && !string.IsNullOrWhiteSpace(skillName))
            dirs.Add(Path.Combine(root, "grounding", skillName!));
        foreach (var dir in dirs)
        {
            var p = Path.Combine(dir, "AGENTS.md");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static List<Block> Segment(string text)
    {
        var body = text;
        if (body.StartsWith("---"))
        {
            var end = body.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end >= 0) { var nl = body.IndexOf('\n', end + 1); body = nl >= 0 ? body[(nl + 1)..] : ""; }
        }
        var blocks = new List<Block>();
        Block cur = new() { Title = "(preamble)" };
        blocks.Add(cur);
        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("## ")) { cur = new Block { Title = line[3..].Trim() }; blocks.Add(cur); }
            foreach (var t in Identifiers(line)) if (!Stop.Contains(t)) cur.Terms.Add(t);
        }
        return blocks.Where(b => b.Terms.Count > 0).ToList();
    }

    private static IEnumerable<string> Identifiers(string s)
    {
        foreach (Match m in Word().Matches(s))
        {
            var w = m.Value;
            if (w.Skip(1).Any(char.IsUpper) || (char.IsUpper(w[0]) && w.Length >= 5)) yield return w;
        }
    }

    // Ambient vocabulary (in > half the blocks) carries no attribution signal — keep the rare terms.
    private static HashSet<string> Distinctive(List<Block> blocks)
    {
        var df = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in blocks) foreach (var t in b.Terms) df[t] = df.GetValueOrDefault(t) + 1;
        var maxDf = Math.Max(1, blocks.Count / 2);
        return df.Where(kv => kv.Value <= maxDf).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // ---- per-scenario assertion diff --------------------------------------
    private static Scen? BuildScen(Scenario s, IetScheme model, double ietPremium)
    {
        var bArm = s.Baseline; var gArm = s.SkilledIsolated;
        var bRes = bArm?.Metrics?.AssertionResults; var gRes = gArm?.Metrics?.AssertionResults;
        if (bRes is null || gRes is null) return null;
        var bRow = Loader.Row(bArm, model); var gRow = Loader.Row(gArm, model);
        var name = s.ScenarioName ?? s.Name ?? "?";
        var sc = new Scen
        {
            Short = Shorten(name), Tier = Tier(name),
            BaseIet = bRow?.Iet ?? 0, GrndIet = gRow?.Iet ?? 0,
            BaseArch = bRow is null ? 0 : bRow.Cache + bRow.NugetWeb + bRow.Web,
            GrndArch = gRow is null ? 0 : gRow.Cache + gRow.NugetWeb + gRow.Web,
        };
        int n = Math.Min(bRes.Count, gRes.Count);
        for (int i = 0; i < n; i++)
        {
            var a = MakeAsrt(bRes[i], gRes[i].Passed);
            if (a is not null) sc.Asserts.Add(a);
        }
        // task pass = every non-reject (functional) assertion passes.
        var func = sc.Asserts.Where(a => a.Kind != "reject").ToList();
        sc.BaseTaskPass = func.Count > 0 && func.All(a => a.BasePass);
        sc.GrndTaskPass = func.Count > 0 && func.All(a => a.GrndPass);
        // classify: correctness (fail→pass), else efficiency vs redundant by the resource divide.
        var bigDivide = (sc.BaseIet > 0 && (sc.BaseIet - sc.GrndIet) / sc.BaseIet >= ietPremium) || sc.BaseArch - sc.GrndArch >= 2;
        sc.Case = !sc.BaseTaskPass && sc.GrndTaskPass ? "correctness"
            : sc.BaseTaskPass && !sc.GrndTaskPass ? "regressed"
            : bigDivide ? "efficiency" : "redundant";
        return sc;
    }

    private static Asrt? MakeAsrt(AssertionResult bRes, bool grndPass)
    {
        var asr = bRes.Assertion; if (asr is null) return null;
        var a = new Asrt { BasePass = bRes.Passed, GrndPass = grndPass };
        switch (asr.Type)
        {
            case 2: // file_contains -> API identifier
                a.Kind = "api";
                foreach (var t in Identifiers(asr.Value ?? "")) if (!Stop.Contains(t)) a.Targets.Add(t);
                break;
            case 11: // reject_tools -> archaeology guard
                a.Kind = "reject"; a.RejectTool = asr.Value;
                break;
            case 9: // run_command -> build (structural) or output oracle
                a.Kind = (asr.CommandArgs?.CommandArguments ?? "").Contains("build") ? "build" : "output";
                break;
            default:
                a.Kind = "other";
                break;
        }
        return a;
    }

    // ---- attribution: assertion.targets ∩ block distinctive terms ----------
    // Assertion values are often a lenient SUBSTRING the eval greps for ("ShowWhen", "Unwrap") while
    // the doc writes the full identifier ("ShowWhenProperty", "MarkoutUnwrap"). Match on containment
    // (either side, min length 5) so the identifier family joins; keep exact for short tokens.
    private static bool TermMatch(string blockTerm, string target)
    {
        if (string.Equals(blockTerm, target, StringComparison.OrdinalIgnoreCase)) return true;
        int min = Math.Min(blockTerm.Length, target.Length);
        if (min < 5) return false;
        return blockTerm.Contains(target, StringComparison.OrdinalIgnoreCase)
            || target.Contains(blockTerm, StringComparison.OrdinalIgnoreCase);
    }

    private static void Attribute(List<Block> blocks, List<Scen> scens, HashSet<string> distinctive, int minOverlap)
    {
        foreach (var sc in scens)
            foreach (var a in sc.Asserts.Where(a => a.Kind == "api" && a.Targets.Count > 0))
            {
                foreach (var blk in blocks)
                {
                    // filter the BLOCK side by distinctive (drop ambient vocab); match targets by family.
                    var overlap = blk.Terms.Where(t => distinctive.Contains(t) && a.Targets.Any(g => TermMatch(t, g))).ToList();
                    if (overlap.Count < minOverlap) continue;
                    blk.Rungs.Add(sc.Short);
                    foreach (var t in overlap) blk.Via.Add(t);
                    if (a.Flip) { blk.Flips++; if (sc.Tier == "CT") blk.FlipHeld++; else blk.FlipAuth++; }
                    else if (a.BothPass) blk.BothPass++;
                    else if (a.StillFail) blk.StillFail++;
                }
            }
    }

    private static bool Covered(List<Block> blocks, HashSet<string> targets) =>
        blocks.Any(b => b.Terms.Any(t => targets.Any(g => TermMatch(t, g))));

    // ---- output ------------------------------------------------------------
    private void Emit(string doc, string model, List<Block> blocks, List<Scen> scens)
    {
        var api = scens.SelectMany(s => s.Asserts.Where(a => a.Kind == "api")).ToList();
        int flips = api.Count(a => a.Flip), redundant = api.Count(a => a.BothPass), failing = api.Count(a => a.StillFail);
        _o.WriteLine($"### Content ledger — `{doc}` | `{model}`\n");
        _o.WriteLine($"_{blocks.Count} blocks · {scens.Count} rungs · {api.Count} API assertions "
            + $"({flips} flipped fail→pass, {failing} still failing, {redundant} baseline-already-knew). "
            + "Attribution = assertion API-id ∩ block terms (curated join; no re-run)._\n");

        // Scenario cases (your 3 cases).
        _o.WriteLine("#### Rung cases\n");
        _o.WriteLine("| case | rungs |");
        _o.WriteLine("| --- | --- |");
        foreach (var g in new[] { "correctness", "efficiency", "redundant", "regressed" })
        {
            var rs = scens.Where(s => s.Case == g).Select(s => s.Short).OrderBy(x => x).ToList();
            if (rs.Count > 0) _o.WriteLine($"| {CaseLabel(g)} | {string.Join(", ", rs)} |");
        }

        // Block justification.
        _o.WriteLine("\n#### Justification (block → API assertions it covers)\n");
        _o.WriteLine("| Content block | load-bearing (flips) | redundant | ineffective | generalization | via |");
        _o.WriteLine("| --- | ---: | ---: | ---: | --- | --- |");
        foreach (var b in blocks)
        {
            int covered = b.Flips + b.BothPass + b.StillFail;
            var gen = b.Flips == 0 ? "—" : b.FlipHeld > 0 && b.FlipAuth == 0 ? "held-out only" : $"{b.FlipHeld}/{b.FlipAuth} held/auth";
            var flag = covered == 0 ? " **ORPHAN**" : b.Flips == 0 ? " *(redundant here)*" : "";
            var via = b.Via.Count == 0 ? "—" : string.Join(" ", b.Via.Take(5));
            _o.WriteLine($"| {b.Title}{flag} | {b.Flips} | {b.BothPass} | {b.StillFail} | {gen} | {via} |");
        }

        // Orphans.
        var orphans = blocks.Where(b => b.Flips + b.BothPass + b.StillFail == 0).Select(b => b.Title).ToList();
        _o.WriteLine($"\n#### Orphans ({orphans.Count}) — blocks no assertion exercises\n");
        _o.WriteLine(orphans.Count == 0 ? "_None — every block is exercised by ≥1 API assertion._"
            : "_Cut candidates, **or** the ladder doesn't test them (grow a rung):_ " + string.Join("; ", orphans));

        // Assertion-level coverage (the rung/assertion side): what content failed to land / is missing.
        var salience = new List<string>(); var missing = new List<string>(); var uncredited = new List<string>();
        foreach (var sc in scens)
            foreach (var a in sc.Asserts.Where(a => a.Kind == "api" && a.Targets.Count > 0))
            {
                bool covered = Covered(blocks, a.Targets);
                var tag = $"{sc.Short}:{string.Join("/", a.Targets.Take(2))}";
                if (a.StillFail && covered) salience.Add(tag);
                else if (a.StillFail && !covered) missing.Add(tag);
                else if (a.Flip && !covered) uncredited.Add(tag);
            }
        _o.WriteLine($"\n#### Assertion coverage\n");
        _o.WriteLine($"- **present but ineffective (salience)** — documented, grounded still fails: {Fmt(salience)}");
        _o.WriteLine($"- **missing content** — undocumented, grounded still fails: {Fmt(missing)}");
        _o.WriteLine($"- **uncredited flips** — grounded fixed it, but no block documents it: {Fmt(uncredited)}");
        _o.WriteLine();
    }

    private static string Fmt(List<string> xs) => xs.Count == 0 ? "_none_" : string.Join(", ", xs);
    private static string CaseLabel(string c) => c switch
    {
        "correctness" => "correctness (baseline fail → grounded pass)",
        "efficiency" => "efficiency (both pass, big IET/arch divide)",
        "redundant" => "redundant (both pass, small divide)",
        "regressed" => "regressed (baseline pass → grounded fail)",
        _ => c,
    };

    private static string Shorten(string name) { var c = name.IndexOf(':'); return (c > 0 ? name[..c] : name).Trim(); }
    private static string Tier(string name) { var m = TierRx().Match(name); return m.Success ? m.Groups[1].Value.ToUpperInvariant() : ""; }
}
