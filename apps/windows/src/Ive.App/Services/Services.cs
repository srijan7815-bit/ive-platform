using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Ive.App.Native;
using Ive.Core;
using Windows.Media.Control;

namespace Ive.App.Services;

/// <summary>API key encryption bound to the current Windows user (DPAPI).</summary>
public sealed class DpapiProtector : ISecretProtector
{
    public string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    public string Unprotect(string cipher) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser));
}

public sealed record MediaState(bool Active, string Title, string Artist, bool Playing)
{
    public static readonly MediaState None = new(false, "", "", false);
}

/// <summary>Now-playing and transport control through the Windows System Media Transport Controls (WinRT).</summary>
public sealed class MediaService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager _mgr;
    private Timer _timer;
    private MediaState _state = MediaState.None;
    private int _busy;

    public MediaState State => _state;

    /// <summary>Substring of the media source app id the user chose (e.g. "Spotify"). Blank = whatever Windows considers current.</summary>
    public string Preferred { get; set; } = "";
    public event Action<MediaState> Changed;

    public async Task StartAsync()
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _timer = new Timer(_ => _ = RefreshAsync(), null, 0, 2000);
        }
        catch { /* media control unavailable on this system: UI shows an idle state */ }
    }

    private async Task RefreshAsync()
    {
        if (_mgr == null || Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var s = Pick();
            var next = MediaState.None;
            if (s != null)
            {
                var props = await s.TryGetMediaPropertiesAsync();
                var playing = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                next = new MediaState(true, props.Title ?? "", props.Artist ?? "", playing);
            }
            if (next != _state) { _state = next; Changed?.Invoke(next); }
        }
        catch { /* transient WinRT failure: keep last state */ }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    private GlobalSystemMediaTransportControlsSession Pick()
    {
        if (_mgr == null) return null;
        if (string.IsNullOrWhiteSpace(Preferred)) return _mgr.GetCurrentSession();
        foreach (var s in _mgr.GetSessions())
            if (s.SourceAppUserModelId.IndexOf(Preferred, StringComparison.OrdinalIgnoreCase) >= 0) return s;
        return null; // the chosen app isn't playing: never fall back to controlling a different app
    }

    /// <summary>Friendly names of apps currently exposing media controls (for the music-app picker).</summary>
    public IReadOnlyList<string> Sources()
    {
        try
        {
            return _mgr?.GetSessions().Select(s => s.SourceAppUserModelId.Split('!').Last().Replace(".exe", "", StringComparison.OrdinalIgnoreCase)).Distinct().ToList() ?? new List<string>();
        }
        catch { return new List<string>(); }
    }

    /// <summary>Starts the user's music app (exe path, URI such as spotify:, or any shell target). Returns an error message or null.</summary>
    public static string Launch(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return "Set a launch target in Settings → Music app.";
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); return null; }
        catch (Exception e) { return e.Message; }
    }

    public async Task ControlAsync(string action)
    {
        try
        {
            var s = Pick();
            if (s == null) return;
            if (action == "playpause") await s.TryTogglePlayPauseAsync();
            else if (action == "next") await s.TrySkipNextAsync();
            else if (action == "prev") await s.TrySkipPreviousAsync();
            await Task.Delay(400);
            await RefreshAsync();
        }
        catch { /* ignore */ }
    }

    public void Dispose() => _timer?.Dispose();
}

internal static class ScreenCapture
{
    /// <summary>Snapshot of a physical-pixel screen rectangle, used as the frosted-glass backdrop of the prompt box.</summary>
    public static BitmapSource Grab(int x, int y, int w, int h)
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
            var hbmp = bmp.GetHbitmap();
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { Win32.DeleteObject(hbmp); }
        }
        catch
        {
            return null;
        }
    }
}
