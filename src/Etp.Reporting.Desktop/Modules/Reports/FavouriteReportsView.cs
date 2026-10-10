using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace Etp.Reporting.Desktop.Modules.Reports;

/// <summary>
/// RA-UI-21 (9 Oct 2026): favourites are the same tiles as the report catalogue (<see cref="ReportListView"/>), in the
/// same columns, and all of them show by default; the category box narrows the list instead of hiding the rest.
/// </summary>
public sealed class FavouriteReportsView : UserControl
{
    internal const string AllCategories = "All categories";

    public FavouriteReportsView(UiPreferences preferences, ShellAccess access, Action<TaskDestination> navigate)
    {
        var tasks = preferences.FavouriteReportCodes.Select(code => TaskNavigation.Find("report-" + code)).OfType<TaskDestination>().Where(task => task.IsAllowed(access)).ToArray();
        var root = new DockPanel { Margin = new Thickness(16) };
        var selector = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,0,0,12) };
        AutomationProperties.SetName(selector, "Favourite report category");
        selector.ItemsSource = new[] { AllCategories }.Concat(tasks.Select(task => task.Category).Distinct().Order()).ToArray();
        DockPanel.SetDock(selector, Dock.Top); root.Children.Add(selector);
        var tiles = ReportListView.CreateTileGrid();
        void Populate()
        {
            tiles.Children.Clear();
            var category = selector.SelectedItem?.ToString();
            foreach (var task in tasks.Where(task => category is null or AllCategories || task.Category == category))
                tiles.Children.Add(ReportListView.CreateTile(task, navigate));
            if (tasks.Length == 0) tiles.Children.Add(new TextBlock { Text = "No favourite reports yet. Select them in General Preferences.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) });
        }
        selector.SelectionChanged += (_, _) => Populate(); selector.SelectedIndex = 0; Populate();
        root.Children.Add(new ScrollViewer { Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = root;
        tiles.Columns = ReportListView.ColumnsFor(ActualWidth);
        SizeChanged += (_, _) => tiles.Columns = ReportListView.ColumnsFor(ActualWidth);
    }
}
