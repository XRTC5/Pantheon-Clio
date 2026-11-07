using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Clio;

/// <summary>
/// The pen's cursor. WPF's own is a dot the size of the stroke, a speck at normal
/// sizes and invisible when the ink matches what is behind it. This is a ring in
/// the pen's colour with light and dark edges, so it shows on any background, and
/// a dot in the middle at the true stroke size.
/// </summary>
internal static class PenCursor
{
    private const double MinRadius = 8;
    private const double Clearance = 3;
    private const double Halo = 5;

    public static Cursor Make(Color color, double strokeSize, double scale)
    {
        var bitmap = Draw(color, strokeSize, scale);
        using var file = new MemoryStream(CurFile(bitmap));
        return new Cursor(file);
    }

    /// <summary>The cursor's picture, square, with the pen tip at its centre.</summary>
    public static BitmapSource Draw(Color color, double strokeSize, double scale)
    {
        var radius = Math.Max(MinRadius, strokeSize / 2 + Clearance);
        var side = (int)Math.Ceiling(2 * (radius + Halo) * scale);
        if (side % 2 != 0) side++;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var center = new Point(side / 2.0, side / 2.0);
            var ring = radius * scale;
            var ink = new SolidColorBrush(color);
            dc.DrawEllipse(null, new Pen(Brushes.Black, 5 * scale), center, ring, ring);
            dc.DrawEllipse(null, new Pen(Brushes.White, 3.6 * scale), center, ring, ring);
            dc.DrawEllipse(null, new Pen(ink, 2.2 * scale), center, ring, ring);
            var dot = strokeSize / 2 * scale;
            // The same light and dark edges, outside the dot so a thin stroke's dot is not covered by them.
            dc.DrawEllipse(Brushes.Black, null, center, dot + 1.2 * scale, dot + 1.2 * scale);
            dc.DrawEllipse(Brushes.White, null, center, dot + 0.6 * scale, dot + 0.6 * scale);
            dc.DrawEllipse(ink, null, center, dot, dot);
        }

        var target = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        // Cursor files hold straight alpha, not the premultiplied alpha a render produces.
        return new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
    }

    /// <summary>A .cur file in memory: one 32-bit image with its hot spot at the centre.</summary>
    private static byte[] CurFile(BitmapSource image)
    {
        var (w, h) = (image.PixelWidth, image.PixelHeight);
        var pixels = new byte[w * h * 4];
        image.CopyPixels(pixels, w * 4, 0);
        var maskRow = (w + 31) / 32 * 4;
        var dibSize = 40 + pixels.Length + maskRow * h;

        using var ms = new MemoryStream();
        using var o = new BinaryWriter(ms);

        // File header and the one directory entry.
        o.Write((ushort)0);
        o.Write((ushort)2);
        o.Write((ushort)1);
        o.Write((byte)w);
        o.Write((byte)h);
        o.Write((ushort)0);
        o.Write((ushort)(w / 2));
        o.Write((ushort)(h / 2));
        o.Write(dibSize);
        o.Write(22);

        // The bitmap header counts the colour and mask rows together, hence twice the height.
        o.Write(40);
        o.Write(w);
        o.Write(h * 2);
        o.Write((ushort)1);
        o.Write((ushort)32);
        o.Write(0);
        o.Write(pixels.Length + maskRow * h);
        o.Write(0);
        o.Write(0);
        o.Write(0);
        o.Write(0);

        // Rows run bottom up. The mask stays clear: alpha decides what shows.
        for (var y = h - 1; y >= 0; y--) o.Write(pixels, y * w * 4, w * 4);
        o.Write(new byte[maskRow * h]);
        o.Flush();
        return ms.ToArray();
    }
}
