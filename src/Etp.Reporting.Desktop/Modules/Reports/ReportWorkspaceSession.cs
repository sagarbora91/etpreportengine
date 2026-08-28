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
        Action<string, ReportWorkspaceControl> reportSelected)
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
            dailySalesWorkspace.ShowLoading();
            return dailySalesWorkspace;
        }

        var definition = ReportWorkspaceRegistry.ForReport(reportCode);
        if (!workspaces.TryGetValue(definition.Id, out var workspace))
        {
            workspace = new ReportWorkspaceControl(definition);
            workspace.ReportSelected += (_, selected) => reportSelected(selected.Code, workspace);
            workspace.ActionRequested += actionRequested;
            workspaces.Add(definition.Id, workspace);
        }
        workspace.DateFromPicker.SelectedDate = dateFrom;
        workspace.DateToPicker.SelectedDate = dateTo;
        workspace.SelectReport(reportCode);
        workspace.ShowLoading($"Loading {ProductReportCatalogue.All.Single(x => x.Code.Equals(reportCode, StringComparison.OrdinalIgnoreCase)).Name}…");
        return workspace;
    }

    public void UpdatePreview(ReportPresentationSnapshot snapshot, IEnumerable? rows, string status)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ExportMetadata is null) return;
        if (snapshot.DailySalesReport is not null && dailySalesWorkspace is not null)
        {
            dailySalesWorkspace.SetReport(snapshot.DailySalesReport);
            return;
        }
        if (snapshot.ReportCode is null || snapshot.VisualReport is null) return;
        var definition = ReportWorkspaceRegistry.ForReport(snapshot.ReportCode);
        if (!workspaces.TryGetValue(definition.Id, out var workspace)) return;
        workspace.SetPreview(ReportVisualPresenter.BuildFocusedPreview(snapshot.VisualReport, rows), status);
    }

    public void ShowDailySalesFailure(string message) => dailySalesWorkspace?.ShowFailure(message);

    public bool FocusPrimaryPeriod(string? reportCode)
    {
        if (string.Equals(reportCode, "dsr", StringComparison.Ordinal) && dailySalesWorkspace is not null)
        {
            dailySalesWorkspace.BusinessDatePicker.Focus();
            return true;
        }
        if (reportCode is null) return false;
        var definition = ReportWorkspaceRegistry.ForReport(reportCode);
        if (!workspaces.TryGetValue(definition.Id, out var workspace)) return false;
        workspace.DateFromPicker.Focus();
        return true;
    }
}
