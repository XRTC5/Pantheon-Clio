namespace Clio.Core;

/// <summary>One display, in physical pixels.</summary>
internal sealed record MonitorInfo
{
    public required Native.RECT Bounds { get; init; }
    public required Native.RECT WorkArea { get; init; }
    public required bool IsPrimary { get; init; }

    /// <summary>Scale factor of this display. Two displays often differ.</summary>
    public double Scale { get; init; } = 1.0;
}

/// <summary>
/// Display enumeration without System.Windows.Forms.Screen. Answers in
/// physical pixels with each monitor's own DPI attached.
/// </summary>
internal static class Monitors
{
    public static List<MonitorInfo> All()
    {
        var found = new List<MonitorInfo>(2);

        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            var info = new Native.MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFOEX>(),
            };

            if (Native.GetMonitorInfo(hMonitor, ref info))
            {
                var scale = 1.0;
                if (Native.GetDpiForMonitor(hMonitor, Native.MONITOR_DPI_TYPE.EFFECTIVE,
                                            out var dpiX, out _) == 0 && dpiX > 0)
                {
                    scale = dpiX / 96.0;
                }

                found.Add(new MonitorInfo
                {
                    Bounds = info.rcMonitor,
                    WorkArea = info.rcWork,
                    IsPrimary = (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0,
                    Scale = scale,
                });
            }

            return true;
        }, IntPtr.Zero);

        if (found.Count == 0)
        {
            found.Add(new MonitorInfo
            {
                Bounds = new Native.RECT { Left = 0, Top = 0, Right = 1280, Bottom = 720 },
                WorkArea = new Native.RECT { Left = 0, Top = 0, Right = 1280, Bottom = 720 },
                IsPrimary = true,
            });
        }

        found.Sort((a, b) => a.IsPrimary == b.IsPrimary ? 0 : a.IsPrimary ? -1 : 1);
        return found;
    }

    public static MonitorInfo Primary => All()[0];

    public static MonitorInfo Containing(int x, int y)
    {
        var all = All();
        foreach (var m in all)
        {
            if (x >= m.Bounds.Left && x < m.Bounds.Right &&
                y >= m.Bounds.Top && y < m.Bounds.Bottom)
            {
                return m;
            }
        }
        return all[0];
    }

    /// <summary>
    /// True when enough of a rectangle is on some display to grab it. A note
    /// left on a monitor that is now unplugged must not be restored into dead space.
    /// </summary>
    public static bool IsReachable(Native.RECT r)
    {
        foreach (var m in All())
        {
            var w = m.WorkArea;
            var ix = Math.Min(r.Right, w.Right) - Math.Max(r.Left, w.Left);
            var iy = Math.Min(r.Bottom, w.Bottom) - Math.Max(r.Top, w.Top);
            if (ix >= 40 && iy >= 40) return true;
        }
        return false;
    }
}
