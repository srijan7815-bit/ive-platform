using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Ive.App.UI;

/// <summary>Design tokens + tiny component factory. The whole UI is built in code: no XAML parsing at runtime, everything is compile-checked.</summary>
public static class Ui
{
    public static readonly FontFamily Font = new("Segoe UI Variable Display, Segoe UI, sans-serif");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, monospace");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static SolidColorBrush Hex(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    public static readonly Color AccentC = Color.FromRgb(0x7D, 0xD3, 0xFC);
    public static readonly Color Accent2C = Color.FromRgb(0xA7, 0x8B, 0xFA);
    public static readonly Color OkC = Color.FromRgb(0x4A, 0xDE, 0x80);
    public static readonly Color WarnC = Color.FromRgb(0xFB, 0xBF, 0x24);
    public static readonly Color BadC = Color.FromRgb(0xF8, 0x71, 0x71);

    public static readonly Brush Ink = Hex("#F2FFFFFF");
    public static readonly Brush Ink2 = Hex("#A6FFFFFF");
    public static readonly Brush Ink3 = Hex("#66FFFFFF");
    public static readonly Brush Accent = Hex("#7DD3FC");
    public static readonly Brush Ok = Hex("#4ADE80");
    public static readonly Brush Warn = Hex("#FBBF24");
    public static readonly Brush Bad = Hex("#F87171");
    public static readonly Brush Dark = Hex("#07080D");
    public static readonly Brush Ghost = Hex("#24FFFFFF");

    public static Brush AccentGradient() => new LinearGradientBrush(AccentC, Accent2C, 20);

    public static TextBlock Text(string text, double size = 13, Brush fg = null, FontWeight? weight = null, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = fg ?? Ink,
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
    };

    /// <summary>Frosted-glass card: translucent fill, specular rim, soft depth. Sits on top of the window's DWM acrylic.</summary>
    public static Border Glass(UIElement child, double radius = 22, bool strong = false) => new()
    {
        Child = child,
        CornerRadius = new CornerRadius(radius),
        BorderThickness = new Thickness(1),
        Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb((byte)(strong ? 0x48 : 0x30), 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(0x10, 255, 255, 255), 1),
            },
        },
        BorderBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xA6, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(0x12, 255, 255, 255), 0.42),
                new GradientStop(Color.FromArgb(0x55, 255, 255, 255), 1),
            },
        },
        Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 8, Direction = 270, Opacity = 0.32, Color = Colors.Black },
    };

    private static void Spring(ScaleTransform t, double to)
    {
        DoubleAnimation A() => new(to, TimeSpan.FromMilliseconds(420)) { EasingFunction = new SpringEase() };
        t.BeginAnimation(ScaleTransform.ScaleXProperty, A());
        t.BeginAnimation(ScaleTransform.ScaleYProperty, A());
    }

    /// <summary>Capsule button with spring hover/press feedback.</summary>
    public static Border Pill(string text, Brush fill, Brush fg, Action click, double size = 13)
    {
        var scale = new ScaleTransform(1, 1);
        var label = Text(text, size, fg ?? Ink, FontWeights.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var b = new Border
        {
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(18, 9, 18, 9),
            Background = fill ?? Ghost,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
            Child = label,
        };
        b.MouseEnter += (_, _) => Spring(scale, 1.05);
        b.MouseLeave += (_, _) => Spring(scale, 1);
        b.MouseLeftButtonDown += (_, _) => Spring(scale, 0.94);
        b.MouseLeftButtonUp += (_, _) => { Spring(scale, 1.05); click?.Invoke(); };
        return b;
    }

    /// <summary>Round glyph button using Segoe Fluent Icons.</summary>
    public static Border IconButton(string glyph, double diameter, Action click, Brush fill = null, Brush fg = null)
    {
        var scale = new ScaleTransform(1, 1);
        var g = new TextBlock
        {
            Text = glyph,
            FontFamily = Icons,
            FontSize = diameter * 0.42,
            Foreground = fg ?? Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var b = new Border
        {
            Width = diameter,
            Height = diameter,
            CornerRadius = new CornerRadius(diameter / 2),
            Background = fill ?? Ghost,
            Cursor = Cursors.Hand,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
            Child = g,
        };
        b.MouseEnter += (_, _) => Spring(scale, 1.08);
        b.MouseLeave += (_, _) => Spring(scale, 1);
        b.MouseLeftButtonDown += (_, _) => Spring(scale, 0.88);
        b.MouseLeftButtonUp += (_, _) => { Spring(scale, 1.08); click?.Invoke(); };
        return b;
    }

    public static Border Chip(string text, Color c) => new()
    {
        CornerRadius = new CornerRadius(999),
        Padding = new Thickness(10, 3, 10, 3),
        Background = new SolidColorBrush(Color.FromArgb(0x2E, c.R, c.G, c.B)),
        Child = Text(text, 11, new SolidColorBrush(c), FontWeights.Bold),
        VerticalAlignment = VerticalAlignment.Top,
    };

    /// <summary>Input surface: rounded translucent field wrapping a borderless TextBox/PasswordBox.</summary>
    public static Border Field(Control input)
    {
        input.Background = Brushes.Transparent;
        input.BorderThickness = new Thickness(0);
        input.Foreground = Ink;
        input.FontSize = 14;
        input.Padding = new Thickness(0, 2, 0, 2);
        input.VerticalContentAlignment = VerticalAlignment.Center;
        if (input is TextBox tb) tb.CaretBrush = Brushes.White;
        if (input is PasswordBox pb) pb.CaretBrush = Brushes.White;
        var box = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 9, 14, 9),
            Background = Hex("#14FFFFFF"),
            BorderBrush = Hex("#1FFFFFFF"),
            BorderThickness = new Thickness(1),
            Child = input,
        };
        input.GotKeyboardFocus += (_, _) => box.BorderBrush = Accent;
        input.LostKeyboardFocus += (_, _) => box.BorderBrush = Hex("#1FFFFFFF");
        return box;
    }
}

/// <summary>iOS-style switch whose knob springs between states.</summary>
public sealed class ToggleSwitch : Border
{
    private readonly TranslateTransform _tt = new();
    private bool _on;

    public event Action<bool> Toggled;

    public ToggleSwitch()
    {
        Width = 46;
        Height = 28;
        CornerRadius = new CornerRadius(14);
        Cursor = Cursors.Hand;
        Child = new Ellipse
        {
            Width = 22,
            Height = 22,
            Fill = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0),
            RenderTransform = _tt,
        };
        MouseLeftButtonUp += (_, _) => { IsOn = !IsOn; Toggled?.Invoke(IsOn); };
        Apply();
    }

    public bool IsOn
    {
        get => _on;
        set { _on = value; Apply(); }
    }

    private void Apply()
    {
        Background = _on ? Ui.Ok : Ui.Hex("#29FFFFFF");
        _tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(_on ? 18 : 0, TimeSpan.FromMilliseconds(450)) { EasingFunction = new SpringEase() });
    }
}
