extern alias EtpApplication;

using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Today;
using DailyReadinessQuery = EtpApplication::Etp.Reporting.Application.DailyReadiness.IDailyReadinessQuery;
using NoSaleDayService = EtpApplication::Etp.Reporting.Application.NoSaleDays.INoSaleDayService;

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

    /// <summary>
    /// 1.12 U1: N1's no-sale service, built from the window's current connection factory (never a connection string).
    /// Null leaves the no-sale lines out (a build without N1, the view tests).
    /// </summary>
    internal Func<NoSaleDayService>? noSaleDays;

    private TodayChecklistPanel? todayChecklist;

    internal TodayChecklistPanel TodayChecklistPanel =>
        todayChecklist ??= CreateTodayChecklist();

    private TodayChecklistPanel CreateTodayChecklist()
    {
        var panel = new TodayChecklistPanel(() => dailyReadinessQuery(), OpenChecklistTask, () => CurrentShellAccess, ShopNameFor)
        {
            // 1.11.3 (U3 ST-08): a line about one shop opens its task with that shop in the header.
            OpenTaskForStore = OpenChecklistTaskForStore,
            // 1.12 U1 (decision 52): N1's no-sale marks, read from the window's current connection, and N1's buttons.
            LoadNoSaleDays = noSaleDays is { } service ? (date, token) => service().LoadAsync(date, token) : null,
            NoSaleButtons = () => NoSaleDayButtons
        };
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

    private void OpenChecklistTaskForStore(string taskId, string storeCode)
    {
        if (string.IsNullOrWhiteSpace(storeCode)) { OpenChecklistTask(taskId); return; }
        if (TaskNavigation.Find(taskId) is not { } task || !taskNavigator!.TryNavigateTask(task)) return;
        if (taskId == "masters") TodayChecklist.SelectTab(FocusedWorkspaceHost.Content as System.Windows.DependencyObject, TodayChecklist.MonthlyTargetsTab);
        SelectHeaderStore(storeCode);
    }

    /// <summary>Moves the header store to <paramref name="storeCode"/>; the header's own scope change then re-shows the
    /// current task for that shop (and asks about drafts first, as any header change does).</summary>
    internal void SelectHeaderStore(string storeCode)
    {
        var item = ShellStoreSelector.Items.OfType<ComboBoxItem>().FirstOrDefault(x => string.Equals(x.Tag?.ToString(), storeCode, StringComparison.OrdinalIgnoreCase));
        if (item is not null && !ReferenceEquals(ShellStoreSelector.SelectedItem, item)) ShellStoreSelector.SelectedItem = item;
    }

    private void OpenChecklistTask(string taskId)
    {
        if (TaskNavigation.Find(taskId) is not { } task) return;
        taskNavigator!.NavigateTask(task);
        if (taskId == "masters") TodayChecklist.SelectTab(FocusedWorkspaceHost.Content as System.Windows.DependencyObject, TodayChecklist.MonthlyTargetsTab);
    }
}
