using System.Runtime.InteropServices;

namespace Clio.Core;

/// <summary>
/// Every P/Invoke Clio needs, in one place. A subset of Argus's Native.cs:
/// the same reasons apply (no WinForms, physical pixels everywhere).
/// </summary>
internal static class Native
{
    // ---------------------------------------------------------------- windows

    public static readonly IntPtr HWND_BOTTOM    = new(1);
    public static readonly IntPtr HWND_TOPMOST   = new(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new(-2);

    [Flags]
    public enum SWP : uint
    {
        NOSIZE         = 0x0001,
        NOMOVE         = 0x0002,
        NOZORDER       = 0x0004,
        NOACTIVATE     = 0x0010,
        NOOWNERZORDER  = 0x0200,
    }

    public const int GWL_EXSTYLE = -20;
    public const int GWL_STYLE   = -16;

    public const int WS_THICKFRAME   = 0x00040000;
    public const int WS_MINIMIZEBOX  = 0x00020000;
    public const int WS_MAXIMIZEBOX  = 0x00010000;

    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_APPWINDOW  = 0x00040000;

    public const int WM_WINDOWPOSCHANGING = 0x0046;
    public const int WM_EXITSIZEMOVE      = 0x0232;
    public const int WM_DPICHANGED        = 0x02E0;
    public const int WM_NCHITTEST         = 0x0084;

    public const int HTLEFT        = 10;
    public const int HTRIGHT       = 11;
    public const int HTTOP         = 12;
    public const int HTTOPLEFT     = 13;
    public const int HTTOPRIGHT    = 14;
    public const int HTBOTTOM      = 15;
    public const int HTBOTTOMLEFT  = 16;
    public const int HTBOTTOMRIGHT = 17;

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width  => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                           int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static int GetWindowLong(IntPtr hWnd, int nIndex)
        => (int)(long)GetWindowLongPtr64(hWnd, nIndex);

    public static void SetWindowLong(IntPtr hWnd, int nIndex, int value)
        => SetWindowLongPtr64(hWnd, nIndex, new IntPtr(value));

    /// <summary>The window rectangle in PHYSICAL pixels, the only unit placement uses.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int RegisterWindowMessage(string lpString);

    // ------------------------------------------------------------------ dpi

    public const int MONITORINFOF_PRIMARY = 0x1;

    public enum MONITOR_DPI_TYPE { EFFECTIVE = 0, ANGULAR = 1, RAW = 2 }

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, MONITOR_DPI_TYPE dpiType,
                                              out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip,
                                                  MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    // ------------------------------------------------------------ tray icon

    public const int NIM_ADD    = 0x0;
    public const int NIM_MODIFY = 0x1;
    public const int NIM_DELETE = 0x2;

    public const int NIF_MESSAGE = 0x01;
    public const int NIF_ICON    = 0x02;
    public const int NIF_TIP     = 0x04;

    public const int WM_APP           = 0x8000;
    public const int WM_TRAYICON      = WM_APP + 1;
    public const int WM_LBUTTONUP     = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONUP     = 0x0205;

    public const int HWND_MESSAGE = -3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    public static extern int ExtractIconEx(string lpszFile, int nIconIndex,
                                           IntPtr[]? phiconLarge, IntPtr[]? phiconSmall,
                                           int nIcons);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ------------------------------------------------------------ memory trim

    /// <summary>
    /// Hands back pages nothing is touching. Called once startup has settled
    /// and after a collapse, the two moments a lot of memory goes dead at once.
    /// </summary>
    [DllImport("psapi.dll")]
    public static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    public static void TrimWorkingSet()
    {
        try { EmptyWorkingSet(GetCurrentProcess()); }
        catch (Exception ex) { Log.Warn($"working set trim failed: {ex.Message}"); }
    }
}
