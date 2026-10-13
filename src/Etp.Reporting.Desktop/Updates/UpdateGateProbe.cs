extern alias EtpApplication;

using System.Diagnostics;
using System.IO;
using EtpApplication::Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop.Updates;

/// <summary>
/// 1.12 wave 2 (B2). Where each field of the update gate comes from (B1 note, "Gate state sources"). Every source is a
/// delegate, so the probe is tested without a window or a database. The ones that touch WPF objects run through
/// <c>OnUi</c> (the window's dispatcher); the ones that touch SQL or the machine run on whatever thread asks.
/// </summary>
internal sealed record UpdateGateSources(
    Func<bool> IsOwner,
    // The Import screen is validating or importing in this window (UI).
    Func<bool> ImportRunningInThisWindow,
    // An import batch is open in the database (<c>open_imports</c> of load_database_operational_health, which also covers the automation run).
    Func<CancellationToken, Task<bool>> ImportOpenInDatabase,
    // Another run holds the automation lease (the nightly health run, a manual health check, or an automatic import).
    Func<CancellationToken, Task<bool>> AutomationLeaseHeld,
    // The Operations screen is running a backup, a recovery drill or a support package in this window (UI).
    Func<bool> OperationsRunnerBusy,
    // The scheduled daily backup task is running right now.
    Func<bool> ScheduledBackupRunning,
    // The scheduled monthly recovery drill task is running right now.
    Func<bool> ScheduledDrillRunning,
    // A screen holds unsaved work: the screens' draft guards, plus the retained review drafts (UI).
    Func<bool> EntryDraftOpen,
    Func<bool> OtherEtpSessionOpen,
    Func<bool> KeysConfigured,
    Func<CancellationToken, Task<bool>> UpdateCheckEnabled);

/// <summary>
/// Builds the <see cref="UpdateGateState"/> the engine and the confirm sheet use. A source that fails reads as "not
/// running" (it must never make the update look blocked forever), except that the gate is read again after the backup, so
/// an import or a draft that started meanwhile still stops the update.
/// </summary>
internal sealed class UpdateGateProbe(UpdateGateSources sources, Func<Func<bool>, bool> onUi)
{
    /// <summary>The window's dispatcher marshal for the real app; tests pass a direct call.</summary>
    public UpdateGateProbe(UpdateGateSources sources) : this(sources, static read => read()) { }

    public async Task<UpdateGateState> ReadAsync(CancellationToken cancellationToken)
    {
        var importOpen = await Safe(sources.ImportOpenInDatabase, cancellationToken).ConfigureAwait(false);
        var leaseHeld = await Safe(sources.AutomationLeaseHeld, cancellationToken).ConfigureAwait(false);
        var enabled = await Safe(sources.UpdateCheckEnabled, cancellationToken).ConfigureAwait(false);

        var isOwner = onUi(sources.IsOwner);
        var importInWindow = onUi(sources.ImportRunningInThisWindow);
        var operationsBusy = onUi(sources.OperationsRunnerBusy);
        var draft = onUi(sources.EntryDraftOpen);

        var backup = operationsBusy || Safe(sources.ScheduledBackupRunning);
        var drill = Safe(sources.ScheduledDrillRunning);
        var importRunning = importInWindow || importOpen;
        // The lease is also held by an automatic import. When an import is open, that is already the sentence shown.
        var healthRun = leaseHeld && !importOpen;
        return new UpdateGateState(
            IsOwner: isOwner,
            KeysConfigured: Safe(sources.KeysConfigured, fallback: false),
            UpdateCheckEnabled: enabled,
            ImportRunning: importRunning,
            HealthRunRunning: healthRun,
            BackupRunning: backup,
            RecoveryDrillRunning: drill,
            EntryDraftOpen: draft,
            OtherEtpSessionOpen: Safe(sources.OtherEtpSessionOpen));
    }

    /// <summary>
    /// The engine's <c>gateNow</c>. The engine calls it from a pool thread inside <c>Task.Run</c>, never from the UI thread,
    /// so waiting for the SQL reads here cannot deadlock.
    /// </summary>
    public UpdateGateState ReadBlocking() => ReadAsync(CancellationToken.None).GetAwaiter().GetResult();

    private static async Task<bool> Safe(Func<CancellationToken, Task<bool>> read, CancellationToken cancellationToken)
    {
        try { return await read(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Updates.Gate", "UPDATE_GATE_SOURCE_UNREADABLE", DesktopDiagnosticSeverity.Warning);
            return false;
        }
    }

    private static bool Safe(Func<bool> read, bool fallback = false)
    {
        try { return read(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            DesktopDiagnostics.Record(exception, "Updates.Gate", "UPDATE_GATE_SOURCE_UNREADABLE", DesktopDiagnosticSeverity.Warning);
            return fallback;
        }
    }
}

/// <summary>The state of the two scheduled maintenance tasks, read with the system's own schtasks (no elevation needed to query).</summary>
internal static class UpdateScheduledTasks
{
    public const string DailyBackup = "ETP Reporting Daily Backup";
    public const string MonthlyRecoveryDrill = "ETP Reporting Monthly Recovery Drill";

    /// <summary>The Status column of <c>schtasks /Query /FO CSV /NH</c> says Running (Windows' English word; any other language reads as not running, which only means no early warning).</summary>
    internal static bool StatusLineMeansRunning(string? csvLine)
    {
        if (string.IsNullOrWhiteSpace(csvLine)) return false;
        var fields = csvLine.Split("\",\"", StringSplitOptions.None);
        return fields.Length >= 3 && fields[2].Trim('"', ' ', '\r', '\n').Equals("Running", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRunning(string taskName)
    {
        try
        {
            var start = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "/Query", "/TN", taskName, "/FO", "CSV", "/NH" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000)) { try { process.Kill(); } catch (InvalidOperationException) { } return false; }
            return process.ExitCode == 0 && StatusLineMeansRunning(output.GetAwaiter().GetResult().Split('\n').FirstOrDefault());
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
