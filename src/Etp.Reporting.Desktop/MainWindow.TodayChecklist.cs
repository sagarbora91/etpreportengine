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
        todayChecklist ??= new TodayChecklistPanel(() => dailyReadinessQuery(), OpenChecklistTask, () => CurrentShellAccess);

    internal void AttachTodayChecklist(DailySalesReportWorkspace dsr)
    {
        dsr.Checklist = TodayChecklistPanel;
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
