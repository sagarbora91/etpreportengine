using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Etp.Reporting.Application.HealthCheck;

namespace Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

/// <summary>
/// What the disk copy task registered: <c>%ProgramData%\EtpReporting\Operations\disk-copy.json</c>, written by
/// <c>install-daily-backup-task.ps1 -DiskCopyRoot</c> only after the task exists.
/// </summary>
public sealed record DiskCopyConfig(string DiskCopyRoot, string BackupFolder, string Database);

/// <summary>What <see cref="DiskBackupProbe"/> needs from the PC; tests substitute it.</summary>
public interface IDiskBackupEnvironment
{
    /// <summary>Null when the disk copy has not been set up on this PC. May throw <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.</summary>
    DiskCopyConfig? ReadConfig();
}

public sealed class WindowsDiskBackupEnvironment : IDiskBackupEnvironment
{
    public static string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "Operations", "disk-copy.json");

    public DiskCopyConfig? ReadConfig()
    {
        if (!File.Exists(ConfigPath)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            var root = document.RootElement;
            var disk = Text(root, "diskCopyRoot");
            if (string.IsNullOrWhiteSpace(disk)) return null;
            var backups = Text(root, "backupFolder");
            if (string.IsNullOrWhiteSpace(backups))
                backups = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "Backups");
            var database = Text(root, "database");
            return new DiskCopyConfig(disk, backups, string.IsNullOrWhiteSpace(database) ? "EtpReporting" : database);
        }
        catch (JsonException)
        {
            throw new IOException("disk-copy.json is not valid JSON.");
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// OPS-11 "Disk backup copy age" (1.12 G1, decision 54). On the PC that copies the verified backup to a second disk
/// (<c>copy-etp-backup-to-disk.ps1</c>): Pass when <c>latest-disk-copy.json</c> on that disk is under 36 hours old, its
/// file is there and its SHA-256 equals the receipt of the newest verified backup; Warning when older, when the disk is
/// not present, when there is no copy yet or when the copy is not the newest backup; never Fail. A PC where the task
/// was never set up is Skipped (NOT_CONFIGURED). Needs no database. Read-only.
/// </summary>
public sealed class DiskBackupProbe(IDiskBackupEnvironment environment, TimeProvider? timeProvider = null) : IHealthProbe
{
    public const int MaximumCopyAgeHours = 36;

    /// <summary>
    /// A newer verified backup than the copy is only a finding once this many hours have passed since that backup was
    /// verified (the copy runs after the 22:00 backup, and after a 14:00 one if the Owner chose both).
    /// </summary>
    public const int MatchGraceHours = 12;

    public const string ReceiptFileName = "latest-disk-copy.json";

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public IReadOnlyList<string> CheckIds { get; } = [HealthCheckId.Ops11];

    public Task<IReadOnlyList<HealthCheckResult>> RunAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            return Task.FromResult<IReadOnlyList<HealthCheckResult>>([Check(timer)]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Task.FromResult<IReadOnlyList<HealthCheckResult>>([Skipped("NOT_READABLE", "The disk backup copy could not be inspected on this PC.", timer.Elapsed)]);
        }
    }

    private HealthCheckResult Check(Stopwatch timer)
    {
        var config = environment.ReadConfig();
        if (config is null)
            return Skipped(HealthCheckTexts.DiskNotConfigured, HealthCheckTexts.DiskSkippedNotConfigured, timer.Elapsed);

        var driveRoot = Path.GetPathRoot(Path.GetFullPath(config.DiskCopyRoot));
        if (string.IsNullOrEmpty(driveRoot) || !Directory.Exists(driveRoot))
            return Make(HealthOutcome.Warning, HealthCheckTexts.DiskNotPresent, HealthCheckTexts.DiskNotPresentText, timer.Elapsed, null,
                [new("DISK", HealthOutcome.Warning, HealthCheckTexts.DiskNotPresent)]);

        var receiptPath = Path.Combine(config.DiskCopyRoot, ReceiptFileName);
        if (!File.Exists(receiptPath))
            return Make(HealthOutcome.Warning, HealthCheckTexts.DiskCopyMissing, HealthCheckTexts.DiskCopyNone, timer.Elapsed, null,
                [new("COPY", HealthOutcome.Warning, HealthCheckTexts.DiskCopyMissing)]);

        var copy = ReadReceipt(receiptPath);
        if (copy is null || (copy.RelativePath is { } relative && !CopyFileExists(config.DiskCopyRoot, relative)))
            return Make(HealthOutcome.Warning, HealthCheckTexts.DiskCopyUnreadable, HealthCheckTexts.DiskCopyUnreadableText, timer.Elapsed, null,
                [new("COPY", HealthOutcome.Warning, HealthCheckTexts.DiskCopyUnreadable)]);

        var now = clock.GetUtcNow();
        var hours = (decimal)Math.Round((now - copy.CopiedUtc).TotalHours, 1);
        var subs = new List<HealthCheckSubResult>
        {
            hours > MaximumCopyAgeHours
                ? new("COPY_AGE", HealthOutcome.Warning, HealthCheckTexts.DiskCopyOld, hours)
                : new("COPY_AGE", HealthOutcome.Pass, null, hours)
        };

        var latest = ReadVerifiedReceipt(Path.Combine(config.BackupFolder, config.Database + "-latest-verified.json"));
        if (latest is null)
            subs.Add(new("BACKUP_MATCH", HealthOutcome.Skipped, "NOT_READABLE"));
        else if (string.Equals(latest.Sha256, copy.Sha256, StringComparison.OrdinalIgnoreCase))
            subs.Add(new("BACKUP_MATCH", HealthOutcome.Pass));
        else if (latest.VerifiedUtc is { } verified && now - verified < TimeSpan.FromHours(MatchGraceHours))
            subs.Add(new("BACKUP_MATCH", HealthOutcome.Pass));
        else
            subs.Add(new("BACKUP_MATCH", HealthOutcome.Warning, HealthCheckTexts.DiskCopyNotLatest));

        var bad = subs.Where(sub => sub.Outcome == HealthOutcome.Warning).ToList();
        if (bad.Count == 0)
            return Make(HealthOutcome.Pass, HealthCheckTexts.Ok, HealthCheckTexts.DiskCopyCurrent(hours), timer.Elapsed, hours, subs);
        var text = bad[0].FindingCode == HealthCheckTexts.DiskCopyOld
            ? HealthCheckTexts.DiskCopyTooOld(hours, MaximumCopyAgeHours)
            : HealthCheckTexts.DiskCopyNotLatestText;
        return Make(HealthOutcome.Warning, bad[0].FindingCode!, text, timer.Elapsed, hours, subs);
    }

    private sealed record CopyReceipt(string Sha256, DateTimeOffset CopiedUtc, string? RelativePath);

    private sealed record VerifiedReceipt(string Sha256, DateTimeOffset? VerifiedUtc);

    private static CopyReceipt? ReadReceipt(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var sha = Text(root, "sha256");
            var copied = Time(root, "copiedAtUtc");
            if (string.IsNullOrWhiteSpace(sha) || copied is null) return null;
            var relative = Text(root, "relativePath");
            return new CopyReceipt(sha, copied.Value, string.IsNullOrWhiteSpace(relative) ? null : relative);
        }
        catch (JsonException) { return null; }
    }

    private static VerifiedReceipt? ReadVerifiedReceipt(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var sha = Text(document.RootElement, "sha256");
            return string.IsNullOrWhiteSpace(sha) ? null : new VerifiedReceipt(sha, Time(document.RootElement, "verifiedAtUtc"));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The file named by the receipt must still be on the disk (a receipt without its file is not a copy).</summary>
    private static bool CopyFileExists(string root, string relative)
    {
        if (relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) return false;
        return File.Exists(Path.Combine(root, relative));
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? Time(JsonElement element, string name) =>
        Text(element, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static HealthCheckResult Make(HealthOutcome outcome, string code, string text, TimeSpan duration, decimal? metric, IReadOnlyList<HealthCheckSubResult> subs) =>
        ImportDataProbeSupport.Make(HealthCheckId.Ops11, outcome, code, text, null, duration, metric, subs);

    private static HealthCheckResult Skipped(string code, string text, TimeSpan duration) =>
        ImportDataProbeSupport.Skipped(HealthCheckId.Ops11, code, text, duration);
}
