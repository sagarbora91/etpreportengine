using System.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// The SQL Server 2025 media carries only ODBC Driver 18, and the Sqlcmd package it ships
/// (Command Line Utilities 15) refuses to install without Driver 17. An installer built from
/// that media offered an option that could never finish. The build now refuses such media
/// before it builds anything.
/// </summary>
public sealed class InstallerBuildInputTests
{
    [Theory]
    [InlineData("msodbcsql17.msi")]
    [InlineData("MsSqlCmdLnUtils.msi")]
    public async Task Sql_media_that_cannot_install_sqlcmd_is_rejected_before_anything_is_built(string missing)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpInstallerInputs", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var media = Directory.CreateDirectory(Path.Combine(root, "media")).FullName;
            foreach (var name in new[] { "SQLEXPR_x64_ENU.exe", "msodbcsql.msi", "msodbcsql17.msi", "MsSqlCmdLnUtils.msi" }.Where(x => x != missing))
                File.WriteAllText(Path.Combine(media, name), "Disposable media marker");
            var output = Path.Combine(root, "installer-output");
            var result = await RunBuildAsync(media, output, Path.Combine(root, "no-release"));
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(missing + ") was found in", result.Output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output), "The installer output folder was created for media that was refused.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Complete_sql_media_passes_the_media_check()
    {
        // The same build with every package present gets past the media check and stops at
        // the next one, the missing release, still before anything is written.
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpInstallerInputs", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var media = Directory.CreateDirectory(Path.Combine(root, "media")).FullName;
            foreach (var name in new[] { "SQLEXPR_x64_ENU.exe", "msodbcsql.msi", "msodbcsql17.msi", "MsSqlCmdLnUtils.msi", "SqlLocalDB.msi" })
                File.WriteAllText(Path.Combine(media, name), "Disposable media marker");
            var output = Path.Combine(root, "installer-output");
            var result = await RunBuildAsync(media, output, Path.Combine(root, "no-release"));
            Assert.NotEqual(0, result.ExitCode);
            Assert.DoesNotContain("was found in", result.Output, StringComparison.Ordinal);
            Assert.Matches("Release executable is missing|Inno Setup 6 is required", result.Output);
            Assert.False(Directory.Exists(output));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task<(int ExitCode, string Output)> RunBuildAsync(string media, string output, string release)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        // -SkipReleaseBuild and a release folder that does not exist: nothing here can build,
        // sign or compile, whatever the checks decide. The refusal is printed on one line,
        // because PowerShell's own error view wraps long messages at the console width.
        static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
        var command = "$ErrorActionPreference = 'Stop'; try { & " + Quote(Path.Combine(root.FullName, "scripts", "build-windows-installer.ps1")) +
            " -SkipReleaseBuild -ReleaseDirectory " + Quote(release) + " -OutputDirectory " + Quote(output) + " -SqlPayloadDirectory " + Quote(media) +
            " } catch { Write-Output ('REFUSED: ' + $_.Exception.Message); exit 1 }";
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await stdout + await stderr);
    }
}
