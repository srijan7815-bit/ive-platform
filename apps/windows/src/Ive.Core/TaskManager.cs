namespace Ive.Core;

public enum TaskStatus { Queued, Running, Awaiting, Done, Error, Cancelled }

public enum PhaseKind { Idle, Queued, Thinking, Tool, Done, Error }

public sealed record Phase(PhaseKind Kind, string Label = "");

public sealed record PendingApproval(string Id, string TaskId, string Title, string Detail);

/// <summary>Immutable snapshot handed to the UI thread.</summary>
public sealed record TaskView(string Id, string Prompt, TaskStatus Status, DateTime CreatedAt, string[] Steps, string Summary, string Error)
{
    public bool Active => Status is TaskStatus.Queued or TaskStatus.Running or TaskStatus.Awaiting;
}

public delegate Task<string> AgentRun(string prompt, Func<ApprovalRequest, Task<bool>> approve, Action<AgentEvent> onEvent, CancellationToken ct);

/// <summary>Task queue, history and approval broker. Events fire on background threads — marshal in the UI.</summary>
public sealed class TaskManager
{
    private sealed class Item
    {
        public string Id = Guid.NewGuid().ToString("N")[..10];
        public string Prompt;
        public TaskStatus Status = TaskStatus.Queued;
        public DateTime CreatedAt = DateTime.Now;
        public List<string> Steps = new();
        public string Summary, Error;
        public CancellationTokenSource Cts = new();
    }

    private readonly object _lock = new();
    private readonly List<Item> _items = new();
    private readonly Dictionary<string, (PendingApproval Info, TaskCompletionSource<bool> Tcs)> _approvals = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AgentRun _run;

    public TaskManager(AgentRun run) => _run = run;

    public event Action TasksChanged;
    public event Action<Phase> PhaseChanged;
    public event Action ApprovalsChanged;

    public IReadOnlyList<TaskView> Snapshot()
    {
        lock (_lock)
            return _items.Select(i => new TaskView(i.Id, i.Prompt, i.Status, i.CreatedAt, i.Steps.ToArray(), i.Summary, i.Error)).ToList();
    }

    public IReadOnlyList<PendingApproval> Approvals()
    {
        lock (_lock) return _approvals.Values.Select(v => v.Info).ToList();
    }

    public int ActiveCount => Snapshot().Count(t => t.Active);

    public string Submit(string text)
    {
        var prompt = (text ?? "").Trim();
        if (prompt.Length == 0) throw new ArgumentException("Prompt is empty.");
        if (prompt.Length > 4000) prompt = prompt[..4000];
        var item = new Item { Prompt = prompt };
        lock (_lock)
        {
            _items.Insert(0, item);
            if (_items.Count > 40) _items.RemoveAt(_items.Count - 1);
        }
        TasksChanged?.Invoke();
        PhaseChanged?.Invoke(new Phase(PhaseKind.Queued));
        _ = Task.Run(() => RunAsync(item));
        return item.Id;
    }

    private async Task RunAsync(Item item)
    {
        try { await _gate.WaitAsync(item.Cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { SetStatus(item, TaskStatus.Cancelled); return; }

        try
        {
            SetStatus(item, TaskStatus.Running);
            PhaseChanged?.Invoke(new Phase(PhaseKind.Thinking));
            var summary = await _run(item.Prompt, req => ApproveAsync(item, req), ev => OnEvent(item, ev), item.Cts.Token).ConfigureAwait(false);
            lock (_lock) item.Summary = summary;
            SetStatus(item, TaskStatus.Done);
            PhaseChanged?.Invoke(new Phase(PhaseKind.Done, summary));
        }
        catch (Exception e) when (e is OperationCanceledException || item.Cts.IsCancellationRequested)
        {
            SetStatus(item, TaskStatus.Cancelled);
            PhaseChanged?.Invoke(new Phase(PhaseKind.Idle));
        }
        catch (Exception e)
        {
            lock (_lock) item.Error = e.Message;
            SetStatus(item, TaskStatus.Error);
            PhaseChanged?.Invoke(new Phase(PhaseKind.Error, e.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnEvent(Item item, AgentEvent ev)
    {
        if (ev.Kind == AgentEventKind.Tool)
        {
            lock (_lock)
            {
                item.Steps.Add(ev.Label);
                if (item.Steps.Count > 80) item.Steps.RemoveAt(0);
            }
            TasksChanged?.Invoke();
            PhaseChanged?.Invoke(new Phase(PhaseKind.Tool, ev.Label));
        }
        else PhaseChanged?.Invoke(new Phase(PhaseKind.Thinking));
    }

    private async Task<bool> ApproveAsync(Item item, ApprovalRequest req)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var info = new PendingApproval(Guid.NewGuid().ToString("N")[..10], item.Id, req.Title, req.Detail);
        lock (_lock) _approvals[info.Id] = (info, tcs);
        SetStatus(item, TaskStatus.Awaiting);
        ApprovalsChanged?.Invoke();
        using var reg = item.Cts.Token.Register(() => Respond(info.Id, false));
        var ok = await tcs.Task.ConfigureAwait(false);
        lock (_lock) if (item.Status == TaskStatus.Awaiting) item.Status = TaskStatus.Running;
        TasksChanged?.Invoke();
        return ok;
    }

    public bool Respond(string approvalId, bool approved)
    {
        TaskCompletionSource<bool> tcs;
        lock (_lock)
        {
            if (!_approvals.Remove(approvalId, out var entry)) return false;
            tcs = entry.Tcs;
        }
        tcs.TrySetResult(approved);
        ApprovalsChanged?.Invoke();
        return true;
    }

    public bool Cancel(string taskId)
    {
        Item item;
        lock (_lock) item = _items.FirstOrDefault(i => i.Id == taskId);
        if (item == null || !new TaskView(item.Id, "", item.Status, default, null, null, null).Active) return false;
        item.Cts.Cancel();
        return true;
    }

    public void ClearFinished()
    {
        lock (_lock) _items.RemoveAll(i => i.Status is TaskStatus.Done or TaskStatus.Error or TaskStatus.Cancelled);
        TasksChanged?.Invoke();
    }

    private void SetStatus(Item item, TaskStatus status)
    {
        lock (_lock) item.Status = status;
        TasksChanged?.Invoke();
    }
}
