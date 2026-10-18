using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Ive.App.Native;
using Ive.App.Services;
using Ive.App.UI;
using Ive.Core;

namespace Ive.App;

/// <summary>Composition root: wires services, windows, hooks and the tray together.</summary>
public sealed class IveHost : IDisposable
{
    private readonly System.Windows.Application _app;
    private SettingsStore _settings;
    private AiClient _ai;
    private TaskManager _tasks;
    private MediaService _media;
    private NotchWindow _notch;
    private PromptWindow _prompt;
    private MainWindow _main;
    private GestureHook _gesture;
    private HotkeyListener _hotkey;
    private System.Windows.Forms.NotifyIcon _tray;

    public IveHost(System.Windows.Application app) => _app = app;

    private void UI(Action a) => _app.Dispatcher.InvokeAsync(a);

    public void Start()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IVE");
        _settings = new SettingsStore(Path.Combine(dir, "settings.json"), new DpapiProtector());
        _ai = new AiClient();
        _tasks = new TaskManager(RunAgent);
        _media = new MediaService();

        _notch = new NotchWindow();
        _prompt = new PromptWindow();
        _main = new MainWindow(_settings, _tasks, _media);

        _tasks.TasksChanged += () => UI(() => { _main.RefreshTasks(); _notch.SetRunning(_tasks.ActiveCount); });
        _tasks.PhaseChanged += p => UI(() => _notch.SetPhase(p));
        _tasks.ApprovalsChanged += () => UI(() => _notch.SetApprovals(_tasks.Approvals()));
        _media.Changed += m => UI(() => { _notch.SetMedia(m); _main.RefreshMedia(); });

        _notch.ApprovalAnswered += (id, ok) => _tasks.Respond(id, ok);
        _notch.MediaCommand += c => _ = _media.ControlAsync(c);
        _notch.OpenRequested += () => _main.ShowAndFocus();
        _prompt.Submitted += text => { try { _tasks.Submit(text); } catch { /* empty prompt */ } };

        _main.TestConnection = TestConnectionAsync;
        _main.SettingsSaved += ApplySettings;
        _main.QuitRequested += () => { _main.AllowClose = true; _app.Shutdown(); };

        _gesture = new GestureHook();
        _gesture.Dragged += (o, e) => UI(() => _prompt.OpenAt(o, e));
        _gesture.Tapped += () => UI(() => { if (_main.IsVisible && _main.IsActive) _main.Hide(); else _main.ShowAndFocus(); });

        _hotkey = new HotkeyListener();
        _hotkey.Pressed += () =>
        {
            Win32.GetCursorPos(out var p);
            var pt = new System.Windows.Point(p.X, p.Y);
            UI(() => _prompt.OpenAt(pt, pt));
        };

        BuildTray();
        _notch.Reposition();
        _notch.Show();
        _notch.Reposition();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => UI(() => _notch.Reposition());

        ApplySettings();
        _gesture.Start();
        _ = _media.StartAsync();
        if (!_settings.HasKey) _main.ShowAndFocus(2);
    }

    private void ApplySettings()
    {
        var s = _settings.Current;
        _gesture.SetTrigger(s.TriggerKey);
        if (!_hotkey.Register(s.Hotkey)) _main.RefreshSettings();
        _notch.SetHasKey(_settings.HasKey);
        try
        {
            using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (run == null) return;
            if (s.LaunchAtLogin) run.SetValue("IVE", "\"" + Environment.ProcessPath + "\"");
            else run.DeleteValue("IVE", false);
        }
        catch { /* registry unavailable: ignore */ }
    }

    private Task<string> RunAgent(string prompt, Func<ApprovalRequest, Task<bool>> approve, Action<AgentEvent> onEvent, CancellationToken ct)
    {
        var s = _settings.Current.Clone();
        var tools = new Toolbox(s.WorkspaceDir, s.RequireApproval, approve);
        return new AgentRunner().RunAsync(prompt, s.WorkspaceDir, tools, (m, t, c) => _ai.ChatAsync(s, m, t, c), onEvent, ct);
    }

    private async Task<string> TestConnectionAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var messages = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "Reply with: ok" } };
            await _ai.ChatAsync(_settings.Current, messages, new JsonArray(), cts.Token);
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    private void BuildTray()
    {
        System.Drawing.Icon icon;
        try
        {
            using var s = typeof(IveHost).Assembly.GetManifestResourceStream("icon.ico");
            icon = s != null ? new System.Drawing.Icon(s) : System.Drawing.SystemIcons.Application;
        }
        catch { icon = System.Drawing.SystemIcons.Application; }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Ask IVE…", null, (_, _) => UI(() =>
        {
            Win32.GetCursorPos(out var p);
            var pt = new System.Windows.Point(p.X, p.Y);
            _prompt.OpenAt(pt, pt);
        }));
        menu.Items.Add("Open IVE", null, (_, _) => UI(() => _main.ShowAndFocus()));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit IVE", null, (_, _) => UI(() => { _main.AllowClose = true; _app.Shutdown(); }));

        _tray = new System.Windows.Forms.NotifyIcon { Icon = icon, Text = "IVE", Visible = true, ContextMenuStrip = menu };
        _tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) UI(() => _main.ShowAndFocus()); };
    }

    public void ShowMain() => UI(() => _main?.ShowAndFocus());

    public void Dispose()
    {
        _gesture?.Dispose();
        _hotkey?.Dispose();
        _media?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
    }
}
