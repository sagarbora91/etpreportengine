using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop.Modules.Settings;

public sealed class GeneralPreferencesView : UserControl
{
    public GeneralPreferencesView(UiPreferences preferences, Action<UiPreferences> save, ShellAccess access)
    {
        var current = preferences;
        var root = new DockPanel { Margin = new Thickness(20) };
        var notice = new TextBlock { Text = "Personal preferences are saved automatically on this device.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) };
        DockPanel.SetDock(notice, Dock.Top); root.Children.Add(notice);
        var tabs = new TabControl(); root.Children.Add(tabs);
        StackPanel Page(string title)
        {
            var panel = new StackPanel { Margin = new Thickness(12) };
            tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return panel;
        }
        var display = Page("Display");
        display.Children.Add(new TextBlock { Text = "Touch: 44-pixel controls. Desktop: 36-pixel controls.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) });
        var densitySelector = new DensitySelector();
        densitySelector.SetDensity(current.Density);
        densitySelector.DensityChanged += (_, density) => { current = current with { Density = density }; save(current); };
        display.Children.Add(densitySelector);
        void Toggle(StackPanel panel, string label, string id, bool selected, bool report)
        {
            var check = new CheckBox { Content = label, IsChecked = selected, MinHeight = 48 };
            AutomationProperties.SetName(check, label);
            check.Click += (_, _) =>
            {
                var values = (report ? current.FavouriteReportCodes : current.PinnedModuleIds).ToList();
                if (check.IsChecked == true && !values.Contains(id)) values.Add(id); else if (check.IsChecked != true) values.Remove(id);
                current = report ? current with { FavouriteReportCodes = values } : current with { PinnedModuleIds = values }; save(current);
            };
            panel.Children.Add(check);
        }
        var reports = Page("Favourite reports");
        foreach (var report in TaskNavigation.All.Where(x => x.ReportCode is not null && x.IsAllowed(access)))
            Toggle(reports, report.Title, report.ReportCode!, current.FavouriteReportCodes.Contains(report.ReportCode!), true);
        Content = root;
    }
}
