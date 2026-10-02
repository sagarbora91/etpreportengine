using System.Diagnostics;
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
    IReadOnlyList<DesktopDiagnosticSqlError>? SqlErrors = null);

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

    public static void Record(
        Exception? exception,
        string source,
        string eventId,
        DesktopDiagnosticSeverity severity = DesktopDiagnosticSeverity.Error,
        string? correlationId = null,
        string? logDirectory = null,
        DateTimeOffset? timestampUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);

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
                RequiredCorrelationId(correlationId),
                exception?.GetType().FullName ?? "Unknown",
                exception?.HResult ?? 0,
                Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                SqlErrorsOf(exception));
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
    }

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
        return SafeToken(string.IsNullOrWhiteSpace(traceId) ? Guid.NewGuid().ToString("N") : traceId, "redacted");
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
