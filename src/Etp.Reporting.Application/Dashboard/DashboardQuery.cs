namespace Etp.Reporting.Application.Dashboard;

public enum DashboardHealthSeverity
{
    Healthy,
    Warning,
    Critical
}

public sealed record DashboardImportHistoryItem(
    string FileName,
    string ReportCode,
    string Status,
    int SourceRows,
    DateTime StartedUtc,
    DateTime? CompletedUtc);

public sealed record DashboardHealthWarning(
    string Code,
    string Message);

public sealed record DashboardHealth(
    DashboardHealthSeverity Severity,
    decimal DatabaseSizeMb,
    DateTime? LastSuccessfulBackupUtc,
    int FailedImportsLast24Hours,
    decimal? BackupFreeSpaceGb,
    IReadOnlyList<DashboardHealthWarning> Warnings);

public sealed record DashboardAuditEvent(
    DateTime EventUtc,
    string EventType,
    string Outcome,
    string? SafeDetail,
    string ApplicationVersion,
    string? ActorName);

public sealed record DashboardSnapshot(
    int ImportedFiles,
    int CompletedBatches,
    long SourceRows,
    DateTime? LatestImportUtc,
    IReadOnlyList<DashboardImportHistoryItem> RecentImports,
    DashboardHealth Health,
    IReadOnlyList<DashboardAuditEvent> RecentAudit);

public interface IDashboardQuery
{
    Task<DashboardSnapshot> LoadAsync(CancellationToken cancellationToken = default);
}
