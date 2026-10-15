using System.Diagnostics;
using static Etp.Reporting.Desktop.Tests.UpdateTestSupport;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12.1 (found in the 1.12.0 VM upgrade, 15 Oct 2026): with exactly ONE active Owner, setup's software update folder step
/// failed with "The property 'Count' cannot be found", because <c>$x = if (...) { $ownerSids }</c> unrolled the one-element array
/// to a string and setup runs under StrictMode (etp-operations-common.ps1). These run the real block from the shipped script
/// with the folder write stubbed (no ACL is changed) under Set-StrictMode -Version Latest, in Windows PowerShell 5.1.
/// </summary>
public sealed class SetupUpdateFolderOneOwnerTests
{
    private const string UpdatesTry = "try {\r\n    # 1.12.1 (VM upgrade 15 Oct 2026)";
    private const string Owner = "S-1-5-21-1111111111-2222222222-3333333333-1001";
    private const string Second = "S-1-5-21-1111111111-2222222222-3333333333-1002";

    private static string Bootstrap() => File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "bootstrap-etp-prerequisites.ps1")).ReplaceLineEndings("\r\n");

    /// <summary>The Updates folder try/catch of the shipped bootstrap, exactly as setup runs it.</summary>
    private static string UpdatesBlock()
    {
        var text = Bootstrap();
        var start = text.IndexOf(UpdatesTry, StringComparison.Ordinal);
        Assert.True(start > 0, "The Updates folder block was not found.");
        var end = text.IndexOf("# 1.12 integration (review B-2", start, StringComparison.Ordinal);
        Assert.True(end > start, "The end of the Updates folder block was not found.");
        var block = text[start..end];
        Assert.Contains("Set-EtpHealthExportFolder -Path (Join-Path $env:ProgramData 'EtpReporting\\Updates')", block, StringComparison.Ordinal);
        return block;
    }

    [Fact]
    public void The_owner_list_is_an_array_before_any_folder_step_and_the_updates_block_wraps_it()
    {
        var text = Bootstrap();
        var reset = text.IndexOf("$ownerSids = @()\r\ntry {\r\n    $ownerIdentities =", StringComparison.Ordinal);
        var health = text.IndexOf("'EtpReporting\\HealthExports'", StringComparison.Ordinal);
        Assert.True(reset > 0 && reset < health, "$ownerSids is set to @() before the health export try.");
        var block = UpdatesBlock();
        Assert.Contains("$updateOwnerSids = @($ownerSids)", block, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\$updateOwnerSids\s*=\s*if\b", block);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(0)]
    public async Task The_updates_folder_is_prepared_under_strict_mode_with_this_many_owners(int owners)
    {
        var sids = owners switch { 0 => "@()", 1 => $"@('{Owner}')", _ => $"@('{Owner}','{Second}')" };
        var bootstrap = Path.Combine(RepositoryRoot(), "scripts", "bootstrap-etp-prerequisites.ps1").Replace("'", "''", StringComparison.Ordinal);
        var blockFile = Path.Combine(Path.GetTempPath(), "EtpUpdatesBlock_" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(blockFile, UpdatesBlock());
        try
        {
            var command = $$"""
                $ErrorActionPreference = 'Stop'
                . '{{bootstrap}}' -ApplicationDirectory 'C:\UnusedBootstrapTest'
                Set-StrictMode -Version Latest
                $script:log = [Collections.Generic.List[string]]::new()
                $script:applied = $null
                function Write-SetupLog { param([string]$Message) $script:log.Add($Message) }
                function Set-EtpHealthExportFolder { param([string]$Path, [object[]]$Rules, [switch]$ResetChildren) $script:applied = @($Rules); return 'C:\Stub\EtpReporting\Updates' }
                # As setup leaves it after the health export step: @() of what the Owners query returned.
                $ownerSids = @({{sids}})
                . '{{blockFile.Replace("'", "''", StringComparison.Ordinal)}}'
                $all = $script:log -join ' | '
                if ($all -match 'WARNING') { throw "Setup warned: $all" }
                if ($all -notmatch 'Software update folder ready at C:\\Stub\\EtpReporting\\Updates .*; {{owners}} active Owner account\(s\) read and write') { throw "Log: $all" }
                $modify = @($script:applied | Where-Object { $_.Rights -eq 'Modify' } | ForEach-Object { $_.Sid })
                if ($modify.Count -ne {{owners}}) { throw "Modify rules: $($modify -join ',')" }
                if ({{owners}} -ge 1 -and $modify[0] -cne '{{Owner}}') { throw "First Owner rule: $($modify[0])" }
                Write-Output 'Updates folder block passed.'
                """;
            var result = await RunCommandAsync(command);
            Assert.True(result.ExitCode == 0, result.Output + result.Error);
            Assert.Contains("Updates folder block passed.", result.Output, StringComparison.Ordinal);
        }
        finally { File.Delete(blockFile); }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunCommandAsync(string command)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }
}
