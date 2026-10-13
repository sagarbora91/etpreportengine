using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Etp.Reporting.Application.HealthCheck;
using Etp.Reporting.Infrastructure.SqlServer.HealthCheck;

namespace Etp.Reporting.SqlServer.Tests;

/// <summary>
/// 1.12 G1: OPS-11 reads what copy-etp-backup-to-disk.ps1 writes. No SQL: the PowerShell script writes the receipt, the
/// C# probe reads it (Pass / Warning / Skipped). Cross-language test of plan Appendix B item 9.
/// </summary>
public sealed class DiskBackupProbeTests : IDisposable
{
    private const string Database = "EtpReporting";
    private readonly string root = Path.Combine(Path.GetTempPath(), "etp-g1p-" + Guid.NewGuid().ToString("N"));
    private string BackupFolder => Path.Combine(root, "backups");
    private string DiskRoot => Path.Combine(root, "disk", "ETP-Backups");

    public DiskBackupProbeTests() => Directory.CreateDirectory(BackupFolder);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class Env(DiskCopyConfig? config) : IDiskBackupEnvironment
    {
        public DiskCopyConfig? ReadConfig() => config;
    }

    private sealed class ThrowingEnv : IDiskBackupEnvironment
    {
        public DiskCopyConfig? ReadConfig() => throw new UnauthorizedAccessException();
    }

    private sealed class FixedClock(DateTimeOffset utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utc;
    }

    private static readonly HealthCheckContext Context = new(new DateOnly(2026, 10, 11), HealthCheckTrigger.Manual,
        DateTimeOffset.UtcNow, [], null, HealthCheckBudget.Default, new Dictionary<string, object>());

    private DiskCopyConfig Config => new(DiskRoot, BackupFolder, Database);

    private static async Task<HealthCheckResult> Run(DiskBackupProbe probe) => Assert.Single(await probe.RunAsync(Context, default));

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }

    private string NewVerifiedBackup(string stamp, DateTime verifiedUtc)
    {
        var path = Path.Combine(BackupFolder, $"{Database}-{stamp}-{Guid.NewGuid():N}.bak");
        var content = new byte[2048];
        RandomNumberGenerator.Fill(content);
        File.WriteAllBytes(path, content);
        File.WriteAllText(Path.Combine(BackupFolder, $"{Database}-latest-verified.json"), JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["schemaVersion"] = 2, ["verified"] = true, ["database"] = Database, ["backupPath"] = path,
            ["sha256"] = Convert.ToHexString(SHA256.HashData(content)), ["lengthBytes"] = content.Length,
            ["verifiedAtUtc"] = verifiedUtc.ToString("o"), ["encryption"] = "NONE"
        }));
        return path;
    }

    private async Task RunScriptAsync()
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var name in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" }) start.Environment[name] = "";
        foreach (var argument in new[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(RepositoryRoot(), "scripts", "copy-etp-backup-to-disk.ps1"),
            "-DiskCopyRoot", DiskRoot, "-BackupFolder", BackupFolder, "-Database", Database, "-TestAllowSystemDrive"
        }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }

    private DateTimeOffset CopiedAt()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(DiskRoot, DiskBackupProbe.ReceiptFileName)));
        return DateTimeOffset.Parse(document.RootElement.GetProperty("copiedAtUtc").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Without_the_task_config_the_check_is_skipped()
    {
        var result = await Run(new DiskBackupProbe(new Env(null)));
        Assert.Equal(HealthOutcome.Skipped, result.Outcome);
        Assert.Equal("NOT_CONFIGURED", result.FindingCode);
        Assert.Equal(HealthCheckId.Ops11, result.CheckId);
        Assert.Equal(HealthCheckArea.Operations, result.Area);
    }

    [Fact]
    public async Task A_config_that_cannot_be_read_is_skipped_not_failed()
    {
        var result = await Run(new DiskBackupProbe(new ThrowingEnv()));
        Assert.Equal(HealthOutcome.Skipped, result.Outcome);
        Assert.Equal("NOT_READABLE", result.FindingCode);
    }

    [Fact]
    public async Task A_receipt_written_by_the_script_gives_Pass_and_an_old_one_gives_Warning()
    {
        NewVerifiedBackup("20261011-220000", DateTime.UtcNow);
        await RunScriptAsync();
        var copied = CopiedAt();

        var fresh = await Run(new DiskBackupProbe(new Env(Config), new FixedClock(copied.AddHours(3))));
        Assert.Equal(HealthOutcome.Pass, fresh.Outcome);
        Assert.Equal(HealthCheckTexts.Ok, fresh.FindingCode);

        var old = await Run(new DiskBackupProbe(new Env(Config), new FixedClock(copied.AddHours(40))));
        Assert.Equal(HealthOutcome.Warning, old.Outcome);
        Assert.Equal(HealthCheckTexts.DiskCopyOld, old.FindingCode);
        Assert.DoesNotContain(old.SubResults!, sub => sub.Outcome == HealthOutcome.Fail);
    }

    [Fact]
    public async Task A_copy_that_is_not_the_newest_verified_backup_warns_after_the_grace_hours_only()
    {
        NewVerifiedBackup("20261011-220000", DateTime.UtcNow);
        await RunScriptAsync();
        var copied = CopiedAt();
        // A newer verified backup the copy has not caught up with.
        NewVerifiedBackup("20261012-220000", copied.UtcDateTime.AddHours(1));

        var soon = await Run(new DiskBackupProbe(new Env(Config), new FixedClock(copied.AddHours(2))));
        Assert.Equal(HealthOutcome.Pass, soon.Outcome);

        var late = await Run(new DiskBackupProbe(new Env(Config), new FixedClock(copied.AddHours(1 + DiskBackupProbe.MatchGraceHours + 1))));
        Assert.Equal(HealthOutcome.Warning, late.Outcome);
        Assert.Equal(HealthCheckTexts.DiskCopyNotLatest, late.FindingCode);
    }

    [Fact]
    public async Task A_missing_disk_or_a_missing_copy_is_a_warning()
    {
        var letter = "ZYXWVUTSRQPONMLKJIHGFED".First(c => !Directory.Exists(c + @":\"));
        var gone = await Run(new DiskBackupProbe(new Env(new DiskCopyConfig(letter + @":\ETP-Backups", BackupFolder, Database))));
        Assert.Equal(HealthOutcome.Warning, gone.Outcome);
        Assert.Equal(HealthCheckTexts.DiskNotPresent, gone.FindingCode);

        Directory.CreateDirectory(DiskRoot);
        var none = await Run(new DiskBackupProbe(new Env(Config)));
        Assert.Equal(HealthOutcome.Warning, none.Outcome);
        Assert.Equal(HealthCheckTexts.DiskCopyMissing, none.FindingCode);
    }

    [Fact]
    public async Task A_receipt_whose_file_is_gone_is_a_warning()
    {
        var bak = NewVerifiedBackup("20261011-220000", DateTime.UtcNow);
        await RunScriptAsync();
        File.Delete(Directory.GetFiles(DiskRoot, Path.GetFileName(bak), SearchOption.AllDirectories).Single());

        var result = await Run(new DiskBackupProbe(new Env(Config), new FixedClock(CopiedAt().AddHours(1))));
        Assert.Equal(HealthOutcome.Warning, result.Outcome);
        Assert.Equal(HealthCheckTexts.DiskCopyUnreadable, result.FindingCode);
    }

    [Fact]
    public void The_check_runs_without_a_database_and_the_catalogue_has_56_ids()
    {
        Assert.Contains(HealthCheckId.Ops11, HealthCheckRunner.RunsWithoutDatabase);
        Assert.Equal(56, HealthCheckId.All.Count);
        Assert.Equal(HealthCheckId.Ops11, HealthCheckId.All.Last(id => id.StartsWith("OPS-", StringComparison.Ordinal)));
    }
}
