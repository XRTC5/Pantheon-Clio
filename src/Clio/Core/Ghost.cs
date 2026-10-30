using System.Runtime.InteropServices;

namespace Clio.Core;

/// <summary>
/// A note that is asleep: a picture of it in a bare layered window, standing
/// exactly where the real note stood, in the same place in the window stack.
///
/// Why it exists: a live WPF note costs about 3.3 MB of private memory at
/// 650x600, and nearly all of that is pixel buffers, because WPF's software
/// renderer keeps its own copies of the window image. The compositor already
/// holds the image for any layered window, so for a note nobody is touching
/// those copies are pure duplication. This window hands the pixels to the
/// compositor once with UpdateLayeredWindow and frees its own, which measured
/// 0.06 MB each.
///
/// It cannot be typed into or dragged. The first mouse movement over it raises
/// Woken, and Notes builds the real note underneath before removing this.
/// </summary>
internal sealed class Ghost : IDisposable
{
    private const string ClassName = "ClioSleepingNote";

    // The delegate must outlive every window of the class, or the GC collects
    // the thunk Windows calls into. One static for the life of the process.
    private static readonly Native.WndProc Proc = WndProc;
    private static readonly Dictionary<IntPtr, Ghost> Live = new();
    private static bool _registered;

    private bool _woke;
    private Native.RECT _rect;

    public NoteData Data { get; }
    public IntPtr Handle { get; private set; }
    public Native.RECT Rect => _rect;

    /// <summary>Raised once, on the first mouse movement or click over the picture.</summary>
    public event Action<Ghost>? Woken;

    /// <param name="pixels">Premultiplied BGRA, width x height of <paramref name="rect"/>.</param>
    /// <param name="below">The live note's window; the picture takes the slot just below it,
    /// which becomes the note's own slot the moment the note closes.</param>
    public Ghost(NoteData data, int[] pixels, Native.RECT rect, IntPtr below, bool topmost)
    {
        Data = data;
        _rect = rect;
        Register();

        var ex = Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
        if (topmost) ex |= Native.WS_EX_TOPMOST;

        Handle = Native.CreateWindowEx(ex, ClassName, "Clio note (asleep)", Native.WS_POPUP,
            rect.Left, rect.Top, rect.Width, rect.Height,
            IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandle(null), IntPtr.Zero);
        if (Handle == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");

        Live[Handle] = this;
        Paint(pixels);

        Native.SetWindowPos(Handle, below, 0, 0, 0, 0,
            (uint)(Native.SWP.NOMOVE | Native.SWP.NOSIZE | Native.SWP.NOACTIVATE));
        Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
    }

    /// <summary>Hands the picture to the compositor, then frees our copy of it.</summary>
    private void Paint(int[] pixels)
    {
        var w = _rect.Width;
        var h = _rect.Height;
        var screen = Native.GetDC(IntPtr.Zero);
        var mem = Native.CreateCompatibleDC(screen);
        var info = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,   // top-down, the same row order as the pixel array
            biPlanes = 1,
            biBitCount = 32,
        };
        var dib = Native.CreateDIBSection(screen, ref info, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            Marshal.Copy(pixels, 0, bits, Math.Min(pixels.Length, w * h));
            var old = Native.SelectObject(mem, dib);
            var dst = new Native.POINT { X = _rect.Left, Y = _rect.Top };
            var size = new Native.SIZE { cx = w, cy = h };
            var src = new Native.POINT();
            var blend = Blend(255);
            Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, Native.ULW_ALPHA);
            Native.SelectObject(mem, old);
        }
        finally
        {
            Native.DeleteObject(dib);
            Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static Native.BLENDFUNCTION Blend(byte alpha) => new()
    {
        BlendOp = 0,
        SourceConstantAlpha = alpha,
        AlphaFormat = Native.AC_SRC_ALPHA,
    };

    public void MoveBy(int left, int top)
    {
        Native.SetWindowPos(Handle, IntPtr.Zero, left, top, 0, 0,
            (uint)(Native.SWP.NOSIZE | Native.SWP.NOZORDER | Native.SWP.NOACTIVATE));
        _rect = new Native.RECT { Left = left, Top = top, Right = left + _rect.Width, Bottom = top + _rect.Height };
    }

    /// <summary>Whole-window fade, without re-sending pixels. Used while stowing.</summary>
    public void SetOpacity(double opacity)
    {
        var blend = Blend((byte)Math.Clamp(opacity * 255, 0, 255));
        Native.UpdateLayeredWindowAlpha(Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
            IntPtr.Zero, IntPtr.Zero, 0, ref blend, Native.ULW_ALPHA);
    }

    public void ApplyLayer(bool topmost) =>
        Native.SetWindowPos(Handle, topmost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
            (uint)(Native.SWP.NOMOVE | Native.SWP.NOSIZE | Native.SWP.NOACTIVATE));

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        Live.Remove(Handle);
        Native.DestroyWindow(Handle);
        Handle = IntPtr.Zero;
    }

    private static void Register()
    {
        if (_registered) return;
        var wc = new Native.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<Native.WNDCLASSEX>(),
            lpfnWndProc = Proc,
            hInstance = Native.GetModuleHandle(null),
            hCursor = Native.LoadCursor(IntPtr.Zero, new IntPtr(32512)),   // IDC_ARROW
            lpszClassName = ClassName,
        };
        if (Native.RegisterClassEx(ref wc) == 0)
            Log.Warn($"sleeping-note class not registered: {Marshal.GetLastWin32Error()}");
        _registered = true;
    }

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            // Never take focus: waking is the only thing a click here should do.
            case Native.WM_MOUSEACTIVATE:
                return new IntPtr(Native.MA_NOACTIVATE);

            case Native.WM_MOUSEMOVE:
            case Native.WM_LBUTTONDOWN:
            case Native.WM_RBUTTONDOWN:
                if (Live.TryGetValue(hwnd, out var ghost) && !ghost._woke)
                {
                    ghost._woke = true;
                    ghost.Woken?.Invoke(ghost);
                }
                return IntPtr.Zero;
        }
        return Native.DefWindowProc(hwnd, msg, wParam, lParam);
    }
}
