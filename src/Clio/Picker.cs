using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Clio;

/// <summary>
/// The one dropdown Clio uses for colours, fonts, sizes and symbols. A Popup
/// built on demand and thrown away on close, rather than a restyled ComboBox:
/// the stock ComboBox template is a pale box, and a dropdown that is not open
/// should cost nothing.
/// </summary>
internal static class Picker
{
    public static Popup Open(FrameworkElement anchor, UIElement content, double maxHeight = 320)
    {
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = new Border
            {
                Background = (Brush)anchor.FindResource("BgRaised"),
                BorderBrush = (Brush)anchor.FindResource("AccentDim"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4),
                Child = new ScrollViewer
                {
                    Content = content,
                    MaxHeight = maxHeight,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
            },
        };
        popup.Closed += (_, _) => popup.Child = null;
        popup.IsOpen = true;
        return popup;
    }

    /// <summary>A full-width row: what a font or size list is made of.</summary>
    public static Button Row(FrameworkElement owner, UIElement content, Action pick)
    {
        var b = new Button
        {
            Style = (Style)owner.FindResource("PickRow"),
            Content = content,
        };
        b.Click += (_, _) =>
        {
            pick();
            if (b.Tag is Popup p) p.IsOpen = false;
        };
        return b;
    }

    /// <summary>Wires every row in a panel to close its popup once picked.</summary>
    public static void CloseOnPick(Panel panel, Popup popup)
    {
        foreach (var child in panel.Children)
            if (child is Button b) b.Tag = popup;
    }

    public static TextBlock Label(FrameworkElement owner, string text) => new()
    {
        Text = text,
        FontFamily = (FontFamily)owner.FindResource("MonoFont"),
        FontSize = 9,
        Foreground = (Brush)owner.FindResource("TextLow"),
        Margin = new Thickness(4, 6, 4, 3),
    };
}
