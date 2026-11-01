using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Clio.Core;

namespace Clio;

/// <summary>
/// Lasso select and Grouping, the last two Advanced Features ticks.
///
/// Lasso: Alt+Shift+drag over empty space draws a freehand loop; whatever it
/// touches is selected, exactly like the Alt+drag box.
///
/// Grouping: select several items (pictures, drawings, text boxes, any mix),
/// right-click, Group. From then on selecting any member selects the whole
/// group, so they drag, restack and delete as one; Ungroup splits it. Groups
/// are saved (NoteData.Groups, lists of stack ids) and are undo steps. With the
/// tick off, groups are kept but members act on their own.
/// </summary>
public partial class NoteWindow
{
    private Polyline? _lasso;

    private static bool Lasso => Store.State.LassoOn;
    private static bool Grouping => Store.State.GroupsOn;

    /// <summary>Only what can move can be selected: pictures and drawings need Images, boxes need Writing.</summary>
    private static bool CanSelect(Border f) => f.Tag is NoteTextBox ? Writing : Interactive;

    // ------------------------------------------------------------------ lasso

    private void StartLasso(Point at)
    {
        _lasso = new Polyline
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Stroke = (Brush)Resources["NoteAccent"],
            StrokeThickness = 1.2,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Fill = (Brush)Resources["NoteAccentSoft"],
            Opacity = 0.6,
            IsHitTestVisible = false,
        };
        _lasso.Points.Add(at);
        Panel.SetZIndex(_lasso, int.MaxValue);
        Body.Children.Add(_lasso);
        System.Windows.Input.Mouse.Capture(Body);
    }

    private void MoveLasso(Point at)
    {
        // A point every few pixels is plenty and keeps the shape cheap.
        if (_lasso is null) return;
        var last = _lasso.Points[^1];
        if ((at - last).Length >= 3) _lasso.Points.Add(at);
    }

    /// <summary>The loop closes on release; everything it touches is selected.</summary>
    private void EndLasso()
    {
        if (_lasso is null) return;
        var points = _lasso.Points.ToList();
        Body.Children.Remove(_lasso);
        _lasso = null;
        System.Windows.Input.Mouse.Capture(null);
        if (points.Count < 3) return;

        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(points.Skip(1), isStroked: true));
        var loop = new PathGeometry(new[] { figure });

        ClearSelection();
        foreach (var f in Items().Where(f => f.IsVisible && CanSelect(f)))
        {
            var r = new RectangleGeometry(new Rect(f.TranslatePoint(new Point(), Body), f.RenderSize));
            if (loop.FillContainsWithDetail(r) != IntersectionDetail.Empty) Select(f);
        }
    }

    // ----------------------------------------------------------------- groups

    /// <summary>The other members of this item's group, while Grouping is on.</summary>
    private IEnumerable<Border> GroupOf(Border frame)
    {
        if (!Grouping) yield break;
        var id = IdOf(frame);
        var group = Data.Groups.FirstOrDefault(g => g.Contains(id));
        if (group is null) yield break;
        foreach (var f in Items())
            if (f != frame && group.Contains(IdOf(f))) yield return f;
    }

    private bool InGroup(Border frame) => Data.Groups.Any(g => g.Contains(IdOf(frame)));

    private void GroupItems(List<Border> frames)
    {
        var ids = frames.Select(IdOf).ToList();
        // An item is in at most one group: joining a new one leaves the old.
        foreach (var g in Data.Groups) g.RemoveAll(ids.Contains);
        Data.Groups.RemoveAll(g => g.Count < 2);
        Data.Groups.Add(ids);
        AfterItemChange();
        Changed();
    }

    private void UngroupItem(Border frame)
    {
        var id = IdOf(frame);
        Data.Groups.RemoveAll(g => g.Contains(id));
        AfterItemChange();
        Changed();
    }

    /// <summary>Removed items leave their groups; a group of one is no group.</summary>
    private void ForgetInGroups(IEnumerable<string> ids)
    {
        var gone = ids.ToHashSet();
        foreach (var g in Data.Groups) g.RemoveAll(gone.Contains);
        Data.Groups.RemoveAll(g => g.Count < 2);
    }

    /// <summary>Group and Ungroup, added to an item's right-click menu.</summary>
    private void AddGroupItems(ContextMenu menu, Border frame)
    {
        var group = new MenuItem { Header = "Group" };
        var ungroup = new MenuItem { Header = "Ungroup" };
        group.Click += (_, _) => GroupItems(Targets(frame));
        ungroup.Click += (_, _) => UngroupItem(frame);

        var sep = new Separator();
        var at = menu.Items.Count - 1;   // just before the delete item
        menu.Items.Insert(at, sep);
        menu.Items.Insert(at, ungroup);
        menu.Items.Insert(at, group);

        menu.Opened += (_, _) =>
        {
            var show = Grouping ? Visibility.Visible : Visibility.Collapsed;
            group.Visibility = ungroup.Visibility = sep.Visibility = show;
            group.IsEnabled = Targets(frame).Count > 1;
            ungroup.IsEnabled = InGroup(frame);
        };
    }
}
