extern alias EtpApplication;

using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Modules.Dashboard;

using DashboardSnapshot = EtpApplication::Etp.Reporting.Application.Dashboard.DashboardSnapshot;

public sealed record DashboardManagementSummary(ExcelReportMetadata Metadata, ExcelReportData Data);

/// <summary>Owns the latest dashboard snapshot, its view state, and management-summary projection.</summary>
public sealed class DashboardPresentationSession
{
    private DashboardSnapshot? snapshot;

    public DashboardViewState? Current { get; private set; }
    public bool HasSnapshot => snapshot is not null;

    public DashboardViewState Show(DashboardSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        snapshot = value;
        Current = DashboardViewState.FromSnapshot(value);
        return Current;
    }

    public DashboardViewState ShowError(string message)
    {
        Current = DashboardViewState.Error(message, Current);
        return Current;
    }

    public DashboardManagementSummary BuildManagementSummary(
        DateOnly dateFrom,
        DateOnly dateTo,
        DateTimeOffset generatedUtc)
    {
        if (snapshot is null) throw new InvalidOperationException("Refresh the dashboard before exporting a management summary.");
        var groups = snapshot.RecentImports.GroupBy(x => x.ReportCode).OrderBy(x => x.Key).ToArray();
        return new(
            new("ETP Management Summary", dateFrom, dateTo, "Operational", "v1",
                "Aggregate operational evidence only; confidential source rows are excluded.", generatedUtc),
            new(
                [new("Report"), new("Files", "#,##0"), new("Rows", "#,##0")],
                groups.Select(x => (IReadOnlyList<object?>)[x.Key, x.Count(), x.Sum(v => v.SourceRows)]).ToArray(),
                ["Total", snapshot.ImportedFiles, snapshot.SourceRows]));
    }
}
