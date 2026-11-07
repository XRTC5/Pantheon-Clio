using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Clio;

/// <summary>
/// The Colours+ picker: a honeycomb of hues with pale tints at the middle, vivid
/// colours further out and deep shades at the rim, then a row of greys. Built
/// when a dropdown opens and thrown away with it, so it costs nothing at rest.
/// </summary>
internal static class Palette
{
    private const int Rings = 7;
    private const double CellWidth = 15.6;
    private const double RowStep = 13.5;
    private const double CellHeight = 18;
    private const double GreyGap = 8;

    /// <summary>Every colour on offer, each with its place on the canvas.</summary>
    public static IEnumerable<(double X, double Y, Color Color)> Cells()
    {
        var cx = Rings * CellWidth;
        var cy = Rings * RowStep;
        for (var r = -Rings; r <= Rings; r++)
        {
            for (var q = -Rings; q <= Rings; q++)
            {
                var ring = (Math.Abs(q) + Math.Abs(r) + Math.Abs(q + r)) / 2;
                if (ring > Rings) continue;
                var x = q * CellWidth + r * CellWidth / 2;
                var y = r * RowStep;
                yield return (cx + x, cy + y, HexColor(x, y, ring));
            }
        }

        var cells = 2 * Rings + 1;
        var greyY = 2 * Rings * RowStep + CellHeight + GreyGap;
        for (var i = 0; i < cells; i++)
        {
            var v = (byte)Math.Round(255.0 * i / (cells - 1));
            yield return (i * CellWidth, greyY, Color.FromRgb(v, v, v));
        }
    }

    private static Color HexColor(double x, double y, int ring)
    {
        if (ring == 0) return Colors.White;
        var t = ring / (double)Rings;
        var hue = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        var lightness = 0.93 - 0.72 * t;
        var saturation = 0.55 + 0.45 * Math.Sin(Math.PI * t);
        return FromHsl(hue, saturation, lightness);
    }

    private static Color FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb(Byte(r + m), Byte(g + m), Byte(b + m));

        static byte Byte(double v) => (byte)Math.Round(255 * v);
    }

    /// <summary>The honeycomb. Set its Tag to the popup holding it and a pick closes that popup.</summary>
    public static Canvas Build(FrameworkElement owner, Color? current, Action<Color> pick)
    {
        var canvas = new Canvas
        {
            Width = (2 * Rings + 1) * CellWidth,
            Height = 2 * Rings * RowStep + 2 * CellHeight + GreyGap,
            Margin = new Thickness(4),
        };
        var style = (Style)owner.FindResource("PaletteCell");
        foreach (var (x, y, color) in Cells())
        {
            var fill = new SolidColorBrush(color);
            fill.Freeze();
            var cell = new Button { Style = style, Background = fill };
            AutomationProperties.SetName(cell, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
            if (color == current)
            {
                cell.BorderBrush = Brushes.White;
                Panel.SetZIndex(cell, 1);
            }
            cell.Click += (_, _) =>
            {
                pick(color);
                if (canvas.Tag is Popup popup) popup.IsOpen = false;
            };
            Canvas.SetLeft(cell, x);
            Canvas.SetTop(cell, y);
            canvas.Children.Add(cell);
        }
        return canvas;
    }
}
