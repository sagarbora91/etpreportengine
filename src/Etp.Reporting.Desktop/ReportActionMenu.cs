using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

internal static class ReportActionMenu
{
    public static Button Create(Action<ReportWorkspaceAction> invoke, List<Control> exports, bool manualEntry)
    {
        var menu = new ContextMenu();
        void Add(string title, ReportWorkspaceAction action)
        {
            var item = new MenuItem { Header = title, MinHeight = 48, Padding = new Thickness(12, 6, 12, 6) };
            AutomationProperties.SetName(item, title + " current report");
            if (action is ReportWorkspaceAction.ExportPdf or ReportWorkspaceAction.ExportExcel) { item.IsEnabled = false; exports.Add(item); }
            item.Click += (_, _) => invoke(action); menu.Items.Add(item);
        }
        Add("Export PDF", ReportWorkspaceAction.ExportPdf); Add("Export Excel", ReportWorkspaceAction.ExportExcel);
        Add("Generate report pack", ReportWorkspaceAction.GenerateReportPack); Add("Open export folder", ReportWorkspaceAction.OpenExportFolder);
        if (manualEntry) Add("Manual entry", ReportWorkspaceAction.OpenManualEntry);
        var button = new Button { Content = "Actions ▾", ContextMenu = menu, Padding = new Thickness(12, 6, 12, 6) };
        AutomationProperties.SetName(button, "Report actions: export, report pack and folder");
        button.Click += (_, _) => { menu.PlacementTarget = button; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; };
        return button;
    }
}
