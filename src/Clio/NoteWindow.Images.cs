using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clio.Core;

namespace Clio;

/// <summary>
/// Pasted pictures: free-floating over the note body, dragged anywhere, resized
/// from the bottom-right corner (keeping their shape), removed from a
/// right-click menu. Each is a PNG in Store.ImagesDir; the note keeps only its
/// file name, position and size, so notes.json stays small.
///
/// Memory: a picture is decoded at the size it is shown, not at the size it was
/// pasted. A 4K screenshot shown 200 wide costs a 200-wide bitmap.
/// </summary>
public partial class NoteWindow
{
    /// <summary>Longest side kept on disk. Bigger pastes are scaled down once, on paste.</summary>
    private const int MaxStoredPx = 2000;

    private const double Grip = 14;

    private FrameworkElement? _dragging;
    private Point _grabOffset;
    private bool _resizing;

    private void LoadImages()
    {
        foreach (var img in Data.Images) AddImageElement(img);
    }

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
        var maxW = Math.Max(60, ImageLayer.ActualWidth * 0.8);
        var k2 = natW > maxW ? maxW / natW : 1;
        var step = 16 * (Data.Images.Count % 6);

        var img = new NoteImage { File = name, X = 8 + step, Y = 8 + step, W = natW * k2, H = natH * k2 };
        Data.Images.Add(img);
        AddImageElement(img);
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
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            Tag = img,
            ToolTip = "Drag to move. Drag the corner to resize. Right-click to remove.",
        };
        Canvas.SetLeft(frame, img.X);
        Canvas.SetTop(frame, img.Y);

        frame.MouseEnter += (_, _) => frame.BorderBrush = (Brush)Resources["NoteAccentSoft"];
        frame.MouseLeave += (_, _) => { if (_dragging != frame) frame.BorderBrush = Brushes.Transparent; };
        frame.MouseMove += Picture_Move;
        frame.MouseLeftButtonDown += Picture_Down;
        frame.MouseLeftButtonUp += Picture_Up;
        frame.ContextMenu = PictureMenu(frame);

        ImageLayer.Children.Add(frame);
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

    /// <summary>
    /// Stacking order, as in any drawing app. The order of Data.Images is the
    /// order of the layer's children, so it is saved with the note and comes
    /// back the same after a restart.
    /// </summary>
    private void Restack(Border frame, int to)
    {
        var img = (NoteImage)frame.Tag;
        var from = Data.Images.IndexOf(img);
        to = Math.Clamp(to, 0, Data.Images.Count - 1);
        if (from < 0 || from == to) return;

        Data.Images.RemoveAt(from);
        Data.Images.Insert(to, img);
        ImageLayer.Children.Remove(frame);
        ImageLayer.Children.Insert(to, frame);
        Changed();
    }

    private ContextMenu PictureMenu(Border frame)
    {
        var menu = new ContextMenu();
        MenuItem Order(string header, Func<int, int> target)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => Restack(frame, target(Data.Images.IndexOf((NoteImage)frame.Tag)));
            return item;
        }

        var forward = Order("Bring forward", i => i + 1);
        var front = Order("Bring to front", _ => Data.Images.Count - 1);
        var backward = Order("Send backward", i => i - 1);
        var back = Order("Send to back", _ => 0);
        menu.Items.Add(forward);
        menu.Items.Add(front);
        menu.Items.Add(backward);
        menu.Items.Add(back);
        menu.Items.Add(new Separator());

        // Greyed out when there is nowhere to go: already on top, or at the bottom.
        menu.Opened += (_, _) =>
        {
            var i = Data.Images.IndexOf((NoteImage)frame.Tag);
            forward.IsEnabled = front.IsEnabled = i < Data.Images.Count - 1;
            backward.IsEnabled = back.IsEnabled = i > 0;
        };

        var remove = new MenuItem { Header = "Remove picture" };
        remove.Click += (_, _) =>
        {
            var img = (NoteImage)frame.Tag;
            ImageLayer.Children.Remove(frame);
            Data.Images.Remove(img);
            // The note is still live, so the file is no longer referenced by
            // anything; History keeps only what a note still lists.
            try { File.Delete(Path.Combine(Store.ImagesDir, img.File)); } catch { }
            Changed();
        };
        menu.Items.Add(remove);
        return menu;
    }

    private void Picture_Down(object sender, MouseButtonEventArgs e)
    {
        var frame = (Border)sender;
        var at = e.GetPosition(frame);
        _resizing = at.X >= frame.ActualWidth - Grip && at.Y >= frame.ActualHeight - Grip;
        _grabOffset = _resizing ? new Point(frame.ActualWidth - at.X, frame.ActualHeight - at.Y) : at;
        _dragging = frame;
        frame.CaptureMouse();
        e.Handled = true;   // do not also place the text caret underneath
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
        var pos = e.GetPosition(ImageLayer);
        if (_resizing)
        {
            // Keep the picture's shape: width follows the pointer, height follows width.
            var aspect = img.H / Math.Max(1, img.W);
            var w = Math.Clamp(pos.X + _grabOffset.X - img.X, 24, Math.Max(24, ImageLayer.ActualWidth - img.X));
            img.W = w;
            img.H = w * aspect;
            frame.Width = img.W;
            frame.Height = img.H;
        }
        else
        {
            img.X = Math.Clamp(pos.X - _grabOffset.X, -img.W + 24, ImageLayer.ActualWidth - 24);
            img.Y = Math.Clamp(pos.Y - _grabOffset.Y, -img.H + 24, ImageLayer.ActualHeight - 24);
            Canvas.SetLeft(frame, img.X);
            Canvas.SetTop(frame, img.Y);
        }
    }

    private void Picture_Up(object sender, MouseButtonEventArgs e)
    {
        var frame = (Border)sender;
        if (_dragging != frame) return;
        frame.ReleaseMouseCapture();
        _dragging = null;

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
