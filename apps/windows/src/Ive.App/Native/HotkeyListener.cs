using System;
using System.Windows.Input;
using System.Windows.Interop;

namespace Ive.App.Native;

/// <summary>Global hotkey via RegisterHotKey on a message-only window.</summary>
internal sealed class HotkeyListener : IDisposable
{
    private const int Id = 0xA11E;
    private readonly HwndSource _source;
    private bool _registered;

    public event Action Pressed;

    public HotkeyListener()
    {
        _source = new HwndSource(new HwndSourceParameters("IveHotkey") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(Hook);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && (int)(long)wParam == Id)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    /// <summary>Accepts specs like "Ctrl+Alt+Space". Returns false if the combination is invalid or already taken.</summary>
    public bool Register(string spec)
    {
        Unregister();
        uint mods = 0x4000; // MOD_NOREPEAT
        uint vk = 0;
        foreach (var raw in (spec ?? "").Split('+'))
        {
            var t = raw.Trim();
            switch (t.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= 0x2; break;
                case "alt": mods |= 0x1; break;
                case "shift": mods |= 0x4; break;
                case "win": case "windows": mods |= 0x8; break;
                default:
                    if (Enum.TryParse<Key>(t, true, out var key)) vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        if (vk == 0) return false;
        _registered = Win32.RegisterHotKey(_source.Handle, Id, mods, vk);
        return _registered;
    }

    private void Unregister()
    {
        if (_registered) Win32.UnregisterHotKey(_source.Handle, Id);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        _source.Dispose();
    }
}
