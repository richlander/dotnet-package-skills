using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Grounding.Run;

// Dataset provenance — the pin key that makes re-runs cheap. A generated arm-dataset is valid to
// REUSE as long as its provenance matches the current corpus + doc:
//   corpus  = pinned nuget version + fixture-set hash  (a change invalidates ALL arms)
//   arm doc = the grounding doc's content hash (+ commit for traceability) (invalidates only that arm)
// Baseline has no doc, so it pins on the corpus alone. Assertions are judgment-side and are NOT part
// of provenance — they re-score over kept sessions without regenerating (see `rescore-assertions`).
internal sealed partial class Provenance
{
    public string? NugetVersion { get; init; }
    public string? FixtureHash { get; init; }
    public string Source { get; init; } = "none";   // agents | readme | skill | none (baseline)
    public string? DocPath { get; init; }            // repo-relative, for traceability
    public string? DocCommit { get; init; }          // last commit touching the doc
    public string? DocContentHash { get; init; }     // sha256 of the doc bytes — the invalidation key

    [GeneratedRegex("""Include\s*=\s*"Markout"[^>]*Version\s*=\s*"\[?([0-9][^"\]]*)""")]
    private static partial Regex MarkoutVersion();

    // Compute the provenance of the arm about to be (or already) generated, from the live tree.
    public static Provenance Compute(string root, string source, string? docPath, string fixturesDir)
    {
        string? docHash = null, docCommit = null, docRel = null;
        if (source != "none" && docPath is not null && File.Exists(docPath))
        {
            docHash = Sha256File(docPath);
            docRel = Rel(root, docPath);
            docCommit = GitLastCommit(root, docRel);
        }
        return new Provenance
        {
            NugetVersion = NugetFrom(fixturesDir),
            FixtureHash = HashDir(fixturesDir),
            Source = source,
            DocPath = docRel,
            DocCommit = docCommit,
            DocContentHash = docHash,
        };
    }

    // Corpus (nuget + fixtures) must match; for a grounded arm the doc content hash must match too.
    // Baseline (source "none") ignores the doc fields. DocCommit is traceability only, not a key.
    public bool ReusableAs(Provenance now) =>
        NugetVersion == now.NugetVersion
        && FixtureHash == now.FixtureHash
        && Source == now.Source
        && (Source == "none" || DocContentHash == now.DocContentHash);

    // Human-readable list of the pin RULES this (a stored pin) violates against the current context —
    // used to explain WHY a reuse/baseline is invalid instead of silently applying a mismatched one.
    public List<string> ViolationsAgainst(Provenance now, bool corpusOnly = false)
    {
        var v = new List<string>();
        if (NugetVersion != now.NugetVersion)
            v.Add($"nuget version (pinned {NugetVersion ?? "—"} vs current {now.NugetVersion ?? "—"})");
        if (FixtureHash != now.FixtureHash)
            v.Add($"fixture set (pinned {FixtureHash ?? "—"} vs current {now.FixtureHash ?? "—"})");
        if (!corpusOnly)
        {
            if (Source != now.Source)
                v.Add($"grounding source (pinned {Source} vs current {now.Source})");
            else if (Source != "none" && DocContentHash != now.DocContentHash)
                v.Add($"doc content ({DocPath ?? "doc"} pinned {DocContentHash ?? "—"} vs current {now.DocContentHash ?? "—"})");
        }
        return v;
    }

    public JsonObject ToJson() => new()
    {
        ["nugetVersion"] = NugetVersion,
        ["fixtureHash"] = FixtureHash,
        ["source"] = Source,
        ["docPath"] = DocPath,
        ["docCommit"] = DocCommit,
        ["docContentHash"] = DocContentHash,
    };

    public static Provenance? FromDataset(string datasetPath)
    {
        if (!File.Exists(datasetPath)) return null;
        try { return FromJson(JsonNode.Parse(File.ReadAllText(datasetPath))?["provenance"]); }
        catch { return null; }
    }

    // Parse a provenance object (as written by ToJson) from a JSON node.
    public static Provenance? FromJson(JsonNode? p)
    {
        if (p is null) return null;
        return new Provenance
        {
            NugetVersion = p["nugetVersion"]?.GetValue<string>(),
            FixtureHash = p["fixtureHash"]?.GetValue<string>(),
            Source = p["source"]?.GetValue<string>() ?? "none",
            DocPath = p["docPath"]?.GetValue<string>(),
            DocCommit = p["docCommit"]?.GetValue<string>(),
            DocContentHash = p["docContentHash"]?.GetValue<string>(),
        };
    }

    // Stamp the provenance object into a produced dataset (top-level "provenance").
    public static void Stamp(string datasetPath, Provenance prov)
    {
        var root = JsonNode.Parse(File.ReadAllText(datasetPath))!.AsObject();
        root["provenance"] = prov.ToJson();
        File.WriteAllText(datasetPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));
    }

    // `grounding provenance <datasets>...` — show each dataset's pin key; with 2+, report whether
    // each subsequent dataset is reusable as the first (same corpus + doc identity).
    public static int Report(IReadOnlyList<string> files)
    {
        var provs = files.Select(f => (file: f, prov: FromDataset(f))).ToList();
        foreach (var (file, prov) in provs)
        {
            Console.WriteLine($"### {Path.GetFileName(file)}");
            if (prov is null) { Console.WriteLine("  (no provenance stamped — pre-provenance dataset)\n"); continue; }
            Console.WriteLine($"  source        {prov.Source}");
            Console.WriteLine($"  nugetVersion  {prov.NugetVersion ?? "—"}");
            Console.WriteLine($"  fixtureHash   {prov.FixtureHash ?? "—"}");
            Console.WriteLine($"  docPath       {prov.DocPath ?? "—"}");
            Console.WriteLine($"  docCommit     {prov.DocCommit ?? "—"}");
            Console.WriteLine($"  docContentHash {prov.DocContentHash ?? "—"}\n");
        }
        if (provs.Count >= 2 && provs[0].prov is { } baseP)
        {
            Console.WriteLine($"Reuse check (vs `{Path.GetFileName(provs[0].file)}`):");
            foreach (var (file, prov) in provs.Skip(1))
            {
                var ok = prov is not null && prov.ReusableAs(baseP);
                Console.WriteLine($"  {(ok ? "REUSABLE ✓" : "regenerate ✗")}  {Path.GetFileName(file)}");
            }
        }
        return 0;
    }

    // ---- helpers -----------------------------------------------------------
    private static string? NugetFrom(string fixturesDir)
    {
        if (!Directory.Exists(fixturesDir)) return null;
        foreach (var csproj in Directory.EnumerateFiles(fixturesDir, "*.csproj", SearchOption.AllDirectories))
        {
            var m = MarkoutVersion().Match(File.ReadAllText(csproj));
            if (m.Success) return m.Groups[1].Value.TrimEnd(']');
        }
        return null;
    }

    // Content hash of a fixture set: sha256 over each file's repo-relative path + bytes, in sorted
    // order, so it is stable across machines and invalidates on any fixture edit/add/remove.
    private static string? HashDir(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        using var sha = SHA256.Create();
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                     .Select(p => (rel: Rel(dir, p), path: p))
                     .OrderBy(x => x.rel, StringComparer.Ordinal))
        {
            var relBytes = Encoding.UTF8.GetBytes(f.rel + "\0");
            sha.TransformBlock(relBytes, 0, relBytes.Length, null, 0);
            var body = File.ReadAllBytes(f.path);
            sha.TransformBlock(body, 0, body.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return "sha256:" + Convert.ToHexString(sha.Hash!)[..16].ToLowerInvariant();
    }

    private static string Sha256File(string path)
    {
        using var sha = SHA256.Create();
        return "sha256:" + Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path)))[..16].ToLowerInvariant();
    }

    private static string Rel(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string? GitLastCommit(string root, string relPath)
    {
        try
        {
            var psi = new ProcessStartInfo("git", $"-C \"{root}\" log -1 --format=%h -- \"{relPath}\"")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            var outp = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return string.IsNullOrEmpty(outp) ? null : outp;
        }
        catch { return null; }
    }
}
