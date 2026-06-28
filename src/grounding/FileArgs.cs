namespace Grounding;

internal static class FileArgs
{
    // Expand glob-ish paths (mirrors Python glob.glob(..., recursive=True) or [p]).
    public static List<string> Expand(IEnumerable<string> paths)
    {
        var outp = new List<string>();
        foreach (var p in paths)
        {
            if (p.Contains('*') || p.Contains('?'))
            {
                var dir = Path.GetDirectoryName(p);
                var pat = Path.GetFileName(p);
                var baseDir = string.IsNullOrEmpty(dir) ? "." : dir;
                if (Directory.Exists(baseDir))
                    outp.AddRange(Directory.GetFiles(baseDir, pat));
            }
            else
            {
                outp.Add(p);
            }
        }
        return outp.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
    }
}
