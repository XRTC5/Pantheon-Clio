using System.Windows;
using System.Windows.Input;
using Clio.Core;

namespace Clio;

public partial class SettingsWindow : Window
{
    /// <summary>Retention steps: a week of single days, then coarser.</summary>
    private static readonly int[] Days = { 1, 2, 3, 4, 5, 6, 7, 10, 14, 21, 30, 60, 90 };

    private readonly Notes _notes;

    /// <summary>Set while Refresh moves the switches, so that is not read as a user press.</summary>
    private bool _syncing;

    internal SettingsWindow(Notes notes)
    {
        _notes = notes;
        InitializeComponent();
        Refresh();

        // What the machine actually has, not just what the file says: a task
        // deleted by hand in Task Scheduler should read as off.
        System.Threading.Tasks.Task.Run(Startup.IsEnabled).ContinueWith(t =>
            Dispatcher.BeginInvoke(() =>
            {
                _syncing = true;
                StartupSwitch.IsChecked = t.Result;
                _syncing = false;
            }));
    }

    internal void Refresh()
    {
        var s = Store.State;
        _syncing = true;
        StartupSwitch.IsChecked = s.RunOnStartup;
        TopSwitch.IsChecked = s.Layer == Layer.OnTop;
        AdvancedSwitch.IsChecked = s.Advanced;
        ImagesSwitch.IsChecked = s.AdvImages;
        // The ticks under the master switch only mean something while it is on.
        AdvancedRows.IsEnabled = s.Advanced;
        AdvancedRows.Opacity = s.Advanced ? 1 : 0.4;
        _syncing = false;
        DaysLabel.Text = s.HistoryDays == 1 ? "1 day" : $"{s.HistoryDays} days";
        HistoryButton.Content = s.History.Count == 0 ? "OPEN HISTORY" : $"OPEN HISTORY  /  {s.History.Count}";
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        var on = StartupSwitch.IsChecked == true;
        Store.State.RunOnStartup = on;
        Store.Save();
        System.Threading.Tasks.Task.Run(() => Startup.Set(on));
    }

    /// <summary>The dropdown: the chevron opens and closes the feature ticks.</summary>
    private void Expand_Click(object sender, RoutedEventArgs e)
    {
        var open = AdvancedExpand.IsChecked == true;
        AdvancedRows.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        AdvancedChevron.Data = System.Windows.Media.Geometry.Parse(open ? "M 0,0 L 3,3 L 6,0" : "M 0,0 L 3,3 L 0,6");
    }

    private void Advanced_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        Store.State.Advanced = AdvancedSwitch.IsChecked == true;
        Store.Save();
        if (Store.State.Advanced) AdvancedExpand.IsChecked = true;
        Refresh();
        _notes.RefreshAdvanced();
    }

    private void Images_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        Store.State.AdvImages = ImagesSwitch.IsChecked == true;
        Store.Save();
        _notes.RefreshAdvanced();
    }

    private void Top_Click(object sender, RoutedEventArgs e)
    {
        if (!_syncing) _notes.SetLayer(TopSwitch.IsChecked == true ? Layer.OnTop : Layer.Normal);
    }

    private void Fewer_Click(object sender, RoutedEventArgs e) => StepDays(-1);
    private void More_Click(object sender, RoutedEventArgs e) => StepDays(+1);

    private void StepDays(int direction)
    {
        var s = Store.State;
        s.HistoryDays = direction > 0
            ? Days.FirstOrDefault(d => d > s.HistoryDays, Days[^1])
            : Days.LastOrDefault(d => d < s.HistoryDays, Days[0]);
        _notes.PurgeHistory();
        Store.Save();
        Refresh();
    }

    private void History_Click(object sender, RoutedEventArgs e) => _notes.OpenHistory();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        base.OnKeyDown(e);
    }
}
