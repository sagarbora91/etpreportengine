using System.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

public sealed class BootstrapPrerequisiteTests
{
    [Fact]
    public async Task Bootstrap_resolves_the_configured_service_and_rejects_unsupported_editions_without_mutations()
    {
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $checks = 0
            foreach ($case in @(
                @('.', 'MSSQLSERVER'), @('localhost', 'MSSQLSERVER'), @('(local)', 'MSSQLSERVER'),
                @('.\ConfiguredInstance', 'MSSQL$ConfiguredInstance'), @('lpc:localhost\ConfiguredInstance', 'MSSQL$ConfiguredInstance'),
                @('np:localhost\ConfiguredInstance', 'MSSQL$ConfiguredInstance'))) {
                if ((Resolve-EtpBootstrapServiceName $case[0] 'ConfiguredDatabase') -cne $case[1]) { throw 'Configured service mismatch.' }
                $checks++
            }
            foreach ($endpoint in @('remote.example\Instance', '(localdb)\MSSQLLocalDB', 'np:\\localhost\pipe\sql\query')) {
                $rejected = $false
                try { Resolve-EtpBootstrapServiceName $endpoint 'ConfiguredDatabase' | Out-Null } catch { $rejected = $true }
                if (-not $rejected) { throw 'Unsupported bootstrap endpoint accepted.' }
                $checks++
            }
            foreach ($case in @(@(16,2,'Standard Edition'), @(16,3,'Enterprise Edition'), @(17,3,'Developer Edition'))) {
                Assert-EtpBootstrapSqlEdition $case[0] $case[1] $case[2]
                $checks++
            }
            foreach ($case in @(@(16,4,'Express Edition'), @(16,2,'Web Edition'), @(15,3,'Enterprise Edition'), @(16,8,'Managed Instance'))) {
                $rejected = $false
                try { Assert-EtpBootstrapSqlEdition $case[0] $case[1] $case[2] } catch { $rejected = $true }
                if (-not $rejected) { throw 'Unsupported encrypted-backup prerequisite accepted.' }
                $checks++
            }
            if ($checks -ne 16) { throw 'Not all bootstrap preflight cases ran.' }
            Write-Output 'Bootstrap preflight behavior passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Bootstrap preflight behavior passed.", result.Output);
    }

    [Fact]
    public async Task User_owned_installation_is_rejected_before_executing_payloads_or_migrations()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpBootstrapBoundary", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var executable = Path.Combine(root, "Etp.Reporting.Desktop.exe");
            var migrationDirectory = Directory.CreateDirectory(Path.Combine(root, "database", "migrations")).FullName;
            await File.WriteAllTextAsync(executable, "Untrusted executable marker");
            await File.WriteAllTextAsync(Path.Combine(migrationDirectory, "9999_marker.sql"), "THROW 51999,'Untrusted migration reached',1;");
            var script = FindBootstrapScript().Replace("'", "''");
            var command = $$"""
                $ErrorActionPreference='Stop'
                . '{{script}}' -ApplicationDirectory '{{root.Replace("'", "''")}}'
                $rejected=$false
                try { Assert-EtpBootstrapPayloads '{{root.Replace("'", "''")}}' }
                catch { $rejected=$_.Exception.Message -match 'non-administrator|owned by Administrators or SYSTEM' }
                if (-not $rejected) { throw 'Untrusted elevated payloads were accepted.' }
                Write-Output 'Untrusted payloads rejected before execution.'
                """;
            var result = await RunPowerShellAsync(["-Command", command]);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("Untrusted payloads rejected before execution.", result.Output);
            Assert.Equal("Untrusted executable marker", await File.ReadAllTextAsync(executable));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Missing_protected_machine_configuration_stops_bootstrap_before_creating_files()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpBootstrapBoundary", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            // The child process sees this empty, disposable ProgramData only.
            var result = await RunPowerShellAsync(["-File", FindBootstrapScript(), "-ApplicationDirectory", root], root);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string FindBootstrapScript()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "scripts", "bootstrap-etp-prerequisites.ps1");
    }

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string[] arguments, string? programData = null)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass" }.Concat(arguments)) start.ArgumentList.Add(argument);
        if (programData is not null) start.Environment["ProgramData"] = programData;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }
}
