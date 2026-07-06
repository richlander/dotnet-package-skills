using System.Text.Json;
using System.Text.Json.Nodes;

namespace Grounding.Run;

// Shared-baseline consistency (push-vs-pull methodology). skill-validator's --baseline-from reuses
// the baseline's SCORED metrics (turnCount, IET) but our `enrich` recomputes toolStats per run from
// its own sessions.db — and a reused-baseline run holds a different (partial) baseline session set,
// so the baseline's trajectory counts (turns / tool calls) diverge between the pull and push arms.
//
// Fix: alongside the baseline file, persist the ENRICHED baseline arms with --baseline-out, then on
// --baseline-from overwrite this run's baseline node with the persisted one — so the baseline is
// byte-identical across delivery arms, keyed by scenario name.
internal static class SharedBaseline
{
    private static string Sidecar(string baselinePath) => baselinePath + ".arms.json";
    private static string ProvFile(string baselinePath) => baselinePath + ".prov.json";

    // Persist each scenario's enriched `baseline` node, keyed by scenario name, plus the corpus
    // provenance so a later --baseline-from can verify it is being reused against a matching corpus.
    public static void Save(string datasetPath, string baselinePath, Provenance prov)
    {
        var root = JsonNode.Parse(File.ReadAllText(datasetPath));
        var arms = new JsonObject();
        foreach (var scen in Scenarios(root))
        {
            var name = scen?["scenarioName"]?.GetValue<string>();
            var baseline = scen?["baseline"];
            if (name is null || baseline is null) continue;
            arms[name] = baseline.DeepClone();
        }
        File.WriteAllText(Sidecar(baselinePath), arms.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        File.WriteAllText(ProvFile(baselinePath), prov.ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    // Verify a --baseline-from pin is valid for the CURRENT run before reusing it: the corpus (nuget
    // version + fixture set) must match, or the baseline is measuring a different world. Returns the
    // violated rules (empty = OK). A pre-provenance pin (no companion file) can't be checked — returns
    // a single "unverified" note so the caller can warn rather than silently trust it.
    public static IReadOnlyList<string> Validate(string baselinePath, Provenance current)
    {
        var pf = ProvFile(baselinePath);
        if (!File.Exists(pf)) return new[] { "unverified: baseline has no provenance companion (pre-provenance pin)" };
        var pinned = Provenance.FromJson(JsonNode.Parse(File.ReadAllText(pf)));
        return pinned is null
            ? new[] { "unverified: baseline provenance unreadable" }
            : pinned.ViolationsAgainst(current, corpusOnly: true);
    }

    // Overwrite this dataset's `baseline` nodes with the persisted (shared) ones, by scenario name.
    public static void Apply(string datasetPath, string baselinePath)
    {
        var side = Sidecar(baselinePath);
        if (!File.Exists(side))
        {
            Console.Error.WriteLine($"   !! shared-baseline sidecar missing: {side} (baseline trajectory not shared).");
            return;
        }
        var arms = JsonNode.Parse(File.ReadAllText(side))!.AsObject();
        var root = JsonNode.Parse(File.ReadAllText(datasetPath));
        int applied = 0;
        foreach (var scen in Scenarios(root))
        {
            var name = scen?["scenarioName"]?.GetValue<string>();
            if (name is null || scen is null || !arms.TryGetPropertyValue(name, out var shared) || shared is null) continue;
            scen["baseline"] = shared.DeepClone();
            applied++;
        }
        File.WriteAllText(datasetPath, root!.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        if (applied == 0)
            Console.Error.WriteLine($"   !! shared baseline applied to 0 scenarios from {side} — scenario names do not match this run (wrong tier/unit?).");
        else
            Console.WriteLine($"   shared baseline applied to {applied} scenario(s) from {side}.");
    }

    private static IEnumerable<JsonNode?> Scenarios(JsonNode? root)
    {
        foreach (var verdict in root?["verdicts"]?.AsArray() ?? new JsonArray())
            foreach (var scen in verdict?["scenarios"]?.AsArray() ?? new JsonArray())
                yield return scen;
    }
}
