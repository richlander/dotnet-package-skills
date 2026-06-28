namespace Grounding;

// Locates the repository root by walking up from the cwd (then the binary
// location) until a directory holding both `grounding/` and `eng/` is found.
internal static class RepoRoot
{
    private static string? _cached;

    public static string? Find()
    {
        if (_cached is not null) return _cached;
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "grounding")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "eng")))
                {
                    _cached = dir.FullName;
                    return _cached;
                }
                dir = dir.Parent;
            }
        }
        return null;
    }
}
