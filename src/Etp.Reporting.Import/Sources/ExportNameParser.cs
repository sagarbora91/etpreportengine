using System.Globalization;
using System.Text.RegularExpressions;

namespace Etp.Reporting.Import.Sources;

/// <summary>
/// Reads when ETP produced an export from its file name (spec 6.3), first matching pattern wins:
/// a Retail <c>yyyyMMddHHmm_</c> prefix (minute), a Service <c>_yyyyMMddHHmmss.xlsx|csv</c> suffix (second),
/// a raw Service window <c>dd.MM.yyyy TO dd.MM.yyyy</c> (the END date, date basis; see <see cref="ParseWindow"/>),
/// a <c>dd.MM.yyyy</c> date (date), otherwise unknown. A pattern whose digits are not a real time is
/// skipped, and nothing is ever estimated: not the file's modified time, not the time of the import.
/// </summary>
public static partial class ExportNameParser
{
    public static ExportTime Parse(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return ExportTime.Unknown;
        var name = Path.GetFileName(fileName.Trim());
        var culture = CultureInfo.InvariantCulture;

        if (RetailPrefix().Match(name) is { Success: true } prefix &&
            DateTime.TryParseExact(prefix.Groups[1].Value, "yyyyMMddHHmm", culture, DateTimeStyles.None, out var minute) &&
            Plausible(minute))
            return ExportTime.AtMinute(minute);

        if (ServiceSuffix().Match(name) is { Success: true } suffix &&
            DateTime.TryParseExact(suffix.Groups[1].Value, "yyyyMMddHHmmss", culture, DateTimeStyles.None, out var second) &&
            Plausible(second))
            return ExportTime.AtSecond(second);

        // A raw Service export names the window it covers ("JOB REPORT 30.09.2026 TO 03.10.2026.csv"); ETP made it on
        // the window's last day, so the end date is the export date (design section 2: the row dates agree).
        if (Window(name) is { } window)
            return ExportTime.OnDate(window.To);

        foreach (Match match in DottedDate().Matches(name))
            if (DateOnly.TryParseExact(match.Value, "dd.MM.yyyy", culture, DateTimeStyles.None, out var date) &&
                Plausible(date.ToDateTime(TimeOnly.MinValue)))
                return ExportTime.OnDate(date);

        return ExportTime.Unknown;
    }

    /// <summary>
    /// The window a raw Service export names, <c>dd.MM.yyyy TO dd.MM.yyyy</c> (any spacing and case around <c>TO</c>, a
    /// stray dot before it as in "26.08.2026. TO 06.09.2026", a trailing space before the extension allowed): its start, kept as information, and its end, which is the export
    /// date. Null when the name states no such window, or a date is not real, or the end is before the start.
    /// </summary>
    public static ExportWindow? ParseWindow(string? fileName) =>
        string.IsNullOrWhiteSpace(fileName) ? null : Window(Path.GetFileName(fileName.Trim()));

    private static ExportWindow? Window(string name)
    {
        var culture = CultureInfo.InvariantCulture;
        foreach (Match match in DottedRange().Matches(name))
            if (DateOnly.TryParseExact(match.Groups[1].Value, "dd.MM.yyyy", culture, DateTimeStyles.None, out var from) &&
                DateOnly.TryParseExact(match.Groups[2].Value, "dd.MM.yyyy", culture, DateTimeStyles.None, out var to) &&
                Plausible(from.ToDateTime(TimeOnly.MinValue)) && Plausible(to.ToDateTime(TimeOnly.MinValue)) && from <= to)
                return new ExportWindow(from, to);
        return null;
    }

    // ETP exports are from this century; anything else is a number that only looks like a time.
    private static bool Plausible(DateTime value) => value.Year is >= 2000 and <= 2099;

    [GeneratedRegex(@"^(\d{12})_", RegexOptions.CultureInvariant)]
    private static partial Regex RetailPrefix();

    [GeneratedRegex(@"_(\d{14})\.(?:xlsx|csv)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ServiceSuffix();

    [GeneratedRegex(@"(?<!\d)\d{2}\.\d{2}\.\d{4}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex DottedDate();

    [GeneratedRegex(@"(?<!\d)(\d{2}\.\d{2}\.\d{4})[.\s]*TO\s*(\d{2}\.\d{2}\.\d{4})(?!\d)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DottedRange();
}

/// <summary>The window a raw export's file name states: <see cref="From"/> is information, <see cref="To"/> the export date.</summary>
public readonly record struct ExportWindow(DateOnly From, DateOnly To);
