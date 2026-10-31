using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clio.Core;

namespace Clio;

/// <summary>
/// Pictures, drawings and the note's stacking order.
///
/// Everything on a note sits in one stack (NoteData.Stack): the text, each
/// drawing ("d:" + id) and each picture (its file name). Default, bottom to
/// top: pictures, then drawings, then writing. Bring forward / send backward
/// move an item through that whole stack.
///
/// Moving and restacking are an Advanced Feature (Settings: Advanced Features
/// plus Images). Off, items still show where they are, locked: nothing
/// disappears because a switch was turned off.
///
/// An item behind the text still answers the mouse everywhere except on the
/// letters, which stay with the text; Alt hands it the letters too, and
/// Arrange mode hands it everything.
///
/// Pictures are PNGs in Store.ImagesDir, decoded only at the size they are
/// shown. A drawing is one doodle session's strokes.
/// </summary>
public partial class NoteWindow
{
    /// <summary>Longest side kept on disk. Bigger pastes are scaled down once, on paste.</summary>
    private const int MaxStoredPx = 2000;

    private const double Grip = 14;
    private const double InkPad = 6;
    private const string TextLayer = "text";

    private FrameworkElement? _dragging;
    private Point _grabOffset;
    private Thickness _dragStart;
    private bool _resizing;

    // Multi-select (Advanced Features, the master switch). A selection stays
    // until a click elsewhere on the note or Esc; nothing has to be held.
    private readonly HashSet<Border> _selected = new();
    private Dictionary<Border, Thickness> _dragGroup = new();
    private bool _wasSelected;
    private bool _moved;
    private System.Windows.Shapes.Rectangle? _box;
    private Point _boxStart;

    private static bool Selecting => Store.State.Advanced;

    private static bool Interactive => Store.State.ImagesOn;

    private void LoadItems()
    {
        MigrateInk();
        foreach (var img in Data.Images) AddImageElement(img);
        foreach (var d in Data.Drawings) AddDrawingElement(d);
        ApplyStack();
        ApplyAdvanced();
    }

    /// <summary>An older note keeps all its ink in one blob: that becomes its one drawing.</summary>
    private void MigrateInk()
    {
        if (Data.Ink is not { Length: > 0 } ink || Data.Drawings.Count > 0) return;
        var d = new NoteDrawing { Id = "legacy", Ink = ink };
        Data.Drawings.Add(d);
        var i = Data.Stack.IndexOf("ink");
        if (i >= 0) Data.Stack[i] = "d:" + d.Id;
        Data.Ink = null;
    }

    /// <summary>Settings changed: items lock or unlock, the Arrange button follows.</summary>
    internal void ApplyAdvanced()
    {
        foreach (var frame in Items())
        {
            frame.IsHitTestVisible = Interactive;
            frame.BorderBrush = EdgeFor(frame, hover: false);
        }
        if (!Interactive) Editor.Cursor = null;
        ShowArrange();
    }

    // ------------------------------------------------------------------ stack

    private IEnumerable<Border> Items() =>
        Body.Children.OfType<Border>().Where(b => b.Tag is NoteImage or NoteDrawing);

    private IEnumerable<Border> DrawingFrames() => Items().Where(b => b.Tag is NoteDrawing);

    private static string IdOf(Border frame) => frame.Tag switch
    {
        NoteImage img => img.File,
        NoteDrawing d => "d:" + d.Id,
        _ => "",
    };

    /// <summary>
    /// The stack with anything missing filled in at its default place, and
    /// anything gone (a removed picture, an erased drawing) dropped. Pictures
    /// default to the back; drawings to just under the writing.
    /// </summary>
    private List<string> Stack()
    {
        var pictures = Data.Images.Select(i => i.File).ToList();
        var drawings = Data.Drawings.Select(d => "d:" + d.Id).ToList();
        var known = pictures.Concat(drawings).Append(TextLayer).ToHashSet();

        var s = Data.Stack.Where(known.Contains).Distinct().ToList();
        if (!s.Contains(TextLayer)) s.Add(TextLayer);
        s.InsertRange(0, pictures.Where(p => !s.Contains(p)).ToList());
        foreach (var d in drawings.Where(d => !s.Contains(d)).ToList())
            s.Insert(s.IndexOf(TextLayer), d);

        Data.Stack = s;
        return s;
    }

    /// <summary>Gives every element its place in the stack.</summary>
    private void ApplyStack()
    {
        var s = Stack();
        Panel.SetZIndex(Editor, s.IndexOf(TextLayer));
        Panel.SetZIndex(Placeholder, s.IndexOf(TextLayer));
        foreach (var frame in Items()) Panel.SetZIndex(frame, s.IndexOf(IdOf(frame)));
        // While drawing, the doodle canvas goes over everything, or the text on
        // top takes every stroke (it did, from fd63ea3 until 5e53af4).
        Panel.SetZIndex(InkHost, s.Count + 1);
    }

    private void PutOnTop(string id)
    {
        Data.Stack = Stack().Where(x => x != id).Append(id).ToList();
        ApplyStack();
    }

    // ---------------------------------------------------------------- arrange

    /// <summary>The Arrange button only appears when there is something to arrange and it is allowed.</summary>
    private void ShowArrange()
    {
        var show = Interactive && (Data.Images.Count > 0 || Data.Drawings.Count > 0);
        ArrangeToggle.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show && ArrangeToggle.IsChecked == true) ArrangeToggle.IsChecked = false;
    }

    private bool Arranging => ArrangeToggle.IsChecked == true;

    /// <summary>
    /// Arrange mode: the editor stops taking the mouse so items behind the text
    /// can be grabbed anywhere, and each shows a dashed edge. Esc ends it.
    /// </summary>
    private void Arrange_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (Arranging && DrawToggle.IsChecked == true) SetDrawing(false);

        Editor.IsHitTestVisible = !Arranging;
        foreach (var frame in Items()) frame.BorderBrush = EdgeFor(frame, hover: false);
    }

    private Brush EdgeFor(Border frame, bool hover)
    {
        if (!Interactive) return Brushes.Transparent;
        if (_selected.Contains(frame)) return (Brush)Resources["NoteAccent"];
        if (hover || _dragging == frame) return (Brush)Resources["NoteAccentSoft"];
        return Arranging ? ArrangeEdge : Brushes.Transparent;
    }

    private static readonly Brush ArrangeEdge = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xC9, 0xD8, 0xE6)));

    /// <summary>
    /// An item behind the text still answers the mouse, as if it were in
    /// front, everywhere except on the letters themselves: those still place
    /// the caret, so text over a picture stays editable. Alt hands even the
    /// letters to the item. RTC, 2026-10-31: "I should be able to click the
    /// photo like if it was at front."
    /// </summary>
    private Border? ItemFor(Point inBody, Point inEditor)
    {
        // In Arrange the items take the mouse directly; while drawing, a stroke
        // that starts over an item is a stroke, not a drag.
        if (!Interactive || Arranging || DrawToggle.IsChecked == true) return null;

        var frame = Items()
            .Where(f => f.IsVisible && new Rect(f.TranslatePoint(new Point(), Body), f.RenderSize).Contains(inBody))
            .OrderByDescending(Panel.GetZIndex)
            .FirstOrDefault();
        if (frame is null) return null;

        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        var behindText = Panel.GetZIndex(frame) < Panel.GetZIndex(Editor);
        if (alt || !behindText) return frame;

        return Editor.GetPositionFromPoint(inEditor, snapToText: false) is null ? frame : null;
    }

    private Border? ItemFor(MouseEventArgs e) => ItemFor(e.GetPosition(Body), e.GetPosition(Editor));

    private void PictureGrab(object sender, MouseButtonEventArgs e)
    {
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        if (ItemFor(e) is { } frame)
        {
            PressItem(frame, e, alt);
            e.Handled = true;
            return;
        }

        // Alt+drag over empty space: a selection box.
        if (alt && Selecting && Interactive && !Arranging && DrawToggle.IsChecked != true
            && new Rect(Body.RenderSize).Contains(e.GetPosition(Body)))
        {
            StartBox(e.GetPosition(Body));
            e.Handled = true;
            return;
        }

        // Any other click on the note lets go of the selection.
        if (_selected.Count > 0) ClearSelection();
    }

    /// <summary>
    /// A press on an item. Alt adds it to the selection (or, if it was already
    /// in, takes it out again on release, if it was not dragged). A plain press
    /// on an unselected item selects only that one. Either way a drag moves
    /// every selected item together.
    /// </summary>
    private void PressItem(Border frame, MouseButtonEventArgs e, bool alt)
    {
        _wasSelected = _selected.Contains(frame);
        if (Selecting)
        {
            if (!alt && !_wasSelected) ClearSelection();
            Select(frame);
        }
        BeginDrag(frame, e.GetPosition(frame));
    }

    private void Select(Border frame)
    {
        _selected.Add(frame);
        frame.BorderBrush = EdgeFor(frame, hover: false);
    }

    private void ClearSelection()
    {
        var was = _selected.ToList();
        _selected.Clear();
        foreach (var f in was) f.BorderBrush = EdgeFor(f, hover: false);
    }

    private void StartBox(Point at)
    {
        _boxStart = at;
        _box = new System.Windows.Shapes.Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Stroke = (Brush)Resources["NoteAccent"],
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Fill = (Brush)Resources["NoteAccentSoft"],
            Opacity = 0.6,
            IsHitTestVisible = false,
            Margin = new Thickness(at.X, at.Y, 0, 0),
        };
        Panel.SetZIndex(_box, int.MaxValue);
        Body.Children.Add(_box);
        Mouse.Capture(Body);
    }

    private void MoveBox(Point at)
    {
        if (_box is null) return;
        var r = new Rect(_boxStart, at);
        _box.Margin = new Thickness(r.X, r.Y, 0, 0);
        _box.Width = r.Width;
        _box.Height = r.Height;
    }

    /// <summary>Releasing the box selects whatever it touches, replacing the selection.</summary>
    private void PictureRelease(object sender, MouseButtonEventArgs e)
    {
        if (_box is null) return;
        var r = new Rect(_boxStart, e.GetPosition(Body));
        Body.Children.Remove(_box);
        _box = null;
        Mouse.Capture(null);

        ClearSelection();
        foreach (var f in Items().Where(f => f.IsVisible))
            if (r.IntersectsWith(new Rect(f.TranslatePoint(new Point(), Body), f.RenderSize))) Select(f);
        e.Handled = true;
    }

    /// <summary>Right-click on an item, even one behind the text: its own stacking and remove menu.</summary>
    private void PictureMenuAt(object sender, MouseButtonEventArgs e)
    {
        if (ItemFor(e) is not { ContextMenu: { } menu } frame) return;
        menu.PlacementTarget = frame;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>The move cursor wherever a click would grab an item, so one behind the text does not hide behind an I-beam.</summary>
    private void PictureHover(object sender, MouseEventArgs e)
    {
        if (_box is not null) { MoveBox(e.GetPosition(Body)); return; }
        if (_dragging is not null || Arranging || !Interactive) return;
        Editor.Cursor = ItemFor(e) is not null ? Cursors.SizeAll : null;
    }

    /// <summary>
    /// A lone Alt press puts a window into its title-bar menu mode when it is
    /// released, which takes focus and closed a menu opened with
    /// Alt+right-click before anything could be chosen. Alt combinations
    /// (Alt+F4) are untouched: only Alt by itself is swallowed.
    /// </summary>
    private void SwallowLoneAlt(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt) e.Handled = true;
    }

    // ------------------------------------------------------------------ paste

    /// <summary>
    /// Ctrl+V with a picture on the clipboard, when Images is on. Text wins when
    /// both are there, so copying a paragraph from a web page pastes the words.
    /// </summary>
    private bool TryPastePicture()
    {
        if (!Interactive) return false;

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

        BeforeItemChange();
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
        PutOnTop(name);
        ApplyAdvanced();
        AfterItemChange();
        Changed();
    }

    // --------------------------------------------------------------- elements

    private Border Frame(object tag, double w, double h, Thickness at, UIElement child, string tip) => new()
    {
        Child = child,
        Width = w,
        Height = h,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = at,
        BorderThickness = new Thickness(1),
        BorderBrush = Brushes.Transparent,
        Background = Brushes.Transparent,
        Cursor = Cursors.SizeAll,
        Tag = tag,
        ToolTip = tip,
        IsHitTestVisible = Interactive,
    };

    private void Wire(Border frame)
    {
        frame.MouseEnter += (_, _) => frame.BorderBrush = EdgeFor(frame, hover: true);
        frame.MouseLeave += (_, _) => frame.BorderBrush = EdgeFor(frame, hover: false);
        frame.MouseMove += Item_Move;
        frame.MouseLeftButtonDown += Item_Down;
        frame.MouseLeftButtonUp += Item_Up;
        frame.ContextMenu = ItemMenu(frame);
        Body.Children.Add(frame);
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
        Wire(Frame(img, img.W, img.H, new Thickness(img.X, img.Y, 0, 0), picture,
            "Drag to move, drag the corner to resize, right-click to restack or remove."));
    }

    /// <summary>
    /// A drawing as an item: its strokes in an InkPresenter, inside a frame the
    /// size of their bounds, so it can be grabbed anywhere within that box.
    /// </summary>
    private void AddDrawingElement(NoteDrawing d)
    {
        var strokes = LoadStrokes(d);
        if (strokes.Count == 0) return;
        var b = strokes.GetBounds();

        var ink = new InkPresenter
        {
            Strokes = strokes,
            RenderTransform = new TranslateTransform(-b.X + InkPad, -b.Y + InkPad),
            IsHitTestVisible = false,
        };
        Wire(Frame(d, b.Width + 2 * InkPad, b.Height + 2 * InkPad,
            new Thickness(b.X - InkPad, b.Y - InkPad, 0, 0), ink,
            "Drag to move, right-click to restack or delete."));
    }

    /// <summary>A drawing's strokes in body coordinates, with any drag offset applied.</summary>
    private static StrokeCollection LoadStrokes(NoteDrawing d)
    {
        try
        {
            using var ms = new MemoryStream(Convert.FromBase64String(d.Ink));
            var strokes = new StrokeCollection(ms);
            if (d.X != 0 || d.Y != 0)
            {
                var m = Matrix.Identity;
                m.Translate(d.X, d.Y);
                strokes.Transform(m, applyToStylusTip: false);
            }
            return strokes;
        }
        catch (Exception ex)
        {
            Log.Error($"drawing {d.Id} unreadable", ex);
            return new StrokeCollection();
        }
    }

    private void RebuildDrawings()
    {
        foreach (var f in DrawingFrames().ToList()) Body.Children.Remove(f);
        foreach (var d in Data.Drawings) AddDrawingElement(d);
        ApplyStack();
        ApplyAdvanced();
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

    /// <summary>The items a menu acts on: the whole selection if this item is part of one.</summary>
    private List<Border> Targets(Border frame) =>
        _selected.Contains(frame) && _selected.Count > 1 ? _selected.ToList() : new List<Border> { frame };

    /// <summary>
    /// Restacks several items at once, keeping their order among themselves.
    /// To front and to back move the group to the end; forward and backward
    /// move each one step, the leading one first so they do not collide.
    /// </summary>
    private void RestackAll(List<Border> frames, string how)
    {
        var s = Stack();
        var ids = frames.Select(IdOf).OrderBy(s.IndexOf).ToList();
        if (how is "front" or "back")
        {
            s = s.Where(x => !ids.Contains(x)).ToList();
            if (how == "front") s.AddRange(ids); else s.InsertRange(0, ids);
        }
        else
        {
            var up = how == "forward";
            foreach (var id in up ? ids.AsEnumerable().Reverse() : ids)
            {
                var i = s.IndexOf(id);
                var j = up ? i + 1 : i - 1;
                if (j < 0 || j >= s.Count || ids.Contains(s[j])) continue;
                (s[i], s[j]) = (s[j], s[i]);
            }
        }
        if (s.SequenceEqual(Stack())) return;
        Data.Stack = s;
        ApplyStack();
        AfterItemChange();
        Changed();
    }

    private ContextMenu ItemMenu(Border frame)
    {
        var menu = new ContextMenu();
        MenuItem Order(string header, string how)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => RestackAll(Targets(frame), how);
            return item;
        }

        // Through the whole stack: one step at a time crosses the other items
        // and the text, and to back puts the item under everything.
        var remove = new MenuItem();
        var forward = Order("Bring forward", "forward");
        var front = Order("Bring to front", "front");
        var backward = Order("Send backward", "backward");
        var back = Order("Send to back", "back");
        menu.Items.Add(forward);
        menu.Items.Add(front);
        menu.Items.Add(backward);
        menu.Items.Add(back);
        menu.Items.Add(new Separator());

        // Greyed out when there is nowhere to go: already on top, or at the bottom.
        menu.Opened += (_, _) =>
        {
            var s = Stack();
            var idx = Targets(frame).Select(f => s.IndexOf(IdOf(f))).ToList();
            forward.IsEnabled = front.IsEnabled = idx.Max() < s.Count - 1 || idx.Count > 1 && idx.Max() - idx.Min() >= idx.Count;
            backward.IsEnabled = back.IsEnabled = idx.Min() > 0 || idx.Count > 1 && idx.Max() - idx.Min() >= idx.Count;
            remove.Header = Targets(frame).Count > 1 ? $"Delete {Targets(frame).Count} items"
                          : frame.Tag is NoteDrawing ? "Delete drawing" : "Remove picture";
        };

        remove.Click += (_, _) =>
        {
            foreach (var f in Targets(frame))
            {
                Body.Children.Remove(f);
                _selected.Remove(f);
                Data.Stack.Remove(IdOf(f));
                // A picture's file stays: undo can bring it back. Files nothing
                // refers to any more are swept when Clio next starts.
                if (f.Tag is NoteImage img) Data.Images.Remove(img);
                else if (f.Tag is NoteDrawing d) Data.Drawings.Remove(d);
            }
            ShowArrange();
            AfterItemChange();
            Changed();
        };
        menu.Items.Add(remove);
        return menu;
    }

    // ------------------------------------------------------------- dragging

    private void Item_Down(object sender, MouseButtonEventArgs e)
    {
        PressItem((Border)sender, e, (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
        e.Handled = true;   // do not also place the text caret underneath
    }

    private void BeginDrag(Border frame, Point at)
    {
        // Only pictures resize; a drawing is moved, not scaled.
        _resizing = frame.Tag is NoteImage && at.X >= frame.ActualWidth - Grip && at.Y >= frame.ActualHeight - Grip;
        _grabOffset = _resizing ? new Point(frame.ActualWidth - at.X, frame.ActualHeight - at.Y) : at;
        _dragStart = frame.Margin;
        _dragging = frame;
        _moved = false;
        // Dragging one selected item moves the whole selection with it.
        var group = _selected.Contains(frame) && !_resizing ? _selected.ToList() : new List<Border> { frame };
        _dragGroup = group.ToDictionary(f => f, f => f.Margin);
        BeforeItemChange();
        frame.BorderBrush = EdgeFor(frame, hover: true);
        frame.CaptureMouse();
    }

    private void Item_Move(object sender, MouseEventArgs e)
    {
        var frame = (Border)sender;
        if (_dragging != frame)
        {
            // Hover: a resize cursor over a picture's corner, a move cursor elsewhere.
            var p = e.GetPosition(frame);
            frame.Cursor = frame.Tag is NoteImage && p.X >= frame.ActualWidth - Grip && p.Y >= frame.ActualHeight - Grip
                ? Cursors.SizeNWSE
                : Cursors.SizeAll;
            return;
        }

        var pos = e.GetPosition(Body);
        if (_resizing && frame.Tag is NoteImage img)
        {
            // Keep the picture's shape: width follows the pointer, height follows width.
            var aspect = img.H / Math.Max(1, img.W);
            var w = Math.Clamp(pos.X + _grabOffset.X - img.X, 24, Math.Max(24, Body.ActualWidth - img.X));
            img.W = w;
            img.H = w * aspect;
            frame.Width = img.W;
            frame.Height = img.H;
            return;
        }

        var left = Math.Clamp(pos.X - _grabOffset.X, -frame.ActualWidth + 24, Body.ActualWidth - 24);
        var top = Math.Clamp(pos.Y - _grabOffset.Y, -frame.ActualHeight + 24, Body.ActualHeight - 24);
        var dx = left - _dragStart.Left;
        var dy = top - _dragStart.Top;
        if (dx != 0 || dy != 0) _moved = true;
        foreach (var (f, start) in _dragGroup)
            f.Margin = new Thickness(start.Left + dx, start.Top + dy, 0, 0);
    }

    private void Item_Up(object sender, MouseButtonEventArgs e)
    {
        var frame = (Border)sender;
        if (_dragging != frame) return;
        frame.ReleaseMouseCapture();
        _dragging = null;
        frame.BorderBrush = EdgeFor(frame, hover: frame.IsMouseOver);

        var dx = frame.Margin.Left - _dragStart.Left;
        var dy = frame.Margin.Top - _dragStart.Top;
        if (_resizing && frame.Tag is NoteImage resized && frame.Child is Image picture)
        {
            // Decode again at the new size: sharp if it grew, smaller if it shrank.
            picture.Source = Decode(Path.Combine(Store.ImagesDir, resized.File), resized.W);
            _moved = true;
        }
        else
        {
            foreach (var f in _dragGroup.Keys)
            {
                switch (f.Tag)
                {
                    case NoteImage img:
                        img.X = f.Margin.Left;
                        img.Y = f.Margin.Top;
                        break;
                    case NoteDrawing d:
                        d.X += dx;
                        d.Y += dy;
                        break;
                }
            }
        }

        // Alt+click on an item that was already selected, without dragging it:
        // take it out of the selection.
        if (!_moved && _wasSelected && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            _selected.Remove(frame);
            frame.BorderBrush = EdgeFor(frame, hover: frame.IsMouseOver);
        }
        _dragGroup = new();
        _resizing = false;
        // A click that did not move or resize anything is not an undo step.
        if (_moved) AfterItemChange();
        Changed();
    }

    private void Changed()
    {
        Data.Edited = DateTime.Now;
        RefreshMeta();
        Store.SaveSoon();
    }
}
