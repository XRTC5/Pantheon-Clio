using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clio.Core;

namespace Clio;

/// <summary>
/// Pasted pictures and the note's stacking order.
///
/// Everything on a note sits in one stack (NoteData.Stack): the text, the
/// drawing layer, and each picture. Default, bottom to top: pictures, then
/// drawings, then writing. Bring forward / send backward move a picture
/// through that whole stack, so it can go behind the text or over it.
///
/// A picture behind the text is under the editor, which covers the body and
/// takes every click, so it is reached by Arrange mode (typing pauses, all
/// pictures take the mouse) or by Alt-drag (one move, no mode).
///
/// Each picture is a PNG in Store.ImagesDir; the note keeps its file name,
/// position and size. A picture is decoded at the size it is shown, so a 4K
/// screenshot shown 200 wide costs a 200-wide bitmap.
/// </summary>
public partial class NoteWindow
{
    /// <summary>Longest side kept on disk. Bigger pastes are scaled down once, on paste.</summary>
    private const int MaxStoredPx = 2000;

    private const double Grip = 14;
    private const string TextLayer = "text";
    private const string InkLayer = "ink";

    private FrameworkElement? _dragging;
    private Point _grabOffset;
    private bool _resizing;

    private void LoadImages()
    {
        foreach (var img in Data.Images) AddImageElement(img);
        ApplyStack();
        ShowArrange();
    }

    // ------------------------------------------------------------------ stack

    /// <summary>
    /// The stack with anything missing filled in at its default place: an older
    /// note has no stack, and a picture pasted since the last arrange is not in it.
    /// Unknown entries (a removed picture) are dropped.
    /// </summary>
    private List<string> Stack()
    {
        var files = Data.Images.Select(i => i.File).ToHashSet();
        var s = Data.Stack.Where(id => id is TextLayer or InkLayer || files.Contains(id)).Distinct().ToList();

        // New pictures go to the back, under everything else: the default order.
        var missing = Data.Images.Select(i => i.File).Where(f => !s.Contains(f)).ToList();
        s.InsertRange(0, missing);
        if (!s.Contains(InkLayer)) s.Insert(s.Contains(TextLayer) ? s.IndexOf(TextLayer) : s.Count, InkLayer);
        if (!s.Contains(TextLayer)) s.Add(TextLayer);

        Data.Stack = s;
        return s;
    }

    /// <summary>Gives every element its place in the stack.</summary>
    private void ApplyStack()
    {
        var s = Stack();
        Panel.SetZIndex(Editor, s.IndexOf(TextLayer));
        Panel.SetZIndex(Placeholder, s.IndexOf(TextLayer));
        Panel.SetZIndex(InkHost, s.IndexOf(InkLayer));
        foreach (var frame in Pictures())
            Panel.SetZIndex(frame, s.IndexOf(((NoteImage)frame.Tag).File));
    }

    private IEnumerable<Border> Pictures() => Body.Children.OfType<Border>().Where(b => b.Tag is NoteImage);

    private void Restack(Border frame, Func<int, int, int> target)
    {
        var s = Stack();
        var id = ((NoteImage)frame.Tag).File;
        var from = s.IndexOf(id);
        var to = Math.Clamp(target(from, s.Count), 0, s.Count - 1);
        if (from == to) return;

        s.RemoveAt(from);
        s.Insert(to, id);
        Data.Stack = s;
        ApplyStack();
        Changed();
    }

    // ---------------------------------------------------------------- arrange

    /// <summary>The Arrange button only appears on a note that has something to arrange.</summary>
    private void ShowArrange()
    {
        ArrangeToggle.Visibility = Data.Images.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Data.Images.Count == 0 && ArrangeToggle.IsChecked == true) ArrangeToggle.IsChecked = false;
    }

    private bool Arranging => ArrangeToggle.IsChecked == true;

    /// <summary>
    /// Arrange mode: the editor stops taking the mouse so pictures behind the
    /// text can be grabbed, and every picture shows a dashed edge so it is
    /// clear what can move. Typing comes back when the mode ends.
    /// </summary>
    private void Arrange_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (Arranging && DrawToggle.IsChecked == true) SetDrawing(false);

        Editor.IsHitTestVisible = !Arranging;
        foreach (var frame in Pictures()) frame.BorderBrush = EdgeFor(frame, hover: false);
    }

    private Brush EdgeFor(Border frame, bool hover)
    {
        if (hover || _dragging == frame) return (Brush)Resources["NoteAccentSoft"];
        return Arranging ? ArrangeEdge : Brushes.Transparent;
    }

    private static readonly Brush ArrangeEdge = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xC9, 0xD8, 0xE6)));

    /// <summary>
    /// Alt-drag: grab the topmost picture under the pointer even if the text is
    /// on top of it. Runs before the editor sees the click.
    /// </summary>
    /// <summary>
    /// A picture behind the text still answers the mouse, as if it were in
    /// front, everywhere except on the letters themselves: those still place
    /// the caret, so text over a picture stays editable. Gaps between lines,
    /// margins and empty space go to the picture. Alt hands even the letters
    /// to the picture. RTC's rule, 2026-10-31: "I should be able to click the
    /// photo like if it was at front."
    /// </summary>
    private Border? PictureFor(Point inBody, Point inEditor)
    {
        if (Arranging) return null;   // in Arrange the pictures take the mouse directly

        var frame = Pictures()
            .Where(f => new Rect(f.TranslatePoint(new Point(), Body), f.RenderSize).Contains(inBody))
            .OrderByDescending(Panel.GetZIndex)
            .FirstOrDefault();
        if (frame is null) return null;

        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        var behindText = Panel.GetZIndex(frame) < Panel.GetZIndex(Editor);
        if (alt || !behindText) return frame;

        return Editor.GetPositionFromPoint(inEditor, snapToText: false) is null ? frame : null;
    }

    private Border? PictureFor(MouseEventArgs e) => PictureFor(e.GetPosition(Body), e.GetPosition(Editor));

    private void PictureGrab(object sender, MouseButtonEventArgs e)
    {
        if (PictureFor(e) is not { } frame) return;
        BeginDrag(frame, e.GetPosition(frame));
        e.Handled = true;
    }

    /// <summary>Right-click on a picture, even one behind the text: its own stacking and remove menu.</summary>
    private void PictureMenuAt(object sender, MouseButtonEventArgs e)
    {
        if (PictureFor(e) is not { ContextMenu: { } menu } frame) return;
        menu.PlacementTarget = frame;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>
    /// The move cursor wherever a click would grab a picture, so a picture
    /// behind the text does not hide behind an I-beam.
    /// </summary>
    private void PictureHover(object sender, MouseEventArgs e)
    {
        if (_dragging is not null || Arranging) return;
        Editor.Cursor = PictureFor(e) is not null ? Cursors.SizeAll : null;
    }

    /// <summary>
    /// A lone Alt press puts a window into its title-bar menu mode when it is
    /// released, which takes focus and closed a picture menu opened with
    /// Alt+right-click before anything could be chosen. Alt combinations
    /// (Alt+F4) are untouched: only Alt by itself is swallowed.
    /// </summary>
    private void SwallowLoneAlt(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) e.Handled = true;
    }

    // ------------------------------------------------------------------ paste

    /// <summary>
    /// Ctrl+V with a picture on the clipboard. Text wins when both are there,
    /// so copying a paragraph from a web page still pastes the words.
    /// </summary>
    private bool TryPastePicture()
    {
        BitmapSource? bitmap;
        try
        {
            bitmap = ClipboardPicture();
        }
        catch (Exception ex)
        {
            Log.Warn($"clipboard picture unreadable: {ex.Message}");
            return false;
        }
        if (bitmap is null) return false;

        try
        {
            AddPicture(bitmap);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("picture paste failed", ex);
            return false;
        }
    }

    private static BitmapSource? ClipboardPicture()
    {
        if (Clipboard.ContainsText()) return null;

        // PNG first: it keeps transparency. The plain bitmap format that every
        // app also offers often arrives with its alpha zeroed, which on a
        // layered window would be an invisible picture.
        if (Clipboard.GetData("PNG") is MemoryStream png)
        {
            return BitmapFrame.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        }

        if (Clipboard.ContainsFileDropList())
        {
            foreach (var file in Clipboard.GetFileDropList())
            {
                if (file is null || !IsPicture(file)) continue;
                using var fs = File.OpenRead(file);
                return BitmapFrame.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            }
        }

        // A bare bitmap (Print Screen, many apps): treat it as opaque.
        if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } dib)
            return new FormatConvertedBitmap(dib, PixelFormats.Bgr32, null, 0);

        return null;
    }

    private static bool IsPicture(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";

    private void AddPicture(BitmapSource bitmap)
    {
        // Keep what is stored bounded: a 6000px photo is not worth 100 MB of PNG.
        var longest = Math.Max(bitmap.PixelWidth, bitmap.PixelHeight);
        if (longest > MaxStoredPx)
        {
            var k = (double)MaxStoredPx / longest;
            bitmap = new TransformedBitmap(bitmap, new ScaleTransform(k, k));
        }

        Directory.CreateDirectory(Store.ImagesDir);
        var name = $"{Data.Id}-{Guid.NewGuid().ToString("N")[..8]}.png";
        using (var fs = File.Create(Path.Combine(Store.ImagesDir, name)))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(fs);
        }

        // Shown at its natural size in device-independent units, but never
        // wider than most of the note; cascaded so repeated pastes do not stack.
        var dpi = VisualTreeHelper.GetDpi(this);
        var natW = bitmap.PixelWidth / dpi.DpiScaleX;
        var natH = bitmap.PixelHeight / dpi.DpiScaleY;
        var maxW = Math.Max(60, Body.ActualWidth * 0.8);
        var k2 = natW > maxW ? maxW / natW : 1;
        var step = 16 * (Data.Images.Count % 6);

        var img = new NoteImage { File = name, X = 8 + step, Y = 8 + step, W = natW * k2, H = natH * k2 };
        Data.Images.Add(img);
        AddImageElement(img);

        // A fresh paste lands on top so you can see it; send it back from its
        // right-click menu. Everything already on the note keeps its order.
        Data.Stack = Stack().Where(id => id != name).Append(name).ToList();
        ApplyStack();
        ShowArrange();
        Changed();
    }

    private void AddImageElement(NoteImage img)
    {
        var path = Path.Combine(Store.ImagesDir, img.File);
        if (!File.Exists(path))
        {
            Log.Warn($"picture {img.File} missing; skipped");
            return;
        }

        var picture = new Image { Stretch = Stretch.Fill, Source = Decode(path, img.W) };
        var frame = new Border
        {
            Child = picture,
            Width = img.W,
            Height = img.H,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(img.X, img.Y, 0, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            Tag = img,
            ToolTip = "Drag to move, drag the corner to resize, right-click to restack or remove. "
                    + "Behind the text? Use Arrange, or hold Alt and drag.",
        };

        frame.MouseEnter += (_, _) => frame.BorderBrush = EdgeFor(frame, hover: true);
        frame.MouseLeave += (_, _) => frame.BorderBrush = EdgeFor(frame, hover: false);
        frame.MouseMove += Picture_Move;
        frame.MouseLeftButtonDown += Picture_Down;
        frame.MouseLeftButtonUp += Picture_Up;
        frame.ContextMenu = PictureMenu(frame);

        Body.Children.Add(frame);
    }

    /// <summary>Decoded to the shown width only: the memory cost follows the screen, not the source.</summary>
    private BitmapImage Decode(string path, double shownWidth)
    {
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;   // read now, do not hold the file open
        bmp.DecodePixelWidth = Math.Max(16, (int)Math.Ceiling(shownWidth * scale));
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private ContextMenu PictureMenu(Border frame)
    {
        var menu = new ContextMenu();
        MenuItem Order(string header, Func<int, int, int> target)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => Restack(frame, target);
            return item;
        }

        // Through the whole stack: one step at a time crosses the drawings and
        // the text, and to back puts the picture under everything.
        var forward = Order("Bring forward", (i, _) => i + 1);
        var front = Order("Bring to front", (_, n) => n - 1);
        var backward = Order("Send backward", (i, _) => i - 1);
        var back = Order("Send to back", (_, _) => 0);
        menu.Items.Add(forward);
        menu.Items.Add(front);
        menu.Items.Add(backward);
        menu.Items.Add(back);
        menu.Items.Add(new Separator());

        // Greyed out when there is nowhere to go: already on top, or at the bottom.
        menu.Opened += (_, _) =>
        {
            var s = Stack();
            var i = s.IndexOf(((NoteImage)frame.Tag).File);
            forward.IsEnabled = front.IsEnabled = i < s.Count - 1;
            backward.IsEnabled = back.IsEnabled = i > 0;
        };

        var remove = new MenuItem { Header = "Remove picture" };
        remove.Click += (_, _) =>
        {
            var img = (NoteImage)frame.Tag;
            Body.Children.Remove(frame);
            Data.Images.Remove(img);
            Data.Stack.Remove(img.File);
            // The note is still live, so the file is no longer referenced by
            // anything; History keeps only what a note still lists.
            try { File.Delete(Path.Combine(Store.ImagesDir, img.File)); } catch { }
            ShowArrange();
            Changed();
        };
        menu.Items.Add(remove);
        return menu;
    }

    // ------------------------------------------------------------- dragging

    private void Picture_Down(object sender, MouseButtonEventArgs e)
    {
        BeginDrag((Border)sender, e.GetPosition((Border)sender));
        e.Handled = true;   // do not also place the text caret underneath
    }

    private void BeginDrag(Border frame, Point at)
    {
        _resizing = at.X >= frame.ActualWidth - Grip && at.Y >= frame.ActualHeight - Grip;
        _grabOffset = _resizing ? new Point(frame.ActualWidth - at.X, frame.ActualHeight - at.Y) : at;
        _dragging = frame;
        frame.BorderBrush = EdgeFor(frame, hover: true);
        frame.CaptureMouse();
    }

    private void Picture_Move(object sender, MouseEventArgs e)
    {
        var frame = (Border)sender;
        if (_dragging != frame)
        {
            // Hover: a resize cursor over the corner, a move cursor elsewhere.
            var p = e.GetPosition(frame);
            frame.Cursor = p.X >= frame.ActualWidth - Grip && p.Y >= frame.ActualHeight - Grip
                ? Cursors.SizeNWSE
                : Cursors.SizeAll;
            return;
        }

        var img = (NoteImage)frame.Tag;
        var pos = e.GetPosition(Body);
        if (_resizing)
        {
            // Keep the picture's shape: width follows the pointer, height follows width.
            var aspect = img.H / Math.Max(1, img.W);
            var w = Math.Clamp(pos.X + _grabOffset.X - img.X, 24, Math.Max(24, Body.ActualWidth - img.X));
            img.W = w;
            img.H = w * aspect;
            frame.Width = img.W;
            frame.Height = img.H;
        }
        else
        {
            img.X = Math.Clamp(pos.X - _grabOffset.X, -img.W + 24, Body.ActualWidth - 24);
            img.Y = Math.Clamp(pos.Y - _grabOffset.Y, -img.H + 24, Body.ActualHeight - 24);
            frame.Margin = new Thickness(img.X, img.Y, 0, 0);
        }
    }

    private void Picture_Up(object sender, MouseButtonEventArgs e)
    {
        var frame = (Border)sender;
        if (_dragging != frame) return;
        frame.ReleaseMouseCapture();
        _dragging = null;
        frame.BorderBrush = EdgeFor(frame, hover: frame.IsMouseOver);

        // After a resize, decode again at the new size: sharp if it grew,
        // smaller in memory if it shrank.
        if (_resizing && frame.Child is Image picture)
        {
            var img = (NoteImage)frame.Tag;
            picture.Source = Decode(Path.Combine(Store.ImagesDir, img.File), img.W);
        }
        _resizing = false;
        Changed();
    }

    private void Changed()
    {
        Data.Edited = DateTime.Now;
        RefreshMeta();
        Store.SaveSoon();
    }
}
