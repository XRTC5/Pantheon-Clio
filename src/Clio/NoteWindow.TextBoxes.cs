using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Clio.Core;

namespace Clio;

/// <summary>
/// Free Drag Writing (Advanced Features): text boxes placed anywhere on the
/// note. A click on empty space (not on words, not on an item) starts one
/// there; it is dragged by a thin strip along its top, widened from its
/// bottom-right corner, and right-clicked for cut/copy/paste plus stacking
/// and delete. An empty box tidies itself away when it loses focus.
///
/// Each box is a full rich-text editor: the format bar, the Docs keys,
/// checkboxes, lists and undo act on whichever editor has focus (Ed). Off,
/// existing boxes stay editable but do not move, and no new ones start.
/// </summary>
public partial class NoteWindow
{
    private const double HandleHeight = 7;

    /// <summary>The editor typing goes to: a focused text box, or the note's main text.</summary>
    private RichTextBox? _active;

    private RichTextBox Ed => _active is { IsLoaded: true } a ? a : Editor;

    private static bool Writing => Store.State.WritingOn;

    /// <summary>
    /// What every editor on the note needs: focus tracking, word-sized undo
    /// steps, and the paste guard that keeps pictures out of the text. Box
    /// editors also take the main text's change, selection and checkbox handlers
    /// (the main text has those from its XAML).
    /// </summary>
    private void WireEditor(RichTextBox rtb, bool main)
    {
        if (!main)
        {
            rtb.TextChanged += Editor_TextChanged;
            rtb.SelectionChanged += Editor_SelectionChanged;
            rtb.PreviewMouseLeftButtonDown += Editor_PreviewMouseDown;
        }

        rtb.GotKeyboardFocus += (_, _) => _active = rtb;

        // Word-sized undo steps: once a space or punctuation mark is in, the
        // next keystroke starts a new undo unit instead of merging into this one.
        rtb.PreviewTextInput += (_, e) =>
        {
            if (e.Text.Length > 0 && (char.IsWhiteSpace(e.Text[0]) || char.IsPunctuation(e.Text[0])))
                Dispatcher.BeginInvoke(rtb.LockCurrentUndoUnit);
        };

        // Pasting a picture would put a bitmap in notes.json and in memory for
        // good. Text, with its formatting, is what an editor holds.
        DataObject.AddPastingHandler(rtb, (_, e) =>
        {
            if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText)) e.CancelCommand();
        });
    }

    private void LoadInto(RichTextBox rtb, string? rich, string text)
    {
        _suppress = true;
        try
        {
            var doc = NewDocument();
            rtb.Document = doc;
            if (rich is { } xaml)
            {
                try
                {
                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
                    new TextRange(doc.ContentStart, doc.ContentEnd).Load(ms, DataFormats.Xaml);
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error("text box formatting unreadable; loading plain text", ex);
                }
            }
            SetPlain(doc, text);
        }
        finally
        {
            _suppress = false;
        }
    }

    private static string TextIn(RichTextBox rtb) =>
        new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text.TrimEnd('\r', '\n');

    private static string XamlOf(RichTextBox rtb)
    {
        using var ms = new MemoryStream();
        new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Save(ms, DataFormats.Xaml);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private IEnumerable<Border> BoxFrames() => Items().Where(b => b.Tag is NoteTextBox);

    private static RichTextBox EditorOf(Border frame) => (RichTextBox)((Grid)frame.Child).Children[1];

    /// <summary>Every box's current text into its NoteTextBox, as Flush does for the main text.</summary>
    private void FlushBoxes()
    {
        foreach (var frame in BoxFrames())
        {
            var tb = (NoteTextBox)frame.Tag;
            var rtb = EditorOf(frame);
            tb.Text = TextIn(rtb);
            tb.Rich = XamlOf(rtb);
        }
    }

    private void AddTextBoxElement(NoteTextBox tb)
    {
        var rtb = new RichTextBox
        {
            Style = (Style)FindResource("BoxEditor"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Foreground = (Brush)FindResource("TextHi"),
            AcceptsReturn = true,
            AcceptsTab = true,
            IsUndoEnabled = true,
            UndoLimit = 100,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        rtb.SetResourceReference(RichTextBox.CaretBrushProperty, "NoteAccent");
        rtb.SetResourceReference(RichTextBox.SelectionBrushProperty, "NoteAccent");
        rtb.SelectionOpacity = 0.3;
        System.Windows.Automation.AutomationProperties.SetName(rtb, "Text box");
        LoadInto(rtb, tb.Rich, tb.Text);

        var handle = new Border
        {
            Height = HandleHeight,
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            ToolTip = "Drag to move. Drag the bottom-right corner to change the width.",
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(handle);
        Grid.SetRow(rtb, 1);
        grid.Children.Add(rtb);
        // Width grip, over the text's bottom-right corner (index 2, after the editor).
        var grip = new Border
        {
            Width = 12, Height = 12, Background = Brushes.Transparent, Cursor = Cursors.SizeWE,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        };
        Grid.SetRowSpan(grip, 2);
        grid.Children.Add(grip);

        var frame = new Border
        {
            Child = grid,
            Width = tb.W,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(tb.X, tb.Y, 0, 0),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Tag = tb,
        };

        // The strip shows where to grab, on hover, while the box can move.
        frame.MouseEnter += (_, _) =>
        {
            if (Writing) handle.Background = (Brush)Resources["NoteAccentSoft"];
            frame.BorderBrush = EdgeFor(frame, hover: true);
        };
        frame.MouseLeave += (_, _) =>
        {
            handle.Background = Brushes.Transparent;
            frame.BorderBrush = EdgeFor(frame, hover: false);
        };
        frame.MouseMove += Item_Move;
        frame.MouseLeftButtonUp += Item_Up;
        grip.MouseLeftButtonDown += (_, e) =>
        {
            if (!Writing) return;
            PressItem(frame, e, alt: false);
            e.Handled = true;
        };
        handle.MouseLeftButtonDown += (_, e) =>
        {
            if (!Writing) return;
            PressItem(frame, e, (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
            e.Handled = true;
        };

        // Right-click anywhere in the box: the usual cut/copy/paste, then the
        // box's own stacking and delete.
        var menu = ItemMenu(frame);
        var at = 0;
        foreach (var (header, cmd) in new[] { ("Cut", ApplicationCommands.Cut), ("Copy", ApplicationCommands.Copy), ("Paste", ApplicationCommands.Paste) })
            menu.Items.Insert(at++, new MenuItem { Header = header, Command = cmd, CommandTarget = rtb });
        menu.Items.Insert(at, new Separator());
        rtb.ContextMenu = menu;
        frame.ContextMenu = menu;

        WireEditor(rtb, main: false);
        rtb.LostKeyboardFocus += (_, _) =>
        {
            // An empty box tidies itself away, as in OneNote. Not an undo step:
            // there was nothing in it to bring back.
            if (TextIn(rtb).Trim().Length == 0 && Body.Children.Contains(frame)) DropBox(frame, record: false);
        };

        Body.Children.Add(frame);
    }

    /// <summary>
    /// Free Drag Writing on: the writing already on the note becomes a text box
    /// in the same place, so it drags like any other (RTC: "I should be able
    /// to drag the writing already done around as well"). It stays a box if
    /// the tick goes off again; boxes are always editable.
    ///
    /// Not an undo step, and the main editor's own undo is cleared: undoing
    /// into the emptied main text would have put a second copy back.
    /// </summary>
    private void MainIntoBox()
    {
        if (PlainText().Trim().Length == 0) return;

        var width = Body.ActualWidth > 0 ? Body.ActualWidth : Math.Max(120, Width - 16);
        var tb = new NoteTextBox { Rich = XamlOf(Editor), Text = PlainText(), X = 0, Y = 0, W = width };

        // Same place in the stack the writing had, so nothing jumps in front of it.
        var s = Stack();
        s.Insert(s.IndexOf(TextLayer) + 1, "t:" + tb.Id);
        Data.Stack = s;
        Data.TextBoxes.Add(tb);

        _suppress = true;
        try { SetPlain(Editor.Document, ""); }
        finally { _suppress = false; }
        Editor.IsUndoEnabled = false;   // toggling it off and on empties its stack
        Editor.IsUndoEnabled = true;
        _undo.RemoveAll(st => st is TextStep t && t.Target == Editor);
        Data.Text = "";
        Data.Rich = null;

        AddTextBoxElement(tb);
        ApplyStack();
        _dirty = true;
        RefreshMeta();
        Changed();
    }

    /// <summary>A click on empty space with Free Drag Writing on: a new box, ready to type in.</summary>
    private void StartTextBox(Point at)
    {
        var w = Math.Clamp(Body.ActualWidth - at.X - 4, 60, 220);
        var tb = new NoteTextBox { X = at.X, Y = Math.Max(0, at.Y - HandleHeight - 6), W = w };
        Data.TextBoxes.Add(tb);
        AddTextBoxElement(tb);
        PutOnTop("t:" + tb.Id);
        ApplyAdvanced();

        var frame = BoxFrames().Last();
        Dispatcher.BeginInvoke(() => EditorOf(frame).Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void DropBox(Border frame, bool record)
    {
        var tb = (NoteTextBox)frame.Tag;
        if (_active == EditorOf(frame)) _active = null;
        Body.Children.Remove(frame);
        _selected.Remove(frame);
        Data.TextBoxes.Remove(tb);
        Data.Stack.Remove("t:" + tb.Id);
        if (record) AfterItemChange();
        RefreshMeta();
        Changed();
    }

    /// <summary>Is this point inside an existing text box? Then a click there is typing, not a new box.</summary>
    private bool InsideBox(Point inBody) =>
        BoxFrames().Any(f => new Rect(f.TranslatePoint(new Point(), Body), f.RenderSize).Contains(inBody));
}
