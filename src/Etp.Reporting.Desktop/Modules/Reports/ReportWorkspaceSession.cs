using System.Collections;
using System.Windows;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Modules.Reports;

/// <summary>Owns cached focused-report controls and coordinates their presentation state.</summary>
public sealed class ReportWorkspaceSession
{
    private readonly Dictionary<string, ReportWorkspaceControl> workspaces = new(StringComparer.OrdinalIgnoreCase);
    private DailySalesReportWorkspace? dailySalesWorkspace;

    public FrameworkElement Activate(
        string reportCode,
        DateTime? dateFrom,
        DateTime? dateTo,
        DateTime businessDate,
        EventHandler<ReportWorkspaceActionRequest> actionRequested,
        Action<string, ReportWorkspaceControl> reportSelected,
        string? storeScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportCode);
        ArgumentNullException.ThrowIfNull(actionRequested);
        ArgumentNullException.ThrowIfNull(reportSelected);
        if (reportCode.Equals("dsr", StringComparison.OrdinalIgnoreCase))
        {
            if (dailySalesWorkspace is null)
            {
                dailySalesWorkspace = new DailySalesReportWorkspace();
                dailySalesWorkspace.ActionRequested += actionRequested;
            }
            dailySalesWorkspace.BusinessDatePicker.SelectedDate = businessDate;
            dailySalesWorkspace.ScopeSelector.SelectedIndex = 0;
            dailySalesWorkspace.ScopeSelector.IsEnabled = false;
            dailySalesWorkspace.ScopeSelector.ToolTip = "DSR always compares Titan and Helios, including combined totals. Use a store sales report for a single store.";
            dailySalesWorkspace.ShowLoading();
            return dailySalesWorkspace;
        }

        var definition = ReportWorkspaceDefinition.ForReport(reportCode);
        if (!workspaces.TryGetValue(definition.Id, out var workspace))
        {
            workspace = new ReportWorkspaceControl(definition);
            workspace.ReportSelected += (_, selected) => reportSelected(selected.Code, workspace);
            workspace.ActionRequested += actionRequested;
            workspaces.Add(definition.Id, workspace);
        }
        workspace.DateFromPicker.SelectedDate = dateFrom;
        workspace.DateToPicker.SelectedDate = dateTo;
        workspace.ScopeSelector.IsEnabled = reportCode is not ("sales-titan" or "sales-helios" or "sales-combined");
        workspace.ScopeSelector.ToolTip = workspace.ScopeSelector.IsEnabled ? "Report store scope" : "This report has a fixed store scope shown in its title.";
        workspace.SelectReport(reportCode);
        workspace.ConfigureTaskScope(storeScope);
        workspace.ShowLoading($"Loading {ProductReportCatalogue.All.Single(x => x.Code.Equals(reportCode, StringComparison.OrdinalIgnoreCase)).Name}…");
        return workspace;
    }

    public void UpdatePreview(ReportPresentationSnapshot snapshot, IEnumerable? rows, string status, Action<object>? showDetails = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ExportMetadata is null)
        {
            if (snapshot.ReportCode == "dsr") dailySalesWorkspace?.ShowFailure(status);
            else if (snapshot.ReportCode is { } code && workspaces.TryGetValue(ReportWorkspaceDefinition.ForReport(code).Id,out var pending)) pending.ShowUnavailable("Report not ready",status);
            return;
        }
        if (snapshot.DailySalesReport is not null && dailySalesWorkspace is not null)
        {
            dailySalesWorkspace.SetReport(snapshot.DailySalesReport);
            return;
        }
        if (snapshot.ReportCode is null || snapshot.VisualReport is null) return;
        var definition = ReportWorkspaceDefinition.ForReport(snapshot.ReportCode);
        if (!workspaces.TryGetValue(definition.Id, out var workspace)) return;
        workspace.SetPreview(ReportVisualPresenter.BuildFocusedPreview(snapshot.VisualReport, rows, showDetails), status);
    }

    public void ShowDailySalesFailure(string message) => dailySalesWorkspace?.ShowFailure(message);

    public bool FocusPrimaryPeriod(string? reportCode)
    {
        if (string.Equals(reportCode, "dsr", StringComparison.Ordinal) && dailySalesWorkspace is not null)
        {
            return false; // The date now lives in the shell header.
        }
        if (reportCode is null) return false;
        var definition = ReportWorkspaceDefinition.ForReport(reportCode);
        if (!workspaces.TryGetValue(definition.Id, out var workspace)) return false;
        if (!workspace.DateFromPicker.IsEnabled) return false;
        workspace.FocusPeriod();
        return true;
    }
}
