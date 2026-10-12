extern alias EtpApplication;

using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Today;
using DailyReadinessQuery = EtpApplication::Etp.Reporting.Application.DailyReadiness.IDailyReadinessQuery;

namespace Etp.Reporting.Desktop;

/// <summary>
/// 1.9.9 Today checklist. The "What's missing" panel sits on Today > Sales (the Today landing
/// screen) between the DSR toolbar and its preview. It is refreshed each time that screen loads,
/// which includes every change of the shell business date or store.
/// </summary>
public partial class MainWindow
{
    // The composition root wires the SQL readiness query; until it is set the panel stays hidden.
    internal Func<DailyReadinessQuery> dailyReadinessQuery = TodayChecklist.Unavailable;
    private TodayChecklistPanel? todayChecklist;

    internal TodayChecklistPanel TodayChecklistPanel =>
        todayChecklist ??= CreateTodayChecklist();

    private TodayChecklistPanel CreateTodayChecklist()
    {
        var panel = new TodayChecklistPanel(() => dailyReadinessQuery(), OpenChecklistTask, () => CurrentShellAccess, ShopNameFor);
        // 1.11.2: a file that arrived while the Owner was in another program leaves the list when he comes back.
        Activated += (_, _) => { if (panel.IsVisible) _ = panel.RefreshLastAsync(); };
        return panel;
    }

    /// <summary>The shop's name from the header store list (its label is the name), or null when the list does not have it.</summary>
    private string? ShopNameFor(string storeCode)
    {
        var item = ShellStoreSelector.Items.OfType<ComboBoxItem>().FirstOrDefault(x => string.Equals(x.Tag?.ToString(), storeCode, StringComparison.OrdinalIgnoreCase));
        var label = item?.Content?.ToString();
        return string.IsNullOrWhiteSpace(label) ? null : label;
    }

    internal void AttachTodayChecklist(DailySalesReportWorkspace dsr)
    {
        // 1.11.0: the "System check" tile (every role) sits above the panel.
        dsr.Checklist = TodayStatusPanel(TodayChecklistPanel);
        var date = ShellBusinessDateSelector.SelectedDate ?? TaskNavigator.InitialBusinessDate;
        var store = (ShellStoreSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        _ = TodayChecklistPanel.RefreshAsync(date, store);
    }

    private void OpenChecklistTask(string taskId)
    {
        if (TaskNavigation.Find(taskId) is not { } task) return;
        taskNavigator!.NavigateTask(task);
        if (taskId == "masters") TodayChecklist.SelectTab(FocusedWorkspaceHost.Content as System.Windows.DependencyObject, TodayChecklist.MonthlyTargetsTab);
    }
}
