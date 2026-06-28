using System.Text.Json;
using System.Text.Json.Nodes;

namespace Grounding.Mcp;

// Minimal stdio JSON-RPC 2.0 MCP server serving NuGet package grounding (AGENTS.md).
// Port of grounding/_mcp/grounding_mcp.py. The WHEN-TO-CALL guidance in the tool
// description is the experimental variable, selected by GROUNDING_GATE.
internal static class McpServer
{
    private const string ToolName = "get_package_context";
    private const string SummaryToolName = "summarize_package_context";
    private const int SummaryFallbackLines = 8;

    private static readonly string Gate =
        (Environment.GetEnvironmentVariable("GROUNDING_GATE") ?? "uncertainty_version").Trim();

    // Repo root holding grounding/. Resolved from --root / GROUNDING_ROOT / discovery,
    // since the harness spawns the MCP server with an unrelated working directory.
    private static string RepoRootPath = "";
    private static string GroundingDir => Path.Combine(RepoRootPath, "grounding");
    private static string CallLog => Path.Combine(RepoRootPath, ".tools", "mcp-calls.log");

    private static readonly Dictionary<string, string> GateDescriptions = new()
    {
        ["task_type"] =
            "Retrieves authoritative documentation and usage guidance for a NuGet "
            + "package. Call this whenever the user asks a question about a package, its "
            + "API, or how to use it.",
        ["uncertainty_version"] =
            "Retrieves the authoritative, version-specific API reference and migration "
            + "guidance for an installed NuGet package. Call this before writing or editing "
            + "code against the package when you are not fully confident of the current API "
            + "for the installed version, or when the installed version may post-date your "
            + "training data (e.g. a new major/GA release). Do not call it for packages whose "
            + "current API you already know with confidence.",
    };

    private const string ProgressiveSummaryDescription =
        "Returns a short summary (name + when-to-use) of the agent guidance available for "
        + "an installed NuGet package, without the full body. Cheap to call. Use it to decide "
        + "whether the package's full guidance is worth retrieving with get_package_context.";
    private const string ProgressiveFullDescription =
        "Returns the full agent guidance (the package's AGENTS.md body) for an installed "
        + "NuGet package. Call this only after summarize_package_context shows the guidance is "
        + "relevant to the task at hand.";
    private const string ResidentIndexBase =
        "Returns the full agent guidance (AGENTS.md body) for an installed NuGet package. "
        + "Below is the always-available index of packages that ship guidance, with a one-line "
        + "summary of when each one matters. Read the index for free; call this tool only for a "
        + "package whose summary is relevant to the task. If no summary is relevant, do not call it.";

    public static int Run(string? root = null)
    {
        RepoRootPath = root
            ?? Environment.GetEnvironmentVariable("GROUNDING_ROOT")
            ?? RepoRoot.Find()
            ?? Directory.GetCurrentDirectory();
        Log($"started; gate='{Gate}'; repo={RepoRootPath}");
        string? raw;
        while ((raw = Console.In.ReadLine()) is not null)
        {
            raw = raw.Trim();
            if (raw.Length == 0) continue;
            JsonObject msg;
            try { msg = JsonNode.Parse(raw)!.AsObject(); }
            catch (JsonException e) { Log($"bad json: {e.Message}"); continue; }

            JsonObject? resp;
            try { resp = Handle(msg); }
            catch (Exception e)
            {
                Log($"handler error: {e.Message}");
                var mid = msg["id"];
                if (mid is null) continue;
                resp = new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = mid.DeepClone(),
                    ["error"] = new JsonObject { ["code"] = -32603, ["message"] = e.Message },
                };
            }
            if (resp is not null)
            {
                Console.Out.Write(resp.ToJsonString() + "\n");
                Console.Out.Flush();
            }
        }
        Log("stdin closed; exiting");
        return 0;
    }

    private static JsonObject? Handle(JsonObject msg)
    {
        var method = msg["method"]?.GetValue<string>();
        var idNode = msg["id"];
        if (idNode is null) return null; // notification

        var id = idNode.DeepClone();
        switch (method)
        {
            case "initialize":
                var clientProto = msg["params"]?["protocolVersion"]?.GetValue<string>() ?? "2024-11-05";
                return new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["result"] = new JsonObject
                    {
                        ["protocolVersion"] = clientProto,
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                        ["serverInfo"] = new JsonObject { ["name"] = "package-grounding", ["version"] = "0.1.0" },
                    },
                };
            case "tools/list":
                return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = new JsonObject { ["tools"] = ListTools() } };
            case "tools/call":
                return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = HandleToolsCall(msg["params"]?.AsObject()) };
            case "ping":
                return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = new JsonObject() };
            default:
                return new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Method not found: {method}" },
                };
        }
    }

    private static JsonObject HandleToolsCall(JsonObject? @params)
    {
        var p = @params ?? new JsonObject();
        var tool = p["name"]?.GetValue<string>() ?? ToolName;
        var args = p["arguments"]?.AsObject() ?? new JsonObject();
        var packageName = Str(args, "packageName") ?? Str(args, "package_name");
        var packageVersion = Str(args, "packageVersion") ?? Str(args, "package_version");
        RecordCall(tool, packageName, packageVersion);
        Log($"tools/call {tool} package={Repr(packageName)} version={Repr(packageVersion)} gate={Gate}");

        var summary = tool == SummaryToolName;
        var text = ContextText(packageName, packageVersion, summary);
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = false,
        };
    }

    private static JsonArray ListTools()
    {
        if (Gate == "progressive")
            return new JsonArray(
                Tool(SummaryToolName, ProgressiveSummaryDescription),
                Tool(ToolName, ProgressiveFullDescription));
        return new JsonArray(Tool(ToolName, ToolDescription()));
    }

    private static JsonObject Tool(string name, string description) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = InputSchema(),
    };

    private static JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["packageName"] = new JsonObject { ["type"] = "string", ["description"] = "NuGet package id, e.g. 'System.CommandLine'." },
            ["packageVersion"] = new JsonObject { ["type"] = "string", ["description"] = "Installed package version, if known." },
        },
        ["required"] = new JsonArray("packageName"),
    };

    private static string ToolDescription()
    {
        if (Gate == "resident_index")
        {
            var manifest = ResidentManifest();
            return manifest.Length > 0 ? $"{ResidentIndexBase}\n\nAvailable package guidance:\n{manifest}" : ResidentIndexBase;
        }
        return GateDescriptions.TryGetValue(Gate, out var d) ? d : GateDescriptions["uncertainty_version"];
    }

    // ---- grounding content helpers (port of the Python module) -----------

    private static (string? Frontmatter, string Body) SplitFrontmatter(string text)
    {
        var lines = PyLines(text);
        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---")
                {
                    var fm = string.Join("\n", lines[1..i]).Trim('\n');
                    var body = string.Join("\n", lines[(i + 1)..]).TrimStart('\n');
                    return (fm, body);
                }
            }
        }
        return (null, text);
    }

    // Mirror Python str.splitlines(): split on line boundaries without yielding a
    // trailing empty element for a final terminator.
    private static string[] PyLines(string s)
    {
        if (s.Length == 0) return Array.Empty<string>();
        var norm = s.Replace("\r\n", "\n").Replace('\r', '\n');
        var parts = norm.Split('\n');
        return parts.Length > 0 && parts[^1].Length == 0 ? parts[..^1] : parts;
    }

    private static string SummaryOf(string text)
    {
        var (fm, _) = SplitFrontmatter(text);
        if (!string.IsNullOrEmpty(fm)) return fm;
        return string.Join("\n", PyLines(text).Take(SummaryFallbackLines));
    }

    private static string BodyOf(string text) => SplitFrontmatter(text).Body;

    private static Dictionary<string, string> FrontmatterFields(string? fm)
    {
        var fields = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(fm)) return fields;
        var lines = PyLines(fm);
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (line.Contains(':') && !line.TrimStart().StartsWith('#') && !(line.StartsWith(' ') || line.StartsWith('\t')))
            {
                var idx = line.IndexOf(':');
                var key = line[..idx].Trim();
                var val = line[(idx + 1)..].Trim();
                if (val is ">" or ">-" or "|" or "|-" or ">+" or "|+")
                {
                    var block = new List<string>();
                    i++;
                    while (i < lines.Length && (lines[i].StartsWith(' ') || lines[i].StartsWith('\t') || lines[i].Trim().Length == 0))
                    {
                        block.Add(lines[i].Trim());
                        i++;
                    }
                    fields[key] = string.Join(" ", block.Where(p => p.Length > 0)).Trim();
                    continue;
                }
                fields[key] = val.Trim('\'', '"');
            }
            i++;
        }
        return fields;
    }

    private static string ResidentManifest()
    {
        if (!Directory.Exists(GroundingDir)) return "";
        var lines = new List<string>();
        var seen = new HashSet<string>();
        foreach (var unit in Directory.EnumerateFileSystemEntries(GroundingDir).OrderBy(x => x, StringComparer.Ordinal))
        {
            var agents = Path.Combine(unit, "AGENTS.md");
            if (!File.Exists(agents)) continue;
            var (fm, _) = SplitFrontmatter(File.ReadAllText(agents));
            var fields = FrontmatterFields(fm);
            var name = fields.TryGetValue("name", out var nm) && nm.Length > 0 ? nm : Path.GetFileName(unit);
            var meta = Path.Combine(unit, "meta.yaml");
            if (File.Exists(meta))
            {
                foreach (var ml0 in PyLines(File.ReadAllText(meta)))
                {
                    var ml = ml0.Trim();
                    if (ml.StartsWith("package:"))
                    {
                        var pkg = ml[8..].Trim().Trim('\'', '"');
                        if (pkg.Length > 0) name = pkg;
                        break;
                    }
                }
            }
            if (!seen.Add(name)) continue;
            var desc = fields.TryGetValue("description", out var d) && d.Length > 0 ? d : "(no summary provided)";
            lines.Add($"- {name}: {desc}");
        }
        return string.Join("\n", lines);
    }

    private static string Norm(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static Dictionary<string, string> PackageIndex()
    {
        var index = new Dictionary<string, string>();
        if (!Directory.Exists(GroundingDir)) return index;
        foreach (var unit in Directory.EnumerateFileSystemEntries(GroundingDir).OrderBy(x => x, StringComparer.Ordinal))
        {
            var agents = Path.Combine(unit, "AGENTS.md");
            if (!File.Exists(agents)) continue;
            index[Norm(Path.GetFileName(unit))] = agents;
            var meta = Path.Combine(unit, "meta.yaml");
            if (File.Exists(meta))
            {
                foreach (var line0 in PyLines(File.ReadAllText(meta)))
                {
                    var line = line0.Trim();
                    if (line.StartsWith("package:"))
                    {
                        var pkg = line[8..].Trim().Trim('\'', '"');
                        if (pkg.Length > 0) index[Norm(pkg)] = agents;
                        break;
                    }
                }
            }
        }
        return index;
    }

    private static string? ResolveAgents(string? packageName) =>
        PackageIndex().TryGetValue(Norm(packageName ?? ""), out var p) ? p : null;

    private static string ContextText(string? packageName, string? packageVersion, bool summary)
    {
        var agents = ResolveAgents(packageName);
        if (agents is null) return $"No grounding context is available for package '{packageName}'.";
        var rawText = File.ReadAllText(agents);
        var payload = summary ? SummaryOf(rawText) : BodyOf(rawText);
        var label = summary ? "Summary for" : "Package context for";
        var header = $"# {label} {packageName}";
        if (!string.IsNullOrEmpty(packageVersion)) header += $" ({packageVersion})";
        return $"{header}\n\n{payload}";
    }

    private static void RecordCall(string tool, string? packageName, string? packageVersion)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CallLog)!);
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            var rec = new JsonObject
            {
                ["ts"] = ts,
                ["gate"] = Gate,
                ["tool"] = tool,
                ["packageName"] = packageName,
                ["packageVersion"] = packageVersion,
                ["pid"] = Environment.ProcessId,
            };
            File.AppendAllText(CallLog, rec.ToJsonString() + "\n");
        }
        catch (Exception e) { Log($"call-log write failed: {e.Message}"); }
    }

    private static string? Str(JsonObject o, string key) =>
        o.TryGetPropertyValue(key, out var v) && v is not null ? v.GetValue<string>() : null;

    private static string Repr(string? s) => s is null ? "None" : $"'{s}'";

    private static void Log(string msg)
    {
        Console.Error.Write($"[grounding-mcp] {msg}\n");
        Console.Error.Flush();
    }
}
