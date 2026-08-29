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
    string ApplicationVersion);

public static class DesktopDiagnostics
{
    internal const long MaxLogFileBytes = 5L * 1024 * 1024;
    internal const int MaxRetainedFiles = 24;
    internal const int RetentionDays = 180;
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
            var directory = logDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EtpReporting",
                "Logs");
            Directory.CreateDirectory(directory);
            var entry = new DesktopDiagnosticEntry(
                timestamp,
                severity,
                SafeToken(eventId, "UNCLASSIFIED"),
                SafeToken(source, "Unknown"),
                RequiredCorrelationId(correlationId),
                exception?.GetType().FullName ?? "Unknown",
                exception?.HResult ?? 0,
                Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown");
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
