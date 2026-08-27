extern alias EtpApplication;

namespace Etp.Reporting.Desktop;

public sealed record DashboardImportActivity(
    string FileName,
    string ReportCode,
    string Status,
    int SourceRows,
    DateTime StartedUtc,
    DateTime? CompletedUtc);

public sealed record DashboardHealthWarning(string Code, string Message);

public sealed record DashboardHealthSnapshot(
    string Severity,
    decimal DatabaseSizeMb,
    DateTime? LastSuccessfulBackupUtc,
    int FailedImportsLast24Hours,
    decimal? BackupFreeSpaceGb,
    IReadOnlyList<DashboardHealthWarning> Warnings);

public sealed record DashboardChartItem(string ReportCode, long SourceRows);

public enum DashboardHealthTone
{
    Healthy,
    Warning,
    Critical
}

public sealed record DashboardViewState(
    string ImportedFiles,
    string CompletedBatches,
    string SourceRows,
    string LatestImport,
    IReadOnlyList<DashboardImportActivity> RecentImports,
    IReadOnlyList<DashboardChartItem> ImportedRowsByReport,
    string DatabaseHealth,
    DashboardHealthTone DatabaseHealthTone,
    string DatabaseSize,
    string LatestBackup,
    string BackupFreeSpace,
    string FailedImports,
    IReadOnlyList<string> HealthWarnings,
    IReadOnlyList<object> RecentAuditEvents,
    string? ErrorMessage = null)
{
    public static DashboardViewState FromSnapshot(EtpApplication::Etp.Reporting.Application.Dashboard.DashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var imports = snapshot.RecentImports.Select(item => new DashboardImportActivity(
            item.FileName, item.ReportCode, item.Status, item.SourceRows, item.StartedUtc, item.CompletedUtc)).ToArray();
        var health = new DashboardHealthSnapshot(
            snapshot.Health.Severity.ToString(),
            snapshot.Health.DatabaseSizeMb,
            snapshot.Health.LastSuccessfulBackupUtc,
            snapshot.Health.FailedImportsLast24Hours,
            snapshot.Health.BackupFreeSpaceGb,
            snapshot.Health.Warnings.Select(warning => new DashboardHealthWarning(warning.Code, warning.Message)).ToArray());
        return Create(
            snapshot.ImportedFiles,
            snapshot.CompletedBatches,
            snapshot.SourceRows,
            snapshot.LatestImportUtc,
            imports,
            health,
            snapshot.RecentAudit.Cast<object>().ToArray());
    }

    public static DashboardViewState Create(
        int importedFiles,
        int completedBatches,
        long sourceRows,
        DateTime? latestImportUtc,
        IReadOnlyList<DashboardImportActivity> recentImports,
        DashboardHealthSnapshot health,
        IReadOnlyList<object> recentAuditEvents)
    {
        ArgumentNullException.ThrowIfNull(recentImports);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(recentAuditEvents);

        var chart = recentImports
            .GroupBy(item => item.ReportCode, StringComparer.Ordinal)
            .Select(group => new DashboardChartItem(group.Key, group.Sum(item => (long)item.SourceRows)))
            .OrderByDescending(item => item.SourceRows)
            .ToArray();
        var tone = health.Severity switch
        {
            "Healthy" => DashboardHealthTone.Healthy,
            "Warning" => DashboardHealthTone.Warning,
            _ => DashboardHealthTone.Critical
        };

        return new(
            importedFiles.ToString("N0"),
            completedBatches.ToString("N0"),
            sourceRows.ToString("N0"),
            latestImportUtc?.ToString("dd MMM yyyy HH:mm") ?? "None",
            recentImports,
            chart,
            health.Severity,
            tone,
            $"{health.DatabaseSizeMb:N2} MB",
            health.LastSuccessfulBackupUtc?.ToString("dd MMM yyyy HH:mm") ?? "Missing",
            health.BackupFreeSpaceGb is { } freeGb ? $"{freeGb:N2} GB" : "Unavailable",
            health.FailedImportsLast24Hours.ToString("N0"),
            health.Warnings.Select(warning => $"{warning.Code}: {warning.Message}").ToArray(),
            recentAuditEvents);
    }

    public static DashboardViewState Error(string message, DashboardViewState? previous = null) => new(
        "-", "-", "-", "Unavailable", previous?.RecentImports ?? [], previous?.ImportedRowsByReport ?? [],
        "Unavailable", DashboardHealthTone.Critical, "Unavailable", "Unavailable", "Unavailable", "Unavailable",
        [], [], message);
}
