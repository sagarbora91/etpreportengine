using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
namespace Etp.Reporting.Desktop;

public static class TaskBodyLayout
{
    private static readonly ConditionalWeakTable<UserControl, Dictionary<string, string>> Selected = new();
    public static UIElement Create(UserControl owner, string title, StackPanel body)
    {
        var grids = body.Children.OfType<DataGrid>().ToArray();
        if (grids.Length == 0) return Scroll(body);
        var scope = ScopeSummary(body);
        var root = new DockPanel();
        if (scope is not null) { DockPanel.SetDock(scope, Dock.Top); root.Children.Add(scope); }
        var status = new StackPanel();
        foreach (var item in body.Children.OfType<TextBlock>().Where(text => text.Name.Contains("Status") || text.Name.Contains("Result")).ToArray())
        { body.Children.Remove(item); item.Margin = new Thickness(0,0,0,6); item.TextWrapping = TextWrapping.NoWrap; item.TextTrimming = TextTrimming.CharacterEllipsis; status.Children.Add(item); }
        DockPanel.SetDock(status, Dock.Top); root.Children.Add(status);
        var tabs = new TabControl();
        foreach (var grid in grids)
        {
            body.Children.Remove(grid); grid.MaxHeight = double.PositiveInfinity; grid.Height = double.NaN; grid.Margin = new Thickness(0);
            var label = AutomationProperties.GetName(grid);
            var page = new Grid(); page.Children.Add(grid);
            var empty = new TextBlock { Text = "No rows to display. Refresh or adjust the task filters.", TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20), IsHitTestVisible = false };
            empty.SetBinding(UIElement.VisibilityProperty, new Binding("Items.Count") { Source = grid, Converter = new EmptyVisibility() });
            page.Children.Add(empty); tabs.Items.Add(Tab(label.Length == 0 ? "Records" : label, page));
        }
        if (Descendants(body).Any(item => item is TextBox or ComboBox or DatePicker or CheckBox))
        {
            var entryFirst = owner is Modules.Registers.RegistersWorkspaceView or Modules.DailyWorkflow.DailyWorkflowWorkspaceView or Modules.OperationsAdministration.AdministrationWorkspaceView or Modules.Imports.ImportWorkspaceView || title == "Sharing Contacts";
            tabs.Items.Insert(0, Tab(entryFirst ? "Entry fields" : "Filters & input", Scroll(body)));
            tabs.SelectedIndex = entryFirst ? 0 : 1;
        }
        else if (body.Children.Count > 0)
        {
            // Read-only introductions and messages remain available without nesting a table in a page scroll.
            tabs.Items.Add(Tab("Guidance", Scroll(body)));
        }
        var remembered = Selected.GetOrCreateValue(owner);
        if (remembered.TryGetValue(title, out var selected)) tabs.SelectedItem = tabs.Items.OfType<TabItem>().FirstOrDefault(tab => (string?)tab.Tag == selected) ?? tabs.SelectedItem;
        tabs.SelectionChanged += (_, e) => { if (e.Source == tabs && tabs.SelectedItem is TabItem { Tag: string name }) remembered[title] = name; };
        root.Children.Add(tabs); return root;
    }
    private static TabItem Tab(string label, UIElement content)
    {
        var tab = new TabItem { Tag = label, Header = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 190 }, Content = content };
        AutomationProperties.SetName(tab, label); return tab;
    }
    private static ScrollViewer Scroll(UIElement body) => new() { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private static TextBlock? ScopeSummary(DependencyObject body)
    {
        var inputs = Descendants(body).OfType<Control>().Where(input => input is DatePicker || input.Name.Contains("Store") && input is TextBox or ComboBox).ToArray();
        if (inputs.Length == 0) return null;
        var labels = inputs.Select(input => AutomationProperties.GetName(input)).ToArray();
        var binding = new MultiBinding { Converter = new ScopeText(labels) };
        foreach (var input in inputs) binding.Bindings.Add(new Binding(input is DatePicker ? "SelectedDate" : input is ComboBox ? "SelectedItem.Content" : "Text") { Source = input });
        var summary = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,6) };
        summary.SetBinding(TextBlock.TextProperty, binding); AutomationProperties.SetName(summary, "Displayed task context"); return summary;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        { yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    private sealed class EmptyVisibility : IValueConverter
    {
        public object Convert(object value, Type type, object parameter, CultureInfo culture) => value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    private sealed class ScopeText(string[] labels) : IMultiValueConverter
    {
        public object Convert(object[] values, Type type, object parameter, CultureInfo culture) => string.Join(" · ", values.Select((value, index) =>
            labels[index] + ": " + (value is DateTime date ? date.ToString("dd MMM yyyy", culture) : value is null || value == DependencyProperty.UnsetValue || string.IsNullOrWhiteSpace(value.ToString()) ? "not selected" : value.ToString())));
        public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
