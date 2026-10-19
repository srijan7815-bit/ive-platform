using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ive.Core;

public sealed record ApprovalRequest(string Title, string Detail);

/// <summary>
/// The agent's hands. Read-only tools run freely; write/delete/run are gated behind
/// <c>approve</c> and a hard-coded protected-path list. Not a sandbox — the approval gate is a UX control.
/// </summary>
public sealed class Toolbox
{
    public const int MaxRead = 200 * 1024;
    private const int MaxOut = 20_000;

    private static readonly Regex[] Protected =
    {
        new(@"^[a-z]:\\windows(\\|$)", RegexOptions.IgnoreCase),
        new(@"^[a-z]:\\program files( \(x86\))?(\\|$)", RegexOptions.IgnoreCase),
        new(@"^[a-z]:\\programdata(\\|$)", RegexOptions.IgnoreCase),
        new(@"^/(etc|usr|bin|sbin|boot|System|Library)(/|$)"),
    };

    private readonly string _workspace;
    private readonly bool _requireApproval;
    private readonly Func<ApprovalRequest, Task<bool>> _approve;
    private readonly Func<string, Task<string>> _music;

    public Toolbox(string workspace, bool requireApproval, Func<ApprovalRequest, Task<bool>> approve, Func<string, Task<string>> music = null)
    {
        _music = music;
        _workspace = workspace;
        _requireApproval = requireApproval;
        _approve = approve;
    }

    public static bool IsProtected(string path) => Protected.Any(r => r.IsMatch(path ?? ""));

    private string Resolve(string p) => Path.GetFullPath(Path.Combine(_workspace, p ?? ""));

    private static JsonObject Fn(string name, string desc, string[] props, string[] required)
    {
        var properties = new JsonObject();
        foreach (var p in props) properties[p] = new JsonObject { ["type"] = "string" };
        var req = new JsonArray();
        foreach (var r in required) req.Add(r);
        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = desc,
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = req },
            },
        };
    }

    /// <summary>Fresh nodes on every call (a JsonNode can only have one parent).</summary>
    public static JsonArray Schemas() => new()
    {
        Fn("read_file", "Read a UTF-8 text file (first 200 KB).", new[] { "path" }, new[] { "path" }),
        Fn("list_dir", "List files and folders in a directory.", new[] { "path" }, new[] { "path" }),
        Fn("write_file", "Create or overwrite a text file. Requires user approval.", new[] { "path", "content" }, new[] { "path", "content" }),
        Fn("delete_file", "Delete a single file. Requires user approval.", new[] { "path" }, new[] { "path" }),
        Fn("run_command", "Run a shell command (60 s timeout). Requires user approval.", new[] { "command", "cwd" }, new[] { "command" }),
        Fn("music_control", "Control the user's chosen music app. action = play_pause | next | previous | open.", new[] { "action" }, new[] { "action" }),
    };

    public static string Describe(string name, JsonObject a)
    {
        string S(string k) => a[k]?.ToString() ?? "";
        return name switch
        {
            "read_file" => "Reading " + Path.GetFileName(S("path")),
            "list_dir" => "Listing " + (Path.GetFileName(S("path")) is { Length: > 0 } n ? n : S("path")),
            "write_file" => "Writing " + Path.GetFileName(S("path")),
            "delete_file" => "Deleting " + Path.GetFileName(S("path")),
            "music_control" => "Music: " + S("action"),
            "run_command" => "Running " + (S("command").Length > 40 ? S("command")[..40] : S("command")),
            _ => name,
        };
    }

    public async Task<JsonObject> ExecuteAsync(string name, JsonObject args, CancellationToken ct)
    {
        try
        {
            string Str(string k) => args[k]?.ToString() ?? "";

            if (name is "write_file" or "delete_file" or "run_command")
            {
                if (name != "run_command" && IsProtected(Resolve(Str("path"))))
                    return Err("Refused: that location is protected.");
                if (_requireApproval && !await _approve(Approval(name, args)).ConfigureAwait(false))
                    return Err("User denied this action.");
            }

            switch (name)
            {
                case "read_file":
                {
                    var bytes = await File.ReadAllBytesAsync(Resolve(Str("path")), ct).ConfigureAwait(false);
                    var take = Math.Min(bytes.Length, MaxRead);
                    return new JsonObject { ["content"] = Encoding.UTF8.GetString(bytes, 0, take), ["truncated"] = bytes.Length > MaxRead };
                }
                case "list_dir":
                {
                    var entries = new JsonArray();
                    foreach (var e in new DirectoryInfo(Resolve(Str("path"))).EnumerateFileSystemInfos().Take(500))
                        entries.Add(new JsonObject { ["name"] = e.Name, ["type"] = e is DirectoryInfo ? "dir" : "file" });
                    return new JsonObject { ["entries"] = entries };
                }
                case "write_file":
                {
                    var target = Resolve(Str("path"));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await File.WriteAllTextAsync(target, Str("content"), ct).ConfigureAwait(false);
                    return new JsonObject { ["ok"] = true, ["path"] = target };
                }
                case "delete_file":
                {
                    var target = Resolve(Str("path"));
                    if (Directory.Exists(target)) return Err("Refusing to delete a directory.");
                    File.Delete(target);
                    return new JsonObject { ["ok"] = true };
                }
                case "music_control":
                    return _music == null ? Err("Music control is not available.") : new JsonObject { ["result"] = await _music(Str("action")) };
                case "run_command":
                    return await RunCommandAsync(Str("command"), string.IsNullOrEmpty(Str("cwd")) ? _workspace : Resolve(Str("cwd")), ct).ConfigureAwait(false);
                default:
                    return Err("Unknown tool: " + name);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return Err(e.Message);
        }
    }

    private static JsonObject Err(string m) => new() { ["error"] = m };

    private static ApprovalRequest Approval(string name, JsonObject a)
    {
        string S(string k) => a[k]?.ToString() ?? "";
        return name switch
        {
            "write_file" => new ApprovalRequest("Write file: " + S("path"), Clip(S("content"), 600)),
            "delete_file" => new ApprovalRequest("Delete file: " + S("path"), "This permanently removes the file."),
            _ => new ApprovalRequest("Run shell command", S("command") + (S("cwd").Length > 0 ? "\n(in " + S("cwd") + ")" : "")),
        };
    }

    private static string Clip(string s, int n = MaxOut) => s.Length <= n ? s : s[..n] + $"\n…[truncated {s.Length - n} chars]";

    private static async Task<JsonObject> RunCommandAsync(string command, string cwd, CancellationToken ct)
    {
        var win = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = win ? "cmd.exe" : "/bin/sh",
            Arguments = win ? "/d /s /c \"" + command + "\"" : "-c \"" + command.Replace("\"", "\\\"") + "\"",
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the command.");
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(true); } catch { /* already gone */ }
            if (ct.IsCancellationRequested) throw;
            return Err("Command timed out after 60 s.");
        }
        return new JsonObject
        {
            ["ok"] = proc.ExitCode == 0,
            ["exitCode"] = proc.ExitCode,
            ["stdout"] = Clip(await stdout),
            ["stderr"] = Clip(await stderr),
        };
    }
}
