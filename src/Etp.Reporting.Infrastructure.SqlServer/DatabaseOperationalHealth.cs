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
    IReadOnlyList<OperationalHealthWarning> Warnings)
{
    public string? LastSuccessfulBackupSha256 { get; init; }
    public DateTime? LastSuccessfulRecoveryDrillUtc { get; init; }
    public string? LastSuccessfulRecoveryDrillSha256 { get; init; }
}

public sealed record DatabaseOperationalHealthThresholds(
    TimeSpan MaximumBackupAge,
    decimal DatabaseSizeWarningPercent,
    int FailedImportWarningCount,
    decimal BackupFreeSpaceWarningGb,
    decimal BackupFreeSpaceCriticalGb)
{
    public TimeSpan MaximumRecoveryDrillAge { get; init; } = TimeSpan.FromDays(45);

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
        decimal? backupFreeSpaceGb = null,
        string? lastBackupSha256 = null,
        DateTime? lastRecoveryDrillUtc = null,
        string? lastRecoveryDrillSha256 = null,
        bool monitorRecoveryDrill = false)
    {
        thresholds ??= DatabaseOperationalHealthThresholds.Default;
        var warnings = new List<OperationalHealthWarning>();

        if (lastBackupUtc is null)
            warnings.Add(new("BACKUP_MISSING", OperationalHealthSeverity.Critical, "No verified encrypted database backup is recorded."));
        else if (nowUtc - lastBackupUtc.Value > thresholds.MaximumBackupAge)
            warnings.Add(new("BACKUP_STALE", OperationalHealthSeverity.Warning, "The latest full database backup is older than the configured limit."));

        if (monitorRecoveryDrill && lastRecoveryDrillUtc is null)
            warnings.Add(new("RECOVERY_DRILL_MISSING", OperationalHealthSeverity.Warning, "No successful receipt-verified recovery drill is recorded."));
        else if (monitorRecoveryDrill && nowUtc - lastRecoveryDrillUtc > thresholds.MaximumRecoveryDrillAge)
            warnings.Add(new("RECOVERY_DRILL_STALE", OperationalHealthSeverity.Warning, "The latest receipt-verified recovery drill is older than the configured limit."));

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
        return new(severity, sizeMb, maxSizeMb, lastBackupUtc, failedImports, backupFreeSpaceGb, warnings)
        {
            LastSuccessfulBackupSha256 = lastBackupSha256,
            LastSuccessfulRecoveryDrillUtc = lastRecoveryDrillUtc,
            LastSuccessfulRecoveryDrillSha256 = lastRecoveryDrillSha256
        };
    }
}

public sealed class DatabaseOperationalHealthRepository(string connectionString)
{
    private const string Sql = "EXEC dbo.load_database_operational_health;";

    public async Task<DatabaseOperationalHealth> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("A SQL Server connection string is required.");
        await using var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(Sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Database health metrics were not returned.");
        var size = reader.GetDecimal(0);
        decimal? max = reader.IsDBNull(1) ? null : reader.GetDecimal(1);
        DateTime? backup = reader.IsDBNull(2) ? null : DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc);
        var failures = reader.GetInt32(3);
        string? backupHash = reader.IsDBNull(4) ? null : reader.GetString(4);
        DateTime? drill = reader.IsDBNull(5) ? null : DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc);
        string? drillHash = reader.IsDBNull(6) ? null : reader.GetString(6);
        decimal? backupFreeSpaceGb = null;
        var backupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "Backups");
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(backupDirectory));
            if (!string.IsNullOrWhiteSpace(root)) backupFreeSpaceGb = Math.Round((decimal)new DriveInfo(root).AvailableFreeSpace / 1024m / 1024m / 1024m, 2);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return DatabaseOperationalHealthEvaluator.Evaluate(size, max, backup, failures, DateTime.UtcNow,
            backupFreeSpaceGb: backupFreeSpaceGb, lastBackupSha256: backupHash,
            lastRecoveryDrillUtc: drill, lastRecoveryDrillSha256: drillHash, monitorRecoveryDrill: true);
    }
}
