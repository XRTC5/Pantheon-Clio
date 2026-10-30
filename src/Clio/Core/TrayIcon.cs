using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Clio.Core;

/// <summary>
/// The notification-area icon: the way back to Clio when the hub is buried on
/// the desktop layer under a maximised window. Shell_NotifyIcon directly, as in
/// Argus, so WinForms never loads.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int IconId = 1;

    private readonly HwndSource _sink;
    private readonly Action _onClick;
    private readonly Func<ContextMenu> _menu;
    private readonly int _taskbarCreated;

    private IntPtr _hIcon;
    private bool _added;

    public TrayIcon(Action onClick, Func<ContextMenu> menu)
    {
        _onClick = onClick;
        _menu = menu;

        _sink = new HwndSource(new HwndSourceParameters("ClioTraySink")
        {
            ParentWindow = new IntPtr(Native.HWND_MESSAGE),
            Width = 1,
            Height = 1,
        });
        _sink.AddHook(WndProc);
        _taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

        Add();
    }

    private void Add()
    {
        if (_hIcon == IntPtr.Zero) _hIcon = LoadOwnIcon();

        var data = NewData();
        data.uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP;
        data.uCallbackMessage = Native.WM_TRAYICON;
        data.hIcon = _hIcon;
        data.szTip = "Clio";

        _added = Native.Shell_NotifyIcon(Native.NIM_ADD, ref data);
        if (!_added) Log.Warn("tray icon could not be added");
    }

    private Native.NOTIFYICONDATA NewData() => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.NOTIFYICONDATA>(),
        hWnd = _sink.Handle,
        uID = IconId,
    };

    private static IntPtr LoadOwnIcon()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return IntPtr.Zero;
        var small = new IntPtr[1];
        return Native.ExtractIconEx(exe, 0, null, small, 1) > 0 ? small[0] : IntPtr.Zero;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            // Explorer restarted and took every tray icon with it.
            Add();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg != Native.WM_TRAYICON) return IntPtr.Zero;

        switch ((int)lParam)
        {
            case Native.WM_LBUTTONUP:
                _onClick();
                handled = true;
                break;

            case Native.WM_RBUTTONUP:
                // Without the foreground call the menu never dismisses on an
                // outside click. A documented shell quirk.
                Native.SetForegroundWindow(_sink.Handle);
                var menu = _menu();
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData();
            Native.Shell_NotifyIcon(Native.NIM_DELETE, ref data);
            _added = false;
        }
        if (_hIcon != IntPtr.Zero)
        {
            Native.DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
        _sink.RemoveHook(WndProc);
        _sink.Dispose();
    }
}
