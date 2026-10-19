using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;

namespace Ive.App.Native;

/// <summary>Global hotkeys via RegisterHotKey on a message-only window. Each hotkey has a small integer id.</summary>
internal sealed class HotkeyListener : IDisposable
{
    private readonly HwndSource _source;
    private readonly HashSet<int> _registered = new();

    public event Action<int> Pressed;

    public HotkeyListener()
    {
        _source = new HwndSource(new HwndSourceParameters("IveHotkey") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(Hook);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY)
        {
            handled = true;
            Pressed?.Invoke((int)(long)wParam);
        }
        return IntPtr.Zero;
    }

    /// <summary>Accepts specs like "Ctrl+Alt+Space". Returns false if invalid or already taken by another app.</summary>
    public bool Register(int id, string spec)
    {
        Unregister(id);
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
        var ok = Win32.RegisterHotKey(_source.Handle, id, mods, vk);
        if (ok) _registered.Add(id);
        return ok;
    }

    private void Unregister(int id)
    {
        if (_registered.Remove(id)) Win32.UnregisterHotKey(_source.Handle, id);
    }

    public void Dispose()
    {
        foreach (var id in new List<int>(_registered)) Unregister(id);
        _source.Dispose();
    }
}
