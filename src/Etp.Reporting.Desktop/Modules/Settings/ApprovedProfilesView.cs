using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.Desktop.Modules.Settings;

public sealed class ApprovedProfilesView : UserControl
{
    public ApprovedProfilesView()
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var heading = new TextBlock { Text = "Approved import profiles shipped with this candidate. Profile changes require a reviewed software update; unknown layouts remain blocked.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,12) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var grid = new DataGrid { IsReadOnly = true, AutoGenerateColumns = true, ItemsSource = ApprovedImportProfileRegistry.All.Select(x => new { x.Identity.ReportCode, x.Identity.LayoutVersion, x.Identity.ProfileVersion, x.Identity.HeaderSignatureSha256 }).ToArray() };
        AutomationProperties.SetName(grid, "Approved import profile catalogue"); root.Children.Add(grid); Content = root;
    }
}
