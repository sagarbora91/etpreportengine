using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Etp.Reporting.Application.Updates;

public enum UpdateAttemptEvent
{
    /// <summary>Written just before setup is started: the backup is taken and the installer is staged and re-checked.</summary>
    Started,
    /// <summary>Written by ETP at the next start, once it has worked out what happened.</summary>
    Outcome,
}

public enum UpdateAttemptOutcome
{
    Succeeded,
    /// <summary>Setup ran and failed (or left SETUP-INCOMPLETE.txt). Data is safe; the backup is named; nothing is restored.</summary>
    Failed,
    /// <summary>Setup never ran: the UAC prompt was declined, or the PC lost power before setup started.</summary>
    NotStarted,
}

/// <summary>
/// 1.12 (B1). One line of <c>%ProgramData%\EtpReporting\Updates\attempts.jsonl</c>. Holds versions, times, hashes and the
/// backup's paths only: no user names, no database content. The backup path is what the failure screen shows
/// ("Backup taken at hh:mm (file)").
/// </summary>
public sealed record UpdateAttemptRecord(
    string AttemptId,
    UpdateAttemptEvent Event,
    string FromVersion,
    string ToVersion,
    string InstallerSha256,
    string? BackupPath,
    DateTimeOffset? BackupVerifiedUtc,
    DateTimeOffset StartedUtc,
    UpdateAttemptOutcome? Outcome,
    string? SetupLogPath,
    DateTimeOffset RecordedUtc)
{
    public static string NewAttemptId() => Guid.NewGuid().ToString("N");

    public static bool IsAttemptIdText(string? text) =>
        text is { Length: 32 } && text.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>Reads and writes attempts.jsonl lines. A line that does not parse is skipped, never fatal (a torn last line after power loss).</summary>
public static class UpdateAttemptJournal
{
    public const string FileName = "attempts.jsonl";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false) },
        WriteIndented = false,
        MaxDepth = 4,
    };

    public static string Serialize(UpdateAttemptRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!UpdateAttemptRecord.IsAttemptIdText(record.AttemptId)) throw new ArgumentException("The attempt id is 32 lower-case hex characters.", nameof(record));
        return JsonSerializer.Serialize(record, Options);
    }

    public static IReadOnlyList<UpdateAttemptRecord> Parse(IEnumerable<string> lines)
    {
        var records = new List<UpdateAttemptRecord>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length > 8 * 1024) continue;
            try
            {
                var record = JsonSerializer.Deserialize<UpdateAttemptRecord>(line, Options);
                if (record is not null && UpdateAttemptRecord.IsAttemptIdText(record.AttemptId)) records.Add(record);
            }
            catch (JsonException)
            {
                // A torn or hand-edited line: skip it.
            }
        }
        return records;
    }

    /// <summary>The newest Started record that has no Outcome record yet: what the next start must resolve and show.</summary>
    public static UpdateAttemptRecord? PendingAttempt(IReadOnlyList<UpdateAttemptRecord> records)
    {
        var resolved = records.Where(r => r.Event == UpdateAttemptEvent.Outcome).Select(r => r.AttemptId).ToHashSet(StringComparer.Ordinal);
        return records.Where(r => r.Event == UpdateAttemptEvent.Started && !resolved.Contains(r.AttemptId))
            .OrderByDescending(r => r.StartedUtc).FirstOrDefault();
    }
}

/// <summary>
/// What setup wrote for an update run (<c>Updates\setup-result.json</c>, from installer\EtpReportingEngine.iss when started
/// with <c>/ETPUPDATE=1 /ETPATTEMPT=&lt;id&gt;</c>). Outcome is SUCCEEDED, FAILED or CANCELLED; reason is a fixed code.
/// </summary>
public sealed record UpdateSetupResult(string AttemptId, string Version, string Outcome, string Reason)
{
    public const string FileName = "setup-result.json";

    public static UpdateSetupResult? TryParse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length == 0 || utf8.Length > 4096) return null;
        if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF) utf8 = utf8[3..];
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 2 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var attempt = Text("attemptId"); var version = Text("version"); var outcome = Text("outcome"); var reason = Text("reason") ?? "";
            if (!UpdateAttemptRecord.IsAttemptIdText(attempt) || version is null || outcome is not ("SUCCEEDED" or "FAILED" or "CANCELLED")) return null;
            return new(attempt!, version, outcome, reason);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// 1.12 (B1, design section 4 rule 8). Works out at the next start what happened to the pending attempt. Never restores and
/// never retries: it only decides which screen the Owner sees ("Update failed. Setup stopped before it finished ... The backup taken
/// before the update is ...").
/// </summary>
public static class UpdateOutcomeResolver
{
    public static UpdateAttemptOutcome Resolve(UpdateAttemptRecord pending, ReleaseVersion running, UpdateSetupResult? setupResult, bool setupIncompleteMarkerPresent)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var reachedTarget = ReleaseVersion.TryParse(pending.ToVersion, out var target) && running == target;
        if (setupResult is not null && string.Equals(setupResult.AttemptId, pending.AttemptId, StringComparison.Ordinal))
        {
            return setupResult.Outcome switch
            {
                "SUCCEEDED" when reachedTarget && !setupIncompleteMarkerPresent => UpdateAttemptOutcome.Succeeded,
                // Cancelled before files were copied: the old build is still installed and nothing changed.
                "CANCELLED" when !reachedTarget => UpdateAttemptOutcome.NotStarted,
                _ => UpdateAttemptOutcome.Failed,
            };
        }
        // No result for this attempt. A marker means setup's post-install step failed (it writes the marker before it could
        // write anything else); the new build running means setup finished but the result file was not written.
        if (setupIncompleteMarkerPresent) return UpdateAttemptOutcome.Failed;
        return reachedTarget ? UpdateAttemptOutcome.Succeeded : UpdateAttemptOutcome.NotStarted;
    }

    public static UpdateAttemptRecord OutcomeRecord(UpdateAttemptRecord pending, UpdateAttemptOutcome outcome, DateTimeOffset nowUtc) =>
        pending with { Event = UpdateAttemptEvent.Outcome, Outcome = outcome, RecordedUtc = nowUtc };

    /// <summary>
    /// The failure screen's words (plan section B, UI). The time is the backup's, in the PC's local time. Review M-3: setup
    /// applies each database change on its own, so a setup that stopped half-way may have applied some of them; the text says
    /// so, names the backup taken before the update and asks for help before ETP is used. It never restores (decision 39).
    /// </summary>
    public static string FailureText(UpdateAttemptRecord pending, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(pending);
        if (pending.BackupVerifiedUtc is not { } verified || string.IsNullOrEmpty(pending.BackupPath))
            return FailedSentence + " ETP has no record of a backup for this update. " + GetHelpSentence;
        var local = TimeZoneInfo.ConvertTime(verified, zone);
        return string.Create(CultureInfo.InvariantCulture,
            $"{FailedSentence} The backup taken before the update is {Path.GetFileName(pending.BackupPath)} (taken at {local:HH:mm}). {GetHelpSentence}");
    }

    /// <summary>Review M-3: what is known after a failed setup (some database changes may already be in place).</summary>
    public const string FailedSentence = "Update failed. Setup stopped before it finished, so some database changes may already be in place.";

    /// <summary>Review M-3: the Owner gets help before using ETP again; Sagar decides whether the backup is needed.</summary>
    public const string GetHelpSentence = "Call Sagar before using ETP.";
}
