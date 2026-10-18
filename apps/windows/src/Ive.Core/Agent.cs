using System.Text.Json.Nodes;

namespace Ive.Core;

public enum AgentEventKind { Thinking, Tool }

public sealed record AgentEvent(AgentEventKind Kind, string Label = "");

public delegate Task<JsonObject> ChatFunc(JsonArray messages, JsonArray tools, CancellationToken ct);

/// <summary>The tool-calling loop. All I/O is injected, so it is fully unit-testable.</summary>
public sealed class AgentRunner
{
    public const int MaxTurns = 16;
    private const int MaxToolResult = 12_000;

    private static string SystemPrompt(string workspace) =>
        "You are IVE, a background agent running on the user's own Windows PC with their permission.\n" +
        $"Today is {DateTime.Now:D}. Relative paths resolve against: {workspace}\n" +
        "You may read and list files freely. Writing files, deleting files and running shell commands require the user's approval — " +
        "call those tools when needed; approval is handled outside of you, and a denial comes back as an error you must respect.\n" +
        "Be decisive and finish in as few steps as possible. When completely done, reply with a short plain-text summary (no tool call).";

    public async Task<string> RunAsync(string prompt, string workspace, Toolbox tools, ChatFunc chat, Action<AgentEvent> onEvent, CancellationToken ct)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = SystemPrompt(workspace) },
            new JsonObject { ["role"] = "user", ["content"] = prompt },
        };

        for (var turn = 0; turn < MaxTurns; turn++)
        {
            ct.ThrowIfCancellationRequested();
            onEvent?.Invoke(new AgentEvent(AgentEventKind.Thinking));
            var msg = await chat(messages, Toolbox.Schemas(), ct).ConfigureAwait(false);
            messages.Add(msg);

            if (msg["tool_calls"] is not JsonArray calls || calls.Count == 0)
                return msg["content"]?.ToString() is { Length: > 0 } text ? text : "(no response)";

            foreach (var node in calls)
            {
                var call = node.AsObject();
                var name = call["function"]?["name"]?.ToString() ?? "";
                JsonObject args;
                try { args = JsonNode.Parse(call["function"]?["arguments"]?.ToString() ?? "{}") as JsonObject ?? new JsonObject(); }
                catch { args = new JsonObject(); }

                onEvent?.Invoke(new AgentEvent(AgentEventKind.Tool, Toolbox.Describe(name, args)));
                var result = await tools.ExecuteAsync(name, args, ct).ConfigureAwait(false);
                var json = result.ToJsonString();
                if (json.Length > MaxToolResult) json = json[..MaxToolResult] + "…[truncated]";
                messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call["id"]?.ToString(), ["content"] = json });
            }
        }
        throw new InvalidOperationException("Task exceeded the maximum number of steps without finishing.");
    }
}
