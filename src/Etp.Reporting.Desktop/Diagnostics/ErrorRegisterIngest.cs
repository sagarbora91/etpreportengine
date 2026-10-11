extern alias EtpApplication;

using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;
using EtpApplication::Etp.Reporting.Application.ErrorRegister;
using EtpApplication::Etp.Reporting.Application.HealthCheck;

namespace Etp.Reporting.Desktop.Diagnostics;

/// <summary>What one ingest run did. <see cref="Failed"/>: the store threw (an ERROR_REGISTER_INGEST_FAILED entry was logged, the cursor stayed). <see cref="AlreadyRunning"/>: another run for the same folder was in progress.</summary>
public sealed record ErrorRegisterIngestRun(
    int Read,
    int Sent,
    int SkippedInvalid,
    int UnreadableLines,
    int Batches,
    int Accepted,
    int Duplicates,
    int Rejected,
    int NewEntries,
    int RecurredEntries,
    bool Failed,
    bool AlreadyRunning);

/// <summary>
/// Sends this account's Error and Critical diagnostics entries to the error register (design 4.3): reads after the
/// cursor, fingerprints each entry (rule v1, computed here only), attaches pre-1.9.9 entries to the one existing
/// entry they can only belong to (soft match), sends at most 500 per call oldest first, and moves the cursor after
/// each accepted call. A failure never reaches the user as a dialog: it writes one ERROR_REGISTER_INGEST_FAILED
/// entry (which a later run ingests) and returns <see cref="ErrorRegisterIngestRun.Failed"/>.
/// </summary>
public sealed class ErrorRegisterIngest
{
    public const int BatchSize = 500;
    public const int MaxBatchesPerRun = 20;
    public const int MaxStackFrames = 16;
    public const int MaxStackJsonLength = 3000;
    public const string FailureEventId = "ERROR_REGISTER_INGEST_FAILED";
    public const string FailureSource = "Diagnostics.ErrorRegister";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    private readonly IErrorRegisterStore _store;
    private readonly string _appVersion;
    private readonly DiagnosticsLogReader _reader;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ErrorRegisterEntry>>>? _loadKnownEntries;

    /// <param name="loadKnownEntries">
    /// Optional: the entries already in the register, for the soft match of old (stackless) entries. Only the Owner and
    /// the automation account can read the register; if the call throws, nothing is soft-matched and each such entry
    /// keeps its own legacy fingerprint. Not called when no entry needs it.
    /// </param>
    public ErrorRegisterIngest(
        IErrorRegisterStore store,
        string appVersion,
        string? logDirectory = null,
        Func<CancellationToken, Task<IReadOnlyList<ErrorRegisterEntry>>>? loadKnownEntries = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _appVersion = appVersion ?? throw new ArgumentNullException(nameof(appVersion));
        _reader = new DiagnosticsLogReader(logDirectory);
        _loadKnownEntries = loadKnownEntries;
    }

    public DiagnosticsLogReader Reader => _reader;

    public async Task<ErrorRegisterIngestRun> RunAsync(CancellationToken cancellationToken = default)
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(_reader.LogDirectory), _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return new ErrorRegisterIngestRun(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, true);
        try
        {
            return await RunCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ErrorRegisterIngestRun> RunCoreAsync(CancellationToken cancellationToken)
    {
        if (!IsSafeVersion(_appVersion))
            return new ErrorRegisterIngestRun(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, false, false);

        var cursor = _reader.ReadCursor();
        var read = _reader.ReadErrorsAfter(cursor);
        int sent = 0, skipped = 0, batches = 0, accepted = 0, duplicates = 0, rejected = 0, newEntries = 0, recurred = 0;
        IReadOnlyList<ErrorRegisterEntry>? known = null;
        var knownLoaded = false;
        var failed = false;

        var window = read.Entries.Take(BatchSize * MaxBatchesPerRun).ToArray();
        foreach (var chunk in window.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = new List<ErrorOccurrence>(chunk.Length);
            foreach (var entry in chunk)
            {
                var occurrence = ToOccurrence(entry);
                if (occurrence is null) { skipped++; continue; }
                if (entry.Stack is null)
                {
                    if (!knownLoaded)
                    {
                        known = await LoadKnownAsync(cancellationToken).ConfigureAwait(false);
                        knownLoaded = true;
                    }
                    occurrence = SoftMatched(occurrence, known);
                }
                items.Add(occurrence);
            }

            try
            {
                if (items.Count > 0)
                {
                    var result = await _store.IngestAsync(items, _appVersion, cancellationToken).ConfigureAwait(false);
                    batches++;
                    sent += items.Count;
                    accepted += result.Accepted;
                    duplicates += result.Duplicates;
                    rejected += result.Rejected;
                    newEntries += result.NewEntries;
                    recurred += result.RecurredEntries;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed = true;
                DesktopDiagnostics.Record(exception, FailureSource, FailureEventId, logDirectory: _reader.LogDirectory,
                    operation: "Error register ingest");
                break;
            }

            var last = chunk[^1];
            _reader.WriteCursor(new RegisterCursor(last.TimestampUtc, CursorReferenceOf(last)));
        }

        return new ErrorRegisterIngestRun(read.Entries.Count, sent, skipped, read.UnreadableLines, batches, accepted, duplicates, rejected, newEntries, recurred, failed, false);
    }

    private async Task<IReadOnlyList<ErrorRegisterEntry>> LoadKnownAsync(CancellationToken cancellationToken)
    {
        if (_loadKnownEntries is null) return [];
        try
        {
            return await _loadKnownEntries(cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return []; // not allowed to read the register, or it is unavailable: legacy fingerprints stand
        }
    }

    private static ErrorOccurrence SoftMatched(ErrorOccurrence occurrence, IReadOnlyList<ErrorRegisterEntry>? known)
    {
        if (known is null || known.Count == 0) return occurrence;
        var match = ErrorFingerprint.SoftMatch(occurrence.Parts, known);
        if (match is null) return occurrence;
        return occurrence with
        {
            Fingerprint = match.Fingerprint,
            FingerprintVersion = match.FingerprintVersion,
            Parts = occurrence.Parts with { Site = match.Site ?? string.Empty }
        };
    }

    internal static string CursorReferenceOf(DesktopDiagnosticEntry entry) =>
        DiagnosticsLogReader.ReferenceOf(entry) ?? entry.CorrelationId ?? string.Empty;

    /// <summary>
    /// One log entry as a register item, or null when it cannot be registered: Information and Warning (Q7), a Ref
    /// shorter than 12 characters, or any part that fails the safe-token allow-list (the database checks again).
    /// </summary>
    internal static ErrorOccurrence? ToOccurrence(DesktopDiagnosticEntry entry)
    {
        var severity = entry.Severity switch
        {
            DesktopDiagnosticSeverity.Error => ErrorRegisterSeverity.Error,
            DesktopDiagnosticSeverity.Critical => ErrorRegisterSeverity.Critical,
            _ => (ErrorRegisterSeverity?)null
        };
        if (severity is null) return null;

        var reference = DiagnosticsLogReader.ReferenceOf(entry);
        if (reference is null || !IsSafeToken(reference)) return null;
        if (!IsSafeVersion(entry.ApplicationVersion)) return null;

        var parts = ErrorFingerprint.PartsOf(entry);
        if (!IsEventCode(parts.EventBase) || !IsSafeToken(parts.SubCode, allowEmpty: true) ||
            !IsSafeToken(parts.Source) || !IsSafeToken(parts.Site, allowEmpty: true) || !IsSafeToken(parts.ExceptionType))
            return null;
        if (!IsOptionalToken(entry.ReportCode) || !IsOptionalToken(entry.StoreCode)) return null;

        var businessDate = ParseDate(entry.BusinessDate, out var businessDateOk);
        var dateFrom = ParseDate(entry.DateFrom, out var dateFromOk);
        if (!businessDateOk || !dateFromOk) return null;

        return new ErrorOccurrence(
            ErrorFingerprint.Compute(parts),
            ErrorFingerprint.Version,
            parts,
            entry.TimestampUtc.ToUniversalTime(),
            severity.Value,
            reference,
            entry.ApplicationVersion,
            entry.ReportCode,
            entry.StoreCode,
            businessDate,
            dateFrom,
            entry.Operation,
            StackOf(entry.Stack));
    }

    /// <summary>The Etp frames only, normalised to <c>Type.Method</c>, repeated neighbours collapsed, at most 16 frames and 3000 characters of JSON.</summary>
    internal static IReadOnlyList<string> StackOf(IReadOnlyList<string>? stack)
    {
        if (stack is null) return [];
        var frames = new List<string>();
        foreach (var frame in stack)
        {
            var site = ErrorFingerprint.NormaliseFrame(frame);
            if (site is null || (frames.Count > 0 && frames[^1] == site)) continue;
            frames.Add(site);
            if (frames.Count == MaxStackFrames) break;
        }
        while (frames.Count > 1 && JsonSerializer.Serialize(frames).Length > MaxStackJsonLength) frames.RemoveAt(frames.Count - 1);
        return frames;
    }

    private static DateOnly? ParseDate(string? text, out bool valid)
    {
        valid = true;
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        valid = false;
        return null;
    }

    private static bool IsOptionalToken(string? value) => value is null || IsSafeToken(value);

    private static bool IsEventCode(string value) =>
        value.Length is > 0 and <= 128 && value.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character) || character == '_');

    // The same allow-list as DesktopDiagnostics.SafeToken and the procedures: [A-Za-z0-9._:<>+-].
    internal static bool IsSafeToken(string? value, bool allowEmpty = false) =>
        value is not null && (value.Length == 0 ? allowEmpty : value.Length <= 256 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '<' or '>' or '+' or '-'));

    internal static bool IsSafeVersion(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 20 && Version.TryParse(value, out _) && value.All(character => char.IsAsciiDigit(character) || character == '.');
}
