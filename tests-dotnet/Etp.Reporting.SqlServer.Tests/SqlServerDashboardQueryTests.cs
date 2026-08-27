using Etp.Reporting.Application.Dashboard;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class SqlServerDashboardQueryTests
{
    [Fact]
    public async Task Maps_existing_repository_results_without_losing_dashboard_fields()
    {
        var latestImport = new DateTime(2026, 8, 27, 9, 15, 0, DateTimeKind.Utc);
        var latestBackup = new DateTime(2026, 8, 27, 7, 0, 0, DateTimeKind.Utc);
        var completed = new DateTime(2026, 8, 27, 9, 16, 0, DateTimeKind.Utc);
        var summary = new OperationalSummary(12, 8, 3456, latestImport,
            [new("R025.xlsx", "R025", "Completed", 123, latestImport, completed)]);
        var health = new DatabaseOperationalHealth(OperationalHealthSeverity.Warning, 100.25m, 10240m, latestBackup, 2, 18.5m,
            [new("BACKUP_SPACE_LOW", OperationalHealthSeverity.Warning, "Backup storage is low.")]);
        IReadOnlyList<OperationalAuditEvent> audit =
            [new(latestImport, "ReportRun", "Succeeded", "Aggregate report", "1.2.3", "operator")];
        var observedAuditLimit = 0;
        var query = new SqlServerDashboardQuery(
            _ => Task.FromResult(summary),
            _ => Task.FromResult(health),
            (limit, _) => { observedAuditLimit = limit; return Task.FromResult(audit); });

        var result = await query.LoadAsync();

        Assert.Equal(25, observedAuditLimit);
        Assert.Equal(12, result.ImportedFiles);
        Assert.Equal(8, result.CompletedBatches);
        Assert.Equal(3456, result.SourceRows);
        Assert.Equal(latestImport, result.LatestImportUtc);
        Assert.Equal(new DashboardImportHistoryItem("R025.xlsx", "R025", "Completed", 123, latestImport, completed), Assert.Single(result.RecentImports));
        Assert.Equal(DashboardHealthSeverity.Warning, result.Health.Severity);
        Assert.Equal(100.25m, result.Health.DatabaseSizeMb);
        Assert.Equal(latestBackup, result.Health.LastSuccessfulBackupUtc);
        Assert.Equal(2, result.Health.FailedImportsLast24Hours);
        Assert.Equal(18.5m, result.Health.BackupFreeSpaceGb);
        Assert.Equal(new DashboardHealthWarning("BACKUP_SPACE_LOW", "Backup storage is low."), Assert.Single(result.Health.Warnings));
        Assert.Equal(new DashboardAuditEvent(latestImport, "ReportRun", "Succeeded", "Aggregate report", "1.2.3", "operator"), Assert.Single(result.RecentAudit));
    }

    [Fact]
    public async Task Starts_all_three_repository_reads_before_awaiting_completion()
    {
        var summaryCompletion = new TaskCompletionSource<OperationalSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthCompletion = new TaskCompletionSource<DatabaseOperationalHealth>(TaskCreationOptions.RunContinuationsAsynchronously);
        var auditCompletion = new TaskCompletionSource<IReadOnlyList<OperationalAuditEvent>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var query = new SqlServerDashboardQuery(
            _ => { calls.Add("summary"); return summaryCompletion.Task; },
            _ => { calls.Add("health"); return healthCompletion.Task; },
            (_, _) => { calls.Add("audit"); return auditCompletion.Task; });

        var load = query.LoadAsync();

        Assert.Equal(["summary", "health", "audit"], calls);
        Assert.False(load.IsCompleted);
        summaryCompletion.SetResult(new(0, 0, 0, null, []));
        healthCompletion.SetResult(new(OperationalHealthSeverity.Healthy, 0, null, null, 0, null, []));
        auditCompletion.SetResult([]);
        await load;
    }

    [Fact]
    public async Task Propagates_the_same_cancellation_token_to_every_repository_read()
    {
        using var cancellation = new CancellationTokenSource();
        var observed = new List<CancellationToken>();
        var query = new SqlServerDashboardQuery(
            token => { observed.Add(token); return Task.FromResult(new OperationalSummary(0, 0, 0, null, [])); },
            token => { observed.Add(token); return Task.FromResult(new DatabaseOperationalHealth(OperationalHealthSeverity.Healthy, 0, null, null, 0, null, [])); },
            (_, token) => { observed.Add(token); return Task.FromResult<IReadOnlyList<OperationalAuditEvent>>([]); });

        await query.LoadAsync(cancellation.Token);

        Assert.Equal(3, observed.Count);
        Assert.All(observed, token => Assert.Equal(cancellation.Token, token));
    }

    [Fact]
    public async Task Does_not_return_a_partial_snapshot_when_any_repository_fails()
    {
        var query = new SqlServerDashboardQuery(
            _ => Task.FromResult(new OperationalSummary(1, 1, 1, null, [])),
            _ => Task.FromException<DatabaseOperationalHealth>(new InvalidOperationException("health failed")),
            (_, _) => Task.FromResult<IReadOnlyList<OperationalAuditEvent>>([]));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => query.LoadAsync());

        Assert.Equal("health failed", exception.Message);
    }
}
