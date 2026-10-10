using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Etp.Reporting.Desktop;

public enum DesktopDiagnosticSeverity
{
    Information,
    Warning,
    Error,
    Critical
}

public sealed record DesktopDiagnosticEntry(
    DateTimeOffset TimestampUtc,
    DesktopDiagnosticSeverity Severity,
    string EventId,
    string Source,
    string CorrelationId,
    string ExceptionType,
    int HResult,
    string ApplicationVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<DesktopDiagnosticSqlError>? SqlErrors = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Operation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ReportCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? StoreCode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BusinessDate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DateFrom = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ParameterName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? InnerExceptionTypes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? Stack = null);

/// <summary>
/// IE-CODE-03 (1.9.9): where a failure happened, in terms that are not personal data: the report code, one store
/// code (or ALL / MULTIPLE), the business date (the report's To date) and the From date of a range.
/// </summary>
public sealed record DesktopDiagnosticContext(
    string? ReportCode = null,
    string? StoreCode = null,
    DateOnly? BusinessDate = null,
    DateOnly? DateFrom = null)
{
    public const string AllStores = "ALL";
    public const string MultipleStores = "MULTIPLE";

    /// <summary>One store code as is; none is ALL, more than one is MULTIPLE (the list itself is not logged).</summary>
    public static string StoreOf(IReadOnlyCollection<string>? stores) =>
        stores is null || stores.Count == 0 ? AllStores : stores.Count == 1 ? stores.First() : MultipleStores;
}

/// <summary>
/// The parts of a SQL Server error that say which statement failed and why, and nothing
/// else. The message text is left out on purpose: SQL Server puts names into it (a Windows
/// account, a value), and this log carries no identities or business values. The number is
/// enough to look the message up; the procedure and line say where it was raised.
/// </summary>
public sealed record DesktopDiagnosticSqlError(int Number, byte State, byte Class, string Procedure, int LineNumber);

public static class DesktopDiagnostics
{
    internal const long MaxLogFileBytes = 5L * 1024 * 1024;
    internal const int MaxRetainedFiles = 24;
    internal const int RetentionDays = 180;

    // Test runs set this so neither they nor the application they launch write into the
    // Owner's log. The shipped application never sets it.
    internal const string DirectoryVariable = "ETP_DIAGNOSTICS_DIRECTORY";
    private static readonly object WriteLock = new();
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Writes one entry and returns its reference (the CorrelationId field), which callers show to the user as
    /// "Ref: ..." so a screenshot or a phone call can be matched to the entry (IE-CODE-03).
    /// The exception's message is never written: .NET, SqlClient and product messages can quote names, accounts,
    /// invoice numbers and paths. Written instead: the caller's fixed operation text, the exception types, the type
    /// and method names of the failing frames, the SQL error numbers and the context tokens.
    /// </summary>
    public static string Record(
        Exception? exception,
        string source,
        string eventId,
        DesktopDiagnosticSeverity severity = DesktopDiagnosticSeverity.Error,
        string? correlationId = null,
        string? logDirectory = null,
        DateTimeOffset? timestampUtc = null,
        DesktopDiagnosticContext? context = null,
        string? operation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        var reference = RequiredCorrelationId(correlationId);

        try
        {
            var timestamp = timestampUtc ?? DateTimeOffset.UtcNow;
            var directory = logDirectory ?? DefaultDirectory(Environment.GetEnvironmentVariable(DirectoryVariable));
            Directory.CreateDirectory(directory);
            var entry = new DesktopDiagnosticEntry(
                timestamp,
                severity,
                SafeToken(eventId, "UNCLASSIFIED"),
                SafeToken(source, "Unknown"),
                reference,
                exception?.GetType().FullName ?? "Unknown",
                exception?.HResult ?? 0,
                Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                SqlErrorsOf(exception),
                SafeOperation(operation),
                OptionalToken(context?.ReportCode),
                OptionalToken(context?.StoreCode),
                context?.BusinessDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                context?.DateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                exception is ArgumentException { ParamName: { } parameter } ? OptionalToken(parameter) : null,
                InnerTypesOf(exception),
                StackOf(exception));
            var line = JsonSerializer.Serialize(entry, SerializerOptions) + Environment.NewLine;

            lock (WriteLock)
            {
                var path = SelectLogPath(directory, timestamp, Encoding.UTF8.GetByteCount(line));
                if (path is not null) File.AppendAllText(path, line, new UTF8Encoding(false));
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (SecurityException) { }
        return reference;
    }

    /// <summary>The user-facing failure text with the entry's reference appended: "... Ref: 1A2B3C4D5E6F".</summary>
    public static string WithReference(string message, string reference) =>
        $"{message.TrimEnd()} Ref: {reference}";

    /// <summary>
    /// IE-CODE-11 (1.9.9): observes a fire-and-forget task. A failure (other than a cancellation) is recorded and
    /// shown as "{operation}: {friendly reason} Ref: ..." through <paramref name="showFailure"/>, instead of the task
    /// faulting unobserved with nothing on screen. The returned task does not fault for the observed task's failure.
    /// </summary>
    public static async Task ObserveAsync(
        Task task,
        string source,
        string eventId,
        string operation,
        Action<string> showFailure,
        DesktopDiagnosticContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(showFailure);
        try { await task; }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            var reference = Record(exception, source, eventId, context: context, operation: operation);
            showFailure(WithReference($"{operation}: {DesktopFriendlyError.Describe(exception)}", reference));
        }
    }

    internal const int MaxStackFrames = 16;
    internal const int MaxInnerExceptionTypes = 4;

    // The type and method name of each frame of the innermost exception that has a stack (where a wrapped fault was
    // raised). No file paths, line numbers, argument values or messages.
    internal static IReadOnlyList<string>? StackOf(Exception? exception)
    {
        Exception? withStack = null;
        for (var depth = 0; exception is not null && depth < 8; depth++, exception = exception.InnerException)
            if (exception.StackTrace is not null) withStack = exception;
        if (withStack is null) return null;
        var frames = new StackTrace(withStack, false).GetFrames()
            .Select(frame => frame.GetMethod())
            .Where(method => method is not null)
            .Select(method => SafeFrame($"{method!.DeclaringType?.FullName ?? "?"}.{method.Name}"))
            .Take(MaxStackFrames)
            .ToArray();
        return frames.Length == 0 ? null : frames;
    }

    internal static IReadOnlyList<string>? InnerTypesOf(Exception? exception)
    {
        var types = new List<string>();
        for (var inner = exception?.InnerException; inner is not null && types.Count < MaxInnerExceptionTypes; inner = inner.InnerException)
            types.Add(inner.GetType().FullName ?? "Unknown");
        return types.Count == 0 ? null : types;
    }

    private static string SafeFrame(string frame)
    {
        var safe = new string(frame.Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '<' or '>' or '`' or '+' or '-' or '[' or ']' or ',').ToArray());
        return safe.Length <= 200 ? safe : safe[..200];
    }

    // Operation text is the caller's fixed product wording ("Stock report failed"). Text that could carry data (a
    // digit, an at sign, a path or quote character) or is long is left out rather than trimmed.
    internal static string? SafeOperation(string? operation)
    {
        if (string.IsNullOrWhiteSpace(operation)) return null;
        var text = operation.Trim();
        return text.Length <= 120 && text.All(character => char.IsAsciiLetter(character) || character is ' ' or '-' or '/' or '(' or ')' or ',')
            ? text
            : null;
    }

    private static string? OptionalToken(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : SafeToken(value, "redacted");

    // Anything but a full path - unset, blank, relative - keeps the Owner's folder, so a
    // stray value cannot scatter diagnostics relative to the working directory.
    internal static string DefaultDirectory(string? configured) =>
        !string.IsNullOrWhiteSpace(configured) && Path.IsPathFullyQualified(configured)
            ? configured
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EtpReporting",
                "Logs");

    internal const int MaxSqlErrors = 8;

    // Finding B, 2 Oct 2026: USER_ACCESS_SAVE_FAILED recorded only "SqlException" and an
    // HResult, so two different refusals looked the same in the log. Any SqlException in the
    // chain (a repository may wrap one) now adds its error numbers, states, classes,
    // procedure names and line numbers.
    internal static IReadOnlyList<DesktopDiagnosticSqlError>? SqlErrorsOf(Exception? exception)
    {
        for (var depth = 0; exception is not null && depth < 8; depth++, exception = exception.InnerException)
        {
            if (exception is not Microsoft.Data.SqlClient.SqlException sql) continue;
            var errors = sql.Errors.Cast<Microsoft.Data.SqlClient.SqlError>()
                .Take(MaxSqlErrors)
                .Select(error => new DesktopDiagnosticSqlError(
                    error.Number,
                    error.State,
                    error.Class,
                    string.IsNullOrEmpty(error.Procedure) ? string.Empty : SafeToken(error.Procedure, "redacted"),
                    error.LineNumber))
                .ToArray();
            return errors.Length == 0 ? null : errors;
        }
        return null;
    }

    private static string RequiredCorrelationId(string? correlationId)
    {
        if (!string.IsNullOrWhiteSpace(correlationId)) return SafeToken(correlationId, "redacted");
        var traceId = Activity.Current?.TraceId.ToString();
        // Twelve hex characters: short enough to read out over the phone, ample to find one entry in the log.
        return SafeToken(string.IsNullOrWhiteSpace(traceId) ? Guid.NewGuid().ToString("N")[..12].ToUpperInvariant() : traceId, "redacted");
    }

    private static string? SelectLogPath(string directory, DateTimeOffset timestamp, int entryBytes)
    {
        if (entryBytes > MaxLogFileBytes) return null;

        var files = Directory.GetFiles(directory, "diagnostics-*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderBy(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .ToList();
        var cutoff = timestamp.UtcDateTime.AddDays(-RetentionDays);
        foreach (var expired in files.Where(file => file.LastWriteTimeUtc < cutoff).ToArray())
            if (!TryDelete(expired, files)) return null;
        if (!TrimToFileCount(files, MaxRetainedFiles)) return null;

        var monthPrefix = $"diagnostics-{timestamp:yyyyMM}";
        var current = files
            .Where(file => file.Name.StartsWith(monthPrefix, StringComparison.Ordinal) &&
                           file.Length + entryBytes <= MaxLogFileBytes)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (current is not null) return current.FullName;

        if (!TrimToFileCount(files, MaxRetainedFiles - 1)) return null;
        var basePath = Path.Combine(directory, $"{monthPrefix}.jsonl");
        return !File.Exists(basePath)
            ? basePath
            : Path.Combine(directory, $"{monthPrefix}-{timestamp:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.jsonl");
    }

    private static bool TrimToFileCount(List<FileInfo> files, int maximumCount)
    {
        while (files.Count > maximumCount)
        {
            var oldest = files[0];
            if (!TryDelete(oldest, files)) return false;
        }
        return true;
    }

    private static bool TryDelete(FileInfo file, List<FileInfo> files)
    {
        try
        {
            file.Delete();
            files.Remove(file);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (SecurityException) { return false; }
    }

    private static string SafeToken(string value, string fallback)
    {
        var token = value.Trim();
        return token.Length is > 0 and <= 128 && token.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or ':' or '_' or '-')
            ? token
            : fallback;
    }
}
