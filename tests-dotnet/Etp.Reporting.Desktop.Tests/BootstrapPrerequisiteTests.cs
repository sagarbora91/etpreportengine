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
            # D9 revised: Express and Web are accepted. They cannot encrypt a backup, which
            # the backup path handles by taking an unencrypted one and recording NONE in
            # the receipt, rather than by refusing to run on the edition the shop owns.
            foreach ($case in @(@(16,2,'Standard Edition'), @(16,3,'Enterprise Edition'), @(17,3,'Developer Edition'), @(16,4,'Express Edition'), @(16,2,'Web Edition'))) {
                Assert-EtpBootstrapSqlEdition $case[0] $case[1] $case[2]
                $checks++
            }
            # Too old, or not a local engine at all: still refused.
            foreach ($case in @(@(15,3,'Enterprise Edition'), @(16,8,'Managed Instance'))) {
                $rejected = $false
                try { Assert-EtpBootstrapSqlEdition $case[0] $case[1] $case[2] } catch { $rejected = $true }
                if (-not $rejected) { throw 'Unsupported SQL Server edition accepted.' }
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

    // Until 27 September 2026 bootstrap read the protected configuration first, and the
    // configuration can only be written once SQL Server's service account exists, so the
    // "Install SQL Server" option could never do its job on a new PC.
    [Fact]
    public async Task Fresh_machine_installs_sql_and_client_tools_before_creating_the_protected_configuration()
    {
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $global:calls = [Collections.Generic.List[string]]::new()
            function Assert-EtpBootstrapAdministrator { $global:calls.Add('admin') }
            function Assert-EtpBootstrapPayloads { param($ApplicationDirectory) $global:calls.Add("payloads($ApplicationDirectory)") }
            function Install-EtpSqlPrerequisitesFromPayload { param($PayloadDirectory, $ServiceName) $global:calls.Add("sql($PayloadDirectory,$ServiceName)") }
            function Invoke-EtpOperationFolderSetup { param($ServiceName, $ServerInstance, $Database, $AutomationPrincipal, [switch]$CreateAutomationAccount) $global:calls.Add("folders($ServiceName,$ServerInstance,$Database,CreateAutomationAccount=$CreateAutomationAccount)") }
            function Get-EtpOperationsConfiguration { throw 'The configuration was read before it could exist.' }
            Initialize-EtpFreshMachine -ApplicationRoot 'C:\Unused' -SqlPayloadDirectory 'C:\UnusedMedia'
            $expected = @('admin', 'payloads(C:\Unused)', 'sql(C:\UnusedMedia,MSSQL$SQLEXPRESS)', 'folders(MSSQL$SQLEXPRESS,.\SQLEXPRESS,EtpReporting,CreateAutomationAccount=True)')
            if (($global:calls -join ' / ') -cne ($expected -join ' / ')) { throw "Fresh machine steps ran as: $($global:calls -join ' / ')" }
            Write-Output 'Fresh machine order passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Fresh machine order passed.", result.Output);
    }

    [Fact]
    public async Task Fresh_machine_without_bundled_media_refuses_before_changing_anything()
    {
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $global:calls = [Collections.Generic.List[string]]::new()
            function Assert-EtpBootstrapAdministrator { $global:calls.Add('admin') }
            function Assert-EtpBootstrapPayloads { param($ApplicationDirectory) $global:calls.Add('payloads') }
            function Install-EtpSqlPrerequisitesFromPayload { param($PayloadDirectory, $ServiceName) $global:calls.Add('sql') }
            function Invoke-EtpOperationFolderSetup { param($ServiceName, $ServerInstance, $Database, $AutomationPrincipal, [switch]$CreateAutomationAccount) $global:calls.Add('folders') }
            $refusals = @()
            foreach ($case in @('no media', 'option unticked')) {
                try {
                    if ($case -eq 'no media') { Initialize-EtpFreshMachine -ApplicationRoot 'C:\Unused' }
                    else { Initialize-EtpFreshMachine -ApplicationRoot 'C:\Unused' -SqlPayloadDirectory 'C:\UnusedMedia' -SkipSqlInstallation }
                    $refusals += "ACCEPTED: $case"
                }
                catch { $refusals += $_.Exception.Message }
            }
            foreach ($refusal in $refusals) {
                if ($refusal -notlike '*Install Microsoft SQL Server 2025 Express*' -or $refusal -notlike '*Nothing was changed*') { throw "Unexpected result: $refusal" }
            }
            if (@($global:calls | Where-Object { $_ -in @('sql', 'folders') }).Count -ne 0) { throw "Something changed: $($global:calls -join ' / ')" }
            Write-Output 'Fresh machine without media refused.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Fresh machine without media refused.", result.Output);
    }

    [Fact]
    public async Task Missing_configuration_with_bundled_media_still_changes_nothing_without_elevation()
    {
        // The real script end to end, as setup starts it on a new PC with the option ticked.
        // Unelevated it stops at the administrator check; elevated, at the ownership check on a
        // user-writable installation folder. Either way before SQL Server or any folder.
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpBootstrapBoundary", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var programData = Directory.CreateDirectory(Path.Combine(root, "ProgramData")).FullName;
            var application = Directory.CreateDirectory(Path.Combine(root, "Application")).FullName;
            var media = Directory.CreateDirectory(Path.Combine(root, "Media")).FullName;
            foreach (var name in FullMedia) await File.WriteAllTextAsync(Path.Combine(media, name), "Disposable media marker " + name);
            var result = await RunPowerShellAsync(["-File", FindBootstrapScript(), "-ApplicationDirectory", application, "-SqlPayloadDirectory", media], programData);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Matches("administrator rights|owned by Administrators or SYSTEM|non-administrator", result.Output);
            Assert.Empty(Directory.EnumerateFileSystemEntries(programData, "*", SearchOption.AllDirectories));
            foreach (var name in FullMedia) Assert.Equal("Disposable media marker " + name, await File.ReadAllTextAsync(Path.Combine(media, name)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // The bundled Sqlcmd (Command Line Utilities 15) stops with error 26010 without ODBC
    // Driver 17, and the SQL Server 2025 media's msodbcsql.msi is Driver 18. The old wildcard
    // msodbcsql*.msi took that one, so Sqlcmd could never install.
    [Fact]
    public async Task Sql_client_plan_puts_odbc17_before_sqlcmd_and_never_takes_driver18_for_17()
    {
        var media = NewPayload(FullMedia);
        try
        {
            var script = FindBootstrapScript().Replace("'", "''");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
                $media = '{{media.Replace("'", "''")}}'
                function Show-Plan([string[]]$Installed) { @(Get-EtpSqlClientInstallPlan -PayloadDirectory $media -InstalledOdbcDrivers $Installed | ForEach-Object { (Split-Path -Leaf $_.Path) + '=' + $_.Terms }) -join ',' }
                $odbc17 = 'msodbcsql17.msi=IACCEPTMSODBCSQLLICENSETERMS=YES'
                $odbc18 = 'msodbcsql.msi=IACCEPTMSODBCSQLLICENSETERMS=YES'
                $sqlcmd = 'MsSqlCmdLnUtils.msi=IACCEPTMSSQLCMDLNUTILSLICENSETERMS=YES'
                $plan = Show-Plan @()
                if ($plan -cne "$odbc17,$odbc18,$sqlcmd") { throw "No drivers installed: $plan" }
                $plan = Show-Plan @('ODBC Driver 17 for SQL Server')
                if ($plan -cne "$odbc18,$sqlcmd") { throw "ODBC 17 installed: $plan" }
                $plan = Show-Plan @('ODBC Driver 17 for SQL Server', 'ODBC Driver 18 for SQL Server', 'SQL Server')
                if ($plan -cne $sqlcmd) { throw "Both drivers installed: $plan" }
                Remove-Item -LiteralPath (Join-Path $media 'msodbcsql17.msi')
                $refused = $false
                try { Show-Plan @('ODBC Driver 18 for SQL Server') | Out-Null } catch { $refused = $_.Exception.Message -like '*ODBC Driver 17*Nothing was installed*' }
                if (-not $refused) { throw 'Media without ODBC Driver 17 was accepted for a PC without it.' }
                $plan = Show-Plan @('ODBC Driver 17 for SQL Server')
                if ($plan -cne "$odbc18,$sqlcmd") { throw "ODBC 17 already installed, media without it: $plan" }
                Remove-Item -LiteralPath (Join-Path $media 'MsSqlCmdLnUtils.msi')
                $refused = $false
                try { Show-Plan @('ODBC Driver 17 for SQL Server') | Out-Null } catch { $refused = $_.Exception.Message -like '*Sqlcmd*Nothing was installed*' }
                if (-not $refused) { throw 'Media without Sqlcmd was accepted.' }
                Write-Output 'Client install plan passed.'
                """;
            var result = await RunPowerShellAsync(["-Command", command]);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("Client install plan passed.", result.Output);
        }
        finally { Directory.Delete(media, recursive: true); }
    }

    [Fact]
    public async Task Bundled_media_installs_the_engine_then_odbc17_odbc18_and_sqlcmd_with_no_restart()
    {
        var media = NewPayload(FullMedia);
        try
        {
            var result = await RunPrerequisiteHarnessAsync(media);
            Assert.True(result.ExitCode == 0, result.Output);
            var calls = CallsFrom(result.Output);
            string Msi(string file, string terms) => $"msiexec.exe /i {Path.Combine(media, file)} /qn /norestart ADDLOCAL=ALL {terms}";
            Assert.Equal(
                [
                    "engine(MSSQL$EtpTestNoSuchInstance9f)",
                    Msi("msodbcsql17.msi", "IACCEPTMSODBCSQLLICENSETERMS=YES"),
                    Msi("msodbcsql.msi", "IACCEPTMSODBCSQLLICENSETERMS=YES"),
                    Msi("MsSqlCmdLnUtils.msi", "IACCEPTMSSQLCMDLNUTILSLICENSETERMS=YES"),
                ],
                calls);
        }
        finally { Directory.Delete(media, recursive: true); }
    }

    [Fact]
    public async Task Media_without_odbc17_stops_before_sql_server_is_installed()
    {
        var media = NewPayload([.. FullMedia.Where(x => x != "msodbcsql17.msi")]);
        try
        {
            var result = await RunPrerequisiteHarnessAsync(media);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("REFUSED:", result.Output);
            Assert.Contains("ODBC Driver 17", result.Output);
            Assert.Empty(CallsFrom(result.Output));
        }
        finally { Directory.Delete(media, recursive: true); }
    }

    [Fact]
    public async Task Bundled_media_in_a_user_writable_folder_is_refused_before_anything_runs()
    {
        // Setup used to copy the media to its {tmp} under the user profile, which this very
        // check refuses, so the option failed on every interactive install.
        var media = NewPayload(FullMedia);
        try
        {
            var script = FindBootstrapScript().Replace("'", "''");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
                $global:calls = [Collections.Generic.List[string]]::new()
                function Install-EtpSqlEngineFromPayload { param($PayloadDirectory, $ServiceName) $global:calls.Add('engine'); throw 'The engine must not be installed from this folder.' }
                function Start-EtpProcess { param($FilePath, [string[]]$Arguments, $Description) $global:calls.Add('process'); throw 'Nothing may run from this folder.' }
                $refused = $false
                try { Install-EtpSqlPrerequisitesFromPayload -PayloadDirectory '{{media.Replace("'", "''")}}' -ServiceName 'MSSQL$EtpTestNoSuchInstance9f' }
                catch { $refused = $_.Exception.Message -match 'owned by Administrators or SYSTEM|non-administrator' }
                if (-not $refused) { throw 'Media in a user-writable folder was accepted.' }
                if ($global:calls.Count -ne 0) { throw "Something ran: $($global:calls -join ' / ')" }
                Write-Output 'User-writable media refused.'
                """;
            var result = await RunPowerShellAsync(["-Command", command]);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("User-writable media refused.", result.Output);
            foreach (var name in FullMedia) Assert.Equal("Disposable media marker " + name, await File.ReadAllTextAsync(Path.Combine(media, name)));
        }
        finally { Directory.Delete(media, recursive: true); }
    }

    [Fact]
    public async Task Bundled_sql_engine_keeps_the_hardened_parameters_and_the_recorded_collation()
    {
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $setupArguments = @(Get-EtpSqlEngineSetupArguments 'SQLEXPRESS')
            $expected = @('/ACTION=Install', '/QUIET', '/IACCEPTSQLSERVERLICENSETERMS', '/FEATURES=SQLENGINE', '/INSTANCENAME=SQLEXPRESS',
                '/SQLSYSADMINACCOUNTS=BUILTIN\Administrators', '/SQLCOLLATION=Latin1_General_CI_AS', '/TCPENABLED=0', '/NPENABLED=0', '/UPDATEENABLED=0')
            if ((@($setupArguments | Sort-Object) -join ' ') -cne (@($expected | Sort-Object) -join ' ')) { throw "SQL Server setup arguments changed: $($setupArguments -join ' ')" }
            if (@($setupArguments | Where-Object { $_ -match '^/(SECURITYMODE|SAPWD)' }).Count -ne 0) { throw 'SQL authentication was enabled.' }
            foreach ($instance in @('bad name;x', 'SQLEXPRESS /SECURITYMODE=SQL', 'NAMEWITHMORETHAN16')) {
                $refused = $false
                try { Get-EtpSqlEngineSetupArguments $instance | Out-Null } catch { $refused = $true }
                if (-not $refused) { throw "An unsafe instance name was accepted: $instance" }
            }
            Write-Output 'SQL Server setup arguments passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("SQL Server setup arguments passed.", result.Output);
    }

    [Fact]
    public async Task Sql_setup_media_is_never_extracted_into_the_user_profile()
    {
        // The extracted setup.exe runs with the administrator's full token. It used to be
        // extracted into the user's own Temp folder, which the user's unelevated processes can
        // write to. Checked structurally: running it needs the real media and elevation.
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'bootstrap-etp-prerequisites.ps1 does not parse.' }
            $temp = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and $node.Member.Extent.Text -eq 'GetTempPath' }, $true))
            if ($temp.Count -ne 0) { throw 'Bootstrap uses the user Temp folder again.' }
            $functions = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Install-EtpSqlEngineFromPayload' }, $true))
            if ($functions.Count -ne 1) { throw 'The engine installer function is missing.' }
            $protected = @($functions[0].FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'New-EtpProtectedDirectory' }, $true))
            if ($protected.Count -ne 1) { throw 'The SQL Server media is not extracted into a protected folder.' }
            $extracts = @($functions[0].FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] -and $node.Extent.Text -match "'extract the SQL Server media'" }, $true))
            if ($extracts.Count -ne 1 -or $extracts[0].Extent.StartOffset -lt $protected[0].Extent.EndOffset) { throw 'The media is extracted before its protected folder exists.' }
            Write-Output 'Protected extraction passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Protected extraction passed.", result.Output);
    }

    [Fact]
    public async Task Protected_work_folder_is_never_an_existing_folder_or_a_path_through_a_link()
    {
        // SQL Server's setup and the backup being restored are placed in a folder created with
        // its protection in one step. A folder that is already there, or a path through a
        // junction, could be someone else's and is refused before anything is written.
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpProtectedFolder", Guid.NewGuid().ToString("N"))).FullName;
        var link = Path.Combine(root, "link");
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            var common = Path.Combine(Path.GetDirectoryName(FindBootstrapScript())!, "etp-operations-common.ps1").Replace("'", "''");
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{common}}'
                $root = '{{root.Replace("'", "''")}}'
                $null = New-Item -ItemType Junction -Path (Join-Path $root 'link') -Target (Join-Path $root 'target')
                try {
                    $existing = $null
                    try { New-EtpProtectedDirectory -Path (Join-Path $root 'target') | Out-Null } catch { $existing = $_.Exception.Message }
                    if ($existing -notlike '*already exists*') { throw "An existing folder was reused: $existing" }
                    $linked = $null
                    try { New-EtpProtectedDirectory -Path (Join-Path $root 'link\work') | Out-Null } catch { $linked = $_.Exception.Message }
                    if ($linked -notlike '*Linked operation paths*') { throw "A path through a link was used: $linked" }
                }
                finally { [IO.Directory]::Delete((Join-Path $root 'link')) }
                Write-Output 'Protected work folder refusals passed.'
                """;
            var result = await RunPowerShellAsync(["-Command", command]);
            Assert.True(result.ExitCode == 0, result.Output);
            Assert.Contains("Protected work folder refusals passed.", result.Output);
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }

    private static readonly string[] FullMedia = ["SQLEXPR_x64_ENU.exe", "msodbcsql.msi", "msodbcsql17.msi", "MsSqlCmdLnUtils.msi", "SqlLocalDB.msi"];

    private static string NewPayload(string[] names)
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpBootstrapMedia", Guid.NewGuid().ToString("N"))).FullName;
        foreach (var name in names) File.WriteAllText(Path.Combine(directory, name), "Disposable media marker " + name);
        return directory;
    }

    // Everything that would touch the machine is replaced by a recorder; the order and the
    // exact command lines are what is checked. The ownership check is replaced too, because
    // a test folder cannot be administrator-owned; it has a test of its own above.
    private static Task<(int ExitCode, string Output)> RunPrerequisiteHarnessAsync(string media)
    {
        var script = FindBootstrapScript().Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{script}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
            $global:sqlcmdInstalled = $false
            function Assert-EtpProtectedInstall { param($Path) }
            function Assert-EtpNoLinks { param($Path) }
            function Install-EtpSqlEngineFromPayload { param($PayloadDirectory, $ServiceName) Write-Output "CALL:engine($ServiceName)" }
            function Get-EtpInstalledOdbcDrivers { @() }
            function Test-EtpSqlCmdInstalled { $global:sqlcmdInstalled }
            function Start-EtpProcess {
                param($FilePath, [string[]]$Arguments, $Description)
                Write-Output ('CALL:' + (Split-Path -Leaf $FilePath) + ' ' + ($Arguments -join ' '))
                if (($Arguments -join ' ') -like '*MsSqlCmdLnUtils.msi*') { $global:sqlcmdInstalled = $true }
            }
            try { Install-EtpSqlPrerequisitesFromPayload -PayloadDirectory '{{media.Replace("'", "''")}}' -ServiceName 'MSSQL$EtpTestNoSuchInstance9f' }
            catch { Write-Output ('REFUSED:' + $_.Exception.Message) }
            """;
        return RunPowerShellAsync(["-Command", command]);
    }

    private static string[] CallsFrom(string output) =>
        [.. output.Split('\n').Select(x => x.TrimEnd('\r')).Where(x => x.StartsWith("CALL:", StringComparison.Ordinal)).Select(x => x["CALL:".Length..])];

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
