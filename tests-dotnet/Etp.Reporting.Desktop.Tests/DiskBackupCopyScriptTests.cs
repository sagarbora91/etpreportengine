using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12 G1 (decision 54): copy-etp-backup-to-disk.ps1 over a temp "backup folder" with a receipt and a temp folder
/// standing in for the second disk (-TestAllowSystemDrive). No SQL, no real disk, no customer data.
/// </summary>
public sealed class DiskBackupCopyScriptTests : IDisposable
{
    private const string Database = "EtpReporting";
    private readonly string root = Path.Combine(Path.GetTempPath(), "etp-g1-" + Guid.NewGuid().ToString("N"));
    private string BackupFolder => Path.Combine(root, "backups");
    private string DiskRoot => Path.Combine(root, "disk", "ETP-Backups");

    public DiskBackupCopyScriptTests() => Directory.CreateDirectory(BackupFolder);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }

    /// <summary>Writes a synthetic .bak and its verified receipt as ETP's backup script would (names: database-yyyyMMdd-HHmmss-guid.bak).</summary>
    private string NewBackup(string stamp, int bytes = 4096, bool writeReceipt = true)
    {
        var path = Path.Combine(BackupFolder, $"{Database}-{stamp}-{Guid.NewGuid():N}.bak");
        var content = new byte[bytes];
        RandomNumberGenerator.Fill(content);
        File.WriteAllBytes(path, content);
        if (writeReceipt)
        {
            var receipt = new Dictionary<string, object?>
            {
                ["schemaVersion"] = 2, ["verified"] = true, ["database"] = Database, ["backupPath"] = path,
                ["sha256"] = Convert.ToHexString(SHA256.HashData(content)), ["lengthBytes"] = content.Length,
                ["verifiedAtUtc"] = DateTime.UtcNow.ToString("o"), ["encryption"] = "NONE"
            };
            File.WriteAllText(Path.Combine(BackupFolder, $"{Database}-latest-verified.json"), JsonSerializer.Serialize(receipt));
        }
        return path;
    }

    private async Task<(int Exit, string Output, string Error)> RunAsync(string diskRoot, bool allowSystemDrive = true, IDictionary<string, string>? environment = null, params string[] extra)
    {
        var script = Path.Combine(RepositoryRoot(), "scripts", "copy-etp-backup-to-disk.ps1");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var name in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" }) start.Environment[name] = "";
        if (environment is not null) foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        var arguments = new List<string>
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-DiskCopyRoot", diskRoot, "-BackupFolder", BackupFolder, "-Database", Database
        };
        if (allowSystemDrive) arguments.Add("-TestAllowSystemDrive");
        arguments.AddRange(extra);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("copy-etp-backup-to-disk.ps1 did not finish within 120 s." + await stdout + await stderr);
        }
        return (process.ExitCode, await stdout, await stderr);
    }

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public async Task A_verified_backup_is_copied_hash_checked_and_receipted()
    {
        var bak = NewBackup("20261011-220000");

        var result = await RunAsync(DiskRoot);

        Assert.True(result.Exit == 0, result.Output + result.Error);
        var copy = Path.Combine(DiskRoot, "2026-10", Path.GetFileName(bak));
        Assert.True(File.Exists(copy));
        Assert.Equal(Sha(bak), Sha(copy));
        Assert.False(File.Exists(copy + ".part"));
        Assert.True(File.Exists(copy + ".receipt.json"));
        using var latest = JsonDocument.Parse(File.ReadAllText(Path.Combine(DiskRoot, "latest-disk-copy.json")));
        Assert.Equal(Sha(bak), latest.RootElement.GetProperty("sha256").GetString());
        Assert.Equal(Path.GetFileName(bak), latest.RootElement.GetProperty("file").GetString());
        Assert.True(DateTimeOffset.TryParse(latest.RootElement.GetProperty("copiedAtUtc").GetString(), out _));
    }

    [Fact]
    public async Task Running_twice_for_the_same_backup_is_harmless()
    {
        NewBackup("20261011-220000");
        Assert.Equal(0, (await RunAsync(DiskRoot)).Exit);
        var second = await RunAsync(DiskRoot);
        Assert.True(second.Exit == 0, second.Output + second.Error);
        Assert.Contains("Already on the backup disk", second.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_backup_changed_after_its_receipt_is_refused_and_nothing_is_copied()
    {
        var bak = NewBackup("20261011-220000");
        var bytes = File.ReadAllBytes(bak);
        bytes[10] ^= 0xFF;
        File.WriteAllBytes(bak, bytes);

        var result = await RunAsync(DiskRoot);

        Assert.Equal(1, result.Exit);
        Assert.Contains("no longer matches", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(DiskRoot, "latest-disk-copy.json")));
        Assert.Empty(Directory.Exists(DiskRoot) ? Directory.GetFiles(DiskRoot, "*.bak", SearchOption.AllDirectories) : []);
    }

    [Fact]
    public async Task A_missing_drive_exits_2_with_one_plain_line()
    {
        NewBackup("20261011-220000");
        var letter = "ZYXWVUTSRQPONMLKJIHGFED".First(c => !Directory.Exists(c + @":\"));

        var result = await RunAsync(letter + @":\ETP-Backups");

        Assert.Equal(2, result.Exit);
        var lines = result.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Single(lines);
        Assert.Contains("not connected", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_system_drive_is_refused()
    {
        NewBackup("20261011-220000");
        var onSystemDrive = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "etp-g1-refused-" + Guid.NewGuid().ToString("N"));

        var result = await RunAsync(onSystemDrive, allowSystemDrive: false);

        Assert.Equal(2, result.Exit);
        Assert.Contains("system drive", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(onSystemDrive));
    }

    [Fact]
    public async Task A_folder_inside_onedrive_is_refused()
    {
        NewBackup("20261011-220000");
        var oneDrive = Path.Combine(root, "OneDrive");
        Directory.CreateDirectory(oneDrive);

        var result = await RunAsync(Path.Combine(oneDrive, "ETP-Backups"), environment: new Dictionary<string, string> { ["OneDrive"] = oneDrive });

        Assert.Equal(2, result.Exit);
        Assert.Contains("OneDrive", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(oneDrive, "ETP-Backups")));
    }

    [Fact]
    public async Task Too_little_free_space_is_refused()
    {
        NewBackup("20261011-220000");

        var result = await RunAsync(DiskRoot, extra: ["-FreeSpaceFactor", "1000000000"]);

        Assert.Equal(2, result.Exit);
        Assert.Contains("free", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(DiskRoot, "latest-disk-copy.json")));
    }

    [Fact]
    public async Task Retention_keeps_the_newest_copies_and_the_first_copy_of_each_recent_month()
    {
        string[] stamps = ["20260901-220000", "20260902-220000", "20261001-220000", "20261002-220000", "20261003-220000"];
        foreach (var stamp in stamps)
        {
            NewBackup(stamp, 512);
            var run = await RunAsync(DiskRoot, extra: ["-KeepDaily", "2", "-KeepMonthly", "2"]);
            Assert.True(run.Exit == 0, run.Output + run.Error);
        }

        var left = Directory.GetFiles(DiskRoot, Database + "-*.bak", SearchOption.AllDirectories)
            .Select(path => Path.GetFileName(path).Split('-')[1]).Order().ToArray();
        // Newest 2 = 1002, 1003; first of the newest 2 months = 1001 (October), 0901 (September). 0902 goes.
        Assert.Equal(["20260901", "20261001", "20261002", "20261003"], left);
        Assert.Equal(left.Length, Directory.GetFiles(DiskRoot, "*.bak.receipt.json", SearchOption.AllDirectories).Length);
    }
}
