using Etp.Reporting.Application.Dashboard;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class SqlServerDashboardQuery : IDashboardQuery
{
    private const int AuditLimit = 25;

    private readonly Func<CancellationToken, Task<OperationalSummary>> loadSummary;
    private readonly Func<CancellationToken, Task<DatabaseOperationalHealth>> loadHealth;
    private readonly Func<int, CancellationToken, Task<IReadOnlyList<OperationalAuditEvent>>> loadAudit;

    public SqlServerDashboardQuery(string connectionString)
        : this(
            cancellationToken => new OperationalStatusRepository(connectionString).LoadAsync(cancellationToken),
            cancellationToken => new DatabaseOperationalHealthRepository(connectionString).LoadAsync(cancellationToken),
            (limit, cancellationToken) => new OperationalAuditRepository(connectionString).LoadRecentAsync(limit, cancellationToken))
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString));
    }

    internal SqlServerDashboardQuery(
        Func<CancellationToken, Task<OperationalSummary>> loadSummary,
        Func<CancellationToken, Task<DatabaseOperationalHealth>> loadHealth,
        Func<int, CancellationToken, Task<IReadOnlyList<OperationalAuditEvent>>> loadAudit)
    {
        this.loadSummary = loadSummary ?? throw new ArgumentNullException(nameof(loadSummary));
        this.loadHealth = loadHealth ?? throw new ArgumentNullException(nameof(loadHealth));
        this.loadAudit = loadAudit ?? throw new ArgumentNullException(nameof(loadAudit));
    }

    public async Task<DashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var summaryTask = loadSummary(cancellationToken);
        var healthTask = loadHealth(cancellationToken);
        var auditTask = loadAudit(AuditLimit, cancellationToken);

        await Task.WhenAll(summaryTask, healthTask, auditTask).ConfigureAwait(false);

        var summary = await summaryTask.ConfigureAwait(false);
        var health = await healthTask.ConfigureAwait(false);
        var audit = await auditTask.ConfigureAwait(false);
        return new(
            summary.ImportedFiles,
            summary.CompletedBatches,
            summary.SourceRows,
            summary.LatestImportUtc,
            summary.RecentImports.Select(Map).ToArray(),
            new(
                Map(health.Severity),
                health.DatabaseSizeMb,
                health.LastSuccessfulBackupUtc,
                health.FailedImportsLast24Hours,
                health.BackupFreeSpaceGb,
                health.Warnings.Select(warning => new DashboardHealthWarning(warning.Code, warning.Message)).ToArray()),
            audit.Select(Map).ToArray());
    }

    private static DashboardImportHistoryItem Map(ImportHistoryRow row) =>
        new(row.FileName, row.ReportCode, row.Status, row.SourceRows, row.StartedUtc, row.CompletedUtc);

    private static DashboardAuditEvent Map(OperationalAuditEvent row) =>
        new(row.EventUtc, row.EventType, row.Outcome, row.SafeDetail, row.ApplicationVersion, row.ActorName);

    private static DashboardHealthSeverity Map(OperationalHealthSeverity severity) => severity switch
    {
        OperationalHealthSeverity.Healthy => DashboardHealthSeverity.Healthy,
        OperationalHealthSeverity.Warning => DashboardHealthSeverity.Warning,
        OperationalHealthSeverity.Critical => DashboardHealthSeverity.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Unknown dashboard health severity.")
    };
}
