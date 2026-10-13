using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

namespace Etp.Reporting.Infrastructure.SqlServer.Morning;

/// <summary>
/// The Morning page's quiet backup line (1.12 U1): the age of the newest off-PC copy, by the same rule as OPS-10
/// (<see cref="OffPcCopyProbe"/>): the newest <c>*.bak.enc</c> in <c>OneDrive\ETP-Backups</c>. A PC without OneDrive or
/// without that folder makes no off-PC copy (Configured false). Anything unreadable is "not configured", never an error.
/// </summary>
public static class OffPcBackupAge
{
    public static (bool Configured, double? AgeHours) Read(IOffPcCopyEnvironment environment, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(environment);
        try
        {
            var root = environment.OneDriveRoot();
            if (string.IsNullOrWhiteSpace(root)) return (false, null);
            var folder = Path.Combine(root, OffPcCopyProbe.BackupFolderName);
            if (!Directory.Exists(folder)) return (false, null);
            var newest = Directory.EnumerateFiles(folder, "*.bak.enc").Select(path => File.GetLastWriteTimeUtc(path))
                .OrderDescending().Cast<DateTime?>().FirstOrDefault();
            if (newest is null) return (true, null);
            var hours = (nowUtc - new DateTimeOffset(newest.Value, TimeSpan.Zero)).TotalHours;
            return (true, Math.Max(0, Math.Round(hours, 1)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return (false, null);
        }
    }
}
