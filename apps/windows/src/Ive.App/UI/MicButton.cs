using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ive.App.Services;

namespace Ive.App.UI;

/// <summary>Round microphone button: tap to start, tap to stop &amp; transcribe. Pulses with the live input level while recording.</summary>
public sealed class MicButton : Border
{
    private readonly VoiceSession _voice;
    private readonly Action<string> _onText;
    private readonly Action<string> _onError;
    private readonly TextBlock _glyph;
    private readonly ScaleTransform _pulse = new(1, 1);

    public MicButton(VoiceSession voice, double diameter, Action<string> onText, Action<string> onError)
    {
        _voice = voice;
        _onText = onText;
        _onError = onError;
        _glyph = new TextBlock
        {
            Text = "\uE720", FontFamily = Ui.Icons, FontSize = diameter * 0.44, Foreground = Ui.Ink,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        Width = diameter;
        Height = diameter;
        CornerRadius = new CornerRadius(diameter / 2);
        Background = Ui.Ghost;
        Cursor = Cursors.Hand;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _pulse;
        Child = _glyph;
        ToolTip = "Voice input";

        MouseLeftButtonUp += (_, _) => Toggle();
        voice.StateChanged += st => Dispatcher.InvokeAsync(() => Apply(st));
        voice.Level += l => Dispatcher.InvokeAsync(() =>
        {
            if (_voice.State != VoiceState.Recording) return;
            var s = 1 + 0.32 * l;
            _pulse.ScaleX = s;
            _pulse.ScaleY = s;
        });
    }

    private void Apply(VoiceState st)
    {
        _pulse.ScaleX = _pulse.ScaleY = 1;
        switch (st)
        {
            case VoiceState.Recording:
                Background = Ui.Bad;
                _glyph.Text = "\uE71A"; // stop
                _glyph.Foreground = Ui.Dark;
                break;
            case VoiceState.Transcribing:
                Background = Ui.Ghost;
                _glyph.Text = "\uE712"; // …
                _glyph.Foreground = Ui.Ink2;
                break;
            default:
                Background = Ui.Ghost;
                _glyph.Text = "\uE720"; // mic
                _glyph.Foreground = Ui.Ink;
                break;
        }
    }

    public async void Toggle()
    {
        try
        {
            if (_voice.State == VoiceState.Idle) _voice.Start();
            else if (_voice.State == VoiceState.Recording)
            {
                var text = await _voice.StopAsync();
                if (!string.IsNullOrWhiteSpace(text)) _onText(text);
            }
        }
        catch (Exception e)
        {
            _onError(e.Message);
        }
    }
}
