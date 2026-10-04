using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Preflight;

public sealed record ImportPreflightResult(
    ImportProfile? Profile,
    WorkbookSheet? Sheet,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool CanImport => Profile is not null && Diagnostics.All(x => x.Severity != ImportDiagnosticSeverity.Blocker);

    /// <summary>The consolidation contract of a workbook whose <c>Info!A1</c> is <c>etp_contract</c> (layout only).</summary>
    public ContractReadResult Contract { get; init; } = ContractReadResult.NotAContract;
}

public sealed class ImportPreflight
{
    private readonly ImportProfileMatcher matcher = new();
    private readonly ConsolidationContractReader contracts = new();

    public ImportPreflightResult Inspect(
        WorkbookSnapshot workbook,
        IEnumerable<ImportProfile> profiles,
        ISet<string>? previouslyImportedSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(profiles);
        var diagnostics = new List<ImportDiagnostic>();
        var materializedProfiles = profiles.ToArray();

        if (workbook.FileSizeBytes <= 0)
            diagnostics.Add(Blocker("FILE_EMPTY", "The source file is empty."));
        if (string.IsNullOrWhiteSpace(workbook.Sha256) || workbook.Sha256.Length != 64 || workbook.Sha256.Any(c => !Uri.IsHexDigit(c)))
            diagnostics.Add(Blocker("FILE_HASH_INVALID", "The source file does not have a valid SHA-256 identity."));
        else if (previouslyImportedSha256?.Contains(workbook.Sha256) == true)
            diagnostics.Add(Blocker("DUPLICATE_FILE", "This exact source file has already been imported."));

        if (workbook.Sheets.Count == 0)
            diagnostics.Add(Blocker("WORKBOOK_NO_SHEETS", "The workbook contains no readable sheets."));

        // A contract workbook is read for its family, store and block dates (spec 6.1); one that cannot be read is refused.
        var contract = contracts.Read(workbook);
        diagnostics.AddRange(contract.Diagnostics);

        var candidates = new List<(WorkbookSheet Sheet, ImportProfile Profile)>();
        foreach (var originalSheet in workbook.Sheets.Where(sheet => !IsNonDataSheet(sheet.Name)))
        {
            var sheet = originalSheet;
            if (sheet.Rows.Count == 0 && (sheet.Headers.Count == 0 || sheet.Headers.All(string.IsNullOrWhiteSpace)))
            {
                // Consolidation explicitly records an absent snapshot as an empty Data sheet.
                // Require both a family identity and the Info empty marker; an arbitrary blank workbook is not a known layout.
                var info = workbook.Sheets.FirstOrDefault(s => s.Name.Equals("Info", StringComparison.OrdinalIgnoreCase));
                var named = EtpReportFamilyRegistry.IdentifyName(workbook.FileName);
                var infoValues = info?.Rows.SelectMany(r => r.Cells).Select(c => c.Value?.ToString() ?? "").ToArray() ?? [];
                if (named is not null && infoValues.Contains(named.FamilyCode, StringComparer.OrdinalIgnoreCase) &&
                    infoValues.Any(value => value.StartsWith("EMPTY", StringComparison.OrdinalIgnoreCase)))
                    sheet = sheet with { Headers = named.Headers, HeaderRowNumber = 1 };
            }
            if (BelowTitleRows(sheet, materializedProfiles, workbook.FileName, contract.Contract?.Header.FamilyCode) is { } titled)
            {
                sheet = titled;
                diagnostics.Add(new(HeaderBelowTitleRows, ImportDiagnosticSeverity.Information,
                    "The header row was found below title rows; the title rows are not imported.", sheet.Name, sheet.HeaderRowNumber));
            }
            if (string.IsNullOrWhiteSpace(sheet.Name))
                diagnostics.Add(Blocker("SHEET_NAME_MISSING", "A worksheet has no name."));
            if (sheet.HeaderRowNumber < 1 || sheet.Headers.Count == 0 || sheet.Headers.Any(string.IsNullOrWhiteSpace))
            {
                diagnostics.Add(Blocker("HEADER_INVALID", "A complete, non-empty header row is required.", sheet.Name));
                continue;
            }

            var layout = WorkbookLayoutNormalizer.Normalize(sheet);
            diagnostics.AddRange(layout.Diagnostics);
            if (layout.Sheet is null) continue;
            var normalizedSheet = layout.Sheet;

            var normalized = normalizedSheet.Headers.Select(ImportProfile.NormalizeHeader).ToArray();
            if (normalized.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
            {
                diagnostics.Add(Blocker("HEADER_DUPLICATE", "Duplicate normalized headers are not allowed.", sheet.Name));
                continue;
            }

            var match = matcher.Match(normalizedSheet.Headers, materializedProfiles, workbook.FileName, sheet.Name,
                contract.Contract?.Header.FamilyCode);
            if (match is not null) candidates.Add((normalizedSheet, match));
            else AddSchemaDifferenceDiagnostics(normalizedSheet, materializedProfiles, diagnostics);
        }

        if (candidates.Count == 0)
            diagnostics.Add(Blocker("LAYOUT_UNKNOWN", "No import profile exactly matches a worksheet header signature."));
        else if (candidates.Count > 1)
            diagnostics.Add(Blocker("LAYOUT_AMBIGUOUS", "More than one worksheet matches an import profile."));
        else if (candidates[0].Sheet.Rows.Count == 0)
            diagnostics.Add(new("EMPTY_EXPORT", ImportDiagnosticSeverity.Information, "Empty export; no rows to import.", candidates[0].Sheet.Name));

        return new(
            candidates.Count == 1 ? candidates[0].Profile : null,
            candidates.Count == 1 ? candidates[0].Sheet : null,
            diagnostics) { Contract = contract };
    }

    /// <summary>Information code: a raw export's header was found below title rows (<see cref="BelowTitleRows"/>).</summary>
    public const string HeaderBelowTitleRows = "HEADER_BELOW_TITLE_ROWS";

    // Title rows above a raw export's header are few (EMPOWERMENT REPORT: 11, TATA REPORT: 15); a header further down is not looked for.
    private const int TitleRowSearchDepth = 30;

    /// <summary>
    /// Some raw ETP exports (EMPOWERMENT REPORT, TATA REPORT) put title and filter rows above the real header, so the
    /// reader's first non-blank row is a title. When the sheet's own header matches no profile, the first of the next
    /// <see cref="TitleRowSearchDepth"/> rows whose cells exactly match a profile's header signature becomes the header,
    /// and only the rows below it are data. A sheet whose header already matches is never changed, so every layout that
    /// matched before matches the same way; nothing is guessed, because the match is the same exact signature match.
    /// </summary>
    private WorkbookSheet? BelowTitleRows(WorkbookSheet sheet, IReadOnlyList<ImportProfile> profiles, string fileName, string? familyCode)
    {
        if (profiles.Count == 0 || sheet.Rows.Count == 0) return null;
        if (sheet.HeaderRowNumber >= 1 && sheet.Headers.Count > 0 && !sheet.Headers.Any(string.IsNullOrWhiteSpace) &&
            matcher.Match(sheet.Headers, profiles, fileName, sheet.Name, familyCode) is not null)
            return null;
        foreach (var row in sheet.Rows.Where(row => row.RowNumber > sheet.HeaderRowNumber).OrderBy(row => row.RowNumber).Take(TitleRowSearchDepth))
        {
            var texts = row.Cells.Select(cell => cell.DisplayText?.Trim() ?? string.Empty).ToList();
            while (texts.Count > 0 && texts[^1].Length == 0) texts.RemoveAt(texts.Count - 1);
            if (texts.Count < 2 || texts.Any(text => text.Length == 0)) continue;
            if (matcher.Match(texts, profiles, fileName, sheet.Name, familyCode) is null) continue;
            return new WorkbookSheet(sheet.Name, row.RowNumber, texts, sheet.Rows.Where(below => below.RowNumber > row.RowNumber).ToArray());
        }
        return null;
    }

    /// <summary>Info, ETP_Excluded and Snapshot History describe a consolidated workbook; they are never data sheets.</summary>
    public static bool IsNonDataSheet(string? sheetName) =>
        ConsolidationContractLayout.NonDataSheets.Contains(sheetName?.Trim() ?? "", StringComparer.OrdinalIgnoreCase);

    private static ImportDiagnostic Blocker(string code, string message, string? sheet = null) =>
        new(code, ImportDiagnosticSeverity.Blocker, message, sheet);

    private static void AddSchemaDifferenceDiagnostics(
        WorkbookSheet sheet,
        IReadOnlyList<ImportProfile> profiles,
        ICollection<ImportDiagnostic> diagnostics)
    {
        if (profiles.Count == 0) return;

        var actual = sheet.Headers.Select(ImportProfile.NormalizeHeader).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var closest = profiles
            .Select(profile => new
            {
                Profile = profile,
                Expected = profile.ExpectedSourceHeaders
                    .Select(ImportProfile.NormalizeHeader)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
            })
            .OrderByDescending(candidate => candidate.Expected.Intersect(actual, StringComparer.OrdinalIgnoreCase).Count())
            .ThenBy(candidate => candidate.Expected.Except(actual, StringComparer.OrdinalIgnoreCase).Count()
                + actual.Except(candidate.Expected, StringComparer.OrdinalIgnoreCase).Count())
            .ThenBy(candidate => candidate.Profile.ReportCode, StringComparer.OrdinalIgnoreCase)
            .First();

        foreach (var missing in closest.Expected.Except(actual, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            diagnostics.Add(new ImportDiagnostic(
                "REQUIRED_COLUMN_MISSING",
                ImportDiagnosticSeverity.Blocker,
                $"Column '{missing}' is required by the closest profile {closest.Profile.ReportCode} ({closest.Profile.LayoutVersion}).",
                sheet.Name,
                ColumnName: missing));

        foreach (var unexpected in actual.Except(closest.Expected, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            diagnostics.Add(new ImportDiagnostic(
                "UNEXPECTED_COLUMN",
                ImportDiagnosticSeverity.Warning,
                $"Column '{unexpected}' is not present in the closest profile {closest.Profile.ReportCode} ({closest.Profile.LayoutVersion}).",
                sheet.Name,
                ColumnName: unexpected));
    }
}
