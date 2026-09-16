using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Etp.Reporting.Desktop;

/// <summary>Local result filtering never changes report totals, exports or the navigation query.</summary>
public sealed class PageSearch
{
    private readonly FrameworkElement content;
    private readonly Popup popup;
    private readonly TextBox query = new() { MinWidth = 220 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
    private readonly List<(DataGrid Grid, IEnumerable? Source, ListCollectionView View)> tables = [];
    private readonly IInputElement? returnFocus;
    private TextBlock? highlighted;
    private object? previousBackground;
    private int matchIndex = -1;

    public PageSearch(FrameworkElement content)
    {
        this.content = content; returnFocus = Keyboard.FocusedElement;
        foreach (var grid in Descendants(content).OfType<DataGrid>().Where(x => x.IsVisible && x.ItemsSource is not null))
        {
            var local = new ListCollectionView(grid.ItemsSource!.Cast<object>().ToList());
            tables.Add((grid, grid.ItemsSource, local)); grid.ItemsSource = local;
        }
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = tables.Count > 0 ? "Filter table rows · Ctrl+F" : "Find in this page · Ctrl+F", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(query); panel.Children.Add(status);
        var actions = new WrapPanel();
        var next = new Button { Content = "Next match", Margin = new Thickness(0, 8, 8, 0), Visibility = tables.Count == 0 ? Visibility.Visible : Visibility.Collapsed };
        next.Click += (_, _) => FindNext(); actions.Children.Add(next);
        var close = new Button { Content = "Close / clear", Margin = new Thickness(0, 8, 0, 0) }; close.Click += (_, _) => Close(); actions.Children.Add(close); panel.Children.Add(actions);
        var border = new Border { Child = panel, BorderThickness = new Thickness(1), Background = (Brush)content.FindResource("Surface"), BorderBrush = (Brush)content.FindResource("Divider") };
        popup = new Popup { PlacementTarget = content, Placement = PlacementMode.Top, StaysOpen = true, Child = border };
        AutomationProperties.SetName(query, tables.Count > 0 ? "Filter current table rows" : "Find text in current page");
        query.TextChanged += (_, _) => { matchIndex = -1; Apply(); };
        panel.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } else if (e.Key == Key.Enter) { FindNext(); e.Handled = true; } };
        KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);
    }

    public void Open() { popup.IsOpen = true; query.Focus(); Apply(); }
    public void Close()
    {
        ClearHighlight();
        foreach (var table in tables) if (ReferenceEquals(table.Grid.ItemsSource, table.View)) table.Grid.ItemsSource = table.Source;
        popup.IsOpen = false;
        if (returnFocus is UIElement target && target.IsVisible && target.IsEnabled) target.Focus();
    }
    private void Apply()
    {
        if (tables.Count == 0) { FindNext(); return; }
        var text = query.Text.Trim();
        foreach (var table in tables) table.View.Filter = item => text.Length == 0 || Matches(item, text);
        status.Text = $"{tables.Sum(x => x.View.Count):N0} row(s) shown. Summary totals and exports keep the complete report. Close clears this filter.";
    }
    public static bool Matches(object? item, string text)
    {
        if (item is null) return false;
        if (item is string value) return value.Contains(text, StringComparison.OrdinalIgnoreCase);
        if (item is System.Data.DataRowView row)
            return row.Row.ItemArray.Any(cell => Convert.ToString(cell, System.Globalization.CultureInfo.CurrentCulture)?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
        var properties = item.GetType().GetProperties().Where(x => x.CanRead && x.GetIndexParameters().Length == 0).ToArray();
        return properties.Length == 0 ? Convert.ToString(item, System.Globalization.CultureInfo.CurrentCulture)?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
            : properties.Any(x => Convert.ToString(x.GetValue(item), System.Globalization.CultureInfo.CurrentCulture)?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
    }
    private void FindNext()
    {
        if (tables.Count > 0) return;
        ClearHighlight(); var text = query.Text.Trim();
        var matches = text.Length == 0 ? [] : Descendants(content).OfType<TextBlock>().Where(x => x.IsVisible && x.Text.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) { status.Text = text.Length == 0 ? "Search the text on this page. Enter moves to the next match." : "No matching page text."; return; }
        matchIndex = (matchIndex + 1) % matches.Length; highlighted = matches[matchIndex]; previousBackground = highlighted.ReadLocalValue(TextBlock.BackgroundProperty);
        highlighted.SetResourceReference(TextBlock.BackgroundProperty, "AccentSoft"); highlighted.BringIntoView(); status.Text = $"Match {matchIndex + 1} of {matches.Length}. Enter for next.";
    }
    private void ClearHighlight()
    {
        if (highlighted is null) return;
        if (previousBackground == DependencyProperty.UnsetValue) highlighted.ClearValue(TextBlock.BackgroundProperty); else highlighted.SetValue(TextBlock.BackgroundProperty, previousBackground);
        highlighted = null;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
