using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Etp.Reporting.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Desktop.Modules.HealthCheck;

/// <summary>What one five-minute tick did about the nightly health check.</summary>
public enum NightlyHealthOutcome
{
    /// <summary>Before 02:30, or the Nightly run for D-1 is already recorded.</summary>
    NotDue,
    /// <summary>Another unattended run (an import or a health check) holds the automation lease; the next tick tries again.</summary>
    Busy,
    /// <summary>The run for this business date failed <see cref="NightlyHealthSchedule.MaxAttemptsPerBusinessDate"/> times; tomorrow's starts fresh.</summary>
    AttemptsUsedUp,
    /// <summary>Less than <see cref="NightlyHealthSchedule.RetryInterval"/> since the last failed attempt.</summary>
    Waiting,
    Completed,
    /// <summary>The run could not complete; <c>HEALTH_RUN_FAILED</c> was reported.</summary>
    Failed
}

/// <summary>The last nightly attempt this account made (a small file beside its diagnostics log).</summary>
public sealed record NightlyHealthAttempt(DateOnly BusinessDate, int Attempts, DateTimeOffset LastAttemptUtc);

public interface INightlyHealthAttemptStore
{
    NightlyHealthAttempt? Load();
    void Save(NightlyHealthAttempt attempt);
}

/// <summary>
/// <c>%LOCALAPPDATA%\EtpReporting\Logs\nightly-health-attempt.json</c> of the account running the automation task.
/// Losing it is harmless (at most three more attempts); an unreadable file counts as none.
/// </summary>
public sealed class FileNightlyHealthAttemptStore(string path) : INightlyHealthAttemptStore
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtpReporting", "Logs", "nightly-health-attempt.json");

    public NightlyHealthAttempt? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new(
                DateOnly.ParseExact(root.GetProperty("businessDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                root.GetProperty("attempts").GetInt32(),
                DateTimeOffset.Parse(root.GetProperty("lastAttemptUtc").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
                                              or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Save(NightlyHealthAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["businessDate"] = attempt.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["attempts"] = attempt.Attempts,
                ["lastAttemptUtc"] = attempt.LastAttemptUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            }));
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// 1.11.0 self-diagnostics, design section 7 and decision 33 (Q1). The nightly health check runs inside the existing
/// five-minute automation task (<c>--automation-once</c>), after the watch-folder import step:
/// <list type="bullet">
/// <item>Due when local time is <b>02:30 or later</b> and no Nightly run is recorded for business date D-1
/// (yesterday). The first tick after 02:30 runs it; if the PC was off, the first tick after it is back on runs it,
/// <b>whatever the time of day</b>, shop hours included (catch-up, Q1 as recommended).</item>
/// <item>It does not depend on watch-folder import being enabled (<see cref="RunAfterAutomationAsync"/>).</item>
/// <item>It holds the automation lease (<see cref="NightlyHealthLease"/>, the same SQL application lock as the
/// import), so it never overlaps an import run or another health run, nightly or manual.</item>
/// <item>A run that cannot complete is reported (<c>HEALTH_RUN_FAILED</c>) and retried at most three times per
/// business date, 30 minutes apart, so a database that is down does not fill the log every five minutes.</item>
/// <item>The task's exit code keeps its meaning (import sources failed); a health Fail never changes it.</item>
/// </list>
/// </summary>
public sealed class NightlyHealthSchedule
{
    /// <summary>02:30 local: after the 22:00 backup, before the shop opens.</summary>
    public static TimeSpan NightlyTime { get; } = new(2, 30, 0);
    public const int MaxAttemptsPerBusinessDate = 3;
    public static TimeSpan RetryInterval { get; } = TimeSpan.FromMinutes(30);

    private readonly Func<CancellationToken, Task<DateOnly?>> loadLastNightlyBusinessDate;
    private readonly Func<CancellationToken, Task<IAsyncDisposable?>> tryAcquireLease;
    private readonly Func<CancellationToken, Task> runNightly;
    private readonly INightlyHealthAttemptStore attempts;
    private readonly TimeProvider time;
    private readonly Action<Exception>? reportFailure;

    /// <param name="loadLastNightlyBusinessDate">The business date of the latest recorded Nightly run, or null (<c>diag.load_health_check_summary</c>).</param>
    /// <param name="tryAcquireLease">The automation lease, or null when another unattended run holds it.</param>
    /// <param name="runNightly">Runs, exports and records one Nightly health check.</param>
    /// <param name="attempts">Where failed attempts are counted.</param>
    /// <param name="time">Clock and local time zone.</param>
    /// <param name="reportFailure">Called once per failed attempt (diagnostics entry and stderr line).</param>
    public NightlyHealthSchedule(
        Func<CancellationToken, Task<DateOnly?>> loadLastNightlyBusinessDate,
        Func<CancellationToken, Task<IAsyncDisposable?>> tryAcquireLease,
        Func<CancellationToken, Task> runNightly,
        INightlyHealthAttemptStore attempts,
        TimeProvider? time = null,
        Action<Exception>? reportFailure = null)
    {
        this.loadLastNightlyBusinessDate = loadLastNightlyBusinessDate ?? throw new ArgumentNullException(nameof(loadLastNightlyBusinessDate));
        this.tryAcquireLease = tryAcquireLease ?? throw new ArgumentNullException(nameof(tryAcquireLease));
        this.runNightly = runNightly ?? throw new ArgumentNullException(nameof(runNightly));
        this.attempts = attempts ?? throw new ArgumentNullException(nameof(attempts));
        this.time = time ?? TimeProvider.System;
        this.reportFailure = reportFailure;
    }

    /// <summary>D-1: yesterday's local date.</summary>
    public static DateOnly BusinessDateFor(DateTime nowLocal) => DateOnly.FromDateTime(nowLocal).AddDays(-1);

    /// <summary>
    /// True at 02:30 local or later when no Nightly run exists for D-1. Before 02:30 it is never due, even when the
    /// last run is days old (the previous night's slot is taken by the run at or after 02:30).
    /// </summary>
    public static bool IsDue(DateTime nowLocal, DateOnly? lastNightlyBusinessDate) =>
        nowLocal.TimeOfDay >= NightlyTime
        && (lastNightlyBusinessDate is not { } last || last < BusinessDateFor(nowLocal));

    /// <summary>Whether another attempt for <paramref name="businessDate"/> may start now.</summary>
    public static NightlyHealthOutcome? AttemptGate(NightlyHealthAttempt? previous, DateOnly businessDate, DateTimeOffset nowUtc)
    {
        if (previous is null || previous.BusinessDate != businessDate) return null;
        if (previous.Attempts >= MaxAttemptsPerBusinessDate) return NightlyHealthOutcome.AttemptsUsedUp;
        if (nowUtc - previous.LastAttemptUtc < RetryInterval) return NightlyHealthOutcome.Waiting;
        return null;
    }

    /// <summary>One tick: runs the nightly check when it is due and the lease is free. Never throws except for cancellation.</summary>
    public async Task<NightlyHealthOutcome> RunIfDueAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = time.GetUtcNow();
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, time.LocalTimeZone).DateTime;
        // Before 02:30 nothing is read, not even the database.
        if (nowLocal.TimeOfDay < NightlyTime) return NightlyHealthOutcome.NotDue;
        var businessDate = BusinessDateFor(nowLocal);
        var previous = attempts.Load();
        if (AttemptGate(previous, businessDate, nowUtc) is { } gate) return gate;

        var attemptRecorded = false;
        void RecordAttempt()
        {
            if (attemptRecorded) return;
            attemptRecorded = true;
            var count = previous is not null && previous.BusinessDate == businessDate ? previous.Attempts + 1 : 1;
            attempts.Save(new(businessDate, count, nowUtc));
        }

        try
        {
            if (!IsDue(nowLocal, await loadLastNightlyBusinessDate(cancellationToken).ConfigureAwait(false))) return NightlyHealthOutcome.NotDue;
            var lease = await tryAcquireLease(cancellationToken).ConfigureAwait(false);
            if (lease is null) return NightlyHealthOutcome.Busy;
            await using (lease.ConfigureAwait(false))
            {
                // Checked again under the lease: a run that finished while this tick waited is not repeated.
                if (!IsDue(nowLocal, await loadLastNightlyBusinessDate(cancellationToken).ConfigureAwait(false))) return NightlyHealthOutcome.NotDue;
                RecordAttempt();
                await runNightly(cancellationToken).ConfigureAwait(false);
                return NightlyHealthOutcome.Completed;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            RecordAttempt();
            try { reportFailure?.Invoke(exception); }
            catch (Exception) { /* reporting must never turn into the task's failure */ }
            return NightlyHealthOutcome.Failed;
        }
    }

    /// <summary>
    /// Runs the watch-folder automation step, then the nightly health step <b>whatever the automation step did</b>:
    /// also when watch-folder import is disabled (it returns early) and when it threw (the health check is what
    /// reports a broken automation). The automation step's exit code is returned unchanged and its exception is
    /// rethrown after the health step; the health step never changes either.
    /// </summary>
    public static async Task<int> RunAfterAutomationAsync(
        Func<CancellationToken, Task<int>> automation,
        Func<CancellationToken, Task<NightlyHealthOutcome>> nightlyHealth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(automation);
        ArgumentNullException.ThrowIfNull(nightlyHealth);
        ExceptionDispatchInfo? automationFailure = null;
        var exitCode = 1;
        try { exitCode = await automation(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { automationFailure = ExceptionDispatchInfo.Capture(exception); }

        try { await nightlyHealth(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { automationFailure?.Throw(); throw; }
        catch (Exception) { /* RunIfDueAsync reports its own failures; nothing here may change the exit code */ }

        automationFailure?.Throw();
        return exitCode;
    }
}

/// <summary>
/// The automation lease for health runs: the SQL application lock <c>ETP_PHASE2_AUTOMATION</c> that
/// <c>AutomatedOperationsService</c> takes for each import run (<c>AutomationSessionLease</c>, internal to the SQL
/// Server assembly), session-owned, never waited for. The manual "Run health check" takes it too, so a manual run
/// started while the nightly run is active is refused ("A health check is already running").
/// </summary>
public static class NightlyHealthLease
{
    /// <summary>Must equal <c>AutomationSessionLease.Resource</c>.</summary>
    public const string Resource = "ETP_PHASE2_AUTOMATION";

    public static async Task<IAsyncDisposable?> TryAcquireAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new SqlCommand(
                "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; SELECT @result;",
                connection);
            command.Parameters.Add(new SqlParameter("@resource", System.Data.SqlDbType.NVarChar, 255) { Value = Resource });
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) >= 0)
                return new Lease(connection);
            await connection.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch
        {
            SqlConnection.ClearPool(connection);
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private sealed class Lease(SqlConnection connection) : IAsyncDisposable
    {
        private bool disposed;

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                await using var release = new SqlCommand(
                    "DECLARE @result int; EXEC @result=sys.sp_releaseapplock @Resource=@resource,@LockOwner='Session'; SELECT @result;",
                    connection) { CommandTimeout = 10 };
                release.Parameters.Add(new SqlParameter("@resource", System.Data.SqlDbType.NVarChar, 255) { Value = Resource });
                // A cancelled health run must still release the lock before the connection goes back to the pool.
                if (Convert.ToInt32(await release.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false), CultureInfo.InvariantCulture) < 0)
                    SqlConnection.ClearPool(connection);
            }
            catch { SqlConnection.ClearPool(connection); }
            finally { await connection.DisposeAsync().ConfigureAwait(false); }
        }
    }
}
