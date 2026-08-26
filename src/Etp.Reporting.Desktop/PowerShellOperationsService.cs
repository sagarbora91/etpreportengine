using System.Diagnostics;
using System.IO;

namespace Etp.Reporting.Desktop;

internal sealed record PowerShellOperationResult(bool Succeeded, string Message);

internal static class PowerShellOperationsService
{
    private static readonly HashSet<string> AllowedScripts = new(StringComparer.OrdinalIgnoreCase)
        { "backup-etp-database.ps1", "invoke-etp-recovery-drill.ps1", "new-etp-support-package.ps1" };

    public static async Task<PowerShellOperationResult> RunAsync(string scriptName, CancellationToken cancellationToken = default)
    {
        if (!AllowedScripts.Contains(scriptName)) throw new ArgumentException("This maintenance operation is not approved.", nameof(scriptName));
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "scripts", scriptName));
        var scriptRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "scripts")) + Path.DirectorySeparatorChar;
        if (!script.StartsWith(scriptRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(script))
            throw new FileNotFoundException("The installed maintenance script is unavailable.", scriptName);
        if ((File.GetAttributes(script) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Linked maintenance scripts cannot be executed.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(script);
        if (!process.Start()) throw new InvalidOperationException("The maintenance operation could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask; var error = await errorTask;
        if (process.ExitCode != 0) return new(false, SafeLastLine(error) ?? "The maintenance operation failed. Review the application diagnostic log.");
        return new(true, SafeLastLine(output) ?? "The maintenance operation completed successfully.");
    }

    private static string? SafeLastLine(string value) => value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim()).LastOrDefault(x => x.Length > 0) is { } line ? line[..Math.Min(line.Length, 300)] : null;
}
