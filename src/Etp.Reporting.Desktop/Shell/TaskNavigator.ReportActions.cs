namespace Etp.Reporting.Desktop;

public sealed partial class TaskNavigator
{
    private PageSearch? pageSearch;
    public void RefreshCurrentReport()
    {
        pageSearch?.Close();
        var code = window.shell.CurrentRoute.FeatureCode;
        if (code is null) return;
        if (window.FocusedWorkspaceHost.Content is ReportWorkspaceControl report)
            window.reportsWorkspaceView.ApplyScope(report.DateFromPicker.SelectedDate, report.DateToPicker.SelectedDate, report.ScopeSelector.SelectedItem?.ToString());
        if (window.FocusedWorkspaceHost.Content is DailySalesReportWorkspace dsr)
            window.reportsWorkspaceView.ApplyScope(dsr.BusinessDatePicker.SelectedDate, dsr.BusinessDatePicker.SelectedDate, dsr.ScopeSelector.SelectedItem?.ToString());
        _ = window.reportsWorkspaceView.RunReportAsync(code);
    }
    public void SearchCurrentPage()
    {
        CloseMasterSearch();
        if (window.FocusedWorkspaceHost.Content is System.Windows.FrameworkElement content)
        {
            pageSearch?.Close(); pageSearch = new PageSearch(content); pageSearch.Open();
        }
    }

    public void GenerateCurrentPack()
    {
        var (date, scope) = window.FocusedWorkspaceHost.Content switch
        {
            ReportWorkspaceControl report => (report.DateToPicker.SelectedDate, report.ScopeSelector.SelectedItem?.ToString()),
            DailySalesReportWorkspace dsr => (dsr.BusinessDatePicker.SelectedDate, dsr.ScopeSelector.SelectedItem?.ToString()),
            _ => (window.ShellBusinessDateSelector.SelectedDate, (window.ShellStoreSelector.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString())
        };
        if (date is { } selected) _ = GeneratePackAsync(selected, scope ?? StoreScopeCatalog.AllStores);
    }

    public async Task GeneratePackAsync(DateTime date, string scope)
    {
        if (navigationPending || window.dailyWorkflowWorkspace.IsBusy) { window.ApplicationStatus.Text = "Wait for the current operation to finish before generating another pack."; return; }
        navigationPending = true;
        try
        {
            if (!await ResolveDraftsAsync()) return;
            var store = window.StoreScopes.Resolve(scope);
            var task = TaskNavigation.Find(store is null ? "combined-pack" : "store-daily-pack")!;
            var decision = window.shell.Navigate(task.Route, window.CurrentShellAccess);
            window.ApplyNavigationDecision(decision);
            if (!decision.IsAllowed) return;
            window.dailyWorkflowWorkspace.BusinessDate = date;
            if (store is null) await window.dailyWorkflowWorkspace.GenerateCombinedDailyPackAsync();
            else { window.dailyWorkflowWorkspace.StoreCode = store; await window.dailyWorkflowWorkspace.GenerateDailyPackAsync(); }
        }
        finally { navigationPending = false; }
    }
}
