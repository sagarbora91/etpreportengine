extern alias EtpApplication;

using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Text.RegularExpressions;
using EtpApplication::Etp.Reporting.Application.HealthCheck;

namespace Etp.Reporting.Desktop.Modules.HealthCheck;

/// <summary>
/// One Windows event the probe asked for. <see cref="Data"/> are the event's own data items (for 18456 the first is the login
/// name). They are used for filtering only and are never exported: the health line carries event ids and counts.
/// </summary>
public sealed record EventLogEntryInfo(string Provider, int EventId, DateTimeOffset Time, IReadOnlyList<string> Data)
{
    public string Text => string.Join(' ', Data);
}

public enum EventLogReadStatus { Ok, Denied, Unavailable }

public sealed record EventLogReadResult(EventLogReadStatus Status, IReadOnlyList<EventLogEntryInfo> Entries);

/// <summary>Read-only access to a Windows event log (replaceable in tests).</summary>
public interface IEventLogSource
{
    EventLogReadResult Read(string logName, IReadOnlyCollection<int> eventIds, DateTimeOffset sinceUtc, int maxEntries, CancellationToken cancellationToken);
}

/// <summary>The real reader: <see cref="EventLogReader"/> with an XPath query. Never writes, never clears.</summary>
public sealed class WindowsEventLogSource : IEventLogSource
{
    public EventLogReadResult Read(string logName, IReadOnlyCollection<int> eventIds, DateTimeOffset sinceUtc, int maxEntries, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) return new(EventLogReadStatus.Unavailable, []);
        var ids = string.Join(" or ", eventIds.Select(id => "EventID=" + id.ToString(CultureInfo.InvariantCulture)));
        var since = sinceUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var xpath = $"*[System[({ids}) and TimeCreated[@SystemTime>='{since}']]]";
        var entries = new List<EventLogEntryInfo>();
        try
        {
            using var reader = new EventLogReader(new EventLogQuery(logName, PathType.LogName, xpath));
            while (entries.Count < maxEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var record = reader.ReadEvent();
                if (record is null) break;
                var data = record.Properties.Select(property => Convert.ToString(property.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
                entries.Add(new(record.ProviderName ?? "", record.Id, record.TimeCreated is { } created ? new DateTimeOffset(created) : sinceUtc, data));
            }
            return new(EventLogReadStatus.Ok, entries);
        }
        catch (UnauthorizedAccessException) { return new(EventLogReadStatus.Denied, []); }
        catch (EventLogException) { return new(EventLogReadStatus.Unavailable, []); }
        catch (InvalidOperationException) { return new(EventLogReadStatus.Unavailable, []); }
    }
}

/// <summary>What the event log held for the last window, after the noise filters. Counts only.</summary>
public sealed record EventLogFindings(
    int EtpCrashes,
    int EtpHangs,
    IReadOnlyDictionary<int, int> EngineErrors,
    IReadOnlyDictionary<int, int> SqlFileErrors,
    int AutomationLoginFailures,
    int IgnoredNoise)
{
    public int EngineErrorCount => EngineErrors.Values.Sum();
    public int SqlFileErrorCount => SqlFileErrors.Values.Sum();
    public int Total => EtpCrashes + EtpHangs + EngineErrorCount + SqlFileErrorCount + AutomationLoginFailures;
}

/// <summary>
/// OPS-09 (design 5.2). The rules are pure so tests can feed synthetic events:
/// ETP crash = Application Error 1000 or .NET Runtime 1026 naming Etp.Reporting.Desktop; ETP hang = Application Hang 1002 naming it;
/// engine error = 3449, 823, 824, 825 from the SQL Server provider; file errors = 17204, 17207, 9002;
/// 18456 only for the configured automation account. Ignored as noise: 1000/1002/1026 events of other programs
/// and non-ETP .NET events, and 18456 for the EtpPhase0Test_Missing_* test logins or any other login.
/// </summary>
public static class EventLogClassifier
{
    public const string EtpProgram = "Etp.Reporting.Desktop";
    public const string TestLoginPrefix = "EtpPhase0Test_Missing_";

    public static readonly IReadOnlyList<int> EngineEventIds = [3449, 823, 824, 825];
    public static readonly IReadOnlyList<int> SqlFileEventIds = [17204, 17207, 9002];
    public static readonly IReadOnlyList<int> QueryEventIds = [1000, 1002, 1026, 3449, 823, 824, 825, 17204, 17207, 9002, 18456];

    private static readonly Regex QuotedLogin = new("'([^']+)'", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static EventLogFindings Classify(IEnumerable<EventLogEntryInfo> entries, OperationsProbeSettings settings)
    {
        var crashes = 0; var hangs = 0; var logins = 0; var noise = 0;
        var engine = new SortedDictionary<int, int>(); var files = new SortedDictionary<int, int>();
        foreach (var entry in entries)
        {
            var provider = entry.Provider.Trim();
            var isSql = settings.SqlEventProviders.Any(name => name.Equals(provider, StringComparison.OrdinalIgnoreCase));
            var namesEtp = entry.Text.Contains(EtpProgram, StringComparison.OrdinalIgnoreCase);
            switch (entry.EventId)
            {
                case 1000 when provider.Equals("Application Error", StringComparison.OrdinalIgnoreCase):
                case 1026 when provider.Equals(".NET Runtime", StringComparison.OrdinalIgnoreCase):
                    if (namesEtp) crashes++; else noise++;
                    break;
                case 1002 when provider.Equals("Application Hang", StringComparison.OrdinalIgnoreCase):
                    if (namesEtp) hangs++; else noise++;
                    break;
                case 3449 or 823 or 824 or 825:
                    if (isSql) Increment(engine, entry.EventId); else noise++;
                    break;
                case 17204 or 17207 or 9002:
                    if (isSql) Increment(files, entry.EventId); else noise++;
                    break;
                case 18456:
                    if (!isSql) { noise++; break; }
                    if (IsAutomationLogin(LoginOf(entry), settings.AutomationAccount)) logins++; else noise++;
                    break;
                default:
                    noise++;
                    break;
            }
        }
        return new(crashes, hangs, engine, files, logins, noise);
    }

    private static void Increment(IDictionary<int, int> counts, int id) => counts[id] = counts.TryGetValue(id, out var count) ? count + 1 : 1;

    private static string LoginOf(EventLogEntryInfo entry)
    {
        var first = entry.Data.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item));
        if (first is not null && !first.Contains(' ')) return first.Trim();
        var quoted = QuotedLogin.Match(entry.Text);
        return quoted.Success ? quoted.Groups[1].Value.Trim() : (first ?? "").Trim();
    }

    /// <summary>True for the configured automation account only; the Phase 0 test logins and every other login are noise.</summary>
    public static bool IsAutomationLogin(string login, string? automationAccount)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(automationAccount)) return false;
        var bare = BareName(login);
        if (bare.StartsWith(TestLoginPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        return login.Equals(automationAccount, StringComparison.OrdinalIgnoreCase)
            || bare.Equals(BareName(automationAccount), StringComparison.OrdinalIgnoreCase);
    }

    private static string BareName(string account)
    {
        var slash = account.LastIndexOf('\\');
        return (slash >= 0 ? account[(slash + 1)..] : account).Trim();
    }
}

/// <summary>OPS-09: the Application event log of the last 24 hours. Exports event ids and counts only.</summary>
public sealed class EventLogProbe(IEventLogSource source, OperationsProbeSettings settings)
{
    public const string LogName = "Application";
    public const int MaxEntries = 5000;

    public async Task<HealthCheckResult> RunAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        EventLogReadResult read;
        try
        {
            read = await Task.Run(() => source.Read(LogName, EventLogClassifier.QueryEventIds, nowUtc - settings.EventLogWindow, MaxEntries, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { read = new(EventLogReadStatus.Unavailable, []); }
        if (read.Status != EventLogReadStatus.Ok)
            return Result(HealthOutcome.Skipped, "EVENT_LOG_UNREADABLE",
                "The Windows Application event log could not be read by this account; the nightly run checks it.", null, stopwatch, null, null);
        return Evaluate(EventLogClassifier.Classify(read.Entries, settings), stopwatch);
    }

    /// <summary>The line for already classified findings (tests call this and <see cref="EventLogClassifier"/> directly).</summary>
    public static HealthCheckResult Evaluate(EventLogFindings findings, Stopwatch? stopwatch = null)
    {
        stopwatch ??= Stopwatch.StartNew();
        var subs = new List<HealthCheckSubResult>();
        if (findings.EtpCrashes > 0) subs.Add(new("etp-crash", HealthOutcome.Fail, "ETP_CRASH", findings.EtpCrashes, Fields("events", "1000,1026")));
        if (findings.EngineErrorCount > 0)
            foreach (var pair in findings.EngineErrors)
                subs.Add(new("event:" + pair.Key.ToString(CultureInfo.InvariantCulture), HealthOutcome.Fail, "SQL_ENGINE_ERROR", pair.Value, Fields("eventId", pair.Key.ToString(CultureInfo.InvariantCulture))));
        if (findings.EtpHangs > 0) subs.Add(new("etp-hang", HealthOutcome.Warning, "ETP_HANG", findings.EtpHangs, Fields("events", "1002")));
        if (findings.AutomationLoginFailures > 0) subs.Add(new("automation-login", HealthOutcome.Warning, "AUTOMATION_LOGIN_FAILED", findings.AutomationLoginFailures, Fields("eventId", "18456")));
        foreach (var pair in findings.SqlFileErrors)
            subs.Add(new("event:" + pair.Key.ToString(CultureInfo.InvariantCulture), HealthOutcome.Warning, "SQL_FILE_ERROR", pair.Value, Fields("eventId", pair.Key.ToString(CultureInfo.InvariantCulture))));

        if (findings.EtpCrashes > 0 || findings.EngineErrorCount > 0)
        {
            var code = findings.EtpCrashes > 0 ? "ETP_CRASH" : "SQL_ENGINE_ERROR";
            return Result(HealthOutcome.Fail, code, Summary(findings, "In the last 24 hours the event log shows"), "health", stopwatch, findings.Total, subs);
        }
        if (subs.Count > 0)
        {
            var code = findings.EtpHangs > 0 ? "ETP_HANG" : findings.AutomationLoginFailures > 0 ? "AUTOMATION_LOGIN_FAILED" : "SQL_FILE_ERROR";
            return Result(HealthOutcome.Warning, code, Summary(findings, "In the last 24 hours the event log shows"), "health", stopwatch, findings.Total, subs);
        }
        return Result(HealthOutcome.Pass, "OK", "No ETP crash or hang and no SQL Server engine error in the Windows event log for the last 24 hours.", null, stopwatch, 0, null);
    }

    private static string Summary(EventLogFindings findings, string prefix)
    {
        var parts = new List<string>();
        if (findings.EtpCrashes > 0) parts.Add($"{findings.EtpCrashes} ETP crash event(s)");
        if (findings.EtpHangs > 0) parts.Add($"{findings.EtpHangs} ETP hang event(s)");
        foreach (var pair in findings.EngineErrors) parts.Add($"SQL Server error {pair.Key} x{pair.Value}");
        foreach (var pair in findings.SqlFileErrors) parts.Add($"SQL Server error {pair.Key} x{pair.Value}");
        if (findings.AutomationLoginFailures > 0) parts.Add($"{findings.AutomationLoginFailures} failed sign-in(s) of the automation account (18456)");
        return prefix + " " + string.Join("; ", parts) + ".";
    }

    private static IReadOnlyDictionary<string, string> Fields(string key, string value) => new Dictionary<string, string> { [key] = value };

    private static HealthCheckResult Result(HealthOutcome outcome, string code, string text, string? fix, Stopwatch stopwatch, decimal? metric, IReadOnlyList<HealthCheckSubResult>? subs) =>
        new(HealthCheckId.Ops09, HealthCheckArea.Operations, outcome, code, text, fix, stopwatch.Elapsed, metric, subs);
}
