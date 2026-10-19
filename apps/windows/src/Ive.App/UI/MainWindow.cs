using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Ive.App.Native;
using Ive.App.Services;
using Ive.Core;
using TaskStatus = Ive.Core.TaskStatus;

namespace Ive.App.UI;

/// <summary>
/// The control center: Activity, Music, Settings. A native WPF window with the Windows 11 acrylic backdrop (DWM),
/// native caption buttons, a spring-driven selection indicator, and a layout that re-flows from a side rail to a
/// bottom tab bar when the window gets narrow.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly TaskManager _tasks;
    private readonly MediaService _media;
    private readonly VoiceSession _voice;

    public Func<Task<string>> TestConnection;
    public bool AllowClose;
    public event Action SettingsSaved;
    public event Action QuitRequested;

    private readonly Grid _shell = new();
    private readonly Grid _navHost = new();
    private readonly StackPanel _navStack = new();
    private readonly Border _indicator;
    private readonly TranslateTransform _ind = new();
    private readonly Border _rail, _logo, _toast;
    private readonly TextBlock _toastText;
    private readonly Grid _body = new();
    private readonly List<Border> _nav = new();
    private readonly List<FrameworkElement> _views = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(2400) };
    private int _view;
    private bool _compact;

    // activity
    private readonly StackPanel _taskList = new();
    private readonly HashSet<string> _open = new(), _seen = new();
    private TextBox _ask;
    private TextBlock _count;

    // music
    private TextBlock _npTitle, _npArtist, _npSource, _note;
    private Border _npPlay;

    // settings
    private PasswordBox _key;
    private TextBox _url, _model, _hotkey, _workspace;
    private ToggleSwitch _approve, _login;
    private WrapPanel _triggerPanel;
    private TextBlock _msg, _keyHint;
    private string _trigger = "RightCtrl", _music = "";
    private TextBox _sttModel, _sttUrl, _voiceHotkey, _musicLaunch;
    private PasswordBox _sttKey;
    private ToggleSwitch _autoSend;
    private WrapPanel _musicPanel;

    public MainWindow(SettingsStore settings, TaskManager tasks, MediaService media, VoiceSession voice)
    {
        _voice = voice;
        _settings = settings;
        _tasks = tasks;
        _media = media;

        Title = "IVE";
        Width = 1080;
        Height = 740;
        MinWidth = 380;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        FontFamily = Ui.Font;
        Foreground = Ui.Ink;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 40,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(-1),
            CornerRadius = new CornerRadius(8),
            UseAeroCaptionButtons = true,
        });

        // --- navigation rail ---
        _logo = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 0, 22), HorizontalAlignment = HorizontalAlignment.Center,
            Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Ui.AccentC, 0), new GradientStop(Ui.Accent2C, 0.6), new GradientStop(Color.FromRgb(0xF4, 0x72, 0xB6), 1) }, 45),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Color = Ui.Accent2C, Opacity = 0.55 },
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(),
        };
        ((RotateTransform)_logo.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-6, 6, TimeSpan.FromSeconds(3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });

        _indicator = new Border
        {
            CornerRadius = new CornerRadius(18), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, RenderTransform = _ind, IsHitTestVisible = false,
            Background = new LinearGradientBrush(Color.FromArgb(0x38, 255, 255, 255), Color.FromArgb(0x14, 255, 255, 255), 90),
            BorderBrush = Ui.Hex("#33FFFFFF"), BorderThickness = new Thickness(1),
        };
        _navHost.Children.Add(_indicator);
        _navHost.Children.Add(_navStack);
        AddNav("\uE81C", "Activity");   // History
        AddNav("\uE8D6", "Music");      // Audio
        AddNav("\uE713", "Settings");   // Setting

        var railStack = new StackPanel();
        railStack.Children.Add(_logo);
        railStack.Children.Add(_navHost);
        _rail = new Border { Child = railStack, Padding = new Thickness(10, 54, 10, 16), VerticalAlignment = VerticalAlignment.Stretch };

        // --- views ---
        _views.Add(Scroll(BuildActivity()));
        _views.Add(Scroll(BuildMusic()));
        _views.Add(Scroll(BuildSettings()));
        foreach (var v in _views) { v.Visibility = Visibility.Collapsed; _body.Children.Add(v); }

        _shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _shell.Children.Add(_body);
        _shell.Children.Add(_rail);

        _toastText = Ui.Text("", 13, Ui.Ink, FontWeights.SemiBold);
        _toast = Ui.Glass(_toastText, 999, true);
        _toast.Padding = new Thickness(20, 10, 20, 10);
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        _toast.VerticalAlignment = VerticalAlignment.Bottom;
        _toast.Margin = new Thickness(0, 0, 0, 30);
        _toast.Opacity = 0;
        _toast.IsHitTestVisible = false;
        _toast.RenderTransform = new TranslateTransform(0, 30);
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(250))); };

        var root = new Grid { Background = Ui.Hex("#5C090A10") };
        root.Children.Add(Blobs());
        root.Children.Add(_shell);
        root.Children.Add(_toast);
        Content = root;

        Loaded += (_, _) => MoveIndicator(false);
        SizeChanged += (_, e) => Relayout(e.NewSize.Width < 700);
        Relayout(false);
        ShowView(0, false);
        RefreshTasks();
        RefreshMedia();
        RefreshSettings();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        Win32.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));        // immersive dark mode
        if (Environment.OSVersion.Version.Build >= 22621)
        {
            var acrylic = 3;                                                   // DWMSBT_TRANSIENTWINDOW
            Win32.DwmSetWindowAttribute(hwnd, 38, ref acrylic, sizeof(int));
        }
        else
        {
            Background = new LinearGradientBrush(Color.FromRgb(9, 10, 16), Color.FromRgb(22, 16, 44), 60); // pre-Win11: solid
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }

    public void ShowAndFocus(int view = -1)
    {
        if (view >= 0) ShowView(view);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
    }

    // ------------------------------------------------------------------ chrome

    private static FrameworkElement Blobs()
    {
        var c = new Canvas { IsHitTestVisible = false };
        void Blob(Color col, double x, double y, double dur)
        {
            var t = new TranslateTransform();
            c.Children.Add(new Ellipse
            {
                Width = 820, Height = 820, RenderTransform = t,
                Fill = new RadialGradientBrush(Color.FromArgb(0x78, col.R, col.G, col.B), Color.FromArgb(0, col.R, col.G, col.B)),
                Margin = new Thickness(x, y, 0, 0),
            });
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 140, TimeSpan.FromSeconds(dur)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 90, TimeSpan.FromSeconds(dur * 1.3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        }
        Blob(Color.FromRgb(0x3B, 0x82, 0xF6), -240, -300, 22);
        Blob(Color.FromRgb(0x8B, 0x5C, 0xF6), 620, 40, 28);
        Blob(Color.FromRgb(0x06, 0xB6, 0xD4), 160, 360, 34);
        return c;
    }

    private void AddNav(string glyph, string label)
    {
        var idx = _nav.Count;
        var icon = new TextBlock { Text = glyph, FontFamily = Ui.Icons, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Ui.Ink2 };
        var text = Ui.Text(label, 11, Ui.Ink2);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.Margin = new Thickness(0, 4, 0, 0);
        var st = new StackPanel();
        st.Children.Add(icon);
        st.Children.Add(text);
        var b = new Border { Width = 72, Padding = new Thickness(0, 10, 0, 10), Margin = new Thickness(0, 0, 0, 6), CornerRadius = new CornerRadius(18), Child = st, Cursor = Cursors.Hand, Background = Brushes.Transparent, Tag = (icon, text) };
        b.MouseLeftButtonUp += (_, _) => ShowView(idx);
        _nav.Add(b);
        _navStack.Children.Add(b);
    }

    private void ShowView(int idx, bool animate = true)
    {
        _view = idx;
        for (var i = 0; i < _views.Count; i++)
        {
            var active = i == idx;
            if (active && _views[i].Visibility != Visibility.Visible)
            {
                _views[i].Visibility = Visibility.Visible;
                _views[i].RenderTransform = new TranslateTransform(0, 16);
                _views[i].BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
                ((TranslateTransform)_views[i].RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = new SpringEase() });
            }
            else if (!active) _views[i].Visibility = Visibility.Collapsed;
            var (icon, text) = ((TextBlock, TextBlock))_nav[i].Tag;
            icon.Foreground = text.Foreground = active ? Ui.Ink : Ui.Ink2;
        }
        if (idx == 2) BuildMusicPills();
        MoveIndicator(animate);
    }

    private void MoveIndicator(bool animate)
    {
        if (!IsLoaded) return; // Loaded handler repositions once real layout exists
        _navHost.UpdateLayout();
        var b = _nav[_view];
        if (b.ActualWidth == 0) return;
        var p = b.TranslatePoint(new Point(0, 0), _navHost);
        _indicator.Width = b.ActualWidth;
        _indicator.Height = b.ActualHeight;
        if (animate)
        {
            _ind.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(p.X, TimeSpan.FromMilliseconds(560)) { EasingFunction = new SpringEase() });
            _ind.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(p.Y, TimeSpan.FromMilliseconds(560)) { EasingFunction = new SpringEase() });
        }
        else
        {
            _ind.BeginAnimation(TranslateTransform.XProperty, null);
            _ind.BeginAnimation(TranslateTransform.YProperty, null);
            _ind.X = p.X;
            _ind.Y = p.Y;
        }
    }

    /// <summary>Auto-fit: side rail on wide windows, bottom tab bar on narrow ones.</summary>
    private void Relayout(bool compact)
    {
        _compact = compact;
        _navStack.Orientation = compact ? Orientation.Horizontal : Orientation.Vertical;
        _logo.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _rail.Padding = compact ? new Thickness(8, 6, 8, 6) : new Thickness(10, 54, 10, 16);
        _rail.Background = compact ? Ui.Hex("#B3101420") : Brushes.Transparent;
        var w = compact ? Math.Max(80, (ActualWidth - 32) / 3 - 4) : 72;
        foreach (var n in _nav) n.Width = w;
        _navStack.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;

        Grid.SetColumn(_body, compact ? 0 : 1);
        Grid.SetColumnSpan(_body, compact ? 2 : 1);
        Grid.SetRow(_body, 0);
        Grid.SetRowSpan(_body, compact ? 1 : 2);
        Grid.SetColumn(_rail, 0);
        Grid.SetColumnSpan(_rail, compact ? 2 : 1);
        Grid.SetRow(_rail, compact ? 1 : 0);
        Grid.SetRowSpan(_rail, compact ? 1 : 2);
        foreach (var v in _views) v.Margin = compact ? new Thickness(0) : new Thickness(0);
        if (IsLoaded) Dispatcher.BeginInvoke(new Action(() => MoveIndicator(false)), DispatcherPriority.Loaded);
    }

    private static ScrollViewer Scroll(FrameworkElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Focusable = false,
    };

    private TextBlock H1(string t)
    {
        var h = Ui.Text(t, 30, Ui.Ink, FontWeights.Bold);
        h.Margin = new Thickness(0, 0, 0, 18);
        return h;
    }

    private void Toast(string msg)
    {
        _toastText.Text = msg;
        _toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
        ((TranslateTransform)_toast.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(520)) { EasingFunction = new SpringEase() });
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private static Grid WithPlaceholder(TextBox box, string hint, double size)
    {
        var ph = Ui.Text(hint, size, Ui.Ink3);
        ph.IsHitTestVisible = false;
        ph.VerticalAlignment = VerticalAlignment.Center;
        box.TextChanged += (_, _) => ph.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var g = new Grid();
        g.Children.Add(ph);
        g.Children.Add(box);
        return g;
    }

    // ------------------------------------------------------------------ activity

    private FrameworkElement BuildActivity()
    {
        var page = new StackPanel { MaxWidth = 860, Margin = new Thickness(12, 46, 28, 28) };
        page.Children.Add(H1("Activity"));

        _ask = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 140, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Foreground = Ui.Ink, CaretBrush = Brushes.White, FontSize = 15.5, VerticalContentAlignment = VerticalAlignment.Center, SelectionBrush = Ui.Accent,
        };
        _ask.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; Send(); }
        };
        var send = Ui.Pill("Send", Ui.AccentGradient(), Ui.Dark, Send);
        send.VerticalAlignment = VerticalAlignment.Bottom;
        var comp = new Grid { Margin = new Thickness(0) };
        comp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        comp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        comp.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mic = new MicButton(_voice, 40, OnVoiceText, m => Toast(m));
        mic.VerticalAlignment = VerticalAlignment.Bottom;
        mic.Margin = new Thickness(0, 0, 8, 0);
        var askHost = WithPlaceholder(_ask, "Ask IVE to do something…", 15.5);
        askHost.Margin = new Thickness(8, 6, 14, 6);
        Grid.SetColumn(mic, 1);
        Grid.SetColumn(send, 2);
        comp.Children.Add(askHost);
        comp.Children.Add(mic);
        comp.Children.Add(send);
        var composer = Ui.Glass(comp, 26, true);
        composer.Padding = new Thickness(14, 8, 8, 8);
        page.Children.Add(composer);

        var bar = new DockPanel { Margin = new Thickness(4, 18, 4, 10), LastChildFill = false };
        _count = Ui.Text("", 12.5, Ui.Ink2);
        _count.VerticalAlignment = VerticalAlignment.Center;
        var clear = Ui.Pill("Clear finished", Ui.Ghost, Ui.Ink2, () => _tasks.ClearFinished(), 12);
        clear.Padding = new Thickness(12, 5, 12, 5);
        DockPanel.SetDock(clear, Dock.Right);
        bar.Children.Add(clear);
        bar.Children.Add(_count);
        page.Children.Add(bar);
        page.Children.Add(_taskList);
        return page;
    }

    private void OnVoiceText(string text)
    {
        _ask.Text = (_ask.Text + " " + text).Trim();
        _ask.CaretIndex = _ask.Text.Length;
        if (_settings.Current.VoiceAutoSend) Send();
    }

    private void Send()
    {
        var text = _ask.Text.Trim();
        if (text.Length == 0) return;
        if (!_settings.HasKey) { Toast("Add your API key first"); ShowView(2); return; }
        try { _tasks.Submit(text); _ask.Text = ""; }
        catch (Exception e) { Toast(e.Message); }
    }

    public void RefreshTasks()
    {
        var list = _tasks.Snapshot();
        var active = list.Count(t => t.Active);
        _count.Text = list.Count == 0 ? "" : $"{list.Count} task{(list.Count > 1 ? "s" : "")}{(active > 0 ? $" · {active} active" : "")}";
        _taskList.Children.Clear();
        if (list.Count == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(0, 56, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            empty.Children.Add(Ui.Text("Nothing yet", 17, Ui.Ink2, FontWeights.SemiBold));
            empty.Children.Add(Ui.Text("Hold your trigger key and right-click-drag anywhere — or ask above.", 13, Ui.Ink3));
            _taskList.Children.Add(empty);
            return;
        }
        foreach (var t in list) _taskList.Children.Add(Card(t));
    }

    private static string Ago(DateTime t)
    {
        var s = Math.Max(1, (int)(DateTime.Now - t).TotalSeconds);
        return s < 60 ? $"{s}s ago" : s < 3600 ? $"{s / 60}m ago" : $"{s / 3600}h ago";
    }

    private Border Card(TaskView t)
    {
        var (chipText, chipColor) = t.Status switch
        {
            TaskStatus.Queued => ("Queued", Ui.AccentC),
            TaskStatus.Running => ("Working", Ui.AccentC),
            TaskStatus.Awaiting => ("Needs approval", Ui.WarnC),
            TaskStatus.Done => ("Done", Ui.OkC),
            TaskStatus.Error => ("Failed", Ui.BadC),
            _ => ("Cancelled", Color.FromRgb(0x9A, 0x9A, 0xA6)),
        };

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(Ui.Text(t.Prompt, 14.5, Ui.Ink, FontWeights.SemiBold, wrap: true));
        var chip = Ui.Chip(chipText, chipColor);
        chip.Margin = new Thickness(10, 2, 0, 0);
        Grid.SetColumn(chip, 1);
        top.Children.Add(chip);
        if (t.Active)
        {
            var cancel = Ui.Pill("Cancel", Ui.Ghost, Ui.Ink, () => _tasks.Cancel(t.Id), 12);
            cancel.Padding = new Thickness(12, 4, 12, 4);
            cancel.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(cancel, 2);
            top.Children.Add(cancel);
        }

        var stack = new StackPanel();
        stack.Children.Add(top);
        if (!string.IsNullOrEmpty(t.Summary)) { var s = Ui.Text(t.Summary, 13.5, Ui.Ink2, wrap: true); s.Margin = new Thickness(0, 10, 0, 0); stack.Children.Add(s); }
        if (!string.IsNullOrEmpty(t.Error)) { var s = Ui.Text(t.Error, 13.5, Ui.Bad, wrap: true); s.Margin = new Thickness(0, 10, 0, 0); stack.Children.Add(s); }
        if (t.Steps.Length > 0)
        {
            var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0), Visibility = _open.Contains(t.Id) ? Visibility.Visible : Visibility.Collapsed };
            foreach (var step in t.Steps) list.Children.Add(Ui.Text("•  " + step, 12.5, Ui.Ink2, wrap: true));
            string Label(bool open) => $"{t.Steps.Length} step{(t.Steps.Length > 1 ? "s" : "")}  {(open ? "▴" : "▾")}";
            var toggle = Ui.Text(Label(_open.Contains(t.Id)), 12, Ui.Ink3);
            toggle.Margin = new Thickness(0, 10, 0, 0);
            toggle.Cursor = Cursors.Hand;
            toggle.MouseLeftButtonUp += (_, _) =>
            {
                var now = !_open.Contains(t.Id);
                if (now) _open.Add(t.Id); else _open.Remove(t.Id);
                list.Visibility = now ? Visibility.Visible : Visibility.Collapsed;
                toggle.Text = Label(now);
            };
            stack.Children.Add(toggle);
            stack.Children.Add(list);
        }
        var when = Ui.Text(Ago(t.CreatedAt), 11.5, Ui.Ink3);
        when.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(when);

        var card = Ui.Glass(stack, 20);
        card.Padding = new Thickness(18, 14, 18, 14);
        card.Margin = new Thickness(0, 0, 0, 12);
        if (_seen.Add(t.Id))
        {
            card.RenderTransform = new TranslateTransform(0, 18);
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)));
            ((TranslateTransform)card.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(560)) { EasingFunction = new SpringEase() });
        }
        return card;
    }

    // ------------------------------------------------------------------ music

    private FrameworkElement BuildMusic()
    {
        var page = new StackPanel { MaxWidth = 860, Margin = new Thickness(12, 46, 28, 28) };
        page.Children.Add(H1("Music"));

        var art = new Border
        {
            Width = 124, Height = 124, CornerRadius = new CornerRadius(26),
            Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(Ui.AccentC, 0), new GradientStop(Ui.Accent2C, 0.55), new GradientStop(Color.FromRgb(0xF4, 0x72, 0xB6), 1) }, 45),
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 14, Direction = 270, Color = Ui.Accent2C, Opacity = 0.5 },
        };
        _npTitle = Ui.Text("Nothing playing", 20, Ui.Ink, FontWeights.SemiBold, wrap: true);
        _npArtist = Ui.Text("", 14, Ui.Ink2);
        var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0) };
        meta.Children.Add(_npTitle);
        meta.Children.Add(_npArtist);
        _npSource = Ui.Text("", 12, Ui.Ink3);
        _npSource.Margin = new Thickness(0, 8, 0, 0);
        meta.Children.Add(_npSource);
        var head = new DockPanel();
        DockPanel.SetDock(art, Dock.Left);
        head.Children.Add(art);
        head.Children.Add(meta);

        _npPlay = Ui.IconButton("\uE768", 66, () => _ = _media.ControlAsync("playpause"), Ui.AccentGradient(), Ui.Dark);
        var transport = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 0) };
        var prev = Ui.IconButton("\uE892", 52, () => _ = _media.ControlAsync("prev"));
        var next = Ui.IconButton("\uE893", 52, () => _ = _media.ControlAsync("next"));
        prev.Margin = new Thickness(0, 0, 14, 0);
        next.Margin = new Thickness(14, 0, 0, 0);
        transport.Children.Add(prev);
        transport.Children.Add(_npPlay);
        transport.Children.Add(next);

        var inner = new StackPanel();
        inner.Children.Add(head);
        inner.Children.Add(transport);
        var card = Ui.Glass(inner, 30, true);
        card.Padding = new Thickness(26);
        page.Children.Add(card);

        var open = Ui.Pill("Open music app", Ui.Ghost, Ui.Ink, () =>
        {
            var err = MediaService.Launch(_settings.Current.MusicLaunch);
            if (err != null) Toast(err);
        });
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, 16, 0, 0);
        page.Children.Add(open);

        _note = Ui.Text("", 12.5, Ui.Ink3, wrap: true);
        _note.Margin = new Thickness(6, 16, 6, 0);
        page.Children.Add(_note);
        return page;
    }

    public void RefreshMedia()
    {
        var m = _media.State;
        _npTitle.Text = m.Active && m.Title.Length > 0 ? m.Title : "Nothing playing";
        _npArtist.Text = m.Active ? m.Artist : "";
        _npSource.Text = string.IsNullOrEmpty(_settings.Current.MusicApp) ? "Controlling: whatever is playing" : "Controlling: " + _settings.Current.MusicApp;
        ((TextBlock)_npPlay.Child).Text = m.Playing ? "\uE769" : "\uE768";
        _note.Text = m.Active ? "" : "Play something in Spotify, YouTube, Apple Music… and it shows up here and in the notch.";
    }

    // ------------------------------------------------------------------ settings

    private FrameworkElement BuildSettings()
    {
        var page = new StackPanel { MaxWidth = 700, Margin = new Thickness(12, 46, 28, 28) };
        page.Children.Add(H1("Settings"));

        _key = new PasswordBox();
        _url = new TextBox();
        _model = new TextBox();
        _hotkey = new TextBox();
        _workspace = new TextBox();
        _sttModel = new TextBox();
        _sttUrl = new TextBox();
        _voiceHotkey = new TextBox();
        _musicLaunch = new TextBox();
        _sttKey = new PasswordBox();
        _autoSend = new ToggleSwitch();
        _musicPanel = new WrapPanel();
        _keyHint = Ui.Text("Encrypted with your Windows account (DPAPI).", 12, Ui.Ink3, wrap: true);
        _keyHint.Margin = new Thickness(2, 6, 0, 0);

        var form = new StackPanel();
        var keyField = new StackPanel();
        keyField.Children.Add(Ui.Field(_key));
        keyField.Children.Add(_keyHint);
        form.Children.Add(Labeled("API key", keyField));
        form.Children.Add(Labeled("API base URL", Ui.Field(_url)));
        form.Children.Add(Labeled("Model", Ui.Field(_model)));
        form.Children.Add(Labeled("Quick-ask shortcut", Ui.Field(_hotkey)));
        form.Children.Add(Labeled("Workspace folder", Ui.Field(_workspace)));

        _triggerPanel = new WrapPanel();
        form.Children.Add(Labeled("Trigger key  (hold it, then right-click-drag)", _triggerPanel));

        _approve = new ToggleSwitch();
        _login = new ToggleSwitch();
        form.Children.Add(Switch(_approve, "Ask before writing files, deleting or running commands (recommended)"));
        form.Children.Add(Switch(_login, "Launch IVE when I sign in"));

        form.Children.Add(Section("Voice input"));
        form.Children.Add(Labeled("Speech model", Ui.Field(_sttModel)));
        form.Children.Add(Labeled("Voice API key (optional — blank reuses the main key)", Ui.Field(_sttKey)));
        form.Children.Add(Labeled("Voice base URL (optional — blank reuses the main one)", Ui.Field(_sttUrl)));
        form.Children.Add(Labeled("Voice shortcut", Ui.Field(_voiceHotkey)));
        form.Children.Add(Switch(_autoSend, "Send automatically after I stop talking"));

        form.Children.Add(Section("Music"));
        form.Children.Add(Labeled("Music app IVE controls (shown when it is playing)", _musicPanel));
        form.Children.Add(Labeled("Launch target for “Open music app” (exe path, spotify:, …)", Ui.Field(_musicLaunch)));

        _msg = Ui.Text("", 13, Ui.Bad, wrap: true);
        var test = Ui.Pill("Test connection", Ui.Ghost, Ui.Ink, async () =>
        {
            Toast("Testing…");
            var err = TestConnection == null ? "Not available" : await TestConnection();
            _msg.Text = err ?? "";
            Toast(err == null ? "Connection works ✓" : "Connection failed");
        });
        var save = Ui.Pill("Save", Ui.AccentGradient(), Ui.Dark, SaveSettings);
        test.Margin = new Thickness(0, 0, 10, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(test);
        buttons.Children.Add(save);
        form.Children.Add(buttons);
        form.Children.Add(_msg);

        var card = Ui.Glass(form, 28, true);
        card.Padding = new Thickness(26);
        page.Children.Add(card);

        var quit = Ui.Pill("Quit IVE", Ui.Hex("#2EF87171"), Ui.Hex("#FFFECACA"), () => QuitRequested?.Invoke(), 12);
        quit.HorizontalAlignment = HorizontalAlignment.Left;
        quit.Margin = new Thickness(0, 18, 0, 0);
        page.Children.Add(quit);
        return page;
    }

    private static TextBlock Section(string t)
    {
        var h = Ui.Text(t, 16, Ui.Ink, FontWeights.Bold);
        h.Margin = new Thickness(0, 10, 0, 12);
        return h;
    }

    private void BuildMusicPills()
    {
        _musicPanel.Children.Clear();
        var names = new List<string> { "" };
        foreach (var n in _media.Sources()) if (!names.Contains(n)) names.Add(n);
        if (!string.IsNullOrEmpty(_music) && !names.Any(n => n.Equals(_music, StringComparison.OrdinalIgnoreCase))) names.Add(_music);
        foreach (var n in names)
        {
            var name = n;
            var selected = string.Equals(name, _music ?? "", StringComparison.OrdinalIgnoreCase);
            var pill = Ui.Pill(name.Length == 0 ? "Auto (system)" : name, selected ? Ui.AccentGradient() : Ui.Ghost, selected ? Ui.Dark : Ui.Ink, () => { _music = name; BuildMusicPills(); }, 12.5);
            pill.Margin = new Thickness(0, 0, 8, 8);
            pill.Padding = new Thickness(14, 7, 14, 7);
            _musicPanel.Children.Add(pill);
        }
    }

    private static StackPanel Labeled(string label, UIElement field)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        var l = Ui.Text(label, 12, Ui.Ink2, FontWeights.SemiBold);
        l.Margin = new Thickness(2, 0, 0, 6);
        p.Children.Add(l);
        p.Children.Add(field);
        return p;
    }

    private static FrameworkElement Switch(ToggleSwitch sw, string text)
    {
        var d = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(sw, Dock.Left);
        var t = Ui.Text(text, 13.5, Ui.Ink, wrap: true);
        t.VerticalAlignment = VerticalAlignment.Center;
        t.Margin = new Thickness(14, 0, 0, 0);
        d.Children.Add(sw);
        d.Children.Add(t);
        return d;
    }

    private void BuildTriggerPills()
    {
        _triggerPanel.Children.Clear();
        foreach (var k in SettingsStore.TriggerKeys)
        {
            var key = k;
            var selected = key == _trigger;
            var pill = Ui.Pill(key, selected ? Ui.AccentGradient() : Ui.Ghost, selected ? Ui.Dark : Ui.Ink, () => { _trigger = key; BuildTriggerPills(); }, 12.5);
            pill.Margin = new Thickness(0, 0, 8, 8);
            pill.Padding = new Thickness(14, 7, 14, 7);
            _triggerPanel.Children.Add(pill);
        }
    }

    public void RefreshSettings()
    {
        var s = _settings.Current;
        _url.Text = s.ApiBaseUrl;
        _model.Text = s.Model;
        _hotkey.Text = s.Hotkey;
        _workspace.Text = s.WorkspaceDir;
        _approve.IsOn = s.RequireApproval;
        _login.IsOn = s.LaunchAtLogin;
        _trigger = s.TriggerKey;
        _sttModel.Text = s.SttModel;
        _sttUrl.Text = s.SttBaseUrl;
        _voiceHotkey.Text = s.VoiceHotkey;
        _autoSend.IsOn = s.VoiceAutoSend;
        _musicLaunch.Text = s.MusicLaunch;
        _music = s.MusicApp ?? "";
        BuildMusicPills();
        _keyHint.Text = _settings.HasKey ? $"Saved ({_settings.KeyHint}) — type a new key to replace it. Encrypted with your Windows account (DPAPI)." : "Encrypted with your Windows account (DPAPI).";
        BuildTriggerPills();
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Update(s =>
            {
                s.ApiBaseUrl = _url.Text;
                s.Model = _model.Text;
                s.Hotkey = _hotkey.Text;
                s.WorkspaceDir = _workspace.Text;
                s.TriggerKey = _trigger;
                s.RequireApproval = _approve.IsOn;
                s.LaunchAtLogin = _login.IsOn;
                s.SttModel = _sttModel.Text;
                s.SttBaseUrl = _sttUrl.Text;
                s.VoiceHotkey = _voiceHotkey.Text;
                s.VoiceAutoSend = _autoSend.IsOn;
                s.MusicApp = _music;
                s.MusicLaunch = _musicLaunch.Text;
                if (_key.Password.Length > 0) s.ApiKey = _key.Password;
                if (_sttKey.Password.Length > 0) s.SttApiKey = _sttKey.Password;
            });
            _key.Clear();
            _sttKey.Clear();
            _msg.Text = "";
            RefreshSettings();
            Toast("Settings saved");
            SettingsSaved?.Invoke();
        }
        catch (Exception e)
        {
            _msg.Text = e.Message;
        }
    }
}
