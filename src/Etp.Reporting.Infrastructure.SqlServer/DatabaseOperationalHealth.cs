using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public enum OperationalHealthSeverity { Healthy, Warning, Critical }

public sealed record OperationalHealthWarning(string Code, OperationalHealthSeverity Severity, string Message);

public sealed record DatabaseOperationalHealth(
    OperationalHealthSeverity Severity,
    decimal DatabaseSizeMb,
    decimal? DatabaseMaxSizeMb,
    DateTime? LastSuccessfulBackupUtc,
    int FailedImportsLast24Hours,
    decimal? BackupFreeSpaceGb,
    IReadOnlyList<OperationalHealthWarning> Warnings);

public sealed record DatabaseOperationalHealthThresholds(
    TimeSpan MaximumBackupAge,
    decimal DatabaseSizeWarningPercent,
    int FailedImportWarningCount,
    decimal BackupFreeSpaceWarningGb,
    decimal BackupFreeSpaceCriticalGb)
{
    public static DatabaseOperationalHealthThresholds Default { get; } =
        new(TimeSpan.FromHours(36), 80m, 1, 20m, 5m);
}

public static class DatabaseOperationalHealthEvaluator
{
    public static DatabaseOperationalHealth Evaluate(
        decimal sizeMb,
        decimal? maxSizeMb,
        DateTime? lastBackupUtc,
        int failedImports,
        DateTime nowUtc,
        DatabaseOperationalHealthThresholds? thresholds = null,
        decimal? backupFreeSpaceGb = null)
    {
        thresholds ??= DatabaseOperationalHealthThresholds.Default;
        var warnings = new List<OperationalHealthWarning>();

        if (lastBackupUtc is null)
            warnings.Add(new("BACKUP_MISSING", OperationalHealthSeverity.Critical, "No successful full database backup is recorded."));
        else if (nowUtc - lastBackupUtc.Value > thresholds.MaximumBackupAge)
            warnings.Add(new("BACKUP_STALE", OperationalHealthSeverity.Warning, "The latest full database backup is older than the configured limit."));

        if (maxSizeMb is > 0 && sizeMb / maxSizeMb.Value * 100m >= thresholds.DatabaseSizeWarningPercent)
            warnings.Add(new("DATABASE_GROWTH", OperationalHealthSeverity.Warning, "Database files have reached the configured size warning threshold."));

        if (failedImports >= thresholds.FailedImportWarningCount)
            warnings.Add(new("FAILED_IMPORTS", OperationalHealthSeverity.Warning, "One or more imports failed in the last 24 hours."));

        if (backupFreeSpaceGb < thresholds.BackupFreeSpaceCriticalGb)
            warnings.Add(new("BACKUP_SPACE_CRITICAL", OperationalHealthSeverity.Critical, "Backup storage has less than 5 GB free. Add storage before the next backup."));
        else if (backupFreeSpaceGb < thresholds.BackupFreeSpaceWarningGb)
            warnings.Add(new("BACKUP_SPACE_LOW", OperationalHealthSeverity.Warning, "Backup storage has less than 20 GB free. Plan additional storage."));

        var severity = warnings.Any(x => x.Severity == OperationalHealthSeverity.Critical)
            ? OperationalHealthSeverity.Critical
            : warnings.Count > 0 ? OperationalHealthSeverity.Warning : OperationalHealthSeverity.Healthy;
        return new(severity, sizeMb, maxSizeMb, lastBackupUtc, failedImports, backupFreeSpaceGb, warnings);
    }
}

public sealed class DatabaseOperationalHealthRepository(string connectionString)
{
    private const string Sql = """
        SELECT
          CAST(SUM(size) * 8.0 / 1024.0 AS decimal(18,2)) AS size_mb,
          CAST(CASE WHEN SUM(CASE WHEN max_size=-1 THEN 1 ELSE 0 END)>0 THEN NULL
                    ELSE SUM(max_size) * 8.0 / 1024.0 END AS decimal(18,2)) AS max_size_mb,
          (SELECT MAX(backup_finish_date) FROM msdb.dbo.backupset
             WHERE database_name=DB_NAME() AND type='D' AND is_copy_only IN (0,1)),
          (SELECT COUNT(*) FROM dbo.import_batches
             WHERE status='Failed' AND started_utc >= DATEADD(hour,-24,SYSUTCDATETIME()))
        FROM sys.database_files;
        """;

    public async Task<DatabaseOperationalHealth> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("A SQL Server connection string is required.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(Sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Database health metrics were not returned.");
        var size = reader.GetDecimal(0);
        decimal? max = reader.IsDBNull(1) ? null : reader.GetDecimal(1);
        DateTime? backup = reader.IsDBNull(2) ? null : DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Local).ToUniversalTime();
        var failures = reader.GetInt32(3);
        decimal? backupFreeSpaceGb = null;
        var backupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "Backups");
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(backupDirectory));
            if (!string.IsNullOrWhiteSpace(root)) backupFreeSpaceGb = Math.Round((decimal)new DriveInfo(root).AvailableFreeSpace / 1024m / 1024m / 1024m, 2);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return DatabaseOperationalHealthEvaluator.Evaluate(size, max, backup, failures, DateTime.UtcNow, backupFreeSpaceGb: backupFreeSpaceGb);
    }
}
