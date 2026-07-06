using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Grounding.Json;

namespace Grounding.Analyze;

// The content ledger — LIET's structural dual. LIET measures the per-rung benefit magnitude;
// the ledger ATTRIBUTES that benefit to specific AGENTS.md content blocks. It is a filter over
// the per-scenario data already collected (baseline pass/archaeology/IET + what baseline dug for,
// grounded pass/archaeology/read-grounding) — no re-run, exactly like LIET.
//
// A block is JUSTIFIED when it maps to >=1 rung where baseline shows a DEFICIT (fail OR
// archaeology>0 OR IET premium) and the block documents the subject baseline went digging for.
// Two health reads fall out: ORPHANS (blocks that map to no deficit-rung) and COVERAGE GAPS
// (deficit-rungs with no attributed block). Attribution here is correlational (subject overlap,
// section granularity, run[last] subjects); leave-one-block-out ablation is the gold standard,
// reserved for contested/ singly-attributed blocks.
internal sealed partial class Ledger
{
    private readonly TextWriter _o;
    public Ledger(TextWriter? o = null) => _o = o ?? Console.Out;

    [GeneratedRegex(@"^https?://([^/]+)(/[^?#]*)?")]
    private static partial Regex Url();

    // Identifier-ish tokens: PascalCase/attribute names, min length 4 to cut noise.
    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9]{3,}")]
    private static partial Regex Word();

    // Terms too generic to carry attribution signal (markdown/plumbing/temp-path noise).
    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "http","https","www","com","org","net","github","github","nuget","dotnet","microsoft",
        "packages","package","index","json","blob","main","master","wiki","tree","docs","doc",
        "html","http","https","raw","refs","heads","releases","download","api","apis",
        "var","folders","tmp","temp","private","users","home","null","true","false","void",
        "this","that","with","from","into","using","use","used","uses","each","both","also",
        "value","values","type","types","name","names","class","public","string","list","new",
        "csharp","code","file","files","path","paths","test","tests","build","dll","exe","sln",
        "csproj","program","console","system","text","output","input","render","rendered",
        "grep","strings","find","cat","head","tail","curl","wget","bash","command","args",
        "markdown","table","tables","field","fields","property","properties","attribute","attributes",
        "example","examples","default","optional","required","return","returns","method","methods",
    };

    private sealed class Block
    {
        public string Title = "";
        public HashSet<string> Terms = new(StringComparer.OrdinalIgnoreCase);
        // deficit-rungs this block is attributed to, keyed by rung short-name -> tier
        public readonly Dictionary<string, string> Rungs = new();
        public readonly HashSet<string> Via = new(StringComparer.OrdinalIgnoreCase); // distinctive terms that drove attribution
        public double DArch, DIet; // summed deltas over attributed rungs
    }

    private sealed class Rung
    {
        public string Short = "";
        public string Tier = "";
        public bool BasePass, GrndPass, Activated;
        public double BaseArch, GrndArch, BaseIet, GrndIet;
        public HashSet<string> Subjects = new(StringComparer.OrdinalIgnoreCase);
        public bool Deficit;
        public string DeficitKind = "";
        public readonly List<string> Attributed = new(); // block titles
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
            var rungs = new List<Rung>();
            foreach (var s in v.Scenarios ?? new())
                if (BuildRung(s, iet, ietPremium) is { } r) rungs.Add(r);

            Attribute(blocks, rungs, distinctive, minOverlap);
            Emit(Path.GetFileName(docPath), model, blocks, rungs);
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

    // Split the AGENTS.md body into blocks at `## ` headings; strip YAML frontmatter. Each block's
    // index-terms are the identifier tokens it documents (backtick code + PascalCase/attribute names).
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
            if (line.StartsWith("## "))
            {
                cur = new Block { Title = line[3..].Trim() };
                blocks.Add(cur);
            }
            foreach (var t in Identifiers(line))
                if (!Stop.Contains(t)) cur.Terms.Add(t);
        }
        return blocks.Where(b => b.Terms.Count > 0).ToList();
    }

    // Identifier tokens worth attributing on: things in `backticks`, [Attributes], and PascalCase
    // names. Lowercased english prose words are filtered by the Stop list at the call site.
    private static IEnumerable<string> Identifiers(string line)
    {
        foreach (Match m in Word().Matches(line))
        {
            var w = m.Value;
            // keep tokens that look like API identifiers: an internal capital, or a capitalized
            // token >=5 chars (Metric, Callout, TreeNode, MarkoutSection, TableFormatter, Tsv...).
            bool internalCap = w.Skip(1).Any(char.IsUpper);
            bool bigCap = char.IsUpper(w[0]) && w.Length >= 5;
            if (internalCap || bigCap) yield return w;
        }
    }

    // ---- per-rung deficit + dig-subjects -----------------------------------
    private static Rung? BuildRung(Scenario s, IetScheme model, double ietPremium)
    {
        var b = Loader.Row(s.Baseline, model);
        var g = Loader.Row(s.SkilledIsolated, model);
        if (b is null) return null;
        var name = s.ScenarioName ?? s.Name ?? "?";
        var r = new Rung
        {
            Short = Shorten(name),
            Tier = Tier(name),
            BasePass = b.Ft > 0 && b.Fp == b.Ft,
            GrndPass = g is not null && g.Ft > 0 && g.Fp == g.Ft,
            Activated = Loader.ActivatedOf(s, "skilledIsolated"),
            BaseArch = b.Cache + b.NugetWeb + b.Web,
            GrndArch = g is null ? 0 : g.Cache + g.NugetWeb + g.Web,
            BaseIet = b.Iet,
            GrndIet = g?.Iet ?? 0,
            Subjects = DigSubjects(s.Baseline),
        };
        // Deficit: baseline underperforms — fails, digs, or over-pays vs the grounded arm.
        var premium = r.BaseIet > 0 && g is not null && (r.BaseIet - r.GrndIet) / r.BaseIet >= ietPremium;
        if (!r.BasePass) { r.Deficit = true; r.DeficitKind = "fail"; }
        else if (r.BaseArch > 0) { r.Deficit = true; r.DeficitKind = "archaeology"; }
        else if (premium) { r.Deficit = true; r.DeficitKind = "iet"; }
        return r;
    }

    // What baseline went digging for: web domains + URL/query keywords + bash grep/inspect targets.
    private static HashSet<string> DigSubjects(Arm? baseline)
    {
        var subs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in baseline?.Metrics?.Events ?? new())
        {
            if (e.Type != "tool.execution_start") continue;
            var name = e.Data?.ToolName ?? "";
            var a = ParseArgs(e.Data?.Arguments ?? "{}");
            string text = name switch
            {
                "web_fetch" => a.TryGetValue("url", out var u) ? PathWords(u) : "",
                "web_search" => a.TryGetValue("query", out var q) ? q : "",
                "bash" => a.TryGetValue("command", out var c) ? c : "",
                _ => "",
            };
            if (text.Length == 0) continue;
            foreach (Match m in Word().Matches(text))
                if (!Stop.Contains(m.Value) && !Noise(m.Value)) subs.Add(m.Value);
        }
        return subs;
    }

    // Reject non-identifier tokens that pollute dig-subjects: hex hashes / session GUIDs, macOS
    // temp-dir slugs, digit-heavy ids. Real API identifiers have vowels, aren't hex, aren't
    // digit-dominated — so this keeps MarkoutSection / GetVersionsAsync and drops sv-9f3a… noise.
    private static bool Noise(string w)
    {
        int digits = w.Count(char.IsDigit);
        if (digits * 5 >= w.Length * 2) return true;                       // >40% digits
        if (w.Length >= 8 && w.All(c => Uri.IsHexDigit(c))) return true;   // hex hash
        if (w.Length >= 12 && !w.Any(c => "aeiouAEIOU".Contains(c))) return true; // vowelless slug
        return false;
    }

    private static string PathWords(string url)
    {
        var m = Url().Match(url);
        return m.Success ? m.Groups[1].Value + " " + m.Groups[2].Value : url;
    }

    private static Dictionary<string, string> ParseArgs(string json)
    {
        var outp = new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return outp;
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) outp[p.Name] = p.Value.GetString() ?? "";
        }
        catch (JsonException) { }
        return outp;
    }

    // Distinctive terms = those NOT shared by most blocks. A term appearing in more than half the
    // blocks is the doc's ambient vocabulary (e.g. NuGetClient, MarkoutSerializer) and carries no
    // attribution signal — matching on it makes every block cover every rung. Keep the rare ones.
    private static HashSet<string> Distinctive(List<Block> blocks)
    {
        var df = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in blocks)
            foreach (var t in b.Terms)
                df[t] = df.GetValueOrDefault(t) + 1;
        var maxDf = Math.Max(1, blocks.Count / 2);
        return df.Where(kv => kv.Value <= maxDf).Select(kv => kv.Key)
                 .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // ---- attribution -------------------------------------------------------
    // A block is attributed to a deficit-rung only when it shares >= minOverlap DISTINCTIVE terms
    // with what baseline dug for. One coincidental token (e.g. a package name that happened to
    // surface once) is not enough — that fragility masks true orphans (see the negative control).
    private static void Attribute(List<Block> blocks, List<Rung> rungs, HashSet<string> distinctive, int minOverlap)
    {
        foreach (var r in rungs.Where(r => r.Deficit))
        {
            var digDistinct = r.Subjects.Where(distinctive.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (digDistinct.Count == 0) continue;
            foreach (var blk in blocks)
            {
                var overlap = blk.Terms.Where(t => digDistinct.Contains(t)).ToList();
                if (overlap.Count < minOverlap) continue;
                r.Attributed.Add(blk.Title);
                blk.Rungs[r.Short] = r.Tier;
                foreach (var t in overlap) blk.Via.Add(t);
                blk.DArch += Math.Max(0, r.BaseArch - r.GrndArch);
                blk.DIet += Math.Max(0, r.BaseIet - r.GrndIet);
            }
        }
    }

    // ---- output ------------------------------------------------------------
    private void Emit(string doc, string model, List<Block> blocks, List<Rung> rungs)
    {
        var deficits = rungs.Where(r => r.Deficit).ToList();
        var hasCt = rungs.Any(r => r.Tier == "CT");
        _o.WriteLine($"### Content ledger — `{doc}` | `{model}`\n");
        _o.WriteLine($"_{blocks.Count} content blocks · {rungs.Count} rungs ({deficits.Count} with a baseline deficit). "
            + "Attribution = baseline's dig-subject ∩ block index-terms (correlational; run[last] subjects; section granularity)._\n");

        // Justification table: rows = blocks, what deficit-rungs each covers.
        _o.WriteLine("#### Justification (block → deficit-rungs it covers)\n");
        _o.WriteLine("| Content block | rungs covered | ΔIET | Δarch | via (distinctive) | generalization |");
        _o.WriteLine("| --- | --- | ---: | ---: | --- | --- |");
        foreach (var b in blocks)
        {
            var covered = b.Rungs.Count;
            var rungList = covered == 0 ? "—" : string.Join(", ", b.Rungs.Keys.OrderBy(x => x));
            var via = covered == 0 ? "—" : string.Join(" ", b.Via.Take(5));
            var gen = "—";
            if (hasCt && covered > 0)
            {
                int held = b.Rungs.Count(kv => kv.Value == "CT");
                int auth = covered - held;
                gen = auth == 0 ? (held > 0 ? "held-out only" : "—") : $"{held}/{auth} held/auth";
            }
            var flag = covered == 0 ? " **ORPHAN**" : "";
            _o.WriteLine($"| {b.Title}{flag} | {rungList} | {(covered == 0 ? "—" : ((long)b.DIet).ToString())} | {(covered == 0 ? "—" : ((long)Math.Round(b.DArch)).ToString())} | {via} | {gen} |");
        }

        // Orphans.
        var orphans = blocks.Where(b => b.Rungs.Count == 0).Select(b => b.Title).ToList();
        _o.WriteLine($"\n#### Orphans ({orphans.Count}) — blocks no deficit-rung attributes\n");
        _o.WriteLine(orphans.Count == 0
            ? "_None — every block is justified by ≥1 deficit-rung._"
            : "_Cut candidates, **or** the ladder doesn't yet exercise them (grow a rung):_ " + string.Join("; ", orphans));

        // Coverage — the honest signal is EMPIRICAL: deficit-rungs the grounded arm did NOT close
        // (still fails, or still digs). Attribution-gaps alone are masked by foundational blocks that
        // match every rung via ambient terms, so we report what the content actually failed to fix.
        var unrec = deficits.Where(r => !r.GrndPass || r.GrndArch >= 1).ToList();
        _o.WriteLine($"\n#### Coverage gaps ({unrec.Count}) — deficit-rungs the grounded arm did NOT close\n");
        if (unrec.Count == 0) _o.WriteLine("_None — every baseline deficit was recovered (grounded passes, no residual archaeology)._");
        else
        {
            _o.WriteLine("| Rung | deficit | grounded still | attributed? | top dig-subjects |");
            _o.WriteLine("| --- | --- | --- | --- | --- |");
            foreach (var r in unrec)
            {
                var still = !r.GrndPass ? "**fails**" : $"digs {r.GrndArch:0.#}";
                // attributed by a SPECIALIZED block (not just a foundational catch-all) is the real test.
                var attr = r.Attributed.Count == 0 ? "no — **undocumented**"
                    : $"yes ({r.Attributed.Count})";
                var top = string.Join(" ", r.Subjects.Where(x => x.Length >= 5).Take(6));
                _o.WriteLine($"| {r.Short} | {r.DeficitKind} | {still} | {attr} | {top} |");
            }
            _o.WriteLine("\n_undocumented + unrecovered = genuinely missing content; documented + unrecovered = "
                + "content present but ineffective (salience/quality, not coverage)._");
        }
        _o.WriteLine();
    }

    // ---- naming ------------------------------------------------------------
    private static string Shorten(string name)
    {
        var colon = name.IndexOf(':');
        var head = colon > 0 ? name[..colon] : name;
        return head.Trim();
    }

    private static string Tier(string name)
    {
        var m = TierRx().Match(name);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
    }

    [GeneratedRegex(@"^\s*([A-Za-z]+)\d")]
    private static partial Regex TierRx();
}
