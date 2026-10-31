using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Clio.Core;

namespace Clio;

/// <summary>
/// One note. Owns its text, its formatting, its doodle layer and its own erase
/// dissolve; stowing, releasing, creating and History belong to <see cref="Notes"/>.
/// </summary>
public partial class NoteWindow : Window
{
    /// <summary>Note accents. Cyan first: it is Argus's accent.</summary>
    public static readonly (string Name, Color Color)[] Accents =
    {
        ("Cyan", Color.FromRgb(0x22, 0xD3, 0xEE)),
        ("Amber", Color.FromRgb(0xF5, 0xA5, 0x24)),
        ("Violet", Color.FromRgb(0xA7, 0x8B, 0xFA)),
        ("Green", Color.FromRgb(0x34, 0xD3, 0x99)),
        ("Rose", Color.FromRgb(0xFB, 0x71, 0x85)),
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

    private static readonly string[] Fonts =
    {
        "Segoe UI Variable Text", "Segoe UI", "Bahnschrift", "Calibri", "Arial",
        "Georgia", "Cambria", "Cambria Math", "Times New Roman",
        "Cascadia Mono", "Consolas", "Segoe Print", "Ink Free",
    };

    /// <summary>Points, as Docs shows them. WPF sizes are 1/96 inch, so they are converted.</summary>
    private static readonly double[] Sizes = { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36 };
    private const double DefaultPt = 10;
    private static double Px(double pt) => pt * 96 / 72;
    private static double Pt(double px) => Math.Round(px * 72 / 96 * 2) / 2;

    private static readonly (string Title, string Chars)[] Symbols =
    {
        ("MATHS", "± × ÷ = ≠ ≈ ≡ < > ≤ ≥ ∞ √ ∛ ∑ ∏ ∫ ∮ ∂ ∆ ∇ ′ ″ ° ‰ ½ ⅓ ¼ ¾ ⁿ ² ³ ¹ ⁰ ₀ ₁ ₂ ₃"),
        ("LOGIC AND SETS", "∈ ∉ ⊂ ⊃ ⊆ ⊇ ∪ ∩ ∅ ∀ ∃ ¬ ∧ ∨ ⊕ ∴ ∵ ℕ ℤ ℚ ℝ ℂ"),
        ("GREEK", "α β γ δ ε ζ η θ λ μ ν ξ π ρ σ τ φ χ ψ ω Γ Δ Θ Λ Π Σ Φ Ψ Ω"),
        ("ARROWS", "→ ← ↑ ↓ ↔ ↕ ⇒ ⇐ ⇔ ↦ ↗ ↘ ⟶"),
        ("SPECIAL", "• · … — – § ¶ † ‡ © ® ™ € £ $ ¥ ¢ ✓ ✗ ★ ☆ ♥ ⚠ ☐ ☑ № ∼ ¦"),
    };

    private const char Open = '☐';   // ☐
    private const char Done = '☑';   // ☑

    private readonly Notes _notes;
    private InkCanvas? _ink;
    private Color _pen = Inks[0];
    private bool _suppress;
    private bool _dirty;

    /// <summary>
    /// Set while code moves a toggle to match the document. Toggles react to
    /// Checked/Unchecked rather than Click, because UI Automation (screen
    /// readers, and the test driver) flips IsChecked without raising Click;
    /// this keeps the toolbar's own sync from being read as a user press.
    /// </summary>
    private bool _syncing;

    internal NoteData Data { get; }
    internal Placer Placer { get; }

    internal NoteWindow(NoteData data, Notes notes)
    {
        Data = data;
        _notes = notes;
        InitializeComponent();

        Serial.Text = $"REC {data.Number:000}";
        ConfirmTitle.Text = $"ERASE REC {data.Number:000}?";

        LoadDocument();
        RefreshMeta();
        ApplyAccent();
        ApplyClear();


        Placer = new Placer(this, resizable: true) { Pinned = data.Pinned };
        Placer.Attach();
        Placer.MoveTo(data.Rect);

        // After the handle exists and the window sits on its display: pictures
        // are decoded at that display's scale, and before it they would read 96 DPI.
        LoadItems();
        StartHistory();
        Placer.Settled += r =>
        {
            Data.Rect = r;
            Store.Save();
        };

        Store.Flushing += Flush;
        Closed += (_, _) => Store.Flushing -= Flush;

        // Pasting a picture would put a bitmap in notes.json and in memory for
        // good. Text, with its formatting, is what a note holds.
        DataObject.AddPastingHandler(Editor, (_, e) =>
        {
            if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText)) e.CancelCommand();
        });

        // Not on SourceInitialized: Attach has already created the handle, so
        // that event has fired and a handler added now would never run.
        Placer.ApplyLayer();
        ShowPinMark();
        PreviewKeyDown += OnKey;
        // Pictures behind the text still take clicks off the letters (and with
        // Alt, on them too); these run before the editor sees the click.
        PreviewMouseLeftButtonDown += PictureGrab;
        PreviewMouseRightButtonUp += PictureMenuAt;
        PreviewMouseMove += PictureHover;
        PreviewKeyDown += SwallowLoneAlt;
        PreviewKeyUp += SwallowLoneAlt;
        // The text's own menu would open on the same right-click; give way
        // when the click belongs to a picture.
        Editor.ContextMenuOpening += (_, e) =>
        {
            if (ItemFor(Mouse.GetPosition(Body), Mouse.GetPosition(Editor)) is not null)
                e.Handled = true;
        };

        // Anything the user does keeps the note awake (see Notes.SleepIdle).
        PreviewMouseMove += (_, _) => Touch();
        PreviewKeyDown += (_, _) => Touch();
        Activated += (_, _) => Touch();
        Deactivated += (_, _) => Touch();
    }

    // -------------------------------------------------------------- sleep

    /// <summary>Last time the user moved over, typed in, focused or left this note.</summary>
    internal DateTime LastTouched { get; private set; } = DateTime.Now;

    private void Touch() => LastTouched = DateTime.Now;

    private bool _dissolving;

    /// <summary>
    /// Safe to swap for a picture: not focused, not under the mouse, and not
    /// mid-doodle, mid-erase or mid-dissolve, any of which a picture would freeze.
    /// </summary>
    internal bool CanSleep =>
        IsVisible && !IsActive && !IsMouseOver && !_dissolving
        && DrawToggle.IsChecked != true && Confirm.Visibility != Visibility.Visible && !Arranging;

    /// <summary>What a woken note restores so it wakes exactly as it slept.</summary>
    internal (double Scroll, bool Format) View => (Editor.VerticalOffset, FormatToggle.IsChecked == true);

    internal void RestoreView((double Scroll, bool Format) view)
    {
        if (view.Format) FormatToggle.IsChecked = true;
        if (view.Scroll <= 0) return;

        // Needs a layout pass first; Loaded comes before the first frame is
        // drawn, so the woken note never shows the top and then jumps.
        RoutedEventHandler? once = null;
        once = (_, _) =>
        {
            Loaded -= once;
            Editor.ScrollToVerticalOffset(view.Scroll);
        };
        Loaded += once;
    }

    /// <summary>The note as it looks now, premultiplied BGRA at the window's physical size.</summary>
    internal int[]? Snapshot(Native.RECT rect)
    {
        if (!ReferenceEquals(Content, Shell) || rect.Width <= 0 || rect.Height <= 0) return null;

        var dpi = VisualTreeHelper.GetDpi(Shell);
        var shot = new RenderTargetBitmap(rect.Width, rect.Height, dpi.PixelsPerInchX, dpi.PixelsPerInchY,
                                          PixelFormats.Pbgra32);
        shot.Render(Shell);
        var px = new int[rect.Width * rect.Height];
        shot.CopyPixels(px, rect.Width * 4, 0);
        return px;
    }

    // ------------------------------------------------------------- document

    private FlowDocument NewDocument()
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = Px(DefaultPt),
            Foreground = (Brush)FindResource("TextHi"),
        };
        doc.Resources.Add(typeof(Paragraph), new Style(typeof(Paragraph))
        {
            Setters = { new Setter(Block.MarginProperty, new Thickness(0)) },
        });
        doc.Resources.Add(typeof(List), new Style(typeof(List))
        {
            Setters =
            {
                new Setter(Block.MarginProperty, new Thickness(0)),
                new Setter(Block.PaddingProperty, new Thickness(18, 0, 0, 0)),
            },
        });
        return doc;
    }

    private void LoadDocument()
    {
        _suppress = true;
        var doc = NewDocument();
        Editor.Document = doc;

        if (Data.Rich is { } xaml)
        {
            try
            {
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
                new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.Xaml);
            }
            catch (Exception ex)
            {
                // Keep the words even if the formatting is lost.
                Log.Error($"formatting for {Data.Id} unreadable; loading plain text", ex);
                SetPlain(doc, Data.Text);
            }
        }
        else
        {
            // A note from before formatting existed: plain text, one paragraph a line.
            SetPlain(doc, Data.Text);
        }
        _suppress = false;
    }

    private static void SetPlain(FlowDocument doc, string text)
    {
        doc.Blocks.Clear();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            doc.Blocks.Add(new Paragraph(new Run(line)));
    }

    private string PlainText()
    {
        var t = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text;
        return t.TrimEnd('\r', '\n');
    }

    /// <summary>Called by Store just before it writes: the one place XAML is produced.</summary>
    internal void Flush()
    {
        // A save during a doodle session (or Clio closing mid-drawing) keeps
        // the strokes: the canvas is split into drawings now, not only at Done.
        SessionToData();
        if (!_dirty) return;
        _dirty = false;

        Data.Text = PlainText();
        using var ms = new MemoryStream();
        new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Save(ms, DataFormats.Xaml);
        Data.Rich = Encoding.UTF8.GetString(ms.ToArray());
    }

    // ---------------------------------------------------------------- look

    private void ApplyAccent()
    {
        var c = Accents[Math.Abs(Data.Color) % Accents.Length].Color;

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

    /// <summary>The header shows when the note was made; the tooltip adds the last edit.</summary>
    private void RefreshMeta()
    {
        // The year lives in the tooltip: with it, the stamp collides with the
        // header buttons at the default note width.
        Stamp.Text = Data.Created.ToString("d MMM HH:mm").ToUpperInvariant();
        Stamp.ToolTip = $"Created {Data.Created:ddd d MMM yyyy, HH:mm}\nEdited {Data.Edited:ddd d MMM yyyy, HH:mm}";
        var empty = Editor.Document.Blocks.Count <= 1 && PlainText().Length == 0;
        Placeholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Alpha 1 of 255: invisible, but not zero. A layered window lets clicks
    /// fall straight through fully transparent pixels, so a truly clear body
    /// would send a click between two lines of text to whatever is behind.
    /// </summary>
    private static readonly Brush ClearFill = Frozen(new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)));

    /// <summary>
    /// Solid or see-through. See-through clears the body only: plates keep the
    /// header and bottom toolbar solid, and the border and brackets are
    /// untouched. The scanlines and wash shrink to the header with the plate.
    /// </summary>
    private void ApplyClear()
    {
        var clear = Data.Clear;
        Shell.Background = clear ? ClearFill : (Brush)FindResource("NoteBg");
        TopPlate.Visibility = BottomPlate.Visibility = clear ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRowSpan(ScanLayer, clear ? 2 : 4);
        Grid.SetRowSpan(WashLayer, clear ? 2 : 4);
    }

    /// <summary>A padlock, shut and lit in the accent when locked, open and dim when not.</summary>
    private UIElement LockGlyph(bool locked)
    {
        var ink = locked ? (Brush)Resources["NoteAccent"] : (Brush)FindResource("TextMid");
        var shackle = locked
            ? "M 3,6 L 3,4 A 3,3 0 0 1 9,4 L 9,6"
            : "M 3,6 L 3,4 A 3,3 0 0 1 9,4";   // open: the right leg lifted out
        var g = new Grid { Width = 12, Height = 14 };
        g.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(shackle + " M 1,6 L 11,6 L 11,13 L 1,13 Z"),
            Stroke = ink, StrokeThickness = 1.3, StrokeLineJoin = PenLineJoin.Round,
        });
        return g;
    }

    private void ShowPinMark()
    {
        Tick.Visibility = Data.Pinned ? Visibility.Collapsed : Visibility.Visible;
        PinMark.Visibility = Data.Pinned ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        var list = new StackPanel();
        for (var i = 0; i < Accents.Length; i++)
        {
            var index = i;
            var (name, c) = Accents[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Ellipse
            {
                Width = 10, Height = 10, Fill = Frozen(new SolidColorBrush(c)), Margin = new Thickness(0, 0, 8, 0),
            });
            row.Children.Add(new TextBlock
            {
                Text = name, FontSize = 11.5, FontWeight = index == Data.Color ? FontWeights.SemiBold : FontWeights.Normal,
            });
            var pick = Picker.Row(this, row, () =>
            {
                Data.Color = index;
                ApplyAccent();
                Store.SaveSoon();
            });
            System.Windows.Automation.AutomationProperties.SetName(pick, name);
            list.Children.Add(pick);
        }

        // See-through toggle, under the swatches as a lens rather than words, so
        // the menu keeps the panel's glyph-and-rule look. Lit in the note's
        // accent while the note is see-through. Only the middle changes; the
        // border keeps its colour.
        list.Children.Add(new Separator());
        var ink = Data.Clear ? (Brush)Resources["NoteAccent"] : (Brush)FindResource("TextMid");
        var lens = new Grid { Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Center };
        lens.Children.Add(new Ellipse
        {
            Width = 9, Height = 9, StrokeThickness = 1.3, Stroke = ink,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        });
        lens.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 8,8 L 13,13"), StrokeThickness = 1.6, Stroke = ink,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
        var hint = Data.Clear ? "Make solid" : "See-through";
        var clear = Picker.Row(this, lens, () =>
        {
            Data.Clear = !Data.Clear;
            ApplyClear();
            Store.SaveSoon();
        });
        clear.HorizontalContentAlignment = HorizontalAlignment.Center;
        clear.ToolTip = hint;
        System.Windows.Automation.AutomationProperties.SetName(clear, hint);

        var pinHint = Data.Pinned ? "Unlock: let it fall behind other windows" : "Lock this note on top";
        var pin = Picker.Row(this, LockGlyph(Data.Pinned), () =>
        {
            Data.Pinned = !Data.Pinned;
            Placer.Pinned = Data.Pinned;
            Placer.ApplyLayer();
            ShowPinMark();
            Store.SaveSoon();
        });
        pin.HorizontalContentAlignment = HorizontalAlignment.Center;
        pin.ToolTip = pinHint;
        System.Windows.Automation.AutomationProperties.SetName(pin, Data.Pinned ? "Unlock" : "Lock on top");

        var tools = new UniformGrid { Columns = 2 };
        tools.Children.Add(clear);
        tools.Children.Add(pin);
        list.Children.Add(tools);

        Picker.CloseOnPick(list, Picker.Open(ColorButton, list));
    }

    // ---------------------------------------------------------------- text

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;

        ConvertTodos();
        TextChangedForUndo();

        _dirty = true;
        Data.Edited = DateTime.Now;
        RefreshMeta();
        RefreshFormatState();
        Store.SaveSoon();
    }

    /// <summary>
    /// "/todo" becomes a checkbox glyph. Looped, because a paste can carry
    /// several. Done through the TextRange so each lands in the undo stack.
    /// </summary>
    private void ConvertTodos()
    {
        _suppress = true;
        try
        {
            while (Find("/todo") is { } hit)
            {
                if (hit.End.GetTextInRun(LogicalDirection.Forward).StartsWith(' '))
                    hit = new TextRange(hit.Start, hit.End.GetPositionAtOffset(1)!);
                hit.Text = Open + " ";
                Editor.CaretPosition = hit.End;
            }
        }
        finally
        {
            _suppress = false;
        }
    }

    private TextRange? Find(string needle)
    {
        for (var p = Editor.Document.ContentStart; p is not null; p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;
            var run = p.GetTextInRun(LogicalDirection.Forward);
            var at = run.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            var start = p.GetPositionAtOffset(at)!;
            return new TextRange(start, start.GetPositionAtOffset(needle.Length)!);
        }
        return null;
    }

    /// <summary>A click on a checkbox glyph ticks it, without moving the caret there.</summary>
    private void Editor_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var at = e.GetPosition(Editor);
        var p = Editor.GetPositionFromPoint(at, snapToText: false);
        if (p is null) return;

        foreach (var dir in new[] { LogicalDirection.Forward, LogicalDirection.Backward })
        {
            var run = p.GetTextInRun(dir);
            if (run.Length == 0) continue;
            var ch = dir == LogicalDirection.Forward ? run[0] : run[^1];
            if (ch != Open && ch != Done) continue;

            var other = dir == LogicalDirection.Forward ? p.GetPositionAtOffset(1) : p.GetPositionAtOffset(-1);
            if (other is null) continue;
            var glyph = dir == LogicalDirection.Forward ? new TextRange(p, other) : new TextRange(other, p);

            glyph.Text = (ch == Open ? Done : Open).ToString();
            e.Handled = true;
            return;
        }
    }

    // ---------------------------------------------------------- formatting

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        FormatBar.Visibility = FormatToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RefreshFormatState();
    }

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e) => RefreshFormatState();

    /// <summary>The toolbar mirrors whatever the caret or selection is sitting in.</summary>
    private void RefreshFormatState()
    {
        if (FormatBar.Visibility != Visibility.Visible) return;
        var sel = Editor.Selection;
        _syncing = true;

        BoldToggle.IsChecked = sel.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w
                               && w.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight();
        ItalicToggle.IsChecked = sel.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle s && s == FontStyles.Italic;
        UnderlineToggle.IsChecked = HasDecoration(TextDecorationLocation.Underline);
        StrikeToggle.IsChecked = HasDecoration(TextDecorationLocation.Strikethrough);

        FontLabel.Text = sel.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily f
            ? f.Source.Split(',')[0].Trim()
            : "mixed";
        SizeLabel.Text = sel.GetPropertyValue(TextElement.FontSizeProperty) is double px ? Pt(px).ToString("0.#") : "-";
        _syncing = false;
    }

    private bool HasDecoration(TextDecorationLocation where) =>
        Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection c
        && c.Any(d => d.Location == where);

    private void Apply(DependencyProperty property, object value, bool markers = true)
    {
        Editor.Selection.ApplyPropertyValue(property, value);
        if (markers && (property == TextElement.FontSizeProperty || property == TextElement.FontFamilyProperty))
            MatchListMarkers(property, value);
        Editor.Focus();
        _dirty = true;
        Store.SaveSoon();
        RefreshFormatState();
    }

    /// <summary>
    /// A list's "1." or bullet is drawn from the List element's font, not from
    /// the runs inside it (nor from the ListItem: setting it there was tried
    /// and changed nothing), so resizing the text left the numbers at the old
    /// size. Every list the selection touches takes the new size or family;
    /// one list's markers share one size, the last one chosen.
    /// </summary>
    private void MatchListMarkers(DependencyProperty property, object value)
    {
        var end = Editor.Selection.End;
        var done = new HashSet<List>();
        var boxed = new HashSet<Paragraph>();
        for (var p = Editor.Selection.Start; p is not null && p.CompareTo(end) <= 0;
             p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            // A checklist box is a character at the start of the line, so it
            // only followed a resize when the selection happened to include
            // it. Selecting the words after it left the box small.
            if (p.Paragraph is { } para && boxed.Add(para) && Boxed(para))
            {
                var start = para.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
                if (start.GetPositionAtOffset(1) is { } after)
                    new TextRange(start, after).ApplyPropertyValue(property, value);
            }

            if (p.Paragraph?.Parent is ListItem { List: { } list } && done.Add(list))
            {
                list.SetValue(property, value);
                // The marker is drawn inside the list's left padding. At 18 it
                // fits a 10pt "1." but clips a 24pt one to a stray dot.
                if (value is double size)
                    list.Padding = new Thickness(Math.Max(18, size * 1.5), 0, 0, 0);
            }
        }
    }

    private void ToggleBold() =>
        Apply(TextElement.FontWeightProperty, BoldNow() ? FontWeights.Normal : FontWeights.Bold);

    private bool BoldNow() =>
        Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w
        && w.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight();

    private void ToggleItalic() =>
        Apply(TextElement.FontStyleProperty,
            Editor.Selection.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle s && s == FontStyles.Italic
                ? FontStyles.Normal
                : FontStyles.Italic);

    /// <summary>Underline and strikethrough share one collection; toggling one keeps the other.</summary>
    private void ToggleDecoration(TextDecorationLocation where)
    {
        var current = Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        var next = new TextDecorationCollection(current?.Where(d => d.Location != where) ?? Enumerable.Empty<TextDecoration>());
        if (!HasDecoration(where))
            next.Add(where == TextDecorationLocation.Underline ? TextDecorations.Underline[0] : TextDecorations.Strikethrough[0]);
        Apply(Inline.TextDecorationsProperty, next);
    }

    /// <summary>
    /// Superscript and subscript as raised or lowered text at 70% size, which
    /// works in every font. OpenType variants would be prettier but silently do
    /// nothing in a font without them.
    /// </summary>
    private void ToggleBaseline(BaselineAlignment target)
    {
        var sel = Editor.Selection;
        var size = sel.GetPropertyValue(TextElement.FontSizeProperty) is double px ? px : Px(DefaultPt);
        var on = sel.GetPropertyValue(Inline.BaselineAlignmentProperty) is BaselineAlignment b && b == target;

        sel.ApplyPropertyValue(Inline.BaselineAlignmentProperty, on ? BaselineAlignment.Baseline : target);
        // markers: false, or a superscript "2" would shrink its list number too.
        Apply(TextElement.FontSizeProperty, on ? size / 0.7 : size * 0.7, markers: false);
    }

    private void StepSize(int direction)
    {
        var current = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double px ? Pt(px) : DefaultPt;
        var next = direction > 0
            ? Sizes.FirstOrDefault(s => s > current, Sizes[^1])
            : Sizes.LastOrDefault(s => s < current, Sizes[0]);
        Apply(TextElement.FontSizeProperty, Px(next));
    }

    private void Bold_Click(object sender, RoutedEventArgs e) { if (!_syncing) ToggleBold(); }
    private void Italic_Click(object sender, RoutedEventArgs e) { if (!_syncing) ToggleItalic(); }
    private void Underline_Click(object sender, RoutedEventArgs e) { if (!_syncing) ToggleDecoration(TextDecorationLocation.Underline); }
    private void Strike_Click(object sender, RoutedEventArgs e) { if (!_syncing) ToggleDecoration(TextDecorationLocation.Strikethrough); }
    private void Super_Click(object sender, RoutedEventArgs e) => ToggleBaseline(BaselineAlignment.Superscript);
    private void Sub_Click(object sender, RoutedEventArgs e) => ToggleBaseline(BaselineAlignment.Subscript);
    private void Bullets_Click(object sender, RoutedEventArgs e) => Run(EditingCommands.ToggleBullets);
    private void Numbers_Click(object sender, RoutedEventArgs e) => Run(EditingCommands.ToggleNumbering);

    private void Clear_Click(object sender, RoutedEventArgs e) => ClearFormatting();
    private void Checklist_Click(object sender, RoutedEventArgs e) => ToggleChecklist();

    /// <summary>
    /// The Docs checklist: every paragraph the selection touches gets a box at
    /// its start, or, if they all have one already, loses it. Same glyph as
    /// /todo, so a click on it ticks it.
    /// </summary>
    private void ToggleChecklist()
    {
        var paragraphs = new List<Paragraph>();
        var end = Editor.Selection.End;
        for (var p = Editor.Selection.Start; p is not null && p.CompareTo(end) <= 0;
             p = p.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (p.Paragraph is { } para && !paragraphs.Contains(para)) paragraphs.Add(para);
        }
        if (paragraphs.Count == 0) return;

        var remove = paragraphs.All(Boxed);
        foreach (var para in paragraphs)
        {
            if (remove) Unbox(para);
            else if (!Boxed(para)) Box(para);
        }
        Editor.Focus();
    }

    private static string TextOf(Paragraph para) => new TextRange(para.ContentStart, para.ContentEnd).Text;

    private static bool Boxed(Paragraph para)
    {
        var t = TextOf(para);
        return t.Length > 0 && (t[0] == Open || t[0] == Done);
    }

    private static void Box(Paragraph para) =>
        para.ContentStart.GetInsertionPosition(LogicalDirection.Forward).InsertTextInRun(Open + " ");

    /// <summary>Removes the box and the space after it.</summary>
    private static void Unbox(Paragraph para)
    {
        var t = TextOf(para);
        var len = t.Length > 1 && t[1] == ' ' ? 2 : 1;
        var start = para.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        new TextRange(start, start.GetPositionAtOffset(len)!).Text = "";
    }

    /// <summary>
    /// Enter, the Docs way. On a checklist line it carries a fresh unticked
    /// box to the new line; on an empty checklist line or an empty bullet or
    /// numbered item it ends the list instead of adding another. A list item
    /// with text is left to the RichTextBox, which already continues lists.
    /// True when the key was dealt with here.
    /// </summary>
    private bool EnterInList()
    {
        if (!Editor.Selection.IsEmpty || Editor.CaretPosition.Paragraph is not { } para) return false;
        var text = TextOf(para);

        if (Boxed(para))
        {
            Editor.BeginChange();
            try
            {
                if (text[1..].Trim().Length == 0)
                {
                    Unbox(para);
                }
                else
                {
                    EditingCommands.EnterParagraphBreak.Execute(null, Editor);
                    if (Editor.CaretPosition.Paragraph is { } next && !Boxed(next))
                    {
                        Box(next);
                        Editor.CaretPosition = next.ContentStart
                            .GetInsertionPosition(LogicalDirection.Forward)
                            .GetPositionAtOffset(2) ?? next.ContentEnd;
                    }
                }
            }
            finally
            {
                Editor.EndChange();
            }
            return true;
        }

        if (para.Parent is ListItem { List: { } list } && text.Trim().Length == 0)
        {
            // Toggling the list's own kind on an empty item lifts it out of
            // the list; toggling the other kind would convert it instead.
            var numbered = list.MarkerStyle is TextMarkerStyle.Decimal
                or TextMarkerStyle.LowerLatin or TextMarkerStyle.UpperLatin
                or TextMarkerStyle.LowerRoman or TextMarkerStyle.UpperRoman;
            Run(numbered ? EditingCommands.ToggleNumbering : EditingCommands.ToggleBullets);
            return true;
        }

        return false;
    }

    private void ClearFormatting()
    {
        Editor.Selection.ClearAllProperties();
        Editor.Focus();
        _dirty = true;
        Store.SaveSoon();
        RefreshFormatState();
    }

    private void Run(RoutedUICommand command)
    {
        command.Execute(null, Editor);
        Editor.Focus();
    }

    private void Font_Click(object sender, RoutedEventArgs e)
    {
        var list = new StackPanel { MinWidth = 170 };
        foreach (var name in Fonts)
        {
            var family = new FontFamily(name);
            list.Children.Add(Picker.Row(this,
                new TextBlock { Text = name, FontFamily = family, FontSize = 13 },
                () => Apply(TextElement.FontFamilyProperty, family)));
        }
        Picker.CloseOnPick(list, Picker.Open(FontButton, list));
    }

    private void Size_Click(object sender, RoutedEventArgs e)
    {
        var list = new StackPanel { MinWidth = 56 };
        foreach (var pt in Sizes)
        {
            list.Children.Add(Picker.Row(this,
                new TextBlock { Text = pt.ToString("0"), FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 11 },
                () => Apply(TextElement.FontSizeProperty, Px(pt))));
        }
        Picker.CloseOnPick(list, Picker.Open(SizeButton, list));
    }

    /// <summary>A grid of maths and special characters, grouped. Stays open so several can go in at once.</summary>
    private void Symbols_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Width = 264 };
        foreach (var (title, chars) in Symbols)
        {
            panel.Children.Add(Picker.Label(this, title));
            var grid = new UniformGrid { Columns = 10 };
            foreach (var sym in chars.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var b = new Button
                {
                    Style = (Style)FindResource("PickRow"),
                    Content = new TextBlock { Text = sym, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center },
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    ToolTip = $"U+{char.ConvertToUtf32(sym, 0):X4}",
                };
                b.Click += (_, _) => Insert(sym);
                grid.Children.Add(b);
            }
            panel.Children.Add(grid);
        }
        Picker.Open(SymbolButton, panel, maxHeight: 360);
    }

    private void Insert(string text)
    {
        Editor.Selection.Text = text;
        Editor.CaretPosition = Editor.Selection.End;
        Editor.Focus();
    }

    // ---------------------------------------------------------------- keys

    /// <summary>
    /// Google Docs shortcuts. Ctrl+B / I / U and Ctrl+Z / Y are the RichTextBox's
    /// own; these are the rest of the Docs set that WPF does not have, or has on
    /// a different key.
    /// </summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        var ctrl = (mods & ModifierKeys.Control) != 0;
        var shift = (mods & ModifierKeys.Shift) != 0;
        var alt = (mods & ModifierKeys.Alt) != 0;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        bool Handle(Action a) { a(); return true; }

        e.Handled = (ctrl, shift, alt, key) switch
        {
            (true, false, false, Key.N) => Handle(() => _notes.NewNote(this)),
            (true, false, false, Key.Z) => Handle(Undo),
            (true, false, false, Key.Y) => Handle(Redo),
            (true, true, false, Key.Z) => Handle(Redo),
            // A picture on the clipboard becomes a free-floating picture; text
            // falls through to the editor's own paste.
            (true, false, false, Key.V) => TryPastePicture(),
            (true, false, false, Key.D) => Handle(() => SetDrawing(DrawToggle.IsChecked != true)),
            (true, false, false, Key.B) => Handle(ToggleBold),
            (true, false, false, Key.I) => Handle(ToggleItalic),
            (true, false, false, Key.U) => Handle(() => ToggleDecoration(TextDecorationLocation.Underline)),
            (false, true, true, Key.D5) => Handle(() => ToggleDecoration(TextDecorationLocation.Strikethrough)),
            (true, true, false, Key.X) => Handle(() => ToggleDecoration(TextDecorationLocation.Strikethrough)),
            (true, false, false, Key.OemPeriod) => Handle(() => ToggleBaseline(BaselineAlignment.Superscript)),
            (true, false, false, Key.OemComma) => Handle(() => ToggleBaseline(BaselineAlignment.Subscript)),
            (true, true, false, Key.OemPeriod) => Handle(() => StepSize(+1)),
            (true, true, false, Key.OemComma) => Handle(() => StepSize(-1)),
            (true, true, false, Key.D7) => Handle(() => Run(EditingCommands.ToggleNumbering)),
            (true, true, false, Key.D8) => Handle(() => Run(EditingCommands.ToggleBullets)),
            (true, true, false, Key.D9) => Handle(ToggleChecklist),
            (false, false, false, Key.Enter) => EnterInList(),
            (true, true, false, Key.L) => Handle(() => Run(EditingCommands.AlignLeft)),
            (true, true, false, Key.E) => Handle(() => Run(EditingCommands.AlignCenter)),
            (true, true, false, Key.R) => Handle(() => Run(EditingCommands.AlignRight)),
            (true, true, false, Key.J) => Handle(() => Run(EditingCommands.AlignJustify)),
            (true, false, false, Key.Oem5) => Handle(ClearFormatting),
            (false, false, false, Key.Escape) => Handle(Escape),
            _ => false,
        };
    }

    private void Escape()
    {
        if (Confirm.Visibility == Visibility.Visible) Confirm.Visibility = Visibility.Collapsed;
        else if (Arranging) ArrangeToggle.IsChecked = false;
        else if (DrawToggle.IsChecked == true) SetDrawing(false);
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
        for (var cur = d; cur is not null; cur = cur is Visual ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur is T hit) return hit;
        return null;
    }

    private void New_Click(object sender, RoutedEventArgs e) => _notes.NewNote(this);

    // ---------------------------------------------------------------- ink

    /// <summary>Tags each stroke in a doodle session with the drawing it came from.</summary>
    private static readonly Guid DrawingKey = new("6f1c2a40-5b7e-4f7e-9d0a-c110d0a1e5e1");

    /// <summary>Id for the drawing this session's new strokes become, fixed once chosen.</summary>
    private string? _sessionNewId;

    /// <summary>
    /// A doodle session draws on one canvas holding every drawing on the note,
    /// so the eraser reaches old drawings too. Each stroke carries the id of
    /// the drawing it came from; new strokes carry none. The drawing items
    /// are hidden while the session runs.
    /// </summary>
    private void BeginSession()
    {
        if (_ink is not null) return;
        _ink = new InkCanvas { Background = Brushes.Transparent };
        foreach (var d in Data.Drawings)
        {
            foreach (var stroke in LoadStrokes(d))
            {
                stroke.AddPropertyData(DrawingKey, d.Id);
                _ink.Strokes.Add(stroke);
            }
        }
        _sessionNewId = null;
        _sessionStrokes.Clear();
        _ink.StrokeCollected += (_, e) => _sessionStrokes.Push(e.Stroke);
        ApplyPen();
        InkHost.Children.Add(_ink);
        foreach (var f in DrawingFrames()) f.Visibility = Visibility.Hidden;
    }

    /// <summary>
    /// Splits the canvas back into drawings: strokes go home by their tag,
    /// untagged ones form this session's new drawing, and a drawing whose
    /// strokes were all erased is gone. Offsets are folded into the strokes.
    /// Safe to call mid-session (a save while drawing), and again at the end.
    /// </summary>
    private void SessionToData()
    {
        if (_ink is null) return;

        var groups = new Dictionary<string, StrokeCollection>();
        foreach (var stroke in _ink.Strokes)
        {
            var id = stroke.ContainsPropertyData(DrawingKey)
                ? (string)stroke.GetPropertyData(DrawingKey)
                : _sessionNewId ??= Guid.NewGuid().ToString("N")[..8];
            var copy = stroke.Clone();
            if (copy.ContainsPropertyData(DrawingKey)) copy.RemovePropertyData(DrawingKey);
            if (!groups.TryGetValue(id, out var g)) groups[id] = g = new StrokeCollection();
            g.Add(copy);
        }

        static string Isf(StrokeCollection strokes)
        {
            using var ms = new MemoryStream();
            strokes.Save(ms, compress: true);
            return Convert.ToBase64String(ms.ToArray());
        }

        foreach (var d in Data.Drawings.ToList())
        {
            if (groups.Remove(d.Id, out var mine))
            {
                d.Ink = Isf(mine);
                d.X = d.Y = 0;
            }
            else
            {
                Data.Drawings.Remove(d);
                Data.Stack.Remove("d:" + d.Id);
            }
        }
        foreach (var (id, strokes) in groups)
            Data.Drawings.Add(new NoteDrawing { Id = id, Ink = Isf(strokes) });

        Data.Ink = null;
    }

    private void EndSession()
    {
        if (_ink is null) return;
        SessionToData();
        InkHost.Children.Clear();
        _ink = null;
        _sessionNewId = null;
        RebuildDrawings();   // new drawings land just under the writing (Stack)
        // One undo step for the whole session, and none if nothing changed.
        var before = _history.Stable?.Drawings.Select(d => d.Id + d.Ink).ToList() ?? new();
        if (!before.SequenceEqual(Data.Drawings.Select(d => d.Id + d.Ink))) AfterItemChange();
        Changed();
    }

    private void SetDrawing(bool on)
    {
        // Pen swatches are built on first use: most notes are never drawn on.
        if (on && Swatches.Children.Count == 0) BuildSwatches();
        if (on && Arranging) ArrangeToggle.IsChecked = false;   // one mode at a time
        if (on) { BeforeItemChange(); BeginSession(); }
        else EndSession();

        DrawBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _syncing = true;
        DrawToggle.IsChecked = on;
        _syncing = false;
        ApplyStack();
        Editor.Cursor = null;
    }

    private void Draw_Click(object sender, RoutedEventArgs e)
    {
        if (!_syncing) SetDrawing(DrawToggle.IsChecked == true);
    }
    private void Draw_Done(object sender, RoutedEventArgs e) => SetDrawing(false);
    private void ClearInk_Click(object sender, RoutedEventArgs e) => _ink?.Strokes.Clear();

    private void Eraser_Click(object sender, RoutedEventArgs e)
    {
        if (!_syncing) ApplyPen();
    }

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
                _syncing = true;
                EraserToggle.IsChecked = false;
                _syncing = false;
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

    private void Erase_Click(object sender, RoutedEventArgs e)
    {
        var days = Store.State.HistoryDays;
        ConfirmSub.Text = $"Kept in History for {days} day{(days == 1 ? "" : "s")}.";
        Confirm.Visibility = Visibility.Visible;
    }

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
        _dissolving = true;
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

        var c = Accents[Math.Abs(Data.Color) % Accents.Length].Color;
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
