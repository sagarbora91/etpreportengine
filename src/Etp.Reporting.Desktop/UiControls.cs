using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Etp.Reporting.Desktop;

public sealed class EmptyState : Border
{
    public EmptyState(string title, string message, string? action = null)
    {
        SetResourceReference(StyleProperty, "SurfaceCard");
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 520 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = (Brush?)Application.Current?.TryFindResource("PrimaryText") ?? Brushes.Black, TextAlignment = TextAlignment.Center });
        panel.Children.Add(new TextBlock { Text = message, Margin = new Thickness(0, 7, 0, 0), Foreground = (Brush?)Application.Current?.TryFindResource("SecondaryText") ?? Brushes.DimGray, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        if (!string.IsNullOrWhiteSpace(action)) panel.Children.Add(new TextBlock { Text = action, Margin = new Thickness(0, 12, 0, 0), Foreground = (Brush?)Application.Current?.TryFindResource("Accent") ?? Brushes.Teal, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
        Child = panel; AutomationProperties.SetName(this, $"{title}. {message}");
    }
}

public sealed class LoadingState : Border
{
    public LoadingState(string message)
    {
        SetResourceReference(BackgroundProperty, "SurfaceSecondary"); CornerRadius = new CornerRadius(12); Padding = new Thickness(18);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new ProgressBar { Width = 90, Height = 7, IsIndeterminate = true, Margin = new Thickness(0, 0, 14, 0) });
        panel.Children.Add(new TextBlock { Text = message, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush?)Application.Current?.TryFindResource("PrimaryText") ?? Brushes.DimGray });
        Child = panel; AutomationProperties.SetName(this, message);
    }
}
