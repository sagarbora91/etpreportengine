using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop;

internal static class ReportActionMenu
{
    /// <param name="canGeneratePack">
    /// WLMHW FIX-17: whether the current role may generate a pack (Owner or Store Manager). It is read each time the
    /// menu opens, so a Viewer sees "Generate report pack" disabled with the reason instead of a refusal later.
    /// </param>
    public static Button Create(Action<ReportWorkspaceAction> invoke, List<Control> exports, bool manualEntry, Func<bool>? canGeneratePack = null)
    {
        var menu = new ContextMenu();
        MenuItem? pack = null;
        bool PackAllowed() => canGeneratePack?.Invoke() ?? true;
        void Add(string title, ReportWorkspaceAction action)
        {
            var item = new MenuItem { Header = title, MinHeight = 48, Padding = new Thickness(12, 6, 12, 6) };
            AutomationProperties.SetName(item, title + " current report");
            if (action is ReportWorkspaceAction.ExportPdf or ReportWorkspaceAction.ExportExcel) { item.IsEnabled = false; exports.Add(item); }
            if (action is ReportWorkspaceAction.GenerateReportPack) pack = item;
            item.Click += (_, _) =>
            {
                if (action is ReportWorkspaceAction.GenerateReportPack && !PackAllowed()) return;
                invoke(action);
            };
            menu.Items.Add(item);
        }
        Add("Export PDF", ReportWorkspaceAction.ExportPdf); Add("Export Excel", ReportWorkspaceAction.ExportExcel);
        Add("Generate report pack", ReportWorkspaceAction.GenerateReportPack); Add("Open export folder", ReportWorkspaceAction.OpenExportFolder);
        if (manualEntry) Add("Manual entry", ReportWorkspaceAction.OpenManualEntry);
        void ApplyPackAccess()
        {
            var allowed = PackAllowed();
            pack!.IsEnabled = allowed;
            pack.ToolTip = allowed ? null : Modules.DailyWorkflow.DailyWorkflowWorkspaceView.PackGenerationNeedsManagerMessage;
            ToolTipService.SetShowOnDisabled(pack, true);
        }
        ApplyPackAccess();
        menu.Opened += (_, _) => ApplyPackAccess();
        var button = new Button { Content = "Actions ▾", ContextMenu = menu, Padding = new Thickness(12, 6, 12, 6) };
        AutomationProperties.SetName(button, "Report actions: export, report pack and folder");
        button.Click += (_, _) => { menu.PlacementTarget = button; menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; menu.IsOpen = true; };
        return button;
    }
}
