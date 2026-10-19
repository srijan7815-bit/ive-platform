using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Ive.App.Native;
using Ive.App.Services;
using Ive.Core;

namespace Ive.App.UI;

/// <summary>
/// The notch: a transparent, click-through, always-on-top window pinned to the top-centre of the primary display.
/// The pill morphs between idle / working / peek / approval using real spring physics driven per frame, and a
/// "bubble" stretches out of it (velocity-based squash and stretch) while tasks run.
/// </summary>
public sealed class NotchWindow : Window
{
    private enum Mode { Idle, Working, Peek, Approval }

    private const double BaseW = 560, BaseH = 330;

    private double _u = 1;
    private readonly Canvas _canvas = new() { Width = BaseW, Height = BaseH };
    private readonly Border _pill, _bubble;
    private readonly Grid _content = new();
    private readonly Ellipse _dot;
    private readonly TextBlock _label, _clock, _aTitle, _aDetail, _aMore, _mTitle, _mArtist;
    private readonly TextBlock _playGlyph = new() { FontFamily = Ui.Icons, FontSize = 16, Foreground = Ui.Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Text = "\uE768" };
    private readonly StackPanel _approvalPanel, _peekPanel;
    private readonly ScaleTransform _bubbleScale = new(1, 1);
    private readonly SpringValue _w = new(172, 190, 17), _h = new(30, 190, 17), _r = new(15, 190, 17), _bx = new(-24, 170, 14), _bo = new(0, 200, 24);
    private readonly SpringValue[] _springs;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _resetTimer = new() { Interval = TimeSpan.FromMilliseconds(4200) };

    private Phase _phase = new(PhaseKind.Idle);
    private IReadOnlyList<PendingApproval> _approvals = Array.Empty<PendingApproval>();
    private int _running;
    private bool _hasKey = true, _hover, _clickThrough = true, _over, _animating;
    private Mode _mode = Mode.Idle;
    private long _hoverSince, _leaveSince, _last;
    private IntPtr _hwnd;

    public event Action OpenRequested;
    public event Action<string, bool> ApprovalAnswered;
    public event Action<string> MediaCommand;

    public NotchWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = Ui.Font;
        Title = "IVE Notch";

        _springs = new[] { _w, _h, _r, _bx, _bo };

        // --- bubble (added first so the pill covers it while merged) ---
        var spinner = new Ellipse
        {
            Width = 18, Height = 18, Stroke = Ui.Accent, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 5, 40 },
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(),
        };
        spinner.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
        _bubble = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Ui.Hex("#FF050507"), Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _bubbleScale, Child = spinner, Opacity = 0,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 4, Direction = 270, Opacity = 0.4 },
        };
        _bubble.MouseLeftButtonUp += (_, _) => OpenRequested?.Invoke();
        _canvas.Children.Add(_bubble);

        // --- header row ---
        _dot = new Ellipse { Width = 7, Height = 7, Fill = Ui.Ok, VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Color = Ui.OkC, Opacity = 0.9 } };
        _dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.55, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        _label = Ui.Text("IVE", 12, Ui.Ink, FontWeights.SemiBold);
        _label.TextTrimming = TextTrimming.CharacterEllipsis;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _clock = Ui.Text("", 11, Ui.Ink3);
        _clock.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { Height = 30, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 0, 16, 0), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _label.Margin = new Thickness(9, 0, 9, 0);
        Grid.SetColumn(_label, 1); Grid.SetColumn(_clock, 2);
        row.Children.Add(_dot); row.Children.Add(_label); row.Children.Add(_clock);
        row.MouseLeftButtonUp += (_, _) => OpenRequested?.Invoke();
        _content.Children.Add(row);

        // --- peek panel (now playing) ---
        _mTitle = Ui.Text("Nothing playing", 12.5, Ui.Ink, FontWeights.SemiBold);
        _mTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _mArtist = Ui.Text("", 11.5, Ui.Ink2);
        _mArtist.TextTrimming = TextTrimming.CharacterEllipsis;
        var disc = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = Ui.AccentGradient() };
        var meta = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        meta.Children.Add(_mTitle); meta.Children.Add(_mArtist);
        var play = Ui.IconButton("\uE768", 34, () => MediaCommand?.Invoke("playpause"), Brushes.Transparent);
        play.Child = _playGlyph;
        var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        ctl.Children.Add(Ui.IconButton("\uE892", 34, () => MediaCommand?.Invoke("prev"), Brushes.Transparent));
        ctl.Children.Add(play);
        ctl.Children.Add(Ui.IconButton("\uE893", 34, () => MediaCommand?.Invoke("next"), Brushes.Transparent));
        var music = new Grid();
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        music.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(meta, 1); Grid.SetColumn(ctl, 2);
        music.Children.Add(disc); music.Children.Add(meta); music.Children.Add(ctl);
        var open = Ui.Text("Open IVE  ›", 11, Ui.Ink3);
        open.HorizontalAlignment = HorizontalAlignment.Right; open.Margin = new Thickness(0, 6, 0, 0); open.Cursor = Cursors.Hand;
        open.MouseLeftButtonUp += (_, _) => OpenRequested?.Invoke();
        _peekPanel = new StackPanel { Margin = new Thickness(16, 36, 16, 0), VerticalAlignment = VerticalAlignment.Top, Opacity = 0, IsHitTestVisible = false };
        _peekPanel.Children.Add(music); _peekPanel.Children.Add(open);
        _content.Children.Add(_peekPanel);

        // --- approval panel ---
        _aTitle = Ui.Text("", 13, Ui.Ink, FontWeights.SemiBold, wrap: true);
        _aTitle.MaxHeight = 38; _aTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _aDetail = Ui.Text("", 11, Ui.Ink2, wrap: true);
        _aDetail.FontFamily = Ui.Mono;
        var detailBox = new Border { CornerRadius = new CornerRadius(12), Background = Ui.Hex("#14FFFFFF"), Padding = new Thickness(10, 8, 10, 8), MaxHeight = 78, ClipToBounds = true, Margin = new Thickness(0, 8, 0, 10), Child = _aDetail };
        var deny = Ui.Pill("Deny", Ui.Ghost, Ui.Ink, () => Answer(false));
        var approve = Ui.Pill("Approve", Ui.Ok, Ui.Hex("#04210F"), () => Answer(true));
        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new ColumnDefinition()); buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); buttons.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(approve, 2);
        buttons.Children.Add(deny); buttons.Children.Add(approve);
        _aMore = Ui.Text("", 10.5, Ui.Ink3);
        _aMore.HorizontalAlignment = HorizontalAlignment.Center; _aMore.Margin = new Thickness(0, 6, 0, 0);
        _approvalPanel = new StackPanel { Margin = new Thickness(16, 36, 16, 0), VerticalAlignment = VerticalAlignment.Top, Opacity = 0, IsHitTestVisible = false };
        _approvalPanel.Children.Add(_aTitle); _approvalPanel.Children.Add(detailBox); _approvalPanel.Children.Add(buttons); _approvalPanel.Children.Add(_aMore);
        _content.Children.Add(_approvalPanel);

        // --- pill shell ---
        _pill = new Border
        {
            Background = Ui.Hex("#FF050507"), BorderThickness = new Thickness(1), Child = _content, ClipToBounds = true,
            BorderBrush = new LinearGradientBrush(Color.FromArgb(0x00, 255, 255, 255), Color.FromArgb(0x26, 255, 255, 255), 90),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 9, Direction = 270, Opacity = 0.5 },
        };
        _canvas.Children.Add(_pill);
        Content = _canvas;

        _poll.Tick += (_, _) => PollPointer();
        _clockTimer.Tick += (_, _) => TickClock();
        _resetTimer.Tick += (_, _) => { _resetTimer.Stop(); if (_phase.Kind is PhaseKind.Done or PhaseKind.Error) { _phase = new Phase(PhaseKind.Idle); Render(); } };
        TickClock();
        Apply();
        Render();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Win32.SetExStyle(_hwnd, Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TRANSPARENT, true);
        _poll.Start();
        _clockTimer.Start();
    }

    /// <summary>Auto-fit: size and place the notch for the primary display (DPI + physical size aware).</summary>
    public void Reposition()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds; // physical px (process is PerMonitorV2)
        var scale = Win32.DpiScaleAt(b.X + b.Width / 2, b.Y + 1);
        _u = Math.Min(Math.Max(Math.Sqrt(b.Width / scale / 1920.0), 0.9), 1.45);
        _u = Math.Min(_u, (b.Width / scale - 16) / BaseW);
        _canvas.LayoutTransform = new ScaleTransform(_u, _u);
        var wPx = (int)(BaseW * _u * scale);
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, b.X + (b.Width - wPx) / 2, b.Y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        Width = BaseW * _u;
        Height = BaseH * _u;
    }

    // ------------------------------------------------------------------ state inputs

    public void SetPhase(Phase p)
    {
        _phase = p;
        _resetTimer.Stop();
        if (p.Kind is PhaseKind.Done or PhaseKind.Error) _resetTimer.Start();
        Render();
    }

    public void SetApprovals(IReadOnlyList<PendingApproval> list) { _approvals = list; Render(); }
    public void SetRunning(int n) { _running = n; Render(); }
    public void SetHasKey(bool has) { _hasKey = has; Render(); }

    public void SetMedia(MediaInfo m)
    {
        _mTitle.Text = m.Active && m.Title.Length > 0 ? m.Title : "Nothing playing";
        _mArtist.Text = m.Active ? m.Artist : "Start music anywhere to control it here";
        _playGlyph.Text = m.Playing ? "\uE769" : "\uE768";
    }

    private void Answer(bool ok)
    {
        if (_approvals.Count > 0) ApprovalAnswered?.Invoke(_approvals[0].Id, ok);
    }

    // ------------------------------------------------------------------ rendering

    private void TickClock() => _clock.Text = DateTime.Now.ToString("t");

    private void Render()
    {
        var busy = _phase.Kind is PhaseKind.Queued or PhaseKind.Thinking or PhaseKind.Tool;
        var mode = _approvals.Count > 0 ? Mode.Approval : _hover ? Mode.Peek : busy ? Mode.Working : Mode.Idle;
        if (mode != _mode)
        {
            _mode = mode;
            Fade(_peekPanel, mode == Mode.Peek ? 1 : 0);
            Fade(_approvalPanel, mode == Mode.Approval ? 1 : 0);
        }
        var (w, h, r) = mode switch
        {
            Mode.Working => (252.0, 32.0, 16.0),
            Mode.Peek => (392.0, 112.0, 30.0),
            Mode.Approval => (452.0, 214.0, 32.0),
            _ => (172.0, 30.0, 15.0),
        };
        _w.Target = w; _h.Target = h; _r.Target = r;
        var detached = _running > 0;
        _bx.Target = detached ? 12 : -24;
        _bo.Target = detached ? 1 : 0;

        if (_approvals.Count > 0)
        {
            _aTitle.Text = _approvals[0].Title;
            _aDetail.Text = _approvals[0].Detail ?? "";
            _aMore.Text = _approvals.Count > 1 ? $"+{_approvals.Count - 1} more waiting" : "";
        }

        _label.Text = _approvals.Count > 0 ? "Needs your approval"
            : _phase.Kind is PhaseKind.Queued or PhaseKind.Thinking ? "Thinking…"
            : _phase.Kind == PhaseKind.Tool ? _phase.Label
            : _phase.Kind == PhaseKind.Done ? "Done ✓"
            : _phase.Kind == PhaseKind.Error ? "Something went wrong"
            : !_hasKey ? "Add your API key" : "IVE";

        var c = _approvals.Count > 0 ? Ui.WarnC : _phase.Kind == PhaseKind.Error ? Ui.BadC : busy ? Ui.AccentC : Ui.OkC;
        _dot.Fill = new SolidColorBrush(c);
        if (_dot.Effect is DropShadowEffect fx) fx.Color = c;

        Kick();
    }

    private static void Fade(UIElement e, double to)
    {
        e.IsHitTestVisible = to > 0;
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(to > 0 ? 260 : 120)) { BeginTime = TimeSpan.FromMilliseconds(to > 0 ? 110 : 0) });
    }

    private void Kick()
    {
        if (_animating) return;
        _animating = true;
        _last = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var dt = Math.Min((now - _last) / (double)Stopwatch.Frequency, 0.05);
        _last = now;
        var moving = false;
        foreach (var s in _springs) { s.Step(dt); moving |= !s.Settled; }
        if (!moving)
        {
            foreach (var s in _springs) s.Snap();
            CompositionTarget.Rendering -= OnFrame;
            _animating = false;
        }
        Apply();
    }

    private void Apply()
    {
        var w = Math.Max(1, _w.Value);
        var h = Math.Max(1, _h.Value);
        var r = Math.Max(0, _r.Value);
        Canvas.SetLeft(_pill, (BaseW - w) / 2);
        Canvas.SetTop(_pill, -r - 1);
        _pill.Width = w;
        _pill.Height = h + r + 1;
        _pill.CornerRadius = new CornerRadius(r);
        _content.Margin = new Thickness(0, r + 1, 0, 0);

        var presence = Math.Min(1, Math.Max(0, _bo.Value));
        var stretch = 1 + Math.Min(Math.Abs(_bx.Velocity) / 1800, 0.45); // squash & stretch from velocity
        var size = 0.4 + 0.6 * presence;
        Canvas.SetLeft(_bubble, (BaseW + w) / 2 + _bx.Value);
        Canvas.SetTop(_bubble, 2);
        _bubble.Opacity = presence;
        _bubbleScale.ScaleX = stretch * size;
        _bubbleScale.ScaleY = size / stretch;
    }

    // ------------------------------------------------------------------ click-through + hover

    private void PollPointer()
    {
        if (!IsVisible || _hwnd == IntPtr.Zero) return;
        Win32.GetCursorPos(out var p);
        Point local;
        try { local = PointFromScreen(new Point(p.X, p.Y)); }
        catch { return; }
        var x = local.X / _u;
        var y = local.Y / _u;
        var left = (BaseW - _w.Value) / 2;
        var inPill = x >= left - 4 && x <= left + _w.Value + 4 && y >= 0 && y <= _h.Value + 4;
        var bx = (BaseW + _w.Value) / 2 + _bx.Value;
        var inBubble = _bo.Value > 0.25 && x >= bx && x <= bx + 28 && y >= 0 && y <= 34;
        _over = inPill || inBubble;

        if (_clickThrough == _over)
        {
            _clickThrough = !_over;
            Win32.SetExStyle(_hwnd, Win32.WS_EX_TRANSPARENT, _clickThrough);
        }

        var now = Environment.TickCount64;
        if (_over)
        {
            _leaveSince = 0;
            if (!_hover)
            {
                if (_hoverSince == 0) _hoverSince = now;
                if (now - _hoverSince > 140) { _hover = true; Render(); }
            }
        }
        else
        {
            _hoverSince = 0;
            if (_hover)
            {
                if (_leaveSince == 0) _leaveSince = now;
                if (now - _leaveSince > 320) { _hover = false; _leaveSince = 0; Render(); }
            }
        }
    }
}
