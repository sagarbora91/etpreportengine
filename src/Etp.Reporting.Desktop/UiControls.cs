using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Etp.Reporting.Desktop;

public sealed class ModuleTile : Button
{
    public ModuleDefinition Definition { get; }

    public ModuleTile(ModuleDefinition definition)
    {
        Definition = definition;
        Tag = definition.Destination;
        MinHeight = 176;
        Margin = new Thickness(7);
        Padding = new Thickness(20);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Background = (Brush)Application.Current.Resources["Surface"];
        BorderBrush = (Brush)Application.Current.Resources["Divider"];
        BorderThickness = new Thickness(1);
        Content = BuildContent(definition);
        AutomationProperties.SetName(this, $"Open {definition.DisplayName}. {definition.Description}. {definition.StatusText}.");
    }

    private static UIElement BuildContent(ModuleDefinition definition)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var iconHost = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(12), Background = AccentTint(definition.Id), HorizontalAlignment = HorizontalAlignment.Left };
        iconHost.Child = new Path { Data = (Geometry)Application.Current.Resources[definition.IconKey], Stroke = Accent(definition.Id), Fill = Brushes.Transparent, StrokeThickness = 1.7, Stretch = Stretch.Uniform, Margin = new Thickness(10) };
        grid.Children.Add(iconHost);
        var title = new TextBlock { Text = definition.DisplayName, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["PrimaryText"], Margin = new Thickness(0, 14, 0, 3) };
        Grid.SetRow(title, 1); grid.Children.Add(title);
        var description = new TextBlock { Text = definition.Description, FontSize = 12.5, Foreground = (Brush)Application.Current.Resources["SecondaryText"], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(description, 2); grid.Children.Add(description);
        var status = new DockPanel();
        var statusText = new TextBlock { Text = definition.StatusText, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Accent(definition.Id) };
        var arrow = new TextBlock { Text = "›", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["SecondaryText"] };
        DockPanel.SetDock(arrow, Dock.Right); status.Children.Add(arrow); status.Children.Add(statusText);
        Grid.SetRow(status, 3); grid.Children.Add(status);
        return grid;
    }

    private static Brush Accent(string id) => id switch
    {
        "reports" => new SolidColorBrush(Color.FromRgb(11, 141, 98)),
        "accounting" => new SolidColorBrush(Color.FromRgb(119, 87, 214)),
        "imports" => new SolidColorBrush(Color.FromRgb(174, 96, 0)),
        "archive" => new SolidColorBrush(Color.FromRgb(8, 127, 130)),
        "exceptions" => new SolidColorBrush(Color.FromRgb(198, 59, 66)),
        _ => (Brush)Application.Current.Resources["Accent"]
    };

    private static Brush AccentTint(string id)
    {
        var colour = ((SolidColorBrush)Accent(id)).Color;
        return new SolidColorBrush(Color.FromArgb(24, colour.R, colour.G, colour.B));
    }
}

public sealed class StatusBadge : Border
{
    public StatusBadge(string text, string brushKey = "Success")
    {
        CornerRadius = new CornerRadius(10); Padding = new Thickness(9, 4, 9, 4); HorizontalAlignment = HorizontalAlignment.Left;
        var brush = (Brush)Application.Current.Resources[brushKey]; Background = WithOpacity(brush, .12); Child = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = brush };
        AutomationProperties.SetName(this, text);
    }
    private static Brush WithOpacity(Brush brush, double opacity) { var clone = brush.Clone(); clone.Opacity = opacity; return clone; }
}

public sealed class EmptyState : Border
{
    public EmptyState(string title, string message, string? action = null)
    {
        Style = (Style)Application.Current.Resources["SurfaceCard"];
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 520 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["PrimaryText"], TextAlignment = TextAlignment.Center });
        panel.Children.Add(new TextBlock { Text = message, Margin = new Thickness(0, 7, 0, 0), Foreground = (Brush)Application.Current.Resources["SecondaryText"], TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        if (!string.IsNullOrWhiteSpace(action)) panel.Children.Add(new TextBlock { Text = action, Margin = new Thickness(0, 12, 0, 0), Foreground = (Brush)Application.Current.Resources["Accent"], FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
        Child = panel; AutomationProperties.SetName(this, $"{title}. {message}");
    }
}

public sealed class LoadingState : Border
{
    public LoadingState(string message)
    {
        Background = (Brush)Application.Current.Resources["SurfaceSecondary"]; CornerRadius = new CornerRadius(12); Padding = new Thickness(18);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new ProgressBar { Width = 90, Height = 7, IsIndeterminate = true, Margin = new Thickness(0, 0, 14, 0) });
        panel.Children.Add(new TextBlock { Text = message, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Application.Current.Resources["SecondaryText"] });
        Child = panel; AutomationProperties.SetName(this, message);
    }
}

public sealed class DetailDrawer : Border
{
    private readonly ContentPresenter presenter = new();
    public event EventHandler? CloseRequested;

    public DetailDrawer()
    {
        Width = 390; HorizontalAlignment = HorizontalAlignment.Right; Background = (Brush)Application.Current.Resources["Surface"];
        BorderBrush = (Brush)Application.Current.Resources["Divider"]; BorderThickness = new Thickness(1, 0, 0, 0); Padding = new Thickness(22); Visibility = Visibility.Collapsed;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(close, "Close detail drawer");
        close.Click += (_, _) => Close(); root.Children.Add(close); Grid.SetRow(presenter, 1); root.Children.Add(presenter); Child = root;
        PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) { Close(); args.Handled = true; } };
        AutomationProperties.SetName(this, "Detail and source evidence drawer");
    }

    public void Open(UIElement content)
    {
        presenter.Content = content; Visibility = Visibility.Visible; Focus();
    }

    public void Close()
    {
        Visibility = Visibility.Collapsed; presenter.Content = null; CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
