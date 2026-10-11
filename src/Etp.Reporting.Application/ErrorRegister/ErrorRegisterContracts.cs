namespace Etp.Reporting.Application.ErrorRegister;

/// <summary>
/// Lifecycle of an error register entry (design 1.11.0, section 4.4).
/// "Verified" is not a status: it is an export flag on a Fixed entry.
/// Stored as the enum name (<c>diag.error_register.status</c>).
/// </summary>
public enum ErrorStatus
{
    /// <summary>First occurrence of this fingerprint. Set by ingest.</summary>
    New,
    /// <summary>The Owner has looked at it (or a ticket id was linked); not fixed yet.</summary>
    Seen,
    /// <summary>By design or accepted. Owner only, needs a reason. Counted, never ticketed.</summary>
    Expected,
    /// <summary>A release that fixes it is installed (<see cref="ErrorRegisterEntry.FixedInVersion"/>).</summary>
    Fixed,
    /// <summary>Seen again from a version at or above the fixed version.</summary>
    Recurred
}

/// <summary>Severity of a registered entry. Warnings are never registered (Q7).</summary>
public enum ErrorRegisterSeverity
{
    Error,
    Critical
}

/// <summary>
/// The six identity parts of fingerprint rule v1 (design 4.2):
/// <c>SHA-256("v1|" + EventBase + "|" + SubCode + "|" + Source + "|" + Site + "|" + ExceptionType + "|" + SqlNumber)</c>.
/// Empty strings, never null, for a missing SubCode or Site. All parts are safe tokens.
/// </summary>
public sealed record ErrorFingerprintParts(
    string EventBase,
    string SubCode,
    string Source,
    string Site,
    string ExceptionType,
    int SqlNumber);

/// <summary>
/// One Error or Critical diagnostics entry read from this account's <c>diagnostics-*.jsonl</c>, ready to send to
/// <c>diag.ingest_error_occurrences</c> (design 4.3). <see cref="Fingerprint"/> is 64 lowercase hex characters,
/// computed in C# only. <see cref="Reference"/> is the 12-character Ref (a legacy 32-hex id is cut to its first 12).
/// Context fields (report, store, dates, operation) are per occurrence, never identity.
/// <see cref="Stack"/> holds normalised <c>Type.Method</c> frames only (no file names, no line numbers).
/// </summary>
public sealed record ErrorOccurrence(
    string Fingerprint,
    int FingerprintVersion,
    ErrorFingerprintParts Parts,
    DateTimeOffset TimestampUtc,
    ErrorRegisterSeverity Severity,
    string Reference,
    string ApplicationVersion,
    string? ReportCode,
    string? StoreCode,
    DateOnly? BusinessDate,
    DateOnly? DateFrom,
    string? Operation,
    IReadOnlyList<string> Stack);

/// <summary>
/// One row of <c>diag.error_register</c>: one distinct bug. <see cref="DisplayId"/> is <c>ER-0001</c> style
/// (from <see cref="ErrorRegisterId"/>). <see cref="EventCode"/> is the EventBase.
/// </summary>
public sealed record ErrorRegisterEntry(
    int ErrorRegisterId,
    string Fingerprint,
    int FingerprintVersion,
    string EventCode,
    string? SubCode,
    string Source,
    string? Site,
    string ExceptionType,
    int SqlNumber,
    ErrorRegisterSeverity MaxSeverity,
    ErrorStatus Status,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    long OccurrenceCount,
    string FirstVersion,
    string LastVersion,
    string? FixedInVersion,
    string? FixedTicket,
    DateTimeOffset? FixedUtc,
    DateTimeOffset? RecurredUtc,
    string? RecurredVersion,
    IReadOnlyList<string> LatestStack,
    string? StatusReason,
    DateTimeOffset StatusChangedUtc,
    string StatusChangedBy,
    int? MergedIntoId)
{
    /// <summary>The id the Owner and the audit use, for example <c>ER-0006</c>.</summary>
    public string DisplayId => $"ER-{ErrorRegisterId:0000}";
}

/// <summary>One row of <c>diag.error_register_versions</c>: how often an entry was seen in one app version.</summary>
public sealed record ErrorRegisterVersionCount(
    string AppVersion,
    long OccurrenceCount,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);

/// <summary>One stored occurrence (<c>diag.error_occurrences</c>). <see cref="ReporterRole"/> is Owner, StoreManager, Viewer, Automation or Unknown.</summary>
public sealed record ErrorOccurrenceRecord(
    long ErrorOccurrenceId,
    DateTimeOffset OccurredUtc,
    string Reference,
    string AppVersion,
    ErrorRegisterSeverity Severity,
    string? ReportCode,
    string? StoreCode,
    DateOnly? BusinessDate,
    DateOnly? DateFrom,
    string? Operation,
    string ReporterRole,
    DateTimeOffset IngestedUtc);

/// <summary>The entry screen's data (<c>diag.load_error_entry</c>): the entry, its versions, the latest 20 occurrences.</summary>
public sealed record ErrorRegisterEntryDetail(
    ErrorRegisterEntry Entry,
    IReadOnlyList<ErrorRegisterVersionCount> Versions,
    IReadOnlyList<ErrorOccurrenceRecord> LatestOccurrences);

/// <summary>Counts returned by <c>diag.ingest_error_occurrences</c>.</summary>
public sealed record ErrorIngestResult(
    int Accepted,
    int Duplicates,
    int Rejected,
    int NewEntries,
    int RecurredEntries);

/// <summary>Counts returned by <c>diag.apply_error_fix_manifest</c>. <see cref="Skipped"/> covers lines for a newer version than the one running.</summary>
public sealed record ErrorFixManifestResult(
    int Lines,
    int EntriesMarkedFixed,
    int AlreadyFixed,
    int NotFound,
    int Skipped);
