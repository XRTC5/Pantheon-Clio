using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Clio.Core;

namespace Clio;

/// <summary>Erased notes, newest first, each with Restore and Delete.</summary>
public partial class HistoryWindow : Window
{
    private readonly Notes _notes;
    private bool _armed;

    internal HistoryWindow(Notes notes)
    {
        _notes = notes;
        InitializeComponent();
        Refresh();
    }

    internal void Refresh()
    {
        var s = Store.State;
        Sub.Text = $"Erased notes are kept for {s.HistoryDays} day{(s.HistoryDays == 1 ? "" : "s")}, " +
                   "then deleted for good. Change it in Settings.";

        List.Children.Clear();
        foreach (var note in s.History.OrderByDescending(n => n.Erased))
            List.Children.Add(Row(note));

        var none = s.History.Count == 0;
        Empty.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        EmptyButton.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        _armed = false;
        EmptyButton.Content = "EMPTY HISTORY";
    }

    private UIElement Row(NoteData note)
    {
        var accent = NoteWindow.Accents[Math.Abs(note.Color) % NoteWindow.Accents.Length].Color;
        var brush = new SolidColorBrush(accent);
        brush.Freeze();

        var erased = note.Erased ?? DateTime.Now;
        var left = erased.AddDays(Store.State.HistoryDays) - DateTime.Now;
        var leftText = left.TotalHours < 24
            ? $"{Math.Max(1, (int)Math.Ceiling(left.TotalHours))}h left"
            : $"{(int)Math.Ceiling(left.TotalDays)}d left";

        var head = new TextBlock { FontFamily = Mono, FontSize = 9.5, Foreground = Low };
        head.Inlines.Add(new System.Windows.Documents.Run($"REC {note.Number:000}") { Foreground = brush });
        head.Inlines.Add(new System.Windows.Documents.Run(
            $"   ERASED {erased:dd MMM HH:mm}".ToUpperInvariant() + $"   {leftText}"));

        var preview = note.Text.Trim();
        if (preview.Length == 0) preview = note.Drawings.Count > 0 || note.Ink is not null ? "(drawing)" : note.Images.Count > 0 ? "(picture)" : "(empty)";

        var body = new TextBlock
        {
            Text = preview,
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 11.5,
            Foreground = (Brush)FindResource("TextHi"),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 50,
            Margin = new Thickness(0, 4, 0, 6),
        };

        var restore = new Button { Style = (Style)FindResource("TextButton"), Content = "RESTORE", Padding = new Thickness(8, 2, 8, 2) };
        restore.Click += (_, _) => _notes.Restore(note);
        AutomationName(restore, $"Restore REC {note.Number:000}");

        var delete = new Button { Style = (Style)FindResource("DangerButton"), Content = "DELETE", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
        delete.Click += (_, _) => _notes.DeleteForGood(note);
        AutomationName(delete, $"Delete REC {note.Number:000}");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(restore);
        buttons.Children.Add(delete);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(body);
        stack.Children.Add(buttons);

        var grid = new Grid { Margin = new Thickness(6, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new Rectangle { Width = 2, Fill = brush, Margin = new Thickness(0, 1, 8, 1) });
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        return new Border
        {
            Child = grid,
            BorderBrush = (Brush)FindResource("RuleSoft"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 4, 0, 6),
        };
    }

    private static void AutomationName(DependencyObject d, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(d, name);

    private FontFamily Mono => (FontFamily)FindResource("MonoFont");
    private Brush Low => (Brush)FindResource("TextLow");

    /// <summary>Two presses, so a stray click cannot wipe everything.</summary>
    private void EmptyAll_Click(object sender, RoutedEventArgs e)
    {
        if (!_armed)
        {
            _armed = true;
            EmptyButton.Content = "PRESS AGAIN TO EMPTY";
            return;
        }
        _notes.EmptyHistory();
    }

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
