using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Ive.App.Native;
using Ive.App.Services;
using Ive.Core;

namespace Ive.App.UI;

/// <summary>
/// "Ask IVE" box. The frosted glass is real: a snapshot of the desktop behind the window is taken just before it
/// appears, Gaussian-blurred and tinted. The box springs out of the exact point where the gesture ended.
/// </summary>
public sealed class PromptWindow : Window
{
    private const double BaseW = 700, BaseH = 280, Pad = 28;

    private readonly Grid _root = new();
    private readonly Grid _host = new();
    private readonly Border _card;
    private readonly Grid _inner = new();
    private readonly System.Windows.Controls.Image _backdrop;
    private readonly TextBox _input;
    private readonly TextBlock _placeholder;
    private readonly WrapPanel _chips;
    private readonly VoiceSession _voice;
    private readonly SettingsStore _settings;
    private readonly MicButton _mic;
    private readonly TextBlock _notice;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly BlurEffect _blur = new() { Radius = 0, RenderingBias = RenderingBias.Performance };
    private bool _open;
    private int _seq;
    private long _openedAt;
    private double _u = 1;

    public event Action<string> Submitted;

    public PromptWindow(VoiceSession voice, SettingsStore settings)
    {
        _voice = voice;
        _settings = settings;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = Ui.Font;
        Title = "Ask IVE";
        Width = BaseW;
        Height = BaseH;

        _backdrop = new System.Windows.Controls.Image
        {
            Width = BaseW, Height = BaseH, Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(-Pad, -Pad, 0, 0),
            Effect = new BlurEffect { Radius = 28, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance },
        };
        var tint = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xB0, 0x14, 0x17, 0x24), 0),
                    new GradientStop(Color.FromArgb(0x96, 0x0C, 0x0E, 0x18), 1),
                },
            },
        };
        var sheen = new Border { Background = new RadialGradientBrush(Color.FromArgb(0x30, 255, 255, 255), Colors.Transparent) { Center = new Point(0.3, 0), GradientOrigin = new Point(0.3, 0), RadiusX = 0.9, RadiusY = 0.9 }, IsHitTestVisible = false };

        // content
        var spark = new System.Windows.Shapes.Ellipse
        {
            Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center,
            Fill = new LinearGradientBrush(Ui.AccentC, Ui.Accent2C, 45),
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 0, Color = Ui.Accent2C, Opacity = 0.9 },
        };
        var head = new DockPanel { LastChildFill = false };
        var title = Ui.Text("Ask IVE", 11.5, Ui.Ink, FontWeights.SemiBold);
        title.Margin = new Thickness(8, 0, 0, 0);
        var keys = Ui.Text("Enter to send · Esc to close", 11, Ui.Ink3);
        DockPanel.SetDock(keys, Dock.Right);
        head.Children.Add(spark); head.Children.Add(title); head.Children.Add(keys);

        _input = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 116, BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Foreground = Ui.Ink, CaretBrush = Brushes.White, FontSize = 17, Padding = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, SelectionBrush = Ui.Accent,
        };
        _placeholder = Ui.Text("Tell IVE what to do…", 17, Ui.Ink3);
        _placeholder.IsHitTestVisible = false;
        var inputHost = new Grid { Margin = new Thickness(0, 6, 12, 6), VerticalAlignment = VerticalAlignment.Center };
        inputHost.Children.Add(_placeholder); inputHost.Children.Add(_input);
        _notice = Ui.Text("", 11.5, Ui.Bad, wrap: true);
        _notice.Visibility = Visibility.Collapsed;
        _mic = new MicButton(voice, 40, OnVoiceText, m => { _notice.Text = m; _notice.Visibility = Visibility.Visible; });
        _mic.VerticalAlignment = VerticalAlignment.Top;
        _mic.Margin = new Thickness(0, 2, 0, 0);
        var inputRow = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_mic, 1);
        inputRow.Children.Add(inputHost); inputRow.Children.Add(_mic);
        _input.TextChanged += (_, _) => { _placeholder.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; _chips.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
        _input.PreviewKeyDown += OnKey;

        _chips = new WrapPanel();
        foreach (var s in new[] { "Summarize my Downloads folder", "Find the 10 largest files here", "Create a TODO.md for today" })
        {
            var text = s;
            var chip = Ui.Pill(text, Ui.Hex("#14FFFFFF"), Ui.Ink2, () => { _input.Text = text; _input.CaretIndex = text.Length; _input.Focus(); }, 11.5);
            chip.Padding = new Thickness(12, 5, 12, 5);
            chip.Margin = new Thickness(0, 0, 6, 6);
            _chips.Children.Add(chip);
        }

        var body = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };
        body.Children.Add(head); body.Children.Add(inputRow); body.Children.Add(_notice); body.Children.Add(_chips);

        _inner.Children.Add(_backdrop); _inner.Children.Add(tint); _inner.Children.Add(sheen); _inner.Children.Add(body);
        _inner.SizeChanged += (_, e) => _inner.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, e.NewSize.Height), 25, 25);

        _card = new Border
        {
            CornerRadius = new CornerRadius(26), BorderThickness = new Thickness(1), Child = _inner,
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xB4, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0x14, 255, 255, 255), 0.4),
                    new GradientStop(Color.FromArgb(0x66, 255, 255, 255), 1),
                },
            },
            Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 12, Direction = 270, Opacity = 0.5 },
        };

        _host.Margin = new Thickness(Pad);
        _host.VerticalAlignment = VerticalAlignment.Top;
        _host.RenderTransformOrigin = new Point(0.5, 0.2);
        _host.RenderTransform = _scale;
        _host.Children.Add(_card);
        _root.Children.Add(_host);
        Content = _root;

        Deactivated += (_, _) => { if (_open && Environment.TickCount64 - _openedAt > 350) CloseBox(); };
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; CloseBox(); }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            var t = _input.Text.Trim();
            if (t.Length > 0) Submitted?.Invoke(t);
            CloseBox();
        }
    }

    /// <summary>origin/end are physical screen pixels.</summary>
    public bool IsOpen => _open;

    public void ToggleVoice() => _mic.Toggle();

    private void OnVoiceText(string text)
    {
        if (!_open) return;
        var t = (_input.Text + " " + text).Trim();
        _input.Text = t;
        _input.CaretIndex = t.Length;
        if (_settings.Current.VoiceAutoSend)
        {
            Submitted?.Invoke(t);
            CloseBox();
        }
    }

    public void OpenAt(Point origin, Point end, bool startVoice = false)
    {
        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)end.X, (int)end.Y)).WorkingArea;
        var scale = Win32.DpiScaleAt((int)end.X, (int)end.Y);
        _u = Math.Min(Math.Max(Math.Sqrt(wa.Width / scale / 1920.0), 0.9), 1.45);
        _u = Math.Min(_u, (wa.Width / scale - 24) / BaseW);
        var wPx = (int)(BaseW * _u * scale);
        var hPx = (int)(BaseH * _u * scale);
        var x = Math.Max(wa.X + 12, Math.Min((int)end.X - wPx / 2, wa.Right - wPx - 12));
        var y = Math.Max(wa.Y + 8, Math.Min((int)end.Y - (int)(36 * _u * scale), wa.Bottom - hPx - 8));

        _backdrop.Source = ScreenCapture.Grab(x, y, wPx, hPx); // before we appear, so we are not in the shot
        _root.LayoutTransform = new ScaleTransform(_u, _u);
        _input.Text = "";
        _notice.Visibility = Visibility.Collapsed;

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        Width = BaseW * _u;
        Height = BaseH * _u;

        var cardLeft = x + Pad * _u * scale;
        var cardW = wPx - 2 * Pad * _u * scale;
        var cardTop = y + Pad * _u * scale;
        var cardH = 130 * _u * scale;
        _host.RenderTransformOrigin = new Point(Clamp((origin.X - cardLeft) / cardW), Clamp((origin.Y - cardTop) / cardH));

        _seq++;
        _open = true;
        _openedAt = Environment.TickCount64;
        BeginAnimation(OpacityProperty, null);
        _host.BeginAnimation(OpacityProperty, null);
        _host.Opacity = 0;
        Show();
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        Win32.ForceForeground(hwnd);
        Activate();
        _input.Focus();
        Keyboard.Focus(_input);
        if (startVoice) _mic.Toggle();

        // liquid entrance: spring scale + fast fade + focus-pull blur
        _host.Effect = _blur;
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.22, 1, TimeSpan.FromMilliseconds(720)) { EasingFunction = new SpringEase() });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(720)) { EasingFunction = new SpringEase() });
        _host.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        var blur = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(460)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        blur.Completed += (_, _) => { if (_open) _host.Effect = null; };
        _blur.BeginAnimation(BlurEffect.RadiusProperty, blur);
    }

    private static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    public void CloseBox()
    {
        if (!_open) return;
        _open = false;
        _voice.Cancel();
        var mine = ++_seq;
        _host.Effect = _blur;
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.4, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        _blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(12, TimeSpan.FromMilliseconds(220)));
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) => { if (mine == _seq) Hide(); };
        _host.BeginAnimation(OpacityProperty, fade);
    }
}
