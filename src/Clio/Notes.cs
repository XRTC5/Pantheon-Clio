using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Clio.Core;

namespace Clio;

/// <summary>
/// The set of notes and the hub they stow into.
///
/// A stowed note has no window at all. It is a few hundred bytes of JSON until
/// it is released, which is the whole memory story of Clio: the cost scales
/// with the notes on screen, not with the notes that exist.
/// </summary>
internal sealed class Notes
{
    private const double NoteWidth = 260, NoteHeight = 240;
    private static readonly TimeSpan Fly = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(40);

    private readonly Dictionary<string, NoteWindow> _open = new();
    private readonly HubWindow _hub;
    private bool _animating;

    public Notes()
    {
        _hub = new HubWindow(this);
    }

    public void Start()
    {
        _hub.Refresh();
        _hub.Show();

        if (!Store.State.Collapsed)
            foreach (var d in Store.State.Notes) Open(d).Show();
    }

    // ------------------------------------------------------------- create

    public void NewNote()
    {
        if (_animating) return;

        var s = Store.State;
        if (s.Collapsed && s.Notes.Count > 0)
        {
            Release(then: NewNote);
            return;
        }

        s.Collapsed = false;
        s.Serial++;
        var data = new NoteData { Number = s.Serial, Color = (s.Serial - 1) % NoteWindow.Accents.Length };
        data.Rect = FreshRect();
        s.Notes.Add(data);
        Store.Save();

        var w = Open(data);
        w.Show();
        w.Activate();
        w.Editor.Focus();
        _hub.Refresh();
    }

    /// <summary>
    /// Just above the hub, cascading so a run of new notes does not stack
    /// exactly on top of itself, and clamped inside the hub's display.
    /// </summary>
    private Native.RECT FreshRect()
    {
        var hub = _hub.Placer.Rect;
        var m = Monitors.Containing(hub.Left + hub.Width / 2, hub.Top + hub.Height / 2);
        var s = m.Scale;
        var w = (int)(NoteWidth * s);
        var h = (int)(NoteHeight * s);
        var step = (int)(26 * s) * (_open.Count % 6);

        var left = hub.Right - w - step;
        var top = hub.Top - h - (int)(12 * s) - step;
        if (top < m.WorkArea.Top) top = hub.Bottom + (int)(12 * s) + step;

        left = Math.Clamp(left, m.WorkArea.Left, Math.Max(m.WorkArea.Left, m.WorkArea.Right - w));
        top = Math.Clamp(top, m.WorkArea.Top, Math.Max(m.WorkArea.Top, m.WorkArea.Bottom - h));
        return new Native.RECT { Left = left, Top = top, Right = left + w, Bottom = top + h };
    }

    private NoteWindow Open(NoteData d)
    {
        // A note last left on a display that is no longer attached comes back
        // next to the hub instead of somewhere it cannot be reached.
        if (d.Width < 40 || d.Height < 40 || !Monitors.IsReachable(d.Rect))
            d.Rect = FreshRect();

        var w = new NoteWindow(d, this);
        w.Closed += (_, _) => _open.Remove(d.Id);
        _open[d.Id] = w;
        return w;
    }

    // ------------------------------------------------------------- erase

    public void Erase(NoteWindow w)
    {
        w.Dissolve(() =>
        {
            Store.State.Notes.Remove(w.Data);
            Store.Save();
            w.Close();
            _hub.Refresh();
            TrimSoon();
        });
    }

    // ------------------------------------------------------ stow / release

    public void Toggle()
    {
        if (_animating) return;
        if (Store.State.Notes.Count == 0) { NewNote(); return; }
        if (Store.State.Collapsed) Release();
        else Stow();
    }

    /// <summary>Every note flies into the hub, shrinking and fading, then its window is closed.</summary>
    public void Stow()
    {
        if (_animating) return;
        var windows = _open.Values.ToList();
        Store.State.Collapsed = true;
        Store.Save();
        _hub.Refresh();
        if (windows.Count == 0) return;

        var (hx, hy) = HubCentre();
        var starts = windows.Select(w => w.Placer.Rect).ToList();

        Animate(windows.Count, (i, p) =>
        {
            var e = Tween.EaseIn(p);
            var r = starts[i];
            var dx = hx - (r.Left + r.Width / 2);
            var dy = hy - (r.Top + r.Height / 2);
            windows[i].Placer.MoveBy(r.Left + (int)(dx * e), r.Top + (int)(dy * e));
            windows[i].ShellScale.ScaleX = windows[i].ShellScale.ScaleY = 1 - 0.92 * e;
            windows[i].Shell.Opacity = 1 - e;
        }, () =>
        {
            foreach (var w in windows) w.Close();
            TrimSoon();
        });
    }

    /// <summary>Every note springs back out of the hub to exactly where it was left.</summary>
    public void Release(Action? then = null)
    {
        if (_animating) return;
        Store.State.Collapsed = false;
        Store.Save();
        _hub.Refresh();

        var (hx, hy) = HubCentre();
        var windows = new List<NoteWindow>();
        foreach (var d in Store.State.Notes)
        {
            if (_open.ContainsKey(d.Id)) continue;
            var w = Open(d);
            w.ShellScale.ScaleX = w.ShellScale.ScaleY = 0.08;
            w.Shell.Opacity = 0;
            var r = d.Rect;
            w.Placer.MoveBy(hx - r.Width / 2, hy - r.Height / 2);
            w.Show();
            windows.Add(w);
        }

        if (windows.Count == 0) { then?.Invoke(); return; }

        Animate(windows.Count, (i, p) =>
        {
            var e = Tween.EaseOut(p);
            var r = windows[i].Data.Rect;
            var fromX = hx - r.Width / 2;
            var fromY = hy - r.Height / 2;
            windows[i].Placer.MoveBy(fromX + (int)((r.Left - fromX) * e), fromY + (int)((r.Top - fromY) * e));
            windows[i].ShellScale.ScaleX = windows[i].ShellScale.ScaleY = 0.08 + 0.92 * e;
            windows[i].Shell.Opacity = e;
        }, () => then?.Invoke());
    }

    /// <summary>One clock for a staggered group, so N notes cost one frame callback, not N.</summary>
    private void Animate(int count, Action<int, double> step, Action done)
    {
        _animating = true;
        var total = Fly + Stagger * (count - 1);
        Tween.Run(total, p =>
        {
            var t = p * total.TotalMilliseconds;
            for (var i = 0; i < count; i++)
            {
                var local = Math.Clamp((t - i * Stagger.TotalMilliseconds) / Fly.TotalMilliseconds, 0, 1);
                step(i, local);
            }
        }, () =>
        {
            _animating = false;
            done();
        });
    }

    private (int X, int Y) HubCentre()
    {
        var r = _hub.Placer.Rect;
        return (r.Left + r.Width / 2, r.Top + r.Height / 2);
    }

    // ------------------------------------------------------------- layer

    public void SetLayer(Layer layer)
    {
        Store.State.Layer = layer;
        Store.Save();
        _hub.Placer.ApplyLayer();
        foreach (var w in _open.Values) w.Placer.ApplyLayer();
    }

    // ------------------------------------------------------------- menu

    /// <summary>Built fresh each time it opens: cheaper than keeping one alive all day.</summary>
    public ContextMenu Menu()
    {
        var s = Store.State;
        var menu = new ContextMenu();

        menu.Items.Add(Item("New note", NewNote));
        if (s.Notes.Count > 0)
            menu.Items.Add(Item(s.Collapsed ? "Release all notes" : "Stow all notes", Toggle));
        menu.Items.Add(new Separator());

        var top = Item("Keep notes on top", () => SetLayer(s.Layer == Layer.OnTop ? Layer.Desktop : Layer.OnTop));
        top.IsChecked = s.Layer == Layer.OnTop;
        menu.Items.Add(top);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Quit Clio", Quit));
        return menu;
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    public void Quit()
    {
        Store.Save();
        Application.Current.Shutdown();
    }

    // ------------------------------------------------------------- memory

    private DispatcherTimer? _trim;

    /// <summary>
    /// Closed windows leave their pages mapped until something asks for them
    /// back. A stow or an erase is the moment a lot of memory goes dead at
    /// once, so trim a little after, when the GC has had a chance to run.
    /// </summary>
    public void TrimSoon()
    {
        _trim ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.ApplicationIdle, (_, _) =>
        {
            _trim!.Stop();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Native.TrimWorkingSet();
        }, Dispatcher.CurrentDispatcher);
        _trim.Stop();
        _trim.Start();
    }
}
