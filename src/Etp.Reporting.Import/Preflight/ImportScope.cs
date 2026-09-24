using System.Globalization;
using System.Text.RegularExpressions;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Conversion;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Preflight;

public sealed record ImportScope(string? StoreCode, DateOnly? PeriodStart, DateOnly? PeriodEnd)
{
    public static ImportScope Detect(WorkbookSnapshot workbook, ImportProfile profile, ImportStagingResult staging, IReadOnlyList<string>? knownStores = null)
    {
        var stores = staging.Rows.Select(row => row.Values.GetValueOrDefault("store_code") as string)
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var family = EtpReportFamilyRegistry.Resolve(profile.ReportCode);
        var dateField = family.Columns.FirstOrDefault(column => column.SourceHeader == family.PrimaryDateHeader)?.CanonicalField;
        var dates = dateField is null ? [] : staging.Rows.Select(row => row.Values.GetValueOrDefault(dateField))
            .OfType<DateOnly>().ToArray();
        var context = workbook.Sheets.Where(sheet => sheet.Name.Equals("Info", StringComparison.OrdinalIgnoreCase))
            .SelectMany(sheet => sheet.Rows.SelectMany(row => row.Cells).Select(cell => cell.Value?.ToString() ?? ""))
            .Prepend(workbook.SourcePath ?? workbook.FileName).ToArray();
        var detectedStore = stores.Length == 1 ? stores[0] : null;
        if (stores.Length == 0)
        {
            var contextual = (knownStores ?? []).Where(code => context.Any(value => Regex.IsMatch(value, @"(?<![A-Z0-9])" + Regex.Escape(code) + @"(?![A-Z0-9])", RegexOptions.IgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (contextual.Length == 1) detectedStore = contextual[0];
        }
        if (dates.Length != 0) return new(detectedStore, dates.Min(), dates.Max());

        var contextualDates = context.SelectMany(FindDates).ToArray();
        var snapshotDate = contextualDates.Length == 0 ? (DateOnly?)null : contextualDates.Max();
        return new(detectedStore, snapshotDate, snapshotDate);
    }

    private static IEnumerable<DateOnly> FindDates(string value)
    {
        foreach (Match match in Regex.Matches(value, @"(?<!\d)(20\d{6})(?:\d{4,6})?(?!\d)|(?<!\d)\d{1,2}[- ](?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*[- ](?:20\d{2}|\d{2})(?!\d)", RegexOptions.IgnoreCase))
        {
            var text = match.Groups[1].Success ? match.Groups[1].Value : match.Value;
            var result = new TypedCellConverter().Convert(text, CanonicalDataType.Date, false);
            if (result.Value is DateOnly date) yield return date;
        }
    }
}
