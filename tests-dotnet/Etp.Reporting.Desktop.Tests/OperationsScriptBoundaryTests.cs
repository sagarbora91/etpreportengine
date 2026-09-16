using System.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

public sealed class OperationsScriptBoundaryTests
{
    [Theory]
    [InlineData("TargetAliases")]
    [InlineData("BackupReceipts")]
    [InlineData("CertificateCustody")]
    [InlineData("CertificateBinding")]
    [InlineData("Retention")]
    [InlineData("Paths")]
    [InlineData("ProtectedInstall")]
    [InlineData("AtomicReceipts")]
    public async Task Operation_helpers_enforce_boundaries_using_disposable_files(string scenario)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var start = new ProcessStartInfo(powerShell)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // Let Windows PowerShell select its own compatible built-in modules.
        start.Environment.Remove("PSModulePath");
        // Repository test scripts are deliberately unsigned; installed operations
        // separately enforce signed scripts and administrator-owned directories.
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(root.FullName, "scripts", "test-etp-operations-boundaries.ps1"), "-Scenario", scenario })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var output = await stdout + await stderr;
        Assert.True(process.ExitCode == 0, output);
        Assert.Contains($"Operations boundary scenario succeeded: {scenario}", output, StringComparison.Ordinal);
    }
}
