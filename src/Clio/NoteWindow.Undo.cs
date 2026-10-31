using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Threading;
using Clio.Core;

namespace Clio;

/// <summary>One state of a note's content, for undo.</summary>
internal sealed record NoteSnapshot(
    string? Rich, string Text, List<NoteImage> Images, List<NoteDrawing> Drawings, List<string> Stack, int Caret);

/// <summary>
/// A note's undo history. Owned by Notes, not by the window, so it survives
/// the note falling asleep (its window is closed then) and waking again.
/// </summary>
internal sealed class NoteUndo
{
    public const int Depth = 40;
    public readonly LinkedList<NoteSnapshot> Undo = new();
    public readonly Stack<NoteSnapshot> Redo = new();
    public NoteSnapshot? Stable;

    public void Push(NoteSnapshot s)
    {
        Undo.AddLast(s);
        if (Undo.Count > Depth) Undo.RemoveFirst();
    }
}

/// <summary>
/// Undo and redo for everything on a note: text, pictures, drawings and the
/// stacking order. Ctrl+Z, Ctrl+Y (or Ctrl+Shift+Z), or the arrows under the
/// note's REC label.
///
/// Snapshot-based: before each change, the last settled state is pushed.
/// A burst of typing is one step (it settles after a short pause), as in
/// Google Docs; a picture move, a restack, a paste, a removal or a whole
/// doodle session is one step each. Inside a doodle session Ctrl+Z takes back
/// the last stroke instead. The editor's own undo is off, so there is one
/// history, not two that disagree.
/// </summary>
public partial class NoteWindow
{
    private NoteUndo _history = new();
    private bool _burst;
    private bool _restoring;
    private DispatcherTimer? _settle;
    private readonly Stack<Stroke> _sessionStrokes = new();

    private void StartHistory()
    {
        _history = _notes.UndoFor(Data.Id);
        _history.Stable ??= Capture();
    }

    private NoteSnapshot Capture()
    {
        using var ms = new MemoryStream();
        new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Save(ms, DataFormats.Xaml);
        var caret = Editor.Document.ContentStart.GetOffsetToPosition(Editor.CaretPosition);
        return new NoteSnapshot(
            Encoding.UTF8.GetString(ms.ToArray()),
            PlainText(),
            Data.Images.Select(i => new NoteImage { File = i.File, X = i.X, Y = i.Y, W = i.W, H = i.H }).ToList(),
            Data.Drawings.Select(d => new NoteDrawing { Id = d.Id, Ink = d.Ink, X = d.X, Y = d.Y }).ToList(),
            Data.Stack.ToList(),
            caret);
    }

    /// <summary>Typing: the first keystroke of a burst records the state before it.</summary>
    private void TextChangedForUndo()
    {
        if (_restoring) return;
        if (!_burst)
        {
            _burst = true;
            Record(_history.Stable);
        }
        _settle ??= new DispatcherTimer(TimeSpan.FromMilliseconds(900), DispatcherPriority.Background,
            (_, _) => EndBurst(), Dispatcher);
        _settle.Stop();
        _settle.Start();
    }

    private void EndBurst()
    {
        _settle?.Stop();
        if (!_burst) return;
        _burst = false;
        _history.Stable = Capture();
    }

    private void Record(NoteSnapshot? before)
    {
        if (before is null) return;
        _history.Push(before);
        _history.Redo.Clear();
    }

    /// <summary>Before an item change: settle any typing so the step before it is exact.</summary>
    private void BeforeItemChange() => EndBurst();

    /// <summary>After an item change: the settled state before it becomes one undo step.</summary>
    private void AfterItemChange()
    {
        if (_restoring) return;
        Record(_history.Stable);
        _history.Stable = Capture();
    }

    private void Undo()
    {
        if (DrawToggle.IsChecked == true) { UndoStroke(); return; }
        EndBurst();
        if (_history.Undo.Last is not { } last) return;
        _history.Undo.RemoveLast();
        _history.Redo.Push(Capture());
        Restore(last.Value);
    }

    private void Redo()
    {
        if (DrawToggle.IsChecked == true) return;
        EndBurst();
        if (_history.Redo.Count == 0) return;
        _history.Push(Capture());
        Restore(_history.Redo.Pop());
    }

    /// <summary>Inside a doodle session: take back the last stroke drawn in it.</summary>
    private void UndoStroke()
    {
        if (_ink is null) return;
        while (_sessionStrokes.Count > 0)
        {
            var s = _sessionStrokes.Pop();
            if (_ink.Strokes.Contains(s)) { _ink.Strokes.Remove(s); return; }
        }
    }

    private void Restore(NoteSnapshot s)
    {
        _restoring = true;
        try
        {
            Data.Rich = s.Rich;
            Data.Text = s.Text;
            LoadDocument();
            Editor.CaretPosition = Editor.Document.ContentStart.GetPositionAtOffset(s.Caret)
                                   ?? Editor.Document.ContentEnd;

            Data.Images = s.Images.Select(i => new NoteImage { File = i.File, X = i.X, Y = i.Y, W = i.W, H = i.H }).ToList();
            Data.Drawings = s.Drawings.Select(d => new NoteDrawing { Id = d.Id, Ink = d.Ink, X = d.X, Y = d.Y }).ToList();
            Data.Stack = s.Stack.ToList();
            foreach (var f in Items().ToList()) Body.Children.Remove(f);
            foreach (var img in Data.Images) AddImageElement(img);
            foreach (var d in Data.Drawings) AddDrawingElement(d);
            ApplyStack();
            ApplyAdvanced();

            _dirty = true;
            Changed();
            _history.Stable = Capture();
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>The arrows, tucked under the REC label: click the label to show them.</summary>
    private void Serial_Click(object sender, MouseButtonEventArgs e)
    {
        Button Arrow(string glyph, string name, bool enabled, Action act)
        {
            var b = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Content = new TextBlock { Text = glyph, FontSize = 13 },
                IsEnabled = enabled,
                Opacity = enabled ? 1 : 0.35,
                ToolTip = name,
                Margin = new Thickness(2, 0, 2, 0),
            };
            System.Windows.Automation.AutomationProperties.SetName(b, name);
            b.Click += (_, _) => act();
            return b;
        }

        EndBurst();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Arrow("↶", "Undo (Ctrl+Z)", _history.Undo.Count > 0, Undo));
        row.Children.Add(Arrow("↷", "Redo (Ctrl+Y)", _history.Redo.Count > 0, Redo));
        Picker.Open(Serial, row);
        e.Handled = true;   // a click on the label is not a drag of the note
    }
}
