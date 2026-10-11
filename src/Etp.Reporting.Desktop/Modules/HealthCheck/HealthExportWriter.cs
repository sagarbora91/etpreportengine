extern alias EtpApplication;

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using EtpApplication::Etp.Reporting.Application.ErrorRegister;
using EtpApplication::Etp.Reporting.Application.HealthCheck;

namespace Etp.Reporting.Desktop.Modules.HealthCheck;

/// <summary>Everything one export file is made from (design section 8).</summary>
/// <param name="Run">The finished run (results, counts, overall).</param>
/// <param name="PreviousRun">The previous run of the same trigger, for <c>previousRun.changed</c>.</param>
/// <param name="Register">The whole register as loaded; the writer selects what goes in (<see cref="HealthExportWriter.SelectRegister"/>).</param>
/// <param name="RegisterDetails">Entry details by register id, when the caller could read them (Owner only); else null.</param>
/// <param name="RecentNightlyRuns">Recent Nightly runs, for the register's <c>verified</c> flag.</param>
/// <param name="WarningsByEvent">Warning-severity diagnostics entries per event code (Q7), when known.</param>
public sealed record HealthExportInput(
    HealthCheckRun Run,
    HealthCheckPreviousRun? PreviousRun,
    IReadOnlyList<ErrorRegisterEntry> Register,
    IReadOnlyDictionary<int, ErrorRegisterEntryDetail>? RegisterDetails = null,
    IReadOnlyList<HealthCheckRunSummary>? RecentNightlyRuns = null,
    IReadOnlyDictionary<string, int>? WarningsByEvent = null);

/// <summary>
/// The outcome of one write. <see cref="FileName"/> is null when nothing was written, and
/// <see cref="FailureCode"/> then says why: <c>EXPORT_FOLDER_MISSING</c> (setup creates the folder, the
/// application never does), <c>EXPORT_FOLDER_LINKED</c> or <c>EXPORT_WRITE_FAILED</c>.
/// </summary>
public sealed record HealthExportResult(string? FileName, string? FullPath, int Redactions, string? FailureCode);

/// <summary>
/// 1.11.0 self-diagnostics (design section 8). Writes one privacy-checked JSON file per health run,
/// <c>health-yyyyMMdd-HHmmss-&lt;Manual|Nightly&gt;.json</c> (local start time), into
/// <c>%ProgramData%\EtpReporting\HealthExports</c>: written to a temporary name and moved, so the weekly audit never
/// reads half a file. Keeps 90 days (Q14): older <c>health-*.json</c> files are deleted by the writer, its own files
/// only. The register goes into every file: entries not Fixed, entries changed in the last 30 days, and Fixed entries
/// for 45 days after the fix (so the <c>verified</c> flag, set after 30 days without recurrence, is seen by a weekly
/// audit). Never throws for a file-system problem: the run is still recorded, with no export file name.
/// </summary>
public sealed partial class HealthExportWriter
{
    public const string Schema = "etp-health-export/1";
    public const int RetentionDays = 90;
    internal const int RegisterChangedDays = 30;
    internal const int FixedExportDays = 45;
    internal const int VerifiedQuietDays = 30;
    internal const int VerifiedNightlyRuns = 5;
    internal const int MaxSampleRefs = 5;
    internal const int MaxContexts = 5;

    /// <summary>Explanation templates the writer itself adds to a run (ERR-01 when the export redacted something).</summary>
    public const string RedactionWarningText = "The health export replaced {count} value(s) that were not safe to export.";
    public const string RedactionFinding = "EXPORT_REDACTED";

    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    private readonly string directory;
    private readonly IReadOnlyList<string> textTemplates;
    private readonly TimeProvider time;

    /// <param name="directory">Normally <see cref="DefaultDirectory"/>.</param>
    /// <param name="textTemplates">The explanation and fix templates (<c>HealthCheckTexts</c>) the guard accepts.</param>
    public HealthExportWriter(string directory, IEnumerable<string> textTemplates, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(textTemplates);
        this.directory = Path.GetFullPath(directory);
        this.textTemplates = [.. textTemplates, RedactionWarningText];
        this.time = time ?? TimeProvider.System;
    }

    /// <summary><c>%ProgramData%\EtpReporting\HealthExports</c> (Workpc: <c>C:\ProgramData\EtpReporting\HealthExports</c>).</summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EtpReporting", "HealthExports");

    public string Directory => directory;

    /// <summary>A new guard with this writer's templates (one per file).</summary>
    public HealthExportPrivacyGuard CreateGuard() => new(textTemplates);

    /// <summary>
    /// How many values the guard would replace, without writing anything. The composition root calls this first so
    /// a redaction can be reported on ERR-01 of the same run (<see cref="WithRedactionWarning"/>).
    /// </summary>
    public int CountRedactions(HealthExportInput input)
    {
        var guard = CreateGuard();
        Serialize(input, guard);
        return guard.Redactions;
    }

    /// <summary>Writes the file and prunes old ones. File-system failures come back as a <see cref="HealthExportResult.FailureCode"/>.</summary>
    public HealthExportResult Write(HealthExportInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var guard = CreateGuard();
        var bytes = Serialize(input, guard);
        try
        {
            if (!System.IO.Directory.Exists(directory)) return new(null, null, guard.Redactions, "EXPORT_FOLDER_MISSING");
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return new(null, null, guard.Redactions, "EXPORT_FOLDER_LINKED");
            var name = FileNameFor(input.Run.Trigger, input.Run.StartedUtc, time.LocalTimeZone);
            var temporary = Path.Combine(directory, "." + Path.GetFileNameWithoutExtension(name) + "-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                stream.Write(bytes);
            string? final = null;
            try
            {
                for (var attempt = 1; attempt <= 9 && final is null; attempt++)
                {
                    var candidate = attempt == 1 ? name : Path.GetFileNameWithoutExtension(name) + "-" + attempt.ToString(CultureInfo.InvariantCulture) + ".json";
                    try { File.Move(temporary, Path.Combine(directory, candidate), overwrite: false); final = candidate; }
                    catch (IOException) when (File.Exists(Path.Combine(directory, candidate))) { }
                }
            }
            finally
            {
                if (final is null && File.Exists(temporary)) File.Delete(temporary);
            }
            if (final is null) return new(null, null, guard.Redactions, "EXPORT_WRITE_FAILED");
            Prune();
            return new(final, Path.Combine(directory, final), guard.Redactions, null);
        }
        catch (IOException) { return new(null, null, guard.Redactions, "EXPORT_WRITE_FAILED"); }
        catch (UnauthorizedAccessException) { return new(null, null, guard.Redactions, "EXPORT_WRITE_FAILED"); }
        catch (System.Security.SecurityException) { return new(null, null, guard.Redactions, "EXPORT_WRITE_FAILED"); }
    }

    /// <summary>
    /// Deletes this writer's own export files dated more than <see cref="RetentionDays"/> days before today (local),
    /// and temporary files a crashed write left behind more than a day ago. Never follows a link; never deletes a
    /// file whose name is not an export name. Returns the number deleted.
    /// </summary>
    public int Prune()
    {
        var deleted = 0;
        try
        {
            if (!System.IO.Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return 0;
            var nowUtc = time.GetUtcNow();
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, time.LocalTimeZone).DateTime);
            var cutoff = today.AddDays(-RetentionDays);
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                var delete = ExportDateOf(file.Name) is { } date
                    ? date < cutoff
                    : TemporaryName().IsMatch(file.Name) && file.LastWriteTimeUtc < nowUtc.UtcDateTime.AddDays(-1);
                if (!delete) continue;
                try { file.Delete(); deleted++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return deleted;
    }

    /// <summary><c>health-yyyyMMdd-HHmmss-&lt;Trigger&gt;.json</c> from the local start time.</summary>
    public static string FileNameFor(HealthCheckTrigger trigger, DateTimeOffset startedUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(startedUtc, zone);
        return "health-" + local.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + trigger + ".json";
    }

    /// <summary>The date in an export file name, or null when the name is not one of this writer's files.</summary>
    public static DateOnly? ExportDateOf(string fileName)
    {
        var match = ExportName().Match(fileName);
        return match.Success && DateOnly.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>Entries that go into the export (see the class summary). Merged entries never do.</summary>
    public static IReadOnlyList<ErrorRegisterEntry> SelectRegister(IEnumerable<ErrorRegisterEntry> entries, DateTimeOffset nowUtc) =>
        entries
            .Where(entry => entry.MergedIntoId is null)
            .Where(entry => entry.Status != ErrorStatus.Fixed
                || entry.StatusChangedUtc >= nowUtc.AddDays(-FixedExportDays)
                || entry.LastSeenUtc >= nowUtc.AddDays(-RegisterChangedDays))
            .OrderBy(entry => entry.ErrorRegisterId)
            .ToArray();

    /// <summary>
    /// Design 4.4: a Fixed entry with no recurrence for 30 days and at least 5 Nightly runs on the fixed version
    /// (or later) since it was fixed.
    /// </summary>
    public static bool IsVerified(ErrorRegisterEntry entry, DateTimeOffset nowUtc, IEnumerable<HealthCheckRunSummary> nightlyRuns)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Status != ErrorStatus.Fixed || entry.FixedInVersion is null) return false;
        var fixedUtc = entry.FixedUtc ?? entry.StatusChangedUtc;
        if (fixedUtc > nowUtc.AddDays(-VerifiedQuietDays)) return false;
        if (entry.LastSeenUtc > fixedUtc && entry.LastSeenUtc > nowUtc.AddDays(-VerifiedQuietDays)) return false;
        if (!TryVersion(entry.FixedInVersion, out var fixedIn)) return false;
        return nightlyRuns.Count(run => run.Trigger == HealthCheckTrigger.Nightly && run.StartedUtc >= fixedUtc
            && TryVersion(run.AppVersion, out var version) && version >= fixedIn) >= VerifiedNightlyRuns;
    }

    /// <summary>
    /// Turns a Pass on ERR-01 into a Warning (<see cref="RedactionFinding"/>) when the export redacted values, and
    /// recounts the run (design 8: a redaction is itself a Warning on ERR-01, because a template or a probe leaked).
    /// A Warning or Fail already on ERR-01 is kept as it is.
    /// </summary>
    public static HealthCheckRun WithRedactionWarning(HealthCheckRun run, int redactions)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (redactions <= 0) return run;
        var results = run.Results.Select(result =>
            result.CheckId == HealthCheckId.Err01 && result.Outcome is HealthOutcome.Pass or HealthOutcome.Known or HealthOutcome.Skipped
                ? result with
                {
                    Outcome = HealthOutcome.Warning,
                    FindingCode = RedactionFinding,
                    Explanation = RedactionWarningText.Replace("{count}", redactions.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
                    MetricValue = redactions
                }
                : result).ToArray();
        var counts = Recount(results);
        return run with { Results = results, Counts = counts, Overall = OverallOf(counts) };
    }

    /// <summary>Line counts of a set of results.</summary>
    public static HealthCheckCounts Recount(IReadOnlyCollection<HealthCheckResult> results) => new(
        results.Count(result => result.Outcome == HealthOutcome.Pass),
        results.Count(result => result.Outcome == HealthOutcome.Warning),
        results.Count(result => result.Outcome == HealthOutcome.Fail),
        results.Count(result => result.Outcome == HealthOutcome.Known),
        results.Count(result => result.Outcome == HealthOutcome.Skipped));

    /// <summary>Fail if any line failed, else Warning if any warned, else Pass.</summary>
    public static HealthOutcome OverallOf(HealthCheckCounts counts) =>
        counts.Fail > 0 ? HealthOutcome.Fail : counts.Warning > 0 ? HealthOutcome.Warning : HealthOutcome.Pass;

    /// <summary>The file's bytes (UTF-8, no BOM). Every string goes through <paramref name="guard"/>.</summary>
    internal byte[] Serialize(HealthExportInput input, HealthExportPrivacyGuard guard)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(guard);
        var run = input.Run;
        var nowUtc = time.GetUtcNow();
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("schema", Schema);

            json.WriteStartObject("run");
            json.WriteString("trigger", run.Trigger.ToString());
            json.WriteString("businessDate", Date(run.BusinessDate));
            json.WriteString("startedUtc", Utc(run.StartedUtc));
            json.WriteString("completedUtc", Utc(run.CompletedUtc));
            WriteNumber(json, "durationMs", guard.Number((long)Math.Max(0, (run.CompletedUtc - run.StartedUtc).TotalMilliseconds)));
            WriteString(json, "appVersion", guard.Token(run.AppVersion));
            WriteString(json, "machine", guard.Token(run.MachineName));
            WriteString(json, "runByRole", guard.Token(run.RunByRole));
            json.WriteString("overall", run.Overall.ToString());
            json.WriteStartObject("counts");
            json.WriteNumber("pass", run.Counts.Pass);
            json.WriteNumber("warning", run.Counts.Warning);
            json.WriteNumber("fail", run.Counts.Fail);
            json.WriteNumber("known", run.Counts.Known);
            json.WriteNumber("skipped", run.Counts.Skipped);
            json.WriteEndObject();
            json.WriteEndObject();

            json.WriteStartArray("results");
            foreach (var result in run.Results) WriteResult(json, result, guard);
            json.WriteEndArray();

            if (input.PreviousRun is { } previous)
            {
                json.WriteStartObject("previousRun");
                json.WriteString("startedUtc", Utc(previous.StartedUtc));
                json.WriteStartArray("changed");
                var before = previous.Results
                    .GroupBy(result => result.CheckId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First().Outcome, StringComparer.Ordinal);
                foreach (var result in run.Results)
                {
                    if (!before.TryGetValue(result.CheckId, out var from) || from == result.Outcome) continue;
                    json.WriteStartObject();
                    WriteString(json, "id", guard.Token(result.CheckId));
                    json.WriteString("from", from.ToString());
                    json.WriteString("to", result.Outcome.ToString());
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            else json.WriteNull("previousRun");

            json.WriteStartArray("register");
            var nightly = input.RecentNightlyRuns ?? [];
            foreach (var entry in SelectRegister(input.Register, nowUtc))
            {
                ErrorRegisterEntryDetail? detail = null;
                input.RegisterDetails?.TryGetValue(entry.ErrorRegisterId, out detail);
                WriteRegisterEntry(json, entry, detail, IsVerified(entry, nowUtc, nightly), guard);
            }
            json.WriteEndArray();

            json.WriteStartObject("warningsByEvent");
            var usedEvents = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in (input.WarningsByEvent ?? new Dictionary<string, int>()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
                json.WriteNumber(guard.Key(pair.Key, usedEvents), pair.Value);
            json.WriteEndObject();

            // Last, so it counts every value above.
            json.WriteStartObject("privacy");
            json.WriteString("guard", HealthExportPrivacyGuard.Name);
            json.WriteNumber("redactions", guard.Redactions);
            json.WriteEndObject();

            json.WriteEndObject();
        }
        return buffer.ToArray();
    }

    private static void WriteResult(Utf8JsonWriter json, HealthCheckResult result, HealthExportPrivacyGuard guard)
    {
        json.WriteStartObject();
        WriteString(json, "id", guard.Token(result.CheckId));
        json.WriteString("area", result.Area.ToString());
        json.WriteString("outcome", result.Outcome.ToString());
        WriteString(json, "finding", guard.Token(result.FindingCode));
        WriteString(json, "explanation", guard.Text(result.Explanation));
        WriteString(json, "fix", guard.TokenOrText(result.FixTaskId));
        WriteNumber(json, "durationMs", guard.Number((long)Math.Max(0, result.Duration.TotalMilliseconds)));
        if (result.MetricValue is not null) WriteNumber(json, "metric", guard.Number(result.MetricValue));
        if (result.UnchangedSince is { } since) json.WriteString("sameSince", Date(since));
        if (result.SubResults is { Count: > 0 } subResults)
        {
            json.WriteStartArray("detail");
            foreach (var sub in subResults)
            {
                json.WriteStartObject();
                var used = new HashSet<string>(StringComparer.Ordinal) { "key", "outcome", "finding", "metric" };
                WriteString(json, "key", guard.Token(sub.Key));
                json.WriteString("outcome", sub.Outcome.ToString());
                if (sub.FindingCode is not null) WriteString(json, "finding", guard.Token(sub.FindingCode));
                if (sub.MetricValue is not null) WriteNumber(json, "metric", guard.Number(sub.MetricValue));
                foreach (var field in (sub.Fields ?? new Dictionary<string, string>()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    WriteString(json, guard.Key(field.Key, used), guard.Token(field.Value));
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }
        json.WriteEndObject();
    }

    private static void WriteRegisterEntry(Utf8JsonWriter json, ErrorRegisterEntry entry, ErrorRegisterEntryDetail? detail, bool verified,
        HealthExportPrivacyGuard guard)
    {
        json.WriteStartObject();
        json.WriteString("id", entry.DisplayId);
        WriteString(json, "fingerprint", guard.FingerprintValue(entry.Fingerprint));
        json.WriteNumber("fingerprintVersion", entry.FingerprintVersion);
        WriteString(json, "event", guard.Token(entry.EventCode));
        WriteString(json, "subCode", guard.Token(EmptyToNull(entry.SubCode)));
        WriteString(json, "source", guard.Token(entry.Source));
        WriteString(json, "site", guard.Token(EmptyToNull(entry.Site)));
        WriteString(json, "exception", guard.Token(entry.ExceptionType));
        WriteNumber(json, "sql", guard.Number((long)entry.SqlNumber));
        json.WriteString("severity", entry.MaxSeverity.ToString());
        json.WriteString("status", entry.Status.ToString());
        json.WriteString("firstSeenUtc", Utc(entry.FirstSeenUtc));
        json.WriteString("lastSeenUtc", Utc(entry.LastSeenUtc));
        WriteNumber(json, "count", guard.Number(entry.OccurrenceCount));
        WriteString(json, "firstVersion", guard.Token(entry.FirstVersion));
        WriteString(json, "lastVersion", guard.Token(entry.LastVersion));

        json.WriteStartObject("versions");
        var usedVersions = new HashSet<string>(StringComparer.Ordinal);
        if (detail is not null)
            foreach (var version in detail.Versions.OrderBy(version => version.AppVersion, StringComparer.Ordinal))
                WriteNumber(json, guard.Key(version.AppVersion, usedVersions), guard.Number(version.OccurrenceCount));
        json.WriteEndObject();

        json.WriteStartArray("sampleRefs");
        if (detail is not null)
            foreach (var reference in detail.LatestOccurrences.Select(occurrence => occurrence.Reference).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxSampleRefs))
                WriteArrayString(json, guard.ReferenceValue(reference));
        json.WriteEndArray();

        json.WriteStartArray("contexts");
        if (detail is not null)
            foreach (var context in detail.LatestOccurrences
                         .Select(occurrence => (occurrence.ReportCode, occurrence.StoreCode, occurrence.BusinessDate))
                         .Distinct().Take(MaxContexts))
            {
                json.WriteStartObject();
                WriteString(json, "report", guard.Token(context.ReportCode));
                WriteString(json, "store", guard.Token(context.StoreCode));
                if (context.BusinessDate is { } date) json.WriteString("date", Date(date)); else json.WriteNull("date");
                json.WriteEndObject();
            }
        json.WriteEndArray();

        json.WriteStartArray("stack");
        foreach (var frame in entry.LatestStack) WriteArrayString(json, guard.Token(frame));
        json.WriteEndArray();

        WriteString(json, "fixedIn", guard.Token(entry.FixedInVersion));
        WriteString(json, "ticket", guard.Token(entry.FixedTicket));
        if (entry.RecurredUtc is { } recurred) json.WriteString("recurredUtc", Utc(recurred)); else json.WriteNull("recurredUtc");
        WriteString(json, "recurredVersion", guard.Token(entry.RecurredVersion));
        json.WriteBoolean("verified", verified);
        json.WriteEndObject();
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static void WriteString(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null) json.WriteNull(name); else json.WriteString(name, value);
    }

    private static void WriteArrayString(Utf8JsonWriter json, string? value)
    {
        if (value is null) json.WriteNullValue(); else json.WriteStringValue(value);
    }

    private static void WriteNumber(Utf8JsonWriter json, string name, long? value)
    {
        if (value is { } number) json.WriteNumber(name, number); else json.WriteNull(name);
    }

    private static void WriteNumber(Utf8JsonWriter json, string name, decimal? value)
    {
        if (value is { } number) json.WriteNumber(name, number); else json.WriteNull(name);
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Utc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static bool TryVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var plus = text.IndexOf('+');
        var core = plus >= 0 ? text[..plus] : text;
        var dash = core.IndexOf('-');
        if (dash >= 0) core = core[..dash];
        if (!Version.TryParse(core.Trim(), out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision));
        return true;
    }

    [GeneratedRegex(@"^health-([0-9]{8})-[0-9]{6}-(?:Manual|Nightly)(?:-[0-9])?\.json$", RegexOptions.CultureInvariant)]
    private static partial Regex ExportName();

    [GeneratedRegex(@"^\.health-[0-9]{8}-[0-9]{6}-(?:Manual|Nightly)-[0-9a-f]{32}\.tmp$", RegexOptions.CultureInvariant)]
    private static partial Regex TemporaryName();
}
