using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Etp.Reporting.Infrastructure.SqlServer.Connections;

namespace Etp.Reporting.Desktop;

internal sealed record PowerShellOperationResult(bool Succeeded, string Message);

/// <summary>
/// The database a maintenance script works on: the instance and the database name, nothing else (no credentials, no
/// options). Built from the connection factory (1.12 A1), so the target has already passed the endpoint policy.
/// The scripts keep their own local-only rule (<c>Assert-EtpLocalSqlTarget</c>): they run only on the database host.
/// </summary>
internal sealed record MaintenanceTarget(string ServerInstance, string Database)
{
    public static MaintenanceTarget For(IEtpConnectionFactory connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        using var connection = connections.Create();
        return new(connection.DataSource, connections.DatabaseName);
    }
}

internal static class PowerShellOperationsService
{
    private static readonly HashSet<string> AllowedScripts = new(StringComparer.OrdinalIgnoreCase)
        { "backup-etp-database.ps1", "invoke-etp-recovery-drill.ps1", "new-etp-support-package.ps1" };

    public static async Task<PowerShellOperationResult> RunAsync(string scriptName, MaintenanceTarget target, CancellationToken cancellationToken = default)
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
        AddDatabaseArguments(process.StartInfo, target);
        if (!process.Start()) throw new InvalidOperationException("The maintenance operation could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask; var error = await errorTask;
        var outcome = MaintenanceScriptOutput.Interpret(scriptName, process.ExitCode, output, error);
        // 1.9.9 (IE-CODE-01). A script that failed without the application throwing used to
        // leave no diagnostics entry at all. The entry names the reason by code only: the
        // script's own words can name accounts and folders, and this log carries neither.
        if (!outcome.Result.Succeeded)
            DesktopDiagnostics.Record(
                new MaintenanceScriptFailedException(scriptName, process.ExitCode, outcome.ReasonCode),
                "OperationsAdministration.Maintenance",
                MaintenanceScriptOutput.FailureEventId(scriptName, outcome.ReasonCode));
        return outcome.Result;
    }

    /// <summary>The scheduled tasks ETP may start on demand (1.12 U1). Only these names are ever passed to PowerShell.</summary>
    internal static readonly IReadOnlySet<string> StartableTasks = new HashSet<string>(StringComparer.Ordinal) { ScheduledTaskOutcome.PublishShopResultsTask };

    /// <summary>
    /// 1.12 U1 ("Publish shop results now" on the Morning page): starts an existing scheduled task, waits up to two minutes
    /// for it to finish and reports its last result as one sentence (<see cref="ScheduledTaskOutcome"/>). It changes no task
    /// and no setting; a task that is missing or that this account may not start is a sentence, never an exception.
    /// </summary>
    public static async Task<PowerShellOperationResult> StartScheduledTaskAsync(string taskName, CancellationToken cancellationToken = default)
    {
        if (!StartableTasks.Contains(taskName)) throw new ArgumentException("This scheduled task cannot be started from ETP.", nameof(taskName));
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
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(ScheduledTaskOutcome.StartScript(taskName));
        if (!process.Start()) throw new InvalidOperationException("The scheduled task could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask; _ = await errorTask;
        var result = ScheduledTaskOutcome.Interpret(output, process.ExitCode);
        if (!result.Succeeded)
            DesktopDiagnostics.Record(new InvalidOperationException("Scheduled task " + taskName + ": " + ScheduledTaskOutcome.Marker(output)),
                "Morning.PublishShopResults", "SCHEDULED_TASK_NOT_COMPLETED", DesktopDiagnosticSeverity.Warning);
        return result;
    }

    internal static void AddDatabaseArguments(ProcessStartInfo startInfo, MaintenanceTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(target.ServerInstance))
            throw new InvalidOperationException("Maintenance requires a SQL Server instance.");
        if (target.Database is null || !System.Text.RegularExpressions.Regex.IsMatch(target.Database, "^[A-Za-z0-9_]+$"))
            throw new InvalidOperationException("Maintenance requires a database name containing letters, numbers or underscores.");
        startInfo.ArgumentList.Add("-ServerInstance"); startInfo.ArgumentList.Add(target.ServerInstance);
        startInfo.ArgumentList.Add("-Database"); startInfo.ArgumentList.Add(target.Database);
    }

}

/// <summary>
/// 1.12 U1: the fixed PowerShell that starts an allow-listed scheduled task and reads its result, and the sentence for
/// each result (the Owner never sees a bare exit code). <c>Publish-ShopResults.ps1</c> exits 0 (done or nothing pending),
/// 2 (done, but a result could not be published or was rejected) or 1 (error).
/// </summary>
internal static class ScheduledTaskOutcome
{
    public const string PublishShopResultsTask = "ETP Publish Shop Results";
    public const string Prefix = "ETP_TASK:";
    public const int WaitSeconds = 120;
    /// <summary>SCHED_S_TASK_HAS_NOT_RUN (0x41303): the task has not run yet.</summary>
    public const long HasNotRun = 0x41303;

    /// <summary>The script; the task name is one of <see cref="PowerShellOperationsService.StartableTasks"/>, so it holds no quote.</summary>
    public static string StartScript(string taskName)
    {
        if (taskName.Contains('\'', StringComparison.Ordinal)) throw new ArgumentException("A task name with a quote is not allowed.", nameof(taskName));
        return string.Join("; ",
            "$ErrorActionPreference='Stop'",
            $"$name='{taskName}'",
            "try { $null = Get-ScheduledTask -TaskName $name } catch { '" + Prefix + "NOT_FOUND'; exit 0 }",
            "$before = (Get-ScheduledTaskInfo -TaskName $name).LastRunTime",
            "try { Start-ScheduledTask -TaskName $name } catch { '" + Prefix + "DENIED'; exit 0 }",
            $"$deadline = (Get-Date).AddSeconds({WaitSeconds})",
            "do { Start-Sleep -Milliseconds 500; $info = Get-ScheduledTaskInfo -TaskName $name; $state = (Get-ScheduledTask -TaskName $name).State } "
                + "while ((Get-Date) -lt $deadline -and ($state -eq 'Running' -or $info.LastRunTime -eq $before))",
            "if ($state -eq 'Running') { '" + Prefix + "RUNNING' } else { '" + Prefix + "RESULT:' + $info.LastTaskResult }");
    }

    /// <summary>The last <c>ETP_TASK:</c> line without its prefix ("RESULT:0", "DENIED"), or "NONE".</summary>
    public static string Marker(string? output) =>
        (output ?? string.Empty).Split('\n').Select(line => line.Trim()).LastOrDefault(line => line.StartsWith(Prefix, StringComparison.Ordinal))?[Prefix.Length..] ?? "NONE";

    public static PowerShellOperationResult Interpret(string? output, int exitCode)
    {
        var marker = Marker(output);
        if (marker.StartsWith("RESULT:", StringComparison.Ordinal) && long.TryParse(marker["RESULT:".Length..], out var code))
            return code switch
            {
                0 => new(true, "Every waiting result was signed and sent to the shops' OneDrive folders."),
                2 => new(false, "Done, but a result could not be published or was rejected (for example a shop without its key). The publish log beside the shop results says which."),
                1 => new(false, "Publishing stopped with an error. The publish log beside the shop results says why; the results stay waiting and are tried again at 07:40."),
                HasNotRun => new(false, "Windows did not run the task. It runs at 07:40 as usual."),
                _ => new(false, $"The publish task ended with code {code}. The results stay waiting and are tried again at 07:40.")
            };
        return marker switch
        {
            "NOT_FOUND" => new(false, "The 'ETP Publish Shop Results' task is not set up on this PC, so results cannot be sent from here. It is registered by Register-WorkpcTasks.ps1 (elevated)."),
            "DENIED" => new(false, "Windows did not let this account start the publish task. It still runs at 07:40, or start it from Task Scheduler."),
            "RUNNING" => new(true, $"The publish task is still running after {WaitSeconds / 60} minutes. Refresh in a minute to see the results sent back."),
            _ => new(false, exitCode == 0
                ? "The publish task gave no result. Refresh in a minute; it also runs at 07:40."
                : $"The publish task could not be started (PowerShell ended with code {exitCode}). It still runs at 07:40.")
        };
    }
}

/// <summary>
/// A maintenance script ended with a non-zero exit code. Carries only the script name, the
/// exit code (as <see cref="Exception.HResult"/>) and the reason code, so a diagnostics entry
/// built from it holds no account names, folders or SQL Server text.
/// </summary>
internal sealed class MaintenanceScriptFailedException : Exception
{
    public MaintenanceScriptFailedException(string scriptName, int exitCode, string reasonCode)
        : base($"Maintenance script {scriptName} failed with exit code {exitCode} ({reasonCode}).")
    {
        ScriptName = scriptName;
        ExitCode = exitCode;
        ReasonCode = reasonCode;
        HResult = exitCode;
    }

    public string ScriptName { get; }
    public int ExitCode { get; }
    public string ReasonCode { get; }
}

internal sealed record MaintenanceScriptOutcome(PowerShellOperationResult Result, string ReasonCode);

/// <summary>
/// 1.9.9 (IE-CODE-01, IE-CODE-02). Reads what a maintenance script printed. Until 1.9.9 the
/// output was read and discarded, and every failure said "Check its prerequisites and
/// protected operations log" - a log that did not exist. The scripts now print the reason on
/// an <c>ETP_FAILURE:</c> line, where it was logged on <c>ETP_LOG:</c>, warnings worth showing
/// on <c>ETP_NOTICE:</c> and the support package's location on <c>ETP_SAVED:</c>. A script
/// that died before it could say anything (refused by the execution policy, a parse error)
/// is read from PowerShell's own error record, which is wrapped at 120 characters when
/// redirected and is joined back here.
/// </summary>
internal static class MaintenanceScriptOutput
{
    internal const string FailurePrefix = "ETP_FAILURE:";
    internal const string LogPrefix = "ETP_LOG:";
    internal const string NoticePrefix = "ETP_NOTICE:";
    internal const string SavedPrefix = "ETP_SAVED:";
    internal const int MaxMessageLength = 1500;

    internal const string NoReason = "NO_REASON";
    internal const string Unclassified = "SCRIPT_FAILED";

    // Order matters: the first match wins, so the specific phrases come before the general.
    private static readonly (string Code, Regex Pattern)[] KnownReasons =
    [
        ("SCRIPT_BLOCKED", Pattern(@"cannot be loaded|running scripts is disabled|is not digitally signed")),
        ("DRILL_RESULT_NOT_RECORDED", Pattern(@"could not be recorded")),
        ("AUTOMATION_NOT_STORE_MANAGER", Pattern(@"is not an active Store Manager")),
        ("AUTOMATION_RIGHTS_MISSING", Pattern(@"does not yet have the operations module's rights")),
        ("AUTOMATION_ACCOUNT_INVALID", Pattern(@"dedicated (?:local )?automation account|Automation cannot run as|automation account must not belong")),
        ("RECOVERY_KEYS_MISSING", Pattern(@"no exported recovery keys")),
        ("CERTIFICATE_CUSTODY", Pattern(@"certificate custody|backup certificate|Export the current certificate")),
        ("SQL_EDITION_UNKNOWN", Pattern(@"edition could not be determined")),
        ("LOW_DISK_SPACE", Pattern(@"below the required free-space limit")),
        ("BACKUP_FOLDER_MISSING", Pattern(@"Complete protected backup-folder setup first")),
        ("LINKED_PATH", Pattern(@"Linked operation paths are not allowed")),
        ("INSTALL_NOT_PROTECTED", Pattern(@"installation folder can be changed by a non-administrator|Install operations in a folder owned by")),
        ("SQLCMD_MISSING", Pattern(@"Install Microsoft Sqlcmd|SQLCMD is not installed")),
        ("SQL_UNREACHABLE", Pattern(@"Could not reach the SQL Server instance")),
        ("SQL_TARGET_NOT_LOCAL", Pattern(@"Choose a SQL Server instance on this computer|Choose a valid database name")),
        ("DRILL_METADATA_MISMATCH", Pattern(@"Backup metadata does not match")),
        ("DRILL_BACKUP_CHANGED", Pattern(@"backup changed during the recovery drill")),
        ("DRILL_ROW_COUNTS", Pattern(@"^Recovery drill failed:")),
        ("RECEIPT_INVALID", Pattern(@"backup receipt|receipt and file differ|receipt backup is outside")),
        ("SUPPORT_HEALTH_QUERY_FAILED", Pattern(@"Support health query failed")),
        ("SQL_OPERATION_FAILED", Pattern(@"The database operation failed")),
    ];

    private static Regex Pattern(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static MaintenanceScriptOutcome Interpret(string scriptName, int exitCode, string? standardOutput, string? standardError)
    {
        var output = Lines(standardOutput);
        var error = Lines(standardError);
        var notices = output.Concat(error)
            .Where(line => line.StartsWith(NoticePrefix, StringComparison.Ordinal))
            .Select(line => line[NoticePrefix.Length..].Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (exitCode == 0)
        {
            var saved = output.LastOrDefault(line => line.StartsWith(SavedPrefix, StringComparison.Ordinal))?[SavedPrefix.Length..].Trim();
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(saved)) parts.Add($"Saved to {saved}");
            // The last line the script wrote itself is its result; anything before it may be
            // the tail of a warning PowerShell wrapped.
            else if (output.LastOrDefault(IsPlainOutput) is { } result) parts.Add(result.Trim());
            parts.AddRange(notices);
            var message = parts.Count == 0 ? "The maintenance operation completed successfully." : Join(parts);
            return new(new(true, message), string.Empty);
        }

        var reason = error.LastOrDefault(line => line.StartsWith(FailurePrefix, StringComparison.Ordinal))?[FailurePrefix.Length..].Trim();
        if (string.IsNullOrEmpty(reason)) reason = ErrorRecordMessage(error);
        var logPath = error.LastOrDefault(line => line.StartsWith(LogPrefix, StringComparison.Ordinal))?[LogPrefix.Length..].Trim();
        var code = ReasonCode(reason);
        var text = string.IsNullOrEmpty(reason)
            ? $"The script stopped (exit code {exitCode}) without giving a reason."
            : Sentence(reason);
        // Only a log the script says it wrote: pointing at a log that does not exist is what
        // this replaced.
        if (!string.IsNullOrEmpty(logPath)) text += $" The failure was recorded in {logPath}.";
        return new(new(false, Truncate(text)), code);
    }

    internal static string ReasonCode(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return NoReason;
        foreach (var (code, pattern) in KnownReasons)
            if (pattern.IsMatch(reason)) return code;
        return Unclassified;
    }

    internal static string FailureEventId(string scriptName, string reasonCode)
    {
        var prefix = scriptName.ToLowerInvariant() switch
        {
            "backup-etp-database.ps1" => "BACKUP_RUN_FAILED",
            "invoke-etp-recovery-drill.ps1" => "RECOVERY_DRILL_RUN_FAILED",
            "new-etp-support-package.ps1" => "SUPPORT_PACKAGE_RUN_FAILED",
            _ => "MAINTENANCE_RUN_FAILED"
        };
        return string.IsNullOrEmpty(reasonCode) ? prefix : $"{prefix}:{reasonCode}";
    }

    // PowerShell writes an uncaught error as the message - wrapped at the host width, with
    // nothing added at the break - followed by "At <script>:<line> char:<n>" and "+ ..." lines.
    // A plain line written by the script itself (the support package's health refusal) has
    // none of those and is returned whole.
    internal static string? ErrorRecordMessage(IReadOnlyList<string> errorLines)
    {
        var builder = new StringBuilder();
        foreach (var raw in errorLines)
        {
            if (raw.StartsWith("ETP_", StringComparison.Ordinal)) continue;
            var trimmed = raw.TrimStart();
            if (Regex.IsMatch(raw, @"^At (?:line:\d+|.+:\d+) char:\d+", RegexOptions.CultureInvariant) || trimmed.StartsWith('+'))
            {
                if (builder.Length > 0) break;
                continue;
            }
            builder.Append(raw);
        }
        var message = Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
        return message.Length == 0 ? null : message;
    }

    // Write-Warning output is wrapped when redirected; the scripts repeat any warning worth
    // showing on an ETP_NOTICE line instead.
    private static bool IsPlainOutput(string line) =>
        !line.StartsWith("ETP_", StringComparison.Ordinal) &&
        !line.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase) &&
        !line.StartsWith("VERBOSE:", StringComparison.OrdinalIgnoreCase);

    private static List<string> Lines(string? text) =>
        (text ?? string.Empty)
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Trim().Length > 0)
            .ToList();

    private static string Sentence(string text)
    {
        var trimmed = text.Trim();
        return trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?') || trimmed.EndsWith(')') ? trimmed : trimmed + ".";
    }

    private static string Join(IEnumerable<string> parts) => Truncate(string.Join(" ", parts.Select(part => Sentence(part.Trim()))));

    private static string Truncate(string text) =>
        text.Length <= MaxMessageLength ? text : text[..(MaxMessageLength - 1)] + "…";
}
