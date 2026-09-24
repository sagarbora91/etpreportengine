extern alias EtpApplication;
using System.Windows.Controls;
using InvestigationHit = EtpApplication::Etp.Reporting.Application.Distribution.InvestigationHit;

namespace Etp.Reporting.Desktop;

public sealed partial class TaskNavigator
{
    private InvestigationHit? pendingInvestigation;
    internal static string RegisterTask(string type) => "register-" + (type switch
    {
        "CREDIT_NOTE" => "credit", "SERVICE_RECEIPT" => "service", "STOCK_TRANSFER" => "transfer", "VENDOR_INVOICE" => "vendor", _ => type.ToLowerInvariant()
    });

    public void NavigateInvestigation(InvestigationHit hit)
    {
        var id = hit.TargetTaskId switch { "invoice-lineage" => "report-invoice-lineage", "sales-item" => "report-sales-item", "received-files" => "source-inbox", _ => hit.TargetTaskId };
        if (TaskNavigation.Find(id) is not { } task || !task.IsAllowed(window.CurrentShellAccess))
        { window.ApplicationStatus.Text = "This result is not available to your role."; return; }
        if (!string.IsNullOrWhiteSpace(hit.StoreCode) && hit.StoreCode != "COMBINED"
            && !window.StoreScopes.Stores.Any(store => store.Code.Equals(hit.StoreCode, StringComparison.OrdinalIgnoreCase)))
        { window.ApplicationStatus.Text = $"Store {hit.StoreCode} is inactive. Ask the Owner to enable it before opening this scoped result."; return; }
        NavigateSafely(() =>
        {
            var decision = window.shell.Navigate(task.Route, window.CurrentShellAccess);
            if (!decision.IsAllowed) return decision;
            var items = window.ShellStoreSelector.Items.OfType<ComboBoxItem>().ToArray();
            appliedStore = Array.FindIndex(items, item => item.Tag?.ToString() == hit.StoreCode);
            if (appliedStore < 0) appliedStore = items.Length - 1;
            if (hit.BusinessDate is { } date) ApplyBusinessDate(date.ToDateTime(TimeOnly.MinValue));
            RestoreHeader(appliedDate, appliedStore);
            window.reportsWorkspaceView.ApplyReportPeriod(appliedDate, appliedDate);
            if (id?.StartsWith("register-", StringComparison.Ordinal) == true) window.registersWorkspaceView.SearchFor(hit.PrimaryReference);
            pendingInvestigation = hit;
            return decision;
        });
    }

    private async Task SelectInvestigationAsync(InvestigationHit hit)
    {
        try
        {
            if (hit.TargetId is not { } id) return;
            switch (hit.TargetTaskId)
            {
                case "import-history" when window.importHistoryView is { } history:
                    await history.ActivateAsync(new(DateOnly.FromDateTime(appliedDate), DateOnly.FromDateTime(appliedDate), hit.StoreCode));
                    if (history.Entries.FirstOrDefault(entry => entry.ImportFileId == id) is { } entry) history.SelectEntry(entry);
                    break;
                case "generations":
                    await window.archiveWorkspaceView.RefreshAsync();
                    if (window.archiveWorkspaceView.FindName("ReportGenerationGrid") is DataGrid archive)
                        archive.SelectedItem = archive.Items.Cast<EtpApplication::Etp.Reporting.Application.Archive.ArchivedReportGenerationSummary>().FirstOrDefault(row => row.Id == id);
                    break;
                case "received-files":
                    await window.sourceInboxWorkspaceView.RefreshAsync();
                    if (window.sourceInboxWorkspaceView.FindName("DocumentsGrid") is DataGrid documents)
                        documents.SelectedItem = documents.Items.Cast<EtpApplication::Etp.Reporting.Application.SourceInbox.SourceInboxDocument>().FirstOrDefault(row => row.Id == id);
                    break;
                case { } target when target.StartsWith("register-", StringComparison.Ordinal):
                    await window.registersWorkspaceView.RefreshAsync();
                    if (window.registersWorkspaceView.FindName("RegisterGrid") is DataGrid registers)
                        registers.SelectedItem = registers.Items.Cast<EtpApplication::Etp.Reporting.Application.Registers.DigitalRegisterEntry>().FirstOrDefault(row => row.Id == id);
                    break;
            }
        }
        catch (Exception ex) { DesktopDiagnostics.Record(ex, "Investigation.Navigation", "RESULT_OPEN_FAILED"); window.ApplicationStatus.Text = DesktopFriendlyError.Describe(ex); }
    }

    private async Task OpenInvestigationReportAsync(string code, string reference)
    {
        await window.reportsWorkspaceView.OpenInvestigationAsync(code, reference);
        if (window.shell.CurrentRoute.FeatureCode != code || window.FocusedWorkspaceHost.Content is not System.Windows.DependencyObject content) return;
        var filter = LogicalChildren(content).OfType<Modules.Reports.ReportDetailFilter>().FirstOrDefault();
        if (filter is not null) filter.Search.Text = reference;
        var grid = LogicalChildren(content).OfType<DataGrid>().FirstOrDefault();
        if (grid is { Items.Count: > 0 }) grid.SelectedIndex = 0;
    }
}
