using System.Text.Json.Nodes;
using Ive.Core;
using Xunit;

namespace Ive.Core.Tests;

public class ToolboxTests
{
    private static string Tmp() => Directory.CreateTempSubdirectory("ive").FullName;

    [Fact]
    public void ProtectedPathsAreDetected()
    {
        Assert.True(Toolbox.IsProtected(@"C:\Windows\System32\x.dll"));
        Assert.True(Toolbox.IsProtected(@"c:\program files\app\a.exe"));
        Assert.True(Toolbox.IsProtected("/etc/passwd"));
        Assert.False(Toolbox.IsProtected(@"C:\Users\me\notes.txt"));
    }

    [Fact]
    public async Task ReadAndListNeedNoApproval()
    {
        var dir = Tmp();
        File.WriteAllText(Path.Combine(dir, "a.txt"), "hello");
        var tb = new Toolbox(dir, true, _ => throw new Exception("no approval expected"));
        Assert.Equal("hello", (await tb.ExecuteAsync("read_file", new JsonObject { ["path"] = "a.txt" }, default))["content"]!.ToString());
        var list = await tb.ExecuteAsync("list_dir", new JsonObject { ["path"] = "." }, default);
        Assert.Single(list["entries"]!.AsArray());
    }

    [Fact]
    public async Task DeniedWriteDoesNotTouchDisk()
    {
        var dir = Tmp();
        ApprovalRequest asked = null;
        var tb = new Toolbox(dir, true, r => { asked = r; return Task.FromResult(false); });
        var res = await tb.ExecuteAsync("write_file", new JsonObject { ["path"] = "x.txt", ["content"] = "nope" }, default);
        Assert.Contains("denied", res["error"]!.ToString());
        Assert.StartsWith("Write file", asked.Title);
        Assert.False(File.Exists(Path.Combine(dir, "x.txt")));
    }

    [Fact]
    public async Task ApprovedWriteAndDeleteWork_DirectoriesAreNeverDeleted()
    {
        var dir = Tmp();
        var tb = new Toolbox(dir, true, _ => Task.FromResult(true));
        await tb.ExecuteAsync("write_file", new JsonObject { ["path"] = "sub/y.txt", ["content"] = "ok" }, default);
        Assert.Equal("ok", File.ReadAllText(Path.Combine(dir, "sub", "y.txt")));
        Assert.Contains("directory", (await tb.ExecuteAsync("delete_file", new JsonObject { ["path"] = "sub" }, default))["error"]!.ToString());
        Assert.True((bool)(await tb.ExecuteAsync("delete_file", new JsonObject { ["path"] = "sub/y.txt" }, default))["ok"]!);
    }
}

public class AgentTests
{
    [Fact]
    public async Task LoopsThroughToolCallsThenReturnsSummary()
    {
        var script = new Queue<JsonObject>(new[]
        {
            JsonNode.Parse("""{"role":"assistant","content":null,"tool_calls":[{"id":"1","function":{"name":"list_dir","arguments":"{\"path\":\".\"}"}}]}""")!.AsObject(),
            JsonNode.Parse("""{"role":"assistant","content":"All done."}""")!.AsObject(),
        });
        var events = new List<AgentEventKind>();
        var dir = Directory.CreateTempSubdirectory("ive").FullName;
        var tb = new Toolbox(dir, true, _ => Task.FromResult(true));
        var result = await new AgentRunner().RunAsync("hi", dir, tb, (m, t, c) => Task.FromResult(script.Dequeue()), e => events.Add(e.Kind), default);
        Assert.Equal("All done.", result);
        Assert.Equal(new[] { AgentEventKind.Thinking, AgentEventKind.Tool, AgentEventKind.Thinking }, events);
    }

    [Fact]
    public async Task RunawayLoopsAreCapped()
    {
        var dir = Directory.CreateTempSubdirectory("ive").FullName;
        var tb = new Toolbox(dir, true, _ => Task.FromResult(true));
        ChatFunc loop = (m, t, c) => Task.FromResult(JsonNode.Parse("""{"role":"assistant","tool_calls":[{"id":"1","function":{"name":"list_dir","arguments":"{}"}}]}""")!.AsObject());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentRunner().RunAsync("x", dir, tb, loop, null, default));
    }
}

public class TaskManagerTests
{
    private static async Task Until(Func<bool> cond)
    {
        for (var i = 0; i < 200 && !cond(); i++) await Task.Delay(10);
        Assert.True(cond());
    }

    [Fact]
    public async Task RunsTasksAndRecordsSummary()
    {
        var tm = new TaskManager((p, a, e, ct) => Task.FromResult("did " + p));
        tm.Submit("a");
        await Until(() => tm.Snapshot().All(t => t.Status == TaskStatus.Done));
        Assert.Equal("did a", tm.Snapshot()[0].Summary);
    }

    [Fact]
    public async Task ApprovalBrokerResolvesTheAgent()
    {
        bool? answer = null;
        var tm = new TaskManager(async (p, approve, e, ct) => { answer = await approve(new ApprovalRequest("T", "D")); return "ok"; });
        tm.Submit("x");
        await Until(() => tm.Approvals().Count == 1);
        Assert.Equal(TaskStatus.Awaiting, tm.Snapshot()[0].Status);
        tm.Respond(tm.Approvals()[0].Id, true);
        await Until(() => tm.Snapshot()[0].Status == TaskStatus.Done);
        Assert.True(answer);
    }

    [Fact]
    public async Task CancellingDeniesPendingApprovalAndEndsCancelled()
    {
        bool? answer = null;
        var tm = new TaskManager(async (p, approve, e, ct) => { answer = await approve(new ApprovalRequest("T", "D")); ct.ThrowIfCancellationRequested(); return "ok"; });
        var id = tm.Submit("x");
        await Until(() => tm.Approvals().Count == 1);
        Assert.True(tm.Cancel(id));
        await Until(() => tm.Snapshot()[0].Status == TaskStatus.Cancelled);
        Assert.False(answer);
        Assert.Empty(tm.Approvals());
    }

    [Fact]
    public async Task ErrorsAreCaptured()
    {
        var tm = new TaskManager((p, a, e, ct) => throw new InvalidOperationException("kaboom"));
        tm.Submit("bad");
        await Until(() => tm.Snapshot()[0].Status == TaskStatus.Error);
        Assert.Contains("kaboom", tm.Snapshot()[0].Error);
    }
}

public class SettingsTests
{
    private sealed class Rev : ISecretProtector
    {
        public string Protect(string p) => new string(p.Reverse().ToArray());
        public string Unprotect(string c) => new string(c.Reverse().ToArray());
    }

    private static string File_() => Path.Combine(Directory.CreateTempSubdirectory("ive").FullName, "settings.json");

    [Fact]
    public void ApiKeyIsEncryptedOnDiskAndRestored()
    {
        var f = File_();
        var s = new SettingsStore(f, new Rev());
        s.Update(x => x.ApiKey = "sk-secret-1234");
        Assert.DoesNotContain("sk-secret-1234", File.ReadAllText(f));
        var again = new SettingsStore(f, new Rev());
        Assert.Equal("sk-secret-1234", again.Current.ApiKey);
        Assert.Equal("••••1234", again.KeyHint);
    }

    [Fact]
    public void ValidationRejectsInsecureUrlsAndBadValues()
    {
        var s = new SettingsStore(File_(), new Rev());
        Assert.Throws<ArgumentException>(() => s.Update(x => x.ApiBaseUrl = "http://evil.example.com/v1"));
        s.Update(x => x.ApiBaseUrl = "http://localhost:8080/v1/");
        Assert.Equal("http://localhost:8080/v1", s.Current.ApiBaseUrl);
        Assert.Throws<ArgumentException>(() => s.Update(x => x.TriggerKey = "Fn"));
        Assert.Throws<ArgumentException>(() => s.Update(x => x.WorkspaceDir = "/definitely/not/here"));
    }
}
