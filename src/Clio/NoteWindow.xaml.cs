using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Clio.Core;

namespace Clio;

/// <summary>
/// One note. Owns its text, its doodle layer and its own erase dissolve;
/// stowing, releasing and creating notes belong to <see cref="Notes"/>.
/// </summary>
public partial class NoteWindow : Window
{
    /// <summary>Note accents. Cyan first: it is Argus's accent.</summary>
    public static readonly Color[] Accents =
    {
        Color.FromRgb(0x22, 0xD3, 0xEE), // cyan
        Color.FromRgb(0xF5, 0xA5, 0x24), // amber
        Color.FromRgb(0xA7, 0x8B, 0xFA), // violet
        Color.FromRgb(0x34, 0xD3, 0x99), // green
        Color.FromRgb(0xFB, 0x71, 0x85), // rose
    };

    /// <summary>Pen colours. A near-white first, so ink reads on every accent.</summary>
    private static readonly Color[] Inks =
    {
        Color.FromRgb(0xE6, 0xF1, 0xFA),
        Color.FromRgb(0x22, 0xD3, 0xEE),
        Color.FromRgb(0xF5, 0xA5, 0x24),
        Color.FromRgb(0xA7, 0x8B, 0xFA),
        Color.FromRgb(0xFB, 0x71, 0x85),
    };

    private const char Open = '☐';   // ☐
    private const char Done = '☑';   // ☑

    private readonly Notes _notes;
    private InkCanvas? _ink;
    private Color _pen = Inks[0];
    private bool _suppress;

    internal NoteData Data { get; }
    internal Placer Placer { get; }

    internal NoteWindow(NoteData data, Notes notes)
    {
        Data = data;
        _notes = notes;
        InitializeComponent();

        Serial.Text = $"REC {data.Number:000}";
        ConfirmTitle.Text = $"ERASE REC {data.Number:000}?";

        _suppress = true;
        Editor.Text = data.Text;
        _suppress = false;
        RefreshMeta();
        ApplyAccent();
        BuildSwatches();

        // Only a note that already carries a drawing pays for an InkCanvas.
        if (data.Ink is not null) EnsureInk();

        Placer = new Placer(this, resizable: true);
        Placer.Attach();
        Placer.MoveTo(data.Rect);
        Placer.Settled += r =>
        {
            Data.Rect = r;
            Store.Save();
        };

        SourceInitialized += (_, _) => Placer.ApplyLayer();
        PreviewKeyDown += OnKey;
    }

    // ---------------------------------------------------------------- look

    private void ApplyAccent()
    {
        var c = Accents[Math.Abs(Data.Color) % Accents.Length];

        Resources["NoteAccent"] = Frozen(new SolidColorBrush(c));
        Resources["NoteAccentSoft"] = Frozen(new SolidColorBrush(Color.FromArgb(0x66, c.R, c.G, c.B)));
        Resources["NoteRule"] = Frozen(new LinearGradientBrush(
            Color.FromArgb(0x99, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B), 0));
        Resources["NoteWash"] = Frozen(new LinearGradientBrush(
            new GradientStopCollection
            {
                new(Color.FromArgb(0x16, c.R, c.G, c.B), 0),
                new(Color.FromArgb(0x00, c.R, c.G, c.B), 0.45),
            }, 90));
    }

    private static T Frozen<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    private void RefreshMeta()
    {
        var t = Data.Edited;
        Stamp.Text = t.Date == DateTime.Today ? t.ToString("HH:mm") : t.ToString("dd MMM").ToUpperInvariant();
        Placeholder.Visibility = Editor.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        Data.Color = (Data.Color + 1) % Accents.Length;
        ApplyAccent();
        Store.SaveSoon();
    }

    // ---------------------------------------------------------------- text

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;

        // "/todo" becomes a checkbox glyph. Replaced through SelectedText so it
        // lands in the undo stack as one edit, not as a wholesale Text reset.
        // Looped, because a paste can carry several at once.
        int at;
        while ((at = Editor.Text.IndexOf("/todo", StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var len = at + 5 < Editor.Text.Length && Editor.Text[at + 5] == ' ' ? 6 : 5;
            _suppress = true;
            Editor.Select(at, len);
            Editor.SelectedText = Open + " ";
            Editor.CaretIndex = at + 2;
            _suppress = false;
        }

        Data.Text = Editor.Text;
        Data.Edited = DateTime.Now;
        RefreshMeta();
        Store.SaveSoon();
    }

    /// <summary>A click on a checkbox glyph ticks it, without moving the caret there.</summary>
    private void Editor_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var i = Editor.GetCharacterIndexFromPoint(e.GetPosition(Editor), snapToText: false);
        if (i < 0 || i >= Editor.Text.Length) return;

        var ch = Editor.Text[i];
        if (ch != Open && ch != Done) return;

        Editor.Select(i, 1);
        Editor.SelectedText = (ch == Open ? Done : Open).ToString();
        Editor.Select(i + 1, 0);
        e.Handled = true;
    }

    // ---------------------------------------------------------------- move

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindParent<ButtonBase>(d) is not null) return;
        try { DragMove(); }
        catch (InvalidOperationException) { /* button already released */ }
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        for (var cur = d; cur is not null; cur = VisualTreeHelper.GetParent(cur))
            if (cur is T hit) return hit;
        return null;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        if (ctrl && e.Key == Key.N) { _notes.NewNote(); e.Handled = true; }
        else if (ctrl && e.Key == Key.D) { SetDrawing(_ink is null || !_ink.IsHitTestVisible); e.Handled = true; }
        else if (e.Key == Key.Escape)
        {
            if (Confirm.Visibility == Visibility.Visible) Confirm.Visibility = Visibility.Collapsed;
            else if (_ink?.IsHitTestVisible == true) SetDrawing(false);
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- ink

    private void EnsureInk()
    {
        if (_ink is not null) return;

        _ink = new InkCanvas { Background = null, IsHitTestVisible = false };

        if (Data.Ink is { } b64)
        {
            try
            {
                using var ms = new MemoryStream(Convert.FromBase64String(b64));
                _ink.Strokes = new StrokeCollection(ms);
            }
            catch (Exception ex)
            {
                Log.Error($"ink for {Data.Id} unreadable", ex);
            }
        }

        _ink.Strokes.StrokesChanged += (_, _) => SaveInk();
        ApplyPen();
        InkHost.Children.Add(_ink);
    }

    private void SaveInk()
    {
        if (_ink is null) return;
        if (_ink.Strokes.Count == 0)
        {
            Data.Ink = null;
        }
        else
        {
            using var ms = new MemoryStream();
            _ink.Strokes.Save(ms, compress: true);
            Data.Ink = Convert.ToBase64String(ms.ToArray());
        }
        Data.Edited = DateTime.Now;
        RefreshMeta();
        Store.SaveSoon();
    }

    private void SetDrawing(bool on)
    {
        if (on) EnsureInk();
        if (_ink is null) return;

        // An InkCanvas with a null background is invisible to the mouse, so
        // the text underneath stays editable; Transparent makes it catch strokes.
        _ink.Background = on ? Brushes.Transparent : null;
        _ink.IsHitTestVisible = on;
        DrawBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        DrawToggle.IsChecked = on;

        if (!on && _ink.Strokes.Count == 0)
        {
            // Nothing was drawn: drop the canvas so this note stays as cheap
            // as a note that was never drawn on.
            InkHost.Children.Clear();
            _ink = null;
            Data.Ink = null;
        }
    }

    private void Draw_Click(object sender, RoutedEventArgs e) => SetDrawing(DrawToggle.IsChecked == true);
    private void Draw_Done(object sender, RoutedEventArgs e) => SetDrawing(false);
    private void ClearInk_Click(object sender, RoutedEventArgs e) => _ink?.Strokes.Clear();

    private void Eraser_Click(object sender, RoutedEventArgs e) => ApplyPen();

    private void BuildSwatches()
    {
        foreach (var c in Inks)
        {
            var b = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Content = new Ellipse { Width = 9, Height = 9, Fill = Frozen(new SolidColorBrush(c)) },
                Tag = c,
            };
            b.Click += (_, _) =>
            {
                _pen = (Color)b.Tag;
                EraserToggle.IsChecked = false;
                ApplyPen();
            };
            Swatches.Children.Add(b);
        }
    }

    private void ApplyPen()
    {
        if (_ink is null) return;
        _ink.EditingMode = EraserToggle.IsChecked == true
            ? InkCanvasEditingMode.EraseByStroke
            : InkCanvasEditingMode.Ink;
        _ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = _pen,
            Width = 2.2,
            Height = 2.2,
            FitToCurve = true,
            IgnorePressure = false,
        };
    }

    // ---------------------------------------------------------------- erase

    private void Erase_Click(object sender, RoutedEventArgs e) => Confirm.Visibility = Visibility.Visible;
    private void Keep_Click(object sender, RoutedEventArgs e) => Confirm.Visibility = Visibility.Collapsed;
    private void ConfirmErase_Click(object sender, RoutedEventArgs e) => _notes.Erase(this);

    /// <summary>
    /// The erase effect. The note is photographed once, the live visual tree is
    /// thrown away, and the photograph is eaten block by block in a downward
    /// scan with a little randomness, each block flaring in the note's accent
    /// just before it goes. Every buffer here is released when the window closes.
    /// </summary>
    internal void Dissolve(Action done)
    {
        Confirm.Visibility = Visibility.Collapsed;
        DrawBar.Visibility = Visibility.Collapsed;
        UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(Shell);
        var pw = Math.Max(1, (int)Math.Ceiling(Shell.ActualWidth * dpi.DpiScaleX));
        var ph = Math.Max(1, (int)Math.Ceiling(Shell.ActualHeight * dpi.DpiScaleY));

        var shot = new RenderTargetBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        shot.Render(Shell);
        var px = new int[pw * ph];
        shot.CopyPixels(px, pw * 4, 0);

        var wb = new WriteableBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
        var whole = new Int32Rect(0, 0, pw, ph);
        wb.WritePixels(whole, px, pw * 4, 0);

        Content = new Image { Source = wb, Stretch = Stretch.Fill, Width = Shell.ActualWidth, Height = Shell.ActualHeight };

        var block = Math.Max(2, (int)Math.Round(4 * dpi.DpiScaleX));
        var bw = (pw + block - 1) / block;
        var bh = (ph + block - 1) / block;
        var n = bw * bh;

        // Order: mostly top to bottom, loosened by noise so the edge is ragged.
        var rnd = new Random();
        var keys = new float[n];
        var order = new int[n];
        for (var i = 0; i < n; i++)
        {
            keys[i] = (i / bw) / (float)bh * 0.6f + rnd.NextSingle() * 0.4f;
            order[i] = i;
        }
        Array.Sort(keys, order);

        var c = Accents[Math.Abs(Data.Color) % Accents.Length];
        const int a = 0xE6;
        var spark = (a << 24) | ((c.R * a / 255) << 16) | ((c.G * a / 255) << 8) | (c.B * a / 255);
        var band = Math.Max(1, n / 12);
        var cleared = 0;

        void Fill(int b, int value)
        {
            var x0 = b % bw * block;
            var y0 = b / bw * block;
            var x1 = Math.Min(pw, x0 + block);
            var y1 = Math.Min(ph, y0 + block);
            for (var y = y0; y < y1; y++)
            {
                var row = y * pw;
                for (var x = x0; x < x1; x++) px[row + x] = value;
            }
        }

        Tween.Run(TimeSpan.FromMilliseconds(720), p =>
        {
            var target = (int)(n * Tween.EaseInOut(p));
            for (var k = cleared; k < target; k++) Fill(order[k], 0);
            var end = Math.Min(n, target + band);
            for (var k = target; k < end; k++)
                if (rnd.Next(5) == 0) Fill(order[k], spark);
            cleared = target;
            wb.WritePixels(whole, px, pw * 4, 0);
        }, done);
    }
}
