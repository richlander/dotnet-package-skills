namespace Grounding;

// Resolves where regenerable eval artifacts (datasets + raw skill-validator
// results) are written. These are NOT committed to any repo: the tree holds
// only inputs (AGENTS.md, eval.yaml, fixtures, TASKS.md, optional SKILL.md).
// Datasets default to a durable, user-owned cache OUTSIDE the tree, overridable
// via GROUNDING_DATA_DIR (or, per-run, `--out`). The distilled quality card is
// the durable artifact and lives in the PR body, not the tree.
internal static class DataCache
{
    // Base cache dir: $GROUNDING_DATA_DIR, else $XDG_CACHE_HOME/grounding,
    // else ~/.cache/grounding. Deliberately not $TMPDIR//tmp — those get
    // purged on reboot/pressure and would silently drop a reusable baseline.
    public static string Base()
    {
        var overridden = Environment.GetEnvironmentVariable("GROUNDING_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overridden)) return overridden;

        var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        var cacheHome = !string.IsNullOrWhiteSpace(xdg)
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
        return Path.Combine(cacheHome, "grounding");
    }

    // Distilled per-unit datasets (the .json the analyze card reads).
    public static string DatasetDir(string unit) => Path.Combine(Base(), $"{unit}-6q");

    // Raw skill-validator run output, grouped under the cache.
    public static string ResultsRoot() => Path.Combine(Base(), "results");

    public static string ResultsDir(string unit, string tag) => Path.Combine(ResultsRoot(), $"{unit}-{tag}");
}
