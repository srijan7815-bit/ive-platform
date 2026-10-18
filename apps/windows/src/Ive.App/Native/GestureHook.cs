using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace Ive.App.Native;

/// <summary>
/// System-wide gesture using native low-level hooks: hold the trigger key, right-click-drag, release.
/// While the trigger is held the right-click is swallowed, so the app underneath never opens a context menu.
/// A lone tap of the trigger key raises <see cref="Tapped"/>. Handlers must return quickly.
/// </summary>
internal sealed class GestureHook : IDisposable
{
    private const int MinDragPx = 14;
    private const int TapMs = 380;

    private readonly Win32.LowLevelProc _kbProc;
    private readonly Win32.LowLevelProc _msProc;
    private IntPtr _kb, _ms;
    private uint _trigger = 0xA3; // VK_RCONTROL
    private bool _held, _other, _down, _dragging, _swallowUp;
    private long _heldAt;
    private Win32.POINT _origin;

    public event Action<Point, Point> Dragged; // origin, end — physical pixels
    public event Action Tapped;

    public GestureHook()
    {
        _kbProc = Keyboard;
        _msProc = Mouse;
    }

    public bool Active => _kb != IntPtr.Zero && _ms != IntPtr.Zero;

    public static uint VkFor(string name) => name switch
    {
        "LeftCtrl" => 0xA2,
        "RightCtrl" => 0xA3,
        "LeftAlt" => 0xA4,
        "RightAlt" => 0xA5,
        "RightShift" => 0xA1,
        "CapsLock" => 0x14,
        _ => 0xA3,
    };

    public void SetTrigger(string name) => _trigger = VkFor(name);

    public void Start()
    {
        if (Active) return;
        var h = Win32.GetModuleHandle(null);
        _kb = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _kbProc, h, 0);
        _ms = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _msProc, h, 0);
    }

    private IntPtr Keyboard(int n, IntPtr w, IntPtr l)
    {
        if (n >= 0)
        {
            var k = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(l);
            var m = (int)(long)w;
            var down = m == Win32.WM_KEYDOWN || m == Win32.WM_SYSKEYDOWN;
            var up = m == Win32.WM_KEYUP || m == Win32.WM_SYSKEYUP;
            if (k.vkCode == _trigger)
            {
                if (down && !_held) { _held = true; _heldAt = Environment.TickCount64; _other = false; }
                else if (up)
                {
                    var tap = _held && !_other && !_dragging && Environment.TickCount64 - _heldAt < TapMs;
                    _held = false;
                    if (tap) Tapped?.Invoke();
                }
            }
            else if (_held && down) _other = true;
        }
        return Win32.CallNextHookEx(IntPtr.Zero, n, w, l);
    }

    private IntPtr Mouse(int n, IntPtr w, IntPtr l)
    {
        if (n >= 0)
        {
            var m = (int)(long)w;
            var s = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(l);
            if (m == Win32.WM_RBUTTONDOWN && _held)
            {
                _down = true; _dragging = false; _origin = s.pt; _swallowUp = true; _other = true;
                return (IntPtr)1; // swallow: no context menu in the app underneath
            }
            if (m == Win32.WM_MOUSEMOVE && _down && !_dragging)
            {
                var dx = s.pt.X - _origin.X; var dy = s.pt.Y - _origin.Y;
                if (dx * dx + dy * dy >= MinDragPx * MinDragPx) _dragging = true;
            }
            else if (m == Win32.WM_RBUTTONUP && _swallowUp)
            {
                var was = _dragging; var o = _origin;
                _swallowUp = false; _down = false; _dragging = false;
                if (was) Dragged?.Invoke(new Point(o.X, o.Y), new Point(s.pt.X, s.pt.Y));
                return (IntPtr)1;
            }
            else if ((m == Win32.WM_LBUTTONDOWN || m == Win32.WM_MBUTTONDOWN) && _held) _other = true;
        }
        return Win32.CallNextHookEx(IntPtr.Zero, n, w, l);
    }

    public void Dispose()
    {
        if (_kb != IntPtr.Zero) Win32.UnhookWindowsHookEx(_kb);
        if (_ms != IntPtr.Zero) Win32.UnhookWindowsHookEx(_ms);
        _kb = _ms = IntPtr.Zero;
    }
}
