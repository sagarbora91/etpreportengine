using System.Diagnostics;

namespace Etp.Reporting.Desktop.Tests;

/// <summary>
/// 1.12.1 P3 (review m-8, m-5). The scheduled backup and drill wait for SQL Server after a reboot with the same rule as the
/// application's --automation-once (SqlStartupWait): only "not ready yet" errors are retried, a refused login or a
/// permission error is not. These run the shipped PowerShell functions with fake probes, no SQL Server.
/// </summary>
public sealed class StartupWaitScriptTests
{
    [Fact]
    public async Task Only_errors_that_mean_not_ready_yet_are_transient()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Common()}}'
            function T($lines) { Test-EtpSqlStartupError -Lines $lines }
            foreach ($ok in @(
                @('Msg 4060, Level 11, State 1, Server X, Line 1','Cannot open database requested by the login.','Msg 18456, Level 14, State 1, Server X, Line 1','Login failed.'),
                @('Msg 921, Level 14, State 1','Database has not been recovered yet.'),
                @('Sqlcmd: Error: Microsoft ODBC Driver 17 for SQL Server : Named Pipes Provider: Could not open a connection to SQL Server [2]. .'),
                @('Sqlcmd: Error: Microsoft ODBC Driver 17 for SQL Server : Login timeout expired.'),
                @('TCP Provider: No connection could be made because the target machine actively refused it.'))) {
                if (-not (T $ok)) { throw "Should wait: $($ok -join ' / ')" }
            }
            foreach ($no in @(
                @('Msg 18456, Level 14, State 1, Server X, Line 1','Login failed for user X.'),
                @('Msg 229, Level 14, State 5, Line 1','The SELECT permission was denied.'),
                @('Msg 262, Level 14, State 1','Permission denied.'),
                @(),
                @('Sqlcmd: Error: something else'))) {
                if (T $no) { throw "Should not wait: $($no -join ' / ')" }
            }
            Write-Output 'Transient table passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Transient table passed.", result.Output);
    }

    [Fact]
    public async Task The_wait_retries_with_backoff_then_returns_when_sql_answers_and_logs_each_wait()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Common()}}'
            $script:calls = 0; $script:slept = @(); $script:clock = 0.0; $script:log = @()
            $probe = { param($s) $script:calls++; if ($script:calls -le 6) { [pscustomobject]@{ ExitCode = 1; Lines = @('Msg 4060, Level 11, State 1','Cannot open database') } } else { [pscustomobject]@{ ExitCode = 0; Lines = @() } } }
            $sleep = { param($n) $script:slept += $n; $script:clock += $n }
            $now = { $script:clock }
            $logger = { param($m) $script:log += $m }
            Wait-EtpSqlReady -SqlCmd 'x' -ServerInstance '.\SQLEXPRESS' -Probe $probe -Sleep $sleep -Now $now -Log $logger
            # Each round tries the instance name and the lpc: form (2 probes); SQL answers on the 7th probe, so 3 sleeps happened.
            if (($script:slept -join ',') -ne '2,4,8') { throw "Backoff: $($script:slept -join ',')" }
            if ($script:log.Count -ne 3 -or -not $script:log[0].StartsWith('ETP-STARTUP-WAIT:')) { throw "Log: $($script:log -join ' | ')" }
            Write-Output 'Retry passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Retry passed.", result.Output);
    }

    [Fact]
    public async Task The_wait_gives_up_with_one_plain_sentence_and_does_not_wait_for_a_refused_login()
    {
        var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{Common()}}'
            $script:clock = 0.0
            $sleep = { param($n) $script:clock += $n }
            $now = { $script:clock }
            $log = { param($m) }
            $down = { param($s) [pscustomobject]@{ ExitCode = 1; Lines = @('Sqlcmd: Error: Named Pipes Provider: Could not open a connection to SQL Server [2].') } }
            $message = $null
            try { Wait-EtpSqlReady -SqlCmd 'x' -ServerInstance '.\SQLEXPRESS' -Probe $down -Sleep $sleep -Now $now -Log $log -BudgetSeconds 40 } catch { $message = $_.Exception.Message }
            if ($message -notmatch '^SQL Server did not become ready \(waited \d+ s\)\. ') { throw "Give-up text: $message" }
            if ($message -match 'Named Pipes|Msg ') { throw "SQL Server's own words leaked: $message" }
            if ($script:clock -gt 40) { throw "Waited past the budget: $($script:clock)" }

            # A refused login is not retried: the caller's own step reports it.
            $script:clock = 0.0; $script:slept2 = 0
            $refused = { param($s) [pscustomobject]@{ ExitCode = 1; Lines = @('Msg 18456, Level 14, State 1','Login failed for user X.') } }
            $sleep2 = { param($n) $script:slept2++ }
            Wait-EtpSqlReady -SqlCmd 'x' -ServerInstance '.\SQLEXPRESS' -Probe $refused -Sleep $sleep2 -Now $now -Log $log
            if ($script:slept2 -ne 0) { throw 'A refused login was waited for.' }
            Write-Output 'Give-up passed.'
            """;
        var result = await RunPowerShellAsync(command);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Give-up passed.", result.Output);
    }

    [Fact]
    public void The_scheduled_scripts_call_the_shared_wait_and_the_runner_asks_for_it()
    {
        var scripts = Path.Combine(RepositoryRoot(), "scripts");
        var backup = File.ReadAllText(Path.Combine(scripts, "backup-etp-database.ps1"));
        Assert.Contains("if ($Purpose -eq 'Scheduled') { Wait-EtpSqlReady", backup, StringComparison.Ordinal);
        Assert.True(backup.IndexOf("Wait-EtpSqlReady", StringComparison.Ordinal) < backup.IndexOf("Resolve-EtpSqlConnection -SqlCmd", StringComparison.Ordinal), "The wait must come before the connection is settled.");
        var drill = File.ReadAllText(Path.Combine(scripts, "invoke-etp-recovery-drill.ps1"));
        Assert.Contains("[switch]$WaitForSql", drill, StringComparison.Ordinal);
        Assert.Contains("if ($WaitForSql) { Wait-EtpSqlReady", drill, StringComparison.Ordinal);
        Assert.Contains("invoke-etp-recovery-drill.ps1') -WaitForSql", File.ReadAllText(Path.Combine(scripts, "invoke-monthly-recovery-drill-runner.ps1")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_splash_window_and_its_start_up_wiring_exist_and_the_refusal_keeps_its_own_event_id()
    {
        var splash = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Etp.Reporting.Desktop", "Shell", "StartupCheckWindow.cs"));
        Assert.Contains("Checking the database...", splash, StringComparison.Ordinal);
        var app = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Etp.Reporting.Desktop", "App.xaml.cs"));
        Assert.Contains("CheckObjectsWithSplashAsync", app, StringComparison.Ordinal);
        Assert.Contains("\"STARTUP_OBJECTS_PENDING\"", app, StringComparison.Ordinal);
        // The splash is the first window: the real window must be made the main one and the app must not exit when the splash closes.
        Assert.Contains("MainWindow = window;", app, StringComparison.Ordinal);
        Assert.Contains("ShutdownMode = ShutdownMode.OnExplicitShutdown;", app, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Etp.Reporting.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
    }

    private static string Common() => Path.Combine(RepositoryRoot(), "scripts", "etp-operations-common.ps1").Replace("'", "''");

    private static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string command)
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
        return (process.ExitCode, await output + await error);
    }
}
