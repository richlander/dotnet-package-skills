using System.Text.Json;
using Microsoft.Extensions.AI;

namespace ZeroDaySearch;

// Benign use of two grounded distractor packages. Neither touches the gotcha its
// grounding warns about:
//   * System.Text.Json here only SERIALIZES a record whose property names already
//     match the JSON (PascalCase round-trips cleanly) — the case-insensitive
//     deserialization trap is never exercised.
//   * Microsoft.Extensions.AI here only constructs a ChatMessage value — no
//     IChatClient, no automatic tool/function calling — so the function-invocation
//     wiring gotcha is never exercised.
// The packages are real project dependencies, but their full guidance is dead weight
// for this task.
public static class Diagnostics
{
    public record BuildInfo(string Tool, int SchemaVersion);

    public static string Describe()
    {
        var info = new BuildInfo("ZeroDaySearch", 1);
        string json = JsonSerializer.Serialize(info);

        var note = new ChatMessage(ChatRole.User, "CVE search diagnostics");

        return $"{json} :: {note.Text}";
    }
}
