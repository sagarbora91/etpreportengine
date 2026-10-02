using System.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

// Workpc, 2 October 2026: SQL Server Express and the bundled Sqlcmd were installed under
// E:\Program Files, and setup failed because Resolve-EtpSqlCmd looked only in
// %ProgramFiles% (C:). These pin where it looks now, and that the protection check still
// decides what it returns.
public sealed class SqlCmdResolutionTests
{
    [Fact]
    public async Task Candidate_paths_cover_a_non_C_install_in_order_and_drop_non_local_folders()
    {
        var command = Prelude() + """
            $list = @(Get-EtpSqlCmdCandidatePaths `
                -RegisteredToolsFolders @('E:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\', '\\server\share\Binn\', 'relative\Binn', '', 'E:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\') `
                -ProgramFilesFolders @('C:\Program Files', '\\server\Program Files', 'E:\Program Files'))
            $expected = @(
                'E:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE',
                'E:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE',
                'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\180\Tools\Binn\SQLCMD.EXE',
                'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE',
                'C:\Program Files\sqlcmd\sqlcmd.exe',
                'E:\Program Files\sqlcmd\sqlcmd.exe')
            if (($list -join '|') -cne ($expected -join '|')) { throw ('Unexpected candidates: ' + ($list -join ' | ')) }

            $env:ProgramFiles = 'C:\Program Files'
            $folders = @(Get-EtpProgramFilesFolders -ApplicationDirectory 'E:\Program Files\Saagar Traders\ETP Reporting Engine\scripts')
            if (($folders -join '|') -cne 'C:\Program Files|E:\Program Files') { throw ('Unexpected Program Files folders: ' + ($folders -join ' | ')) }
            $folders = @(Get-EtpProgramFilesFolders -ApplicationDirectory '\\server\share\ETP\scripts')
            if (($folders -join '|') -cne 'C:\Program Files') { throw ('A network install added a Program Files folder: ' + ($folders -join ' | ')) }

            # The real registry reader only ever returns text, whatever this PC has.
            foreach ($folder in @(Get-EtpRegisteredSqlCmdFolders)) { if ($folder -isnot [string]) { throw 'The registry reader returned something other than a path.' } }
            Write-Output 'Candidate order passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Candidate order passed.", result.Output);
    }

    [Fact]
    public async Task Sqlcmd_registered_on_another_drive_is_found_and_still_has_to_pass_the_protection_check()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpSqlCmdResolution", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            // A stand-in for E:\Program Files: the registry names its ODBC 17 tools folder.
            var registered = Directory.CreateDirectory(Path.Combine(root, "OtherDrive", "Program Files", "Microsoft SQL Server", "Client SDK", "ODBC", "170", "Tools", "Binn")).FullName;
            var registeredSqlCmd = Path.Combine(registered, "SQLCMD.EXE");
            File.WriteAllText(registeredSqlCmd, "Disposable sqlcmd marker");
            // A stand-in for %ProgramFiles%, with its own ODBC 18 copy.
            var programFiles = Directory.CreateDirectory(Path.Combine(root, "SystemDrive", "Program Files")).FullName;
            var fallback = Path.Combine(Directory.CreateDirectory(Path.Combine(programFiles, "Microsoft SQL Server", "Client SDK", "ODBC", "180", "Tools", "Binn")).FullName, "SQLCMD.EXE");
            File.WriteAllText(fallback, "Disposable sqlcmd marker");
            var empty = Directory.CreateDirectory(Path.Combine(root, "Empty", "Program Files")).FullName;

            string Q(string value) => value.Replace("'", "''");
            var command = Prelude() + $$"""
                function Get-EtpRegisteredSqlCmdFolders { @($global:registered) }
                function Get-EtpProgramFilesFolders { @($global:programFiles) }
                # A test folder cannot be administrator-owned, so the check is a recorder that
                # refuses whatever the case says. The real check has its own boundary test.
                function Assert-EtpProtectedInstall { param($Path) $global:checked += $Path; if ($global:refuse -contains $Path) { throw 'Install operations in a folder owned by Administrators or SYSTEM.' } }

                # 1. The registered non-C: copy is found first, and only after it was checked.
                $global:registered = @('{{Q(registered)}}\'); $global:programFiles = @('{{Q(programFiles)}}'); $global:refuse = @(); $global:checked = @()
                $found = Resolve-EtpSqlCmd
                if ($found -ine '{{Q(registeredSqlCmd)}}') { throw "Registered sqlcmd not chosen: $found" }
                if (($global:checked -join '|') -ine '{{Q(registeredSqlCmd)}}') { throw 'The chosen sqlcmd was not checked first.' }

                # 2. An unprotected copy is never returned; the next protected one is.
                $global:refuse = @('{{Q(registeredSqlCmd)}}'); $global:checked = @()
                $found = Resolve-EtpSqlCmd
                if ($found -ine '{{Q(fallback)}}') { throw "Unprotected sqlcmd not passed over: $found" }

                # 3. Nothing protected: the refusal is reported, not "not installed".
                $global:refuse = @('{{Q(registeredSqlCmd)}}', '{{Q(fallback)}}')
                $message = $null
                try { Resolve-EtpSqlCmd | Out-Null } catch { $message = $_.Exception.Message }
                if ($message -notmatch 'owned by Administrators or SYSTEM') { throw "Unprotected sqlcmd accepted or misreported: $message" }

                # 4. A path the operator names is checked and never silently replaced.
                $global:refuse = @('{{Q(fallback)}}'); $message = $null
                try { Resolve-EtpSqlCmd '{{Q(fallback)}}' | Out-Null } catch { $message = $_.Exception.Message }
                if ($message -notmatch 'owned by Administrators or SYSTEM') { throw "An unprotected explicit sqlcmd was accepted: $message" }

                # 5. Nothing anywhere.
                $global:registered = @(); $global:programFiles = @('{{Q(empty)}}'); $global:refuse = @(); $message = $null
                try { Resolve-EtpSqlCmd | Out-Null } catch { $message = $_.Exception.Message }
                if ($message -ne 'Install Microsoft Sqlcmd in a protected Program Files folder.') { throw "Unexpected message with no sqlcmd: $message" }
                Write-Output 'Non-C: sqlcmd resolution passed.'
                """;
            var result = await RunPowerShellAsync(command);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("Non-C: sqlcmd resolution passed.", result.Output);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string Prelude()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var script = Path.Combine(root.FullName, "scripts", "etp-operations-common.ps1").Replace("'", "''");
        return $"$ErrorActionPreference = 'Stop'\n. '{script}'\n";
    }

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string command)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new TimeoutException(await output + await error); }
        return (process.ExitCode, await output + await error);
    }
}
