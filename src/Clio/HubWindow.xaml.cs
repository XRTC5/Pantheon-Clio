using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Clio.Core;

namespace Clio;

public partial class HubWindow : Window
{
    /// <summary>Logical size; converted to physical pixels per monitor.</summary>
    public const double LogicalWidth = 198, LogicalHeight = 46;

    private readonly Notes _notes;
    private Point? _pressAt;

    internal Placer Placer { get; }

    internal HubWindow(Notes notes)
    {
        _notes = notes;
        InitializeComponent();

        Placer = new Placer(this, resizable: false);
        Placer.Attach();
        Place();
        Placer.Settled += r =>
        {
            Store.State.HubLeft = r.Left;
            Store.State.HubTop = r.Top;
            Store.State.HasHub = true;
            Store.Save();
        };

        SourceInitialized += (_, _) => Placer.ApplyLayer();
    }

    /// <summary>
    /// The remembered spot, or the bottom-right of the primary work area on a
    /// first run or when the remembered spot is on a display that has gone.
    /// </summary>
    private void Place()
    {
        var s = Store.State;
        if (s.HasHub)
        {
            var m = Monitors.Containing(s.HubLeft, s.HubTop);
            var r = Sized(s.HubLeft, s.HubTop, m.Scale);
            if (Monitors.IsReachable(r)) { Placer.MoveTo(r); return; }
        }

        var p = Monitors.Primary;
        var w = (int)(LogicalWidth * p.Scale);
        var h = (int)(LogicalHeight * p.Scale);
        var gap = (int)(16 * p.Scale);
        Placer.MoveTo(Sized(p.WorkArea.Right - w - gap, p.WorkArea.Bottom - h - gap, p.Scale));
    }

    private static Native.RECT Sized(int left, int top, double scale) => new()
    {
        Left = left,
        Top = top,
        Right = left + (int)(LogicalWidth * scale),
        Bottom = top + (int)(LogicalHeight * scale),
    };

    public void Refresh()
    {
        var count = Store.State.Notes.Count;
        var stowed = Store.State.Collapsed;
        var noun = count == 1 ? "RECORD" : "RECORDS";

        Status.Text = count == 0 ? "NO RECORDS" : stowed ? $"{count} {noun} STOWED" : $"{count} {noun} OUT";
        Status.Foreground = (Brush)FindResource(stowed && count > 0 ? "Accent" : "TextLow");

        // Chevron points to where the notes will go: up and out, or down and in.
        Chevron.Data = Geometry.Parse(stowed ? "M 0,4.5 L 4.5,0 L 9,4.5" : "M 0,0 L 4.5,4.5 L 9,0");
        ToggleButton.ToolTip = stowed ? "Release all notes" : "Stow all notes";
    }

    // ------------------------------------------------- click versus drag

    private void Surface_Down(object sender, MouseButtonEventArgs e)
    {
        if (IsOnButton(e)) return;
        _pressAt = e.GetPosition(this);
    }

    private void Surface_Move(object sender, MouseEventArgs e)
    {
        if (_pressAt is not { } at || e.LeftButton != MouseButtonState.Pressed) return;

        var d = e.GetPosition(this) - at;
        if (Math.Abs(d.X) < 4 && Math.Abs(d.Y) < 4) return;

        _pressAt = null;
        try { DragMove(); }
        catch (InvalidOperationException) { /* released mid-gesture */ }
    }

    private void Surface_Up(object sender, MouseButtonEventArgs e)
    {
        if (_pressAt is null) return;
        _pressAt = null;
        _notes.Toggle();
    }

    private void Surface_Menu(object sender, MouseButtonEventArgs e)
    {
        var menu = _notes.Menu();
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static bool IsOnButton(MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is ButtonBase) return true;
        return false;
    }

    private void New_Click(object sender, RoutedEventArgs e) => _notes.NewNote();
    private void Toggle_Click(object sender, RoutedEventArgs e) => _notes.Toggle();
    private void Settings_Click(object sender, RoutedEventArgs e) => _notes.OpenSettings();
}
