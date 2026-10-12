extern alias EtpApplication;

using System.Diagnostics;
using System.IO;
using EtpApplication::Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop.Updates;

/// <summary>
/// 1.12 (B1, design section 4 rule 5). A fresh verified backup before every in-app update, through the existing backup
/// path: the installed <c>scripts\backup-etp-database.ps1</c> with <c>-Purpose PreUpdate</c> (receipt purpose PRE_UPDATE,
/// never rotated away) and <c>-ResultPath</c> in the version's staging folder. The receipt is then read back and checked
/// (<see cref="PreUpdateBackupReceipt"/>). No receipt, no update.
///
/// The launch mirrors <see cref="PowerShellOperationsService"/> (same powershell.exe, PSModulePath cleared, RemoteSigned,
/// ProtectedOperationPath check, the same -ServerInstance/-Database arguments). That service's allow-list takes no extra
/// arguments, so this is a separate, fixed launcher for exactly one script; wave 2 (or A1's seam) may fold the two together.
/// </summary>
internal static class PreUpdateBackupRunner
{
    public const string ScriptName = "backup-etp-database.ps1";
    public const string ReceiptFileName = "pre-update-backup.json";

    public static async Task<PreUpdateBackupCheck> RunAsync(string connectionString, string stagingFolder, CancellationToken cancellationToken)
    {
        var scriptRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "scripts")) + Path.DirectorySeparatorChar;
        var script = Path.GetFullPath(Path.Combine(scriptRoot, ScriptName));
        if (!File.Exists(script)) throw new FileNotFoundException("The installed backup script is unavailable.", ScriptName);
        if ((File.GetAttributes(script) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked maintenance scripts cannot be executed.");
        ProtectedOperationPath.Validate(script);

        var resultPath = Path.Combine(stagingFolder, ReceiptFileName);
        if (File.Exists(resultPath)) File.Delete(resultPath);
        var start = BuildStartInfo(script, connectionString, resultPath);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The backup could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        _ = await output.ConfigureAwait(false);
        _ = await error.ConfigureAwait(false);
        if (process.ExitCode != 0 || !File.Exists(resultPath))
        {
            DesktopDiagnostics.Record(new MaintenanceScriptFailedException(ScriptName, process.ExitCode, "PRE_UPDATE_BACKUP_FAILED"),
                "Updates.PreUpdateBackup", "PRE_UPDATE_BACKUP_FAILED");
            return new PreUpdateBackupCheck(PreUpdateBackupProblem.Unreadable, null, null);
        }
        // The database the script was told to back up (ArchitectureGuardTests: no SqlClient here, so read it back from the arguments).
        var database = DatabaseArgument(start);
        return PreUpdateBackupReceipt.Check(await File.ReadAllBytesAsync(resultPath, cancellationToken).ConfigureAwait(false), database, DateTimeOffset.UtcNow);
    }

    internal static string DatabaseArgument(ProcessStartInfo start)
    {
        var index = start.ArgumentList.IndexOf("-Database");
        return index >= 0 && index + 1 < start.ArgumentList.Count ? start.ArgumentList[index + 1] : "";
    }

    internal static ProcessStartInfo BuildStartInfo(string script, string connectionString, string resultPath)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", script }) start.ArgumentList.Add(argument);
        PowerShellOperationsService.AddDatabaseArguments(start, connectionString);
        start.ArgumentList.Add("-Purpose"); start.ArgumentList.Add("PreUpdate");
        start.ArgumentList.Add("-ResultPath"); start.ArgumentList.Add(resultPath);
        return start;
    }
}
