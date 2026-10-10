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
        window.reportsWorkspaceView.RunReportObserved(code);
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
        if (date is { } selected) GeneratePackObserved(selected, scope ?? StoreScopeCatalog.AllStores);
    }

    public void GeneratePackObserved(DateTime date, string scope) =>
        Observe(GeneratePackAsync(date, scope), "REPORT_PACK_GENERATION_FAILED", "Generate pack failed",
            new(StoreCode: window.StoreScopes.Resolve(scope) ?? DesktopDiagnosticContext.AllStores, BusinessDate: DateOnly.FromDateTime(date)));

    // IE-CODE-11 (1.9.9): navigation with drafts, window close, Generate pack and investigation hits run fire and
    // forget. Their try blocks have no catch, so a fault (a draft save, a dialog) died unobserved with nothing on
    // screen. Observed, it reaches the status bar with a reference and the diagnostics log.
    internal void Observe(Task task, string eventId, string operation, DesktopDiagnosticContext? context = null) =>
        _ = DesktopDiagnostics.ObserveAsync(task, "Shell.Navigation", eventId, operation,
            message => window.ApplicationStatus.Text = message, context);

    public async Task GeneratePackAsync(DateTime date, string scope)
    {
        // Titan FIX-17: the Actions menu and Ctrl+Shift+P both land here. A Viewer may not save a report generation
        // (SQL grants it to the Owner and Store Managers), so say so and stay put instead of failing on Close day.
        if (!window.CurrentShellAccess.CanImport)
        {
            window.ApplicationStatus.Text = Modules.DailyWorkflow.DailyWorkflowWorkspaceView.PackGenerationNeedsManagerMessage;
            return;
        }
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
