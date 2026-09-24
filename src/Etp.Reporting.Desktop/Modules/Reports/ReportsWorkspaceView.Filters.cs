using System.Windows;
using System.Windows.Controls;

namespace Etp.Reporting.Desktop.Modules.Reports;

public partial class ReportsWorkspaceView
{
    private ReportWorkspaceControl? filterWorkspace;
    private string FilterSignature => string.Join("|", StoreFilterInput.Text, BrandSegmentFilterInput.Text, TransactionTypeFilterInput.Text, ItemFilterInput.Text);

    // Attach on every activation: the focused report is a different visual tree
    // from the legacy workspace, and cached report headers must not own stale panels.
    public void AttachQueryFilters(ReportWorkspaceControl workspace)
    {
        filterWorkspace = workspace;
        if (QueryFiltersPanel.Parent is Panel panel) panel.Children.Remove(QueryFiltersPanel);
        else if (QueryFiltersPanel.Parent is ContentControl host) host.Content = null;
        QueryFiltersPanel.Visibility = Visibility.Visible;
        workspace.AttachQueryFilters(QueryFiltersPanel, () => FilterSignature);
    }

    public void ApplyReportPeriod(DateTime? from, DateTime? to)
    {
        ReportFrom.SelectedDate = from ?? DateTime.Today;
        ReportTo.SelectedDate = to ?? from ?? DateTime.Today;
    }

    private void ConfigureQueryFilters(string report)
    {
        var salesLines = report.StartsWith("sales-", StringComparison.Ordinal) || report == "invoice-lineage";
        Configure(BrandSegmentFilterInput, salesLines || report is "stock-closing" or "stock-brand" or "stock-slow");
        Configure(TransactionTypeFilterInput, salesLines);
        Configure(ItemFilterInput, salesLines || report.StartsWith("stock-", StringComparison.Ordinal) && report != "stock-physical");
        StoreFilterInput.IsEnabled = report is not ("dsr" or "sales-combined");
        void Configure(TextBox input, bool enabled) { input.IsEnabled = enabled; if (!enabled) input.Clear(); }
    }

    private async void ApplyQueryFilters_Click(object sender, RoutedEventArgs e) => await ApplyQueryFiltersAsync();

    private async void ClearQueryFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (var input in new[] { StoreFilterInput, BrandSegmentFilterInput, TransactionTypeFilterInput, ItemFilterInput })
            if (input.IsEnabled) input.Clear();
        await ApplyQueryFiltersAsync();
    }

    private Task ApplyQueryFiltersAsync()
    {
        if (CurrentReportCode is not { } code) return Task.CompletedTask;
        if (filterWorkspace is { } workspace)
            ApplyReportPeriod(workspace.DateFromPicker.SelectedDate, workspace.DateToPicker.SelectedDate);
        return RunReportAsync(code);
    }

    private string AppliedQueryScope()
    {
        var scope = ReportScope();
        static string Values(IReadOnlyList<string>? values) => values is null ? "All" : string.Join(", ", values);
        return $"Applied scope: {scope.DateFrom:yyyy-MM-dd} to {scope.DateTo:yyyy-MM-dd}; Stores: {Values(scope.StoreCodes)}; Brand segments: {Values(scope.BrandSegments)}; Transaction types: {Values(scope.TransactionTypes)}; Items: {Values(scope.ItemCodes)}";
    }

    public async Task OpenInvestigationAsync(string report, string reference)
    {
        BrandSegmentFilterInput.Clear(); TransactionTypeFilterInput.Clear();
        ItemFilterInput.Text = report == "sales-item" ? reference : string.Empty;
        ReportSearchInput.Clear();
        await RunReportAsync(report);
        ApplyReportFilter();
    }
}
