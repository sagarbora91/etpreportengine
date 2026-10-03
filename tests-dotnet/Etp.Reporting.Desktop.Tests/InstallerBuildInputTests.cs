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

    [Fact]
    public async Task The_media_folder_handed_to_the_compiler_is_never_overwritten_by_the_signing_loop()
    {
        // The media path once shared its variable with the signing loop's. PowerShell's foreach
        // leaves its variable set to the last item, so every signed build passed the last signed
        // .ps1 to ISCC as the media folder and failed. Signing needs a real certificate, so the
        // rule is checked on the script itself: the variable the compiler argument is built
        // from is no loop's variable, and is assigned only by the media check.
        var script = Path.Combine(RepositoryRoot(), "scripts", "build-windows-installer.ps1").Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'build-windows-installer.ps1 does not parse.' }
            $arguments = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.ExpandableStringExpressionAst] -and $node.Value.StartsWith('/DSqlPayloadDirectory=') }, $true))
            if ($arguments.Count -ne 1) { throw 'The compiler argument for the SQL media was not found exactly once.' }
            $names = @($arguments[0].NestedExpressions | Where-Object { $_ -is [System.Management.Automation.Language.VariableExpressionAst] } | ForEach-Object { $_.VariablePath.UserPath })
            if ($names.Count -ne 1) { throw 'The compiler argument is not built from one variable.' }
            $media = $names[0]
            $loops = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.ForEachStatementAst] -and $node.Variable.VariablePath.UserPath -ieq $media }, $true))
            if ($loops.Count -ne 0) { throw "A foreach loop reuses `$$media, the SQL media variable." }
            $check = @($ast.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.IfStatementAst] -and $_.Clauses[0].Item1.Extent.Text -eq '$SqlPayloadDirectory' })
            if ($check.Count -ne 1) { throw 'The SQL media check was not found.' }
            $assignments = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and $node.Left.VariablePath.UserPath -ieq $media }, $true))
            foreach ($assignment in $assignments) {
                $inCheck = $assignment.Extent.StartOffset -ge $check[0].Extent.StartOffset -and $assignment.Extent.EndOffset -le $check[0].Extent.EndOffset
                $initialisesBeforeCheck = $assignment.Right.Extent.Text -eq '$null' -and $assignment.Extent.EndOffset -le $check[0].Extent.StartOffset
                if (-not ($inCheck -or $initialisesBeforeCheck)) { throw "`$$media is assigned outside the media check: $($assignment.Extent.Text)" }
            }
            if ($assignments.Count -ne 2) { throw "Expected `$$media to be set to null and then to the media folder, found $($assignments.Count) assignments." }
            Write-Output 'Media variable passed.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Media variable passed.", result.Output);
    }

    [Fact]
    public async Task A_named_compiler_that_does_not_exist_is_refused_before_anything_is_built()
    {
        // No -SkipReleaseBuild: the compiler is now found before the release build and its test
        // gate, so a wrong path stops the build at once instead of after the whole gate has run.
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpInstallerInputs", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var missing = Path.Combine(root, "no-inno", "ISCC.exe");
            var output = Path.Combine(root, "installer-output");
            var release = Path.Combine(root, "release");
            var result = await RunBuildAsync(output, release, " -InnoSetupCompiler " + Quote(missing), skipReleaseBuild: false);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("The Inno Setup compiler was not found at: " + missing, result.Output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
            Assert.False(Directory.Exists(release), "The release build started although the compiler was missing.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task A_named_compiler_folder_is_used()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpInstallerInputs", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var inno = Directory.CreateDirectory(Path.Combine(root, "Some Tools", "Inno Setup 6")).FullName;
            File.WriteAllText(Path.Combine(inno, "ISCC.exe"), "Disposable compiler marker");
            var output = Path.Combine(root, "installer-output");
            var result = await RunBuildAsync(output, Path.Combine(root, "no-release"), " -InnoSetupCompiler " + Quote(inno), skipReleaseBuild: true);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Using Inno Setup compiler: " + Path.Combine(inno, "ISCC.exe"), result.Output, StringComparison.Ordinal);
            Assert.Contains("Release executable is missing", result.Output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task A_compiler_on_path_is_found_wherever_it_is_installed()
    {
        // Workpc keeps Inno Setup in E:\Tools\Inno Setup 6, none of the folders the build used to
        // know, so the build refused a PC that had the compiler on PATH.
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "EtpInstallerInputs", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var inno = Directory.CreateDirectory(Path.Combine(root, "portable-inno")).FullName;
            File.WriteAllText(Path.Combine(inno, "ISCC.exe"), "Disposable compiler marker");
            var output = Path.Combine(root, "installer-output");
            var result = await RunBuildAsync(output, Path.Combine(root, "no-release"), "", skipReleaseBuild: true, prependToPath: inno);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Using Inno Setup compiler: " + Path.Combine(inno, "ISCC.exe"), result.Output, StringComparison.Ordinal);
            Assert.Contains("Release executable is missing", result.Output, StringComparison.Ordinal);
            Assert.False(Directory.Exists(output));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task The_installer_build_names_no_machine_specific_folder()
    {
        // Tool locations come from parameters, PATH, the registry and environment variables,
        // never from a drive letter written into the script.
        var script = Path.Combine(RepositoryRoot(), "scripts", "build-windows-installer.ps1").Replace("'", "''");
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            $tokens = $null; $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script}}', [ref]$tokens, [ref]$errors)
            if (@($errors).Count -ne 0) { throw 'build-windows-installer.ps1 does not parse.' }
            $literals = @($ast.FindAll({ param($node) ($node -is [System.Management.Automation.Language.StringConstantExpressionAst] -or $node -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) -and $node.Value -match '^[A-Za-z]:[\\/]' }, $true))
            if ($literals.Count -ne 0) { throw ('Machine-specific paths: ' + (($literals | ForEach-Object { $_.Extent.Text }) -join ', ')) }
            Write-Output 'No machine-specific paths.'
            """;
        var result = await RunPowerShellAsync(["-Command", command]);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("No machine-specific paths.", result.Output);
    }

    private static string RepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Etp.Reporting.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return root.FullName;
    }

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass" }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await stdout + await stderr);
    }

    private static Task<(int ExitCode, string Output)> RunBuildAsync(string media, string output, string release)
        => RunBuildAsync(output, release, " -SqlPayloadDirectory " + Quote(media), skipReleaseBuild: true);

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private static async Task<(int ExitCode, string Output)> RunBuildAsync(string output, string release, string extraArguments, bool skipReleaseBuild, string? prependToPath = null)
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
        if (prependToPath is not null) start.Environment["PATH"] = prependToPath + Path.PathSeparator + start.Environment["PATH"];
        var command = "$ErrorActionPreference = 'Stop'; try { & " + Quote(Path.Combine(root.FullName, "scripts", "build-windows-installer.ps1")) +
            (skipReleaseBuild ? " -SkipReleaseBuild" : "") + " -ReleaseDirectory " + Quote(release) + " -OutputDirectory " + Quote(output) + extraArguments +
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
