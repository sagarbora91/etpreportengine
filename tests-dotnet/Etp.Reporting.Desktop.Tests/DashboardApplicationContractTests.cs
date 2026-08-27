using Etp.Reporting.Application.Dashboard;

namespace Etp.Reporting.Desktop.Tests;

public sealed class DashboardApplicationContractTests
{
    [Fact]
    public void Snapshot_preserves_the_current_dashboard_query_shape()
    {
        var started = new DateTime(2026, 8, 27, 9, 30, 0, DateTimeKind.Utc);
        var completed = started.AddMinutes(2);
        var snapshot = new DashboardSnapshot(
            3,
            2,
            125,
            completed,
            [new("sales.xlsx", "R025", "Completed", 125, started, completed)],
            new(
                DashboardHealthSeverity.Warning,
                425.5m,
                started.AddDays(-1),
                1,
                18.75m,
                [new("BACKUP_SPACE_LOW", "Backup storage is approaching its configured limit.")]),
            [new(started, "ImportBatch", "Succeeded", "Aggregate import completed", "1.8.2", "operator")]);

        Assert.Equal(3, snapshot.ImportedFiles);
        Assert.Equal(2, snapshot.CompletedBatches);
        Assert.Equal(125, snapshot.SourceRows);
        Assert.Equal(completed, snapshot.LatestImportUtc);
        Assert.Equal("R025", Assert.Single(snapshot.RecentImports).ReportCode);
        Assert.Equal(DashboardHealthSeverity.Warning, snapshot.Health.Severity);
        Assert.Equal("BACKUP_SPACE_LOW", Assert.Single(snapshot.Health.Warnings).Code);
        Assert.Equal("ImportBatch", Assert.Single(snapshot.RecentAudit).EventType);
    }

    [Fact]
    public async Task Query_contract_forwards_cancellation_without_framework_or_storage_types()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        IDashboardQuery query = new CancellingDashboardQuery();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.LoadAsync(cancellation.Token));

        var method = typeof(IDashboardQuery).GetMethod(nameof(IDashboardQuery.LoadAsync));
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<DashboardSnapshot>), method.ReturnType);
        Assert.Equal(typeof(CancellationToken), Assert.Single(method.GetParameters()).ParameterType);
    }

    private sealed class CancellingDashboardQuery : IDashboardQuery
    {
        public Task<DashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromCanceled<DashboardSnapshot>(cancellationToken);
    }
}
