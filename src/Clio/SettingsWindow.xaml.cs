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
