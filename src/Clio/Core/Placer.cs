using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Clio.Core;

/// <summary>
/// What every Clio window needs from Win32: placement in physical pixels, the
/// shared layer (desktop or on top), a resize band on a borderless window, and
/// a callback when the user lets go after a drag or resize.
///
/// Same mechanisms as Argus's DockManager, for the same reasons: bottom of the
/// Z-order rather than WorkerW reparenting, and no WPF Left/Top anywhere,
/// because those are device-independent units and silently wrong on a machine
/// with two scale factors.
/// </summary>
internal sealed class Placer
{
    private readonly Window _window;
    private readonly bool _resizable;
    private IntPtr _hwnd;
    private bool _positioning;

    public Placer(Window window, bool resizable)
    {
        _window = window;
        _resizable = resizable;
    }

    public IntPtr Handle => _hwnd;

    /// <summary>Raised when a user drag or resize finishes, with the final rect.</summary>
    public event Action<Native.RECT>? Settled;

    /// <summary>Creates the HWND without showing it, so the first frame is already in place.</summary>
    public void Attach()
    {
        _hwnd = new WindowInteropHelper(_window).EnsureHandle();
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        // Off the taskbar and out of Alt-Tab: a note is not an application window.
        var ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        ex |= Native.WS_EX_TOOLWINDOW;
        ex &= ~Native.WS_EX_APPWINDOW;
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, ex);
    }

    public Native.RECT Rect
    {
        get
        {
            Native.GetWindowRect(_hwnd, out var r);
            return r;
        }
    }

    public void MoveTo(Native.RECT r) => MoveTo(r.Left, r.Top, r.Width, r.Height);

    public void MoveTo(int left, int top, int width, int height)
    {
        if (_hwnd == IntPtr.Zero) return;
        _positioning = true;
        try
        {
            Native.SetWindowPos(_hwnd, IntPtr.Zero, left, top, width, height,
                (uint)(Native.SWP.NOZORDER | Native.SWP.NOACTIVATE | Native.SWP.NOOWNERZORDER));
        }
        finally
        {
            _positioning = false;
        }
    }

    /// <summary>Moves without resizing. Used every frame of the collapse animation.</summary>
    public void MoveBy(int left, int top)
    {
        _positioning = true;
        try
        {
            Native.SetWindowPos(_hwnd, IntPtr.Zero, left, top, 0, 0,
                (uint)(Native.SWP.NOSIZE | Native.SWP.NOZORDER | Native.SWP.NOACTIVATE));
        }
        finally
        {
            _positioning = false;
        }
    }

    public void ApplyLayer()
    {
        if (_hwnd == IntPtr.Zero) return;
        var after = Store.State.Layer == Layer.OnTop ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST;
        Native.SetWindowPos(_hwnd, after, 0, 0, 0, 0,
            (uint)(Native.SWP.NOMOVE | Native.SWP.NOSIZE | Native.SWP.NOACTIVATE));
        if (Store.State.Layer == Layer.Desktop)
        {
            Native.SetWindowPos(_hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0,
                (uint)(Native.SWP.NOMOVE | Native.SWP.NOSIZE | Native.SWP.NOACTIVATE));
        }
    }

    /// <summary>Scale of the monitor this window is on. 1.0 at 96 DPI.</summary>
    public double Scale
    {
        get
        {
            var r = Rect;
            return Monitors.Containing(r.Left + r.Width / 2, r.Top + r.Height / 2).Scale;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_WINDOWPOSCHANGING when Store.State.Layer == Layer.Desktop && !_positioning:
            {
                // Desktop layer: whatever tries to raise the window - a click,
                // a focus change - is rewritten to "stay at the bottom".
                var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
                pos.hwndInsertAfter = Native.HWND_BOTTOM;
                pos.flags &= ~(uint)Native.SWP.NOZORDER;
                Marshal.StructureToPtr(pos, lParam, false);
                break;
            }

            case Native.WM_NCHITTEST when _resizable:
            {
                // Borderless plus AllowsTransparency has no resize frame. This is
                // the fix Argus needed too: report the rim as resize edges.
                var hit = ResizeHit(lParam);
                if (hit != 0) { handled = true; return hit; }
                break;
            }

            case Native.WM_EXITSIZEMOVE:
                if (!_positioning) Settled?.Invoke(Rect);
                break;

            case Native.WM_DPICHANGED:
                // Honour the suggested rect when the user drags across displays;
                // swallow it while we are placing the window ourselves, or the
                // restored rectangle gets rescaled out from under us.
                if (!_positioning)
                {
                    MoveTo(Marshal.PtrToStructure<Native.RECT>(lParam));
                }
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private IntPtr ResizeHit(IntPtr lParam)
    {
        if (!Native.GetWindowRect(_hwnd, out var r)) return IntPtr.Zero;

        var x = (short)((long)lParam & 0xFFFF);
        var y = (short)(((long)lParam >> 16) & 0xFFFF);

        var band = (int)Math.Ceiling(6 * Scale);
        var left = x < r.Left + band;
        var right = x >= r.Right - band;
        var top = y < r.Top + band;
        var bottom = y >= r.Bottom - band;

        return (top, bottom, left, right) switch
        {
            (true, _, true, _) => Native.HTTOPLEFT,
            (true, _, _, true) => Native.HTTOPRIGHT,
            (_, true, true, _) => Native.HTBOTTOMLEFT,
            (_, true, _, true) => Native.HTBOTTOMRIGHT,
            (true, _, _, _) => Native.HTTOP,
            (_, true, _, _) => Native.HTBOTTOM,
            (_, _, true, _) => Native.HTLEFT,
            (_, _, _, true) => Native.HTRIGHT,
            _ => 0,
        };
    }
}
