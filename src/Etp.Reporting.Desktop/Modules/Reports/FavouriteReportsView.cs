using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
namespace Etp.Reporting.Desktop.Modules.Reports;

public sealed class FavouriteReportsView : UserControl
{
    public FavouriteReportsView(UiPreferences preferences, ShellAccess access, Action<TaskDestination> navigate)
    {
        var tasks = preferences.FavouriteReportCodes.Select(code => TaskNavigation.Find("report-" + code)).OfType<TaskDestination>().Where(task => task.IsAllowed(access)).ToArray();
        var root = new DockPanel { Margin = new Thickness(16) };
        var selector = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0,0,0,12) };
        AutomationProperties.SetName(selector, "Favourite report category"); selector.ItemsSource = tasks.Select(task => task.Category).Distinct().Order().ToArray();
        DockPanel.SetDock(selector, Dock.Top); root.Children.Add(selector);
        var tiles = new UniformGrid { Columns = 3 };
        void Populate()
        {
            tiles.Children.Clear();
            foreach (var task in tasks.Where(task => task.Category == selector.SelectedItem?.ToString()))
            {
                var button = new Button { Content = new TextBlock { Text = task.Title, TextWrapping = TextWrapping.Wrap, FontSize = 16 }, MinHeight = 90, Padding = new Thickness(12), Margin = new Thickness(6) };
                AutomationProperties.SetName(button, task.Path); button.Click += (_, _) => navigate(task); tiles.Children.Add(button);
            }
            if (tasks.Length == 0) tiles.Children.Add(new TextBlock { Text = "No favourite reports yet. Select them in General Preferences.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) });
        }
        selector.SelectionChanged += (_, _) => Populate(); selector.SelectedIndex = 0; Populate();
        root.Children.Add(new ScrollViewer { Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = root;
    }
}
