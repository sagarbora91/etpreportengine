using System.Globalization;
using Etp.Reporting.Application.Imports;

namespace Etp.Reporting.Application.Morning;

/// <summary>
/// 1.12.1 lane P4 (decision 64 #3): the route argument of a Morning card's Fix button, so the fix screen opens the one
/// refused file of that card and not the whole Problems list. It names the day, the card's store and the file (its
/// SHA-256 when ETP read the bytes, else its file name). Text form <c>fix:yyyyMMdd:STORE:key</c>; a store code and a
/// Windows file name never hold a colon, and the key is the last part, so it may. The argument is a route value only;
/// it never names a path and is never stored on disk.
/// </summary>
public sealed record MorningFixArgument(DateOnly BusinessDate, string StoreCode, string FileKey)
{
    public const string Prefix = "fix:";

    /// <summary>The Fix argument for one refused import of a store card; null when the attempt names no file at all.</summary>
    public static MorningFixArgument? For(DateOnly businessDate, string storeCode, ImportHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(storeCode)) return null;
        var key = !string.IsNullOrWhiteSpace(entry.Result.SourceSha256) ? entry.Result.SourceSha256.Trim()
            : !string.IsNullOrWhiteSpace(entry.Result.FileName) ? entry.Result.FileName.Trim()
            : null;
        return key is null ? null : new(businessDate, storeCode.Trim(), key);
    }

    public override string ToString() =>
        Prefix + BusinessDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ":" + StoreCode + ":" + FileKey;

    /// <summary>Reads an argument this type wrote; anything else (an older "attempt:41" key, a bare file name) is null.</summary>
    public static MorningFixArgument? TryParse(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument)) return null;
        var text = argument.Trim();
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var parts = text[Prefix.Length..].Split(':', 3);
        if (parts.Length != 3) return null;
        if (!DateOnly.TryParseExact(parts[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        var store = parts[1].Trim();
        var key = parts[2].Trim();
        if (store.Length == 0 || key.Length == 0) return null;
        return new(date, store, key);
    }

    /// <summary>
    /// True for a problem row of this file: the same SHA-256 (any store: the bytes are the file), or the same file name in
    /// this store or with no store (a file refused before ETP learned its store is kept in every store's history). The
    /// same file name in another store is a different file (exports carry one name in every store, Titan audit R-09).
    /// </summary>
    public bool Matches(string? fileName, string? storeCode, string? sourceSha256)
    {
        if (!string.IsNullOrWhiteSpace(sourceSha256) && string.Equals(sourceSha256.Trim(), FileKey, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName.Trim(), FileKey, StringComparison.OrdinalIgnoreCase))
            return false;
        return string.IsNullOrWhiteSpace(storeCode) || string.Equals(storeCode.Trim(), StoreCode, StringComparison.OrdinalIgnoreCase);
    }
}
