extern alias EtpApplication;

using System.IO;
using System.Text;
using EtpApplication::Etp.Reporting.Application.Updates;

namespace Etp.Reporting.Desktop.Updates;

/// <summary>
/// 1.12 (B1). <c>Updates\attempts.jsonl</c>: one line per event, appended and flushed to disk before setup starts, so the
/// next start can say what happened even after a power cut. Also reads <c>Updates\setup-result.json</c>, which setup writes.
/// </summary>
internal sealed class UpdateAttemptStore(string updatesRoot)
{
    private const long MaximumReadBytes = 1024 * 1024;

    public string AttemptsPath => UpdatePaths.AttemptsFile(updatesRoot);

    public void Append(UpdateAttemptRecord record)
    {
        var line = UpdateAttemptJournal.Serialize(record) + "\n";
        var bytes = new UTF8Encoding(false).GetBytes(line);
        using var stream = new FileStream(AttemptsPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    public IReadOnlyList<UpdateAttemptRecord> ReadAll()
    {
        try
        {
            if (!File.Exists(AttemptsPath)) return [];
            using var stream = new FileStream(AttemptsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // Only the tail matters; a journal that grew past 1 MB is read from its last megabyte (the first line may be cut and is skipped).
            if (stream.Length > MaximumReadBytes) stream.Seek(-MaximumReadBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream, new UTF8Encoding(false));
            var lines = new List<string>();
            while (reader.ReadLine() is { } line) lines.Add(line);
            return UpdateAttemptJournal.Parse(lines);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public UpdateSetupResult? ReadSetupResult()
    {
        var path = UpdatePaths.SetupResultFile(updatesRoot);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 4096) return null;
            return UpdateSetupResult.TryParse(File.ReadAllBytes(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// At start-up (wave 2 calls this once, Owner or not, before the shell opens): resolve the pending attempt, record its
    /// outcome and return it for the result screen. Null when nothing is pending. Never restores and never retries.
    /// </summary>
    public (UpdateAttemptRecord Attempt, UpdateAttemptOutcome Outcome)? ResolvePending(ReleaseVersion running, bool setupIncompleteMarkerPresent, DateTimeOffset nowUtc)
    {
        var pending = UpdateAttemptJournal.PendingAttempt(ReadAll());
        if (pending is null) return null;
        var outcome = UpdateOutcomeResolver.Resolve(pending, running, ReadSetupResult(), setupIncompleteMarkerPresent);
        try { Append(UpdateOutcomeResolver.OutcomeRecord(pending, outcome, nowUtc)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DesktopDiagnostics.Record(exception, "Updates.Journal", "UPDATE_OUTCOME_NOT_RECORDED");
        }
        return (pending, outcome);
    }

    /// <summary>The installed program folder's SETUP-INCOMPLETE.txt (installer\EtpReportingEngine.iss writes it on failure).</summary>
    public static bool SetupIncompleteMarkerPresent() =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "SETUP-INCOMPLETE.txt"));
}
