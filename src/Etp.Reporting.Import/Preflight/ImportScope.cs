using System.Globalization;
using System.Text.RegularExpressions;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Conversion;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Preflight;

public sealed record ImportScope(string? StoreCode, DateOnly? PeriodStart, DateOnly? PeriodEnd)
{
    /// <summary>
    /// For an undated family (R010, R023, SOR_AGEING), the snapshot date of each run of data rows (spec 6.4). The period is
    /// min..max of these dates. Empty for dated families, and when the workbook itself does not state its date.
    /// </summary>
    public IReadOnlyList<SnapshotBlock> SnapshotBlocks { get; init; } = [];

    /// <summary>Dating diagnostics: <c>SNAPSHOT_DATE_AMBIGUOUS</c>, <c>SNAPSHOT_MULTIPLE_DATES</c>, <c>SNAPSHOT_DATE_FROM_FOLDER</c>, <c>INFO_BLOCKS_UNUSABLE</c>.</summary>
    public IReadOnlyList<ImportDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>
    /// True when an undated family's workbook states no snapshot date by any tier (spec 6.4). Only the folder import can
    /// still date it, from siblings that agree (tier 7); every other route refuses it with <c>SNAPSHOT_DATE_UNKNOWN</c>.
    /// </summary>
    public bool AwaitsSiblingDate { get; init; }

    /// <summary>
    /// True for a family whose rows hold no date of their own (R010, R023, SOR_AGEING): each row is a reading of the
    /// snapshot date of its block, so that date is part of the row's identity (planner 1 content keys).
    /// </summary>
    public static bool IsUndatedFamily(string reportCode)
    {
        var family = EtpReportFamilyRegistry.Resolve(reportCode);
        return family.Columns.All(column => column.SourceHeader != family.PrimaryDateHeader);
    }

    /// <summary>
    /// The snapshot date of one data row of an undated family: the date of the block that holds it, else
    /// <paramref name="businessDate"/> (a workbook dated as a whole by its siblings or the override has no blocks).
    /// </summary>
    public DateOnly? SnapshotDateOf(string sheetName, int sheetRow, DateOnly? businessDate) =>
        SnapshotBlock.DateOf(SnapshotBlocks, sheetName, sheetRow) ?? businessDate;

    /// <summary>Refuses a workbook whose snapshot date only its folder siblings could give; for routes that have no siblings.</summary>
    public void RequireOwnSnapshotDate()
    {
        if (AwaitsSiblingDate)
            throw new ImportSourceException(ImportCodes.SnapshotDateUnknown,
                "The snapshot date of this stock or status report could not be found. Import the ETP file under its original name, or import its whole folder.");
    }

    public static ImportScope Detect(WorkbookSnapshot workbook, ImportProfile profile, ImportStagingResult staging, IReadOnlyList<string>? knownStores = null,
        WorkbookSheet? dataSheet = null, ContractReadResult? contract = null)
    {
        contract ??= new ConsolidationContractReader().Read(workbook);
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
        if (stores.Length == 0 && contract.Contract?.Header.StoreCode is { } contractStore)
            detectedStore = contractStore.ToUpperInvariant();
        else if (stores.Length == 0)
        {
            var contextual = (knownStores ?? []).Where(code => context.Any(value => Regex.IsMatch(value, @"(?<![A-Z0-9])" + Regex.Escape(code) + @"(?![A-Z0-9])", RegexOptions.IgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (contextual.Length == 1) detectedStore = contextual[0];
        }
        if (dates.Length != 0)
        {
            // Planner 1 holds one closing-stock snapshot per file; P5 splits a multi-date file into one snapshot per date.
            var snapshotDates = profile.ReportCode == "CLOSING_STOCK" ? dates.Distinct().Order().ToArray() : [];
            return new(detectedStore, dates.Min(), dates.Max())
            {
                Diagnostics = snapshotDates.Length > 1
                    ? [new(ImportCodes.SnapshotMultipleDates, ImportDiagnosticSeverity.Blocker,
                        $"This closing-stock file holds {snapshotDates.Length} snapshot dates ({string.Join(", ", snapshotDates.Select(Text))}). " +
                        "Import one ETP closing-stock export per date.", dataSheet?.Name, ColumnName: family.PrimaryDateHeader)]
                    : []
            };
        }

        if (dateField is null)
        {
            var sheet = dataSheet ?? workbook.Sheets.FirstOrDefault(candidate => !ConsolidationContractLayout.NonDataSheets
                .Contains(candidate.Name.Trim(), StringComparer.OrdinalIgnoreCase));
            if (sheet is null) return new(detectedStore, null, null);
            var dating = new SnapshotDateResolver().Resolve(workbook, sheet, contract);
            // Only a workbook no tier dated at all is left for the folder import to date from its siblings (tier 7). A row that
            // a dating tier left uncovered stays a blocker: it must never fall through to a sibling or context date.
            var diagnostics = dating.NoTierDated
                ? dating.Diagnostics.Where(diagnostic => diagnostic.Code != ImportCodes.SnapshotDateUnknown).ToArray()
                : dating.Diagnostics;
            return new(detectedStore, dating.From, dating.To)
                { SnapshotBlocks = dating.Blocks, Diagnostics = diagnostics, AwaitsSiblingDate = dating.NoTierDated };
        }

        // An empty export of a dated family keeps its legacy context date; it carries no rows to date.
        var contextualDates = context.SelectMany(FindDates).ToArray();
        var snapshotDate = contextualDates.Length == 0 ? (DateOnly?)null : contextualDates.Max();
        return new(detectedStore, snapshotDate, snapshotDate);
    }

    private static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
