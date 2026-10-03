using System.Globalization;
using System.Text.RegularExpressions;

namespace Etp.Reporting.Import.Sources;

/// <summary>
/// Reads when ETP produced an export from its file name (spec 6.3), first matching pattern wins:
/// a Retail <c>yyyyMMddHHmm_</c> prefix (minute), a Service <c>_yyyyMMddHHmmss.xlsx|csv</c> suffix (second),
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

        foreach (Match match in DottedDate().Matches(name))
            if (DateOnly.TryParseExact(match.Value, "dd.MM.yyyy", culture, DateTimeStyles.None, out var date) &&
                Plausible(date.ToDateTime(TimeOnly.MinValue)))
                return ExportTime.OnDate(date);

        return ExportTime.Unknown;
    }

    // ETP exports are from this century; anything else is a number that only looks like a time.
    private static bool Plausible(DateTime value) => value.Year is >= 2000 and <= 2099;

    [GeneratedRegex(@"^(\d{12})_", RegexOptions.CultureInvariant)]
    private static partial Regex RetailPrefix();

    [GeneratedRegex(@"_(\d{14})\.(?:xlsx|csv)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ServiceSuffix();

    [GeneratedRegex(@"(?<!\d)\d{2}\.\d{2}\.\d{4}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex DottedDate();
}
