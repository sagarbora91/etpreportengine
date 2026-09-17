using System.Diagnostics;
using System.IO;
using Microsoft.Data.SqlClient;
using Etp.Reporting.Desktop.Modules.Settings;

namespace Etp.Reporting.Desktop;

internal sealed record PowerShellOperationResult(bool Succeeded, string Message);

internal static class PowerShellOperationsService
{
    private static readonly HashSet<string> AllowedScripts = new(StringComparer.OrdinalIgnoreCase)
        { "backup-etp-database.ps1", "invoke-etp-recovery-drill.ps1", "new-etp-support-package.ps1" };

    public static async Task<PowerShellOperationResult> RunAsync(string scriptName, string connectionString, CancellationToken cancellationToken = default)
    {
        if (!AllowedScripts.Contains(scriptName)) throw new ArgumentException("This maintenance operation is not approved.", nameof(scriptName));
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "scripts", scriptName));
        var scriptRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "scripts")) + Path.DirectorySeparatorChar;
        if (!script.StartsWith(scriptRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(script))
            throw new FileNotFoundException("The installed maintenance script is unavailable.", scriptName);
        if ((File.GetAttributes(script) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked maintenance scripts cannot be executed.");

        ProtectedOperationPath.Validate(script);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.Environment.Remove("PSModulePath");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        // A4.3 accepted unsigned installs. AllSigned here would refuse every maintenance
        // operation the owner can start -- backup, recovery drill, support package -- on
        // an unsigned build. The script is still constrained by the allow-list above and
        // by ProtectedOperationPath.Validate, which refuses a non-administrator-writable
        // folder, so relaxing the policy does not widen which scripts can run.
        process.StartInfo.ArgumentList.Add("RemoteSigned");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(script);
        AddDatabaseArguments(process.StartInfo, connectionString);
        if (!process.Start()) throw new InvalidOperationException("The maintenance operation could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask; var error = await errorTask;
        if (process.ExitCode != 0) return new(false, "The maintenance operation failed. Check its prerequisites and protected operations log.");
        return new(true, "The maintenance operation completed successfully.");
    }

    internal static void AddDatabaseArguments(ProcessStartInfo startInfo, string connectionString)
    {
        var validation = ConnectionStringValidation.Validate(connectionString);
        if (!validation.IsValid) throw new InvalidOperationException(validation.Error);
        var target = new SqlConnectionStringBuilder(validation.ConnectionString);
        if (!System.Text.RegularExpressions.Regex.IsMatch(target.InitialCatalog, "^[A-Za-z0-9_]+$"))
            throw new InvalidOperationException("Maintenance requires a database name containing letters, numbers or underscores.");
        startInfo.ArgumentList.Add("-ServerInstance"); startInfo.ArgumentList.Add(target.DataSource);
        startInfo.ArgumentList.Add("-Database"); startInfo.ArgumentList.Add(target.InitialCatalog);
    }

}
