using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using Clio.Core;

namespace Clio;

/// <summary>
/// Undo and redo, as in Word and Google Docs: Ctrl+Z, Ctrl+Y (or Ctrl+Shift+Z),
/// or the arrows under the note's REC label.
///
/// One timeline, two kinds of step:
///   Text: the editor's own undo, which keeps the caret where the change was.
///     Typing is cut into word-sized steps (a space or punctuation ends one),
///     so Ctrl+Z takes back the last word, not everything typed since a pause.
///     Formatting, deleting and pasting are steps of their own.
///   Items: a picture or drawing moved, resized, restacked, pasted, removed,
///     or a whole doodle session. These restore only the items, never the text.
///
/// The first version (40f273c) snapshotted the whole note, text included: one
/// Ctrl+Z reverted a burst of typing at once and threw the caret to the start,
/// and the reload raised a text change that wiped the redo stack. RTC: "always
/// deletes the entire thing and goes to the start of the first row."
///
/// Inside a doodle session, Ctrl+Z and Ctrl+Y step through strokes and erases.
/// The history lives with the note's window; a note that has one waits five
/// minutes, not fifteen seconds, before it sleeps (Notes.SleepIdle).
/// </summary>
public partial class NoteWindow
{
    private abstract record Step;
    private sealed record TextStep(RichTextBox Target) : Step;
    private sealed record ItemStep(ItemState State) : Step;
    private sealed record ItemState(List<NoteImage> Images, List<NoteDrawing> Drawings, List<NoteTextBox> Boxes, List<string> Stack, List<List<string>> Groups);
    private sealed record InkStep(StrokeCollection Added, StrokeCollection Removed);

    private const int Depth = 100;
    private readonly List<Step> _undo = new();
    private readonly Stack<Step> _redo = new();
    private ItemState? _stableItems;

    private readonly Stack<InkStep> _inkUndo = new();
    private readonly Stack<InkStep> _inkRedo = new();
    private bool _inkApplying;

    /// <summary>True once there is anything to undo; Notes lets such a note stay awake longer.</summary>
    internal bool HasHistory => _undo.Count > 0 || _redo.Count > 0;

    private void StartHistory()
    {
        _stableItems = CaptureItems();
    }

    private ItemState CaptureItems()
    {
        FlushBoxes();
        return new(
        Data.Images.Select(i => new NoteImage { File = i.File, X = i.X, Y = i.Y, W = i.W, H = i.H }).ToList(),
        Data.Drawings.Select(d => new NoteDrawing { Id = d.Id, Ink = d.Ink, X = d.X, Y = d.Y }).ToList(),
        Data.TextBoxes.Select(t => new NoteTextBox { Id = t.Id, Rich = t.Rich, Text = t.Text, X = t.X, Y = t.Y, W = t.W }).ToList(),
        Data.Stack.ToList(),
        Data.Groups.Select(g => g.ToList()).ToList());
    }

    private void Push(Step s)
    {
        _undo.Add(s);
        if (_undo.Count > Depth) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>Called from TextChanged: a new editor undo unit is one text step on the timeline.</summary>
    private void TextChangedForUndo(RichTextBox rtb, TextChangedEventArgs e)
    {
        if (e.UndoAction == UndoAction.Create) Push(new TextStep(rtb));
    }

    /// <summary>Kept for the call sites; item steps need nothing before the change.</summary>
    private void BeforeItemChange() { }

    /// <summary>After an item change: the item state before it becomes one step.</summary>
    private void AfterItemChange()
    {
        if (_stableItems is not null) Push(new ItemStep(_stableItems));
        _stableItems = CaptureItems();
    }

    private void Undo()
    {
        if (DrawToggle.IsChecked == true) { InkUndo(); return; }
        while (_undo.Count > 0)
        {
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);

        switch (step)
        {
            case TextStep t when t.Target.IsLoaded && t.Target.CanUndo:
                t.Target.Undo();
                _redo.Push(step);
                Editor.Focus();
                return;
            case ItemStep item:
                _redo.Push(new ItemStep(CaptureItems()));
                RestoreItems(item.State);
                return;
        }
        }
    }

    private void Redo()
    {
        if (DrawToggle.IsChecked == true) { InkRedo(); return; }
        while (_redo.Count > 0)
        {
        var step = _redo.Pop();

        switch (step)
        {
            case TextStep t when t.Target.IsLoaded && t.Target.CanRedo:
                t.Target.Redo();
                _undo.Add(step);
                return;
            case ItemStep item:
                _undo.Add(new ItemStep(CaptureItems()));
                RestoreItems(item.State);
                return;
        }
        }
    }

    private void RestoreItems(ItemState s)
    {
        Data.Images = s.Images.Select(i => new NoteImage { File = i.File, X = i.X, Y = i.Y, W = i.W, H = i.H }).ToList();
        Data.Drawings = s.Drawings.Select(d => new NoteDrawing { Id = d.Id, Ink = d.Ink, X = d.X, Y = d.Y }).ToList();
        Data.TextBoxes = s.Boxes.Select(t => new NoteTextBox { Id = t.Id, Rich = t.Rich, Text = t.Text, X = t.X, Y = t.Y, W = t.W }).ToList();
        Data.Stack = s.Stack.ToList();
        Data.Groups = s.Groups.Select(g => g.ToList()).ToList();
        ClearSelection();
        _active = null;
        foreach (var f in Items().ToList()) Body.Children.Remove(f);
        foreach (var img in Data.Images) AddImageElement(img);
        foreach (var d in Data.Drawings) AddDrawingElement(d);
        foreach (var tb in Data.TextBoxes) AddTextBoxElement(tb);
        ApplyStack();
        ApplyAdvanced();
        _stableItems = CaptureItems();
        Changed();
    }

    // ------------------------------------------------------- doodle sessions

    /// <summary>Every stroke drawn or erased in a session is one step, so an erase can be undone too.</summary>
    private void WatchSession(InkCanvas ink)
    {
        _inkUndo.Clear();
        _inkRedo.Clear();
        ink.Strokes.StrokesChanged += (_, e) =>
        {
            if (_inkApplying) return;
            _inkUndo.Push(new InkStep(new StrokeCollection(e.Added), new StrokeCollection(e.Removed)));
            _inkRedo.Clear();
        };
    }

    private void InkUndo()
    {
        if (_ink is null || _inkUndo.Count == 0) return;
        var step = _inkUndo.Pop();
        ApplyInk(remove: step.Added, add: step.Removed);
        _inkRedo.Push(step);
    }

    private void InkRedo()
    {
        if (_ink is null || _inkRedo.Count == 0) return;
        var step = _inkRedo.Pop();
        ApplyInk(remove: step.Removed, add: step.Added);
        _inkUndo.Push(step);
    }

    private void ApplyInk(StrokeCollection remove, StrokeCollection add)
    {
        _inkApplying = true;
        try
        {
            foreach (var s in remove) if (_ink!.Strokes.Contains(s)) _ink.Strokes.Remove(s);
            foreach (var s in add) if (!_ink!.Strokes.Contains(s)) _ink.Strokes.Add(s);
        }
        finally
        {
            _inkApplying = false;
        }
    }

    // --------------------------------------------------------------- arrows

    /// <summary>
    /// The arrows, tucked under the REC label. Opened on release, not press:
    /// opened on press, the release that followed counted as a click outside
    /// the popup and closed it before an arrow could be reached.
    /// </summary>
    private void Serial_Click(object sender, MouseButtonEventArgs e)
    {
        bool CanUndo() => DrawToggle.IsChecked == true ? _inkUndo.Count > 0 : _undo.Count > 0;
        bool CanRedo() => DrawToggle.IsChecked == true ? _inkRedo.Count > 0 : _redo.Count > 0;

        Button? undo = null, redo = null;
        void Refresh()
        {
            foreach (var (b, ok) in new[] { (undo!, CanUndo()), (redo!, CanRedo()) })
            {
                b.IsEnabled = ok;
                b.Opacity = ok ? 1 : 0.35;
            }
        }

        Button Arrow(string glyph, string name, Action act)
        {
            var b = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Content = new TextBlock { Text = glyph, FontSize = 13 },
                ToolTip = name,
                Margin = new Thickness(2, 0, 2, 0),
            };
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            // The popup stays open so several steps can be taken in a row.
            b.Click += (_, _) => { act(); Refresh(); };
            return b;
        }

        undo = Arrow("↶", "Undo (Ctrl+Z)", Undo);
        redo = Arrow("↷", "Redo (Ctrl+Y)", Redo);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(undo);
        row.Children.Add(redo);
        Refresh();
        Picker.Open(Serial, row);
        e.Handled = true;
    }
}
