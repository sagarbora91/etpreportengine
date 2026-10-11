using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Etp.Reporting.Desktop.Diagnostics;

/// <summary>Where the last ingest stopped: the TimestampUtc and 12-character Ref of the last entry sent.</summary>
public sealed record RegisterCursor(DateTimeOffset TimestampUtc, string Reference);

/// <summary>Error and Critical entries after the cursor, oldest first, and how many lines could not be read.</summary>
public sealed record DiagnosticsLogReadResult(IReadOnlyList<DesktopDiagnosticEntry> Entries, int UnreadableLines);

/// <summary>
/// Reads this Windows account's <c>diagnostics-*.jsonl</c> (design 4.3) for the error register: Error and Critical
/// entries after a cursor, Warning counts per event code (Q7: warnings are counted, never registered), and the
/// cursor file <c>register-cursor.json</c>. The log is written concurrently by the application, so files are opened
/// with shared access and a half-written last line is skipped. Nothing here throws for a missing or locked file.
/// </summary>
public sealed class DiagnosticsLogReader
{
    public const string CursorFileName = "register-cursor.json";
    public const int ReferenceLength = 12;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly TimeSpan FileClockSlack = TimeSpan.FromMinutes(2);

    private readonly string _directory;

    public DiagnosticsLogReader(string? directory = null)
    {
        _directory = directory ?? DesktopDiagnostics.DefaultDirectory(Environment.GetEnvironmentVariable(DesktopDiagnostics.DirectoryVariable));
    }

    public string LogDirectory => _directory;

    /// <summary>The Ref as the register stores it: the first 12 characters of the CorrelationId (a legacy 32-hex id is cut). Null when shorter.</summary>
    public static string? ReferenceOf(DesktopDiagnosticEntry entry)
    {
        var id = entry.CorrelationId?.Trim();
        return string.IsNullOrEmpty(id) || id.Length < ReferenceLength ? null : id[..ReferenceLength];
    }

    /// <summary>Error and Critical entries strictly after <paramref name="cursor"/> (all of them when null), oldest first.</summary>
    public DiagnosticsLogReadResult ReadErrorsAfter(RegisterCursor? cursor)
    {
        var entries = new List<(DesktopDiagnosticEntry Entry, string Reference)>();
        var unreadable = 0;
        foreach (var line in Lines(cursor?.TimestampUtc))
        {
            var entry = Parse(line);
            if (entry is null) { unreadable++; continue; }
            if (entry.Severity is not (DesktopDiagnosticSeverity.Error or DesktopDiagnosticSeverity.Critical)) continue;
            var reference = ReferenceOf(entry) ?? entry.CorrelationId ?? string.Empty;
            if (cursor is not null && Compare(entry.TimestampUtc, reference, cursor) <= 0) continue;
            entries.Add((entry, reference));
        }
        var ordered = entries
            .OrderBy(item => item.Entry.TimestampUtc)
            .ThenBy(item => item.Reference, StringComparer.Ordinal)
            .Select(item => item.Entry)
            .ToArray();
        return new DiagnosticsLogReadResult(ordered, unreadable);
    }

    /// <summary>Warnings per event base (<c>EventId</c> up to the first '.' or ':'), optionally only those at or after a time.</summary>
    public IReadOnlyDictionary<string, int> CountWarnings(DateTimeOffset? sinceUtc = null)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in Lines(sinceUtc))
        {
            var entry = Parse(line);
            if (entry is null || entry.Severity != DesktopDiagnosticSeverity.Warning) continue;
            if (sinceUtc is not null && entry.TimestampUtc < sinceUtc) continue;
            var key = ErrorFingerprint.EventBaseOf(entry.EventId);
            if (key.Length == 0) continue;
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return counts;
    }

    /// <summary>Null when the file is missing, unreadable or not a cursor (losing it is harmless: the database ignores duplicates).</summary>
    public RegisterCursor? ReadCursor()
    {
        try
        {
            var path = Path.Combine(_directory, CursorFileName);
            if (!File.Exists(path)) return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!TryGet(root, "timestampUtc", out var stamp) || stamp.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(stamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
                return null;
            var reference = TryGet(root, "ref", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return new RegisterCursor(timestamp, reference ?? string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the cursor through a temporary file. Returns false (and changes nothing) if the folder is not writable.</summary>
    public bool WriteCursor(RegisterCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, CursorFileName);
            var temporary = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            var json = JsonSerializer.Serialize(new
            {
                timestampUtc = cursor.TimestampUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture),
                @ref = cursor.Reference
            });
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    /// <summary>-1, 0 or 1: the entry against the cursor, by time and then by Ref (ordinal).</summary>
    internal static int Compare(DateTimeOffset timestamp, string reference, RegisterCursor cursor)
    {
        var byTime = timestamp.UtcDateTime.CompareTo(cursor.TimestampUtc.UtcDateTime);
        return byTime != 0 ? Math.Sign(byTime) : Math.Sign(string.CompareOrdinal(reference, cursor.Reference));
    }

    internal static DesktopDiagnosticEntry? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            var entry = JsonSerializer.Deserialize<DesktopDiagnosticEntry>(line, Options);
            return entry is null || entry.TimestampUtc == default || string.IsNullOrEmpty(entry.EventId) ? null : entry;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }

    // Lines of every log file that could hold an entry at or after the given time. A file last written well before
    // that time cannot (entries are written when they are stamped), so a fresh cursor skips old months cheaply.
    private IEnumerable<string> Lines(DateTimeOffset? sinceUtc)
    {
        var paths = Array.Empty<string>();
        try
        {
            if (System.IO.Directory.Exists(_directory))
                paths = System.IO.Directory.GetFiles(_directory, "diagnostics-*.jsonl", SearchOption.TopDirectoryOnly);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            paths = Array.Empty<string>();
        }

        foreach (var path in paths.OrderBy(path => path, StringComparer.Ordinal))
        {
            if (sinceUtc is not null && LastWriteOrMax(path) < sinceUtc.Value.UtcDateTime - FileClockSlack) continue;
            StreamReader? reader = null;
            try
            {
                reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), Encoding.UTF8);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                continue;
            }

            using (reader)
            {
                while (true)
                {
                    string? line;
                    try { line = reader.ReadLine(); }
                    catch (IOException) { break; }
                    if (line is null) break;
                    yield return line;
                }
            }
        }
    }

    private static DateTime LastWriteOrMax(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException) { return DateTime.MaxValue; }
    }
}
