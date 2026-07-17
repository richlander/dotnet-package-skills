using System.Text;

namespace Grounding.Run;

// Parses an AGENTS.md (YAML frontmatter + body) and renders the SKILL.md the
// skill-validator harness consumes.
internal sealed class SkillDoc
{
    public string? Name;
    public string? Description;
    public string Body = "";

    private const string GeneratedMarker =
        "<!-- Transient grounding wrapper (SKILL.md or .agent.md) synthesized from AGENTS.md by the harness at eval time. Do not edit or commit. -->";

    public static SkillDoc ParseAgents(string agentsPath, string? metaPath)
    {
        var text = File.ReadAllText(agentsPath);
        var (frontmatter, body) = SplitFrontmatter(text);
        var doc = new SkillDoc
        {
            Name = ExtractScalar(frontmatter, "name"),
            Description = ExtractScalar(frontmatter, "description"),
            Body = body,
        };
        if (metaPath is not null && File.Exists(metaPath))
        {
            var meta = File.ReadAllText(metaPath);
            doc.Name ??= ExtractScalar(meta, "name");
            doc.Description ??= ExtractScalar(meta, "description");
        }
        return doc;
    }

    // Build a doc from meta.yaml alone (no AGENTS.md). Used by SKILL.md-only units, whose
    // graded artifact is skills/<unit>/SKILL.md; here we only need name/description for the
    // baseline/readme wrapper. Body stays empty (never rendered for --source skill/none).
    public static SkillDoc FromMeta(string? metaPath, string unit)
    {
        var doc = new SkillDoc { Name = unit };
        if (metaPath is not null && File.Exists(metaPath))
        {
            var meta = File.ReadAllText(metaPath);
            doc.Name = ExtractScalar(meta, "name") ?? unit;
            doc.Description = ExtractScalar(meta, "description");
        }
        return doc;
    }

    // Split leading `---` ... `---` frontmatter from the body; strip blank lines
    // immediately after the closing fence. If absent, the whole file is the body.
    private static (string Frontmatter, string Body) SplitFrontmatter(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0] != "---")
            return ("", text.Replace("\r\n", "\n"));
        var fm = new List<string>();
        var i = 1;
        for (; i < lines.Length && lines[i] != "---"; i++) fm.Add(lines[i]);
        i++; // skip closing ---
        while (i < lines.Length && lines[i].Trim().Length == 0) i++;
        var body = string.Join("\n", lines.Skip(i));
        if (!body.EndsWith('\n')) body += "\n";
        return (string.Join("\n", fm), body);
    }

    // Read a scalar `key:` value, folding block scalars (>-, >, |, |-) into one
    // space-joined line, matching the original awk extractor.
    private static string? ExtractScalar(string yaml, string key)
    {
        var lines = yaml.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(key + ":")) continue;
            var val = lines[i][(key.Length + 1)..].Trim();
            if (val is ">-" or ">" or "|" or "|-")
            {
                var parts = new List<string>();
                for (var j = i + 1; j < lines.Length; j++)
                {
                    if (lines[j].Length > 0 && !char.IsWhiteSpace(lines[j][0])) break;
                    parts.Add(lines[j].Trim());
                }
                return string.Join(" ", parts).Trim();
            }
            return val;
        }
        return null;
    }

    public string Render(string body)
    {
        var esc = (Description ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("name: ").Append(Name ?? "").Append('\n');
        sb.Append("description: \"").Append(esc).Append("\"\n");
        sb.Append("---\n\n");
        sb.Append(GeneratedMarker).Append("\n\n");
        sb.Append(body);
        return sb.ToString();
    }

    // grep -c '' on the body: number of newlines (body always ends with one).
    public int BodyLineCount
    {
        get
        {
            if (Body.Length == 0) return 0;
            var n = Body.Count(c => c == '\n');
            return Body.EndsWith('\n') ? n : n + 1;
        }
    }
}
