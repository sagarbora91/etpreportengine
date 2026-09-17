extern alias EtpApplication;

using System.Globalization;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.Desktop.Modules.OperationsAdministration;

public sealed record DatabaseRecoveryLine(string Item, string Status, string Detail);

/// <summary>
/// R4. Turns the operational health record into the Database and recovery block.
/// The rule that matters: evidence that is absent reads <c>Missing</c> and evidence
/// that is too old reads <c>Stale (n days)</c>. Neither may ever read as healthy,
/// because an owner who sees "Healthy" beside a backup that was never taken has been
/// told the opposite of the truth.
/// </summary>
public static class DatabaseRecoveryPresentation
{
    public const string Missing = "Missing";

    public static string Describe(DateTime? recordedUtc, TimeSpan maximumAge, DateTime utcNow)
    {
        if (recordedUtc is not { } moment) return Missing;
        var age = utcNow - moment;
        if (age > maximumAge)
        {
            var days = Math.Max(1, (int)Math.Floor(age.TotalDays));
            return $"Stale ({days} {(days == 1 ? "day" : "days")})";
        }
        return moment.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture);
    }

    public static IReadOnlyList<DatabaseRecoveryLine> Lines(
        DatabaseOperationalHealth? health,
        DatabaseOperationalHealthThresholds thresholds,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        if (health is null)
            return [new("Database and recovery", Missing, "Health could not be read from the database.")];

        var lines = new List<DatabaseRecoveryLine>
        {
            new("Database size",
                $"{health.DatabaseSizeMb:N0} MB",
                health.DatabaseMaxSizeMb is { } limit
                    ? $"Edition limit {limit:N0} MB"
                    : "No edition size limit"),

            new("Backup folder free space",
                health.BackupFreeSpaceGb is { } free ? $"{free:N1} GB" : Missing,
                health.BackupFreeSpaceGb is null
                    ? "The backup folder could not be read."
                    : $"Warn below {thresholds.BackupFreeSpaceWarningGb:N0} GB, critical below {thresholds.BackupFreeSpaceCriticalGb:N0} GB"),

            new("Last successful import",
                // An import is a daily event; anything older than a day is worth saying so.
                Describe(health.LastSuccessfulImportUtc, TimeSpan.FromDays(1), utcNow),
                health.LastSuccessfulImportUtc is null
                    ? "No completed import has been recorded."
                    : $"{health.LastSuccessfulImportFiles:N0} file(s)"),

            new("Last verified backup",
                Describe(health.LastSuccessfulBackupUtc, thresholds.MaximumBackupAge, utcNow),
                Fingerprint(health.LastSuccessfulBackupSha256, "No verified backup receipt has been recorded.")),

            new("Last verified recovery drill",
                Describe(health.LastSuccessfulRecoveryDrillUtc, thresholds.MaximumRecoveryDrillAge, utcNow),
                Fingerprint(health.LastSuccessfulRecoveryDrillSha256, "No verified recovery drill has been recorded.")),

            new("Failed imports, last 24 hours",
                health.FailedImportsLast24Hours.ToString("N0", CultureInfo.CurrentCulture),
                health.FailedImportsLast24Hours == 0 ? "None" : "Open Import, then Problems, to see them.")
        };

        foreach (var warning in health.Warnings)
            lines.Add(new("Warning", warning.Severity.ToString(), warning.Message));

        return lines;
    }

    private static string Fingerprint(string? sha256, string absent) =>
        string.IsNullOrWhiteSpace(sha256) ? absent : "Fingerprint " + sha256[..Math.Min(16, sha256.Length)];
}
