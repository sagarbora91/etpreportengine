using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Preflight;

public sealed record ImportPreflightResult(
    ImportProfile? Profile,
    WorkbookSheet? Sheet,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool CanImport => Profile is not null && Diagnostics.All(x => x.Severity != ImportDiagnosticSeverity.Blocker);
}

public sealed class ImportPreflight
{
    private readonly ImportProfileMatcher matcher = new();

    public ImportPreflightResult Inspect(
        WorkbookSnapshot workbook,
        IEnumerable<ImportProfile> profiles,
        ISet<string>? previouslyImportedSha256 = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(profiles);
        var diagnostics = new List<ImportDiagnostic>();

        if (workbook.FileSizeBytes <= 0)
            diagnostics.Add(Blocker("FILE_EMPTY", "The source file is empty."));
        if (string.IsNullOrWhiteSpace(workbook.Sha256) || workbook.Sha256.Length != 64 || workbook.Sha256.Any(c => !Uri.IsHexDigit(c)))
            diagnostics.Add(Blocker("FILE_HASH_INVALID", "The source file does not have a valid SHA-256 identity."));
        else if (previouslyImportedSha256?.Contains(workbook.Sha256) == true)
            diagnostics.Add(Blocker("DUPLICATE_FILE", "This exact source file has already been imported."));

        if (workbook.Sheets.Count == 0)
            diagnostics.Add(Blocker("WORKBOOK_NO_SHEETS", "The workbook contains no readable sheets."));

        var candidates = new List<(WorkbookSheet Sheet, ImportProfile Profile)>();
        foreach (var sheet in workbook.Sheets)
        {
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

            var match = matcher.Match(normalizedSheet.Headers, profiles);
            if (match is not null) candidates.Add((normalizedSheet, match));
        }

        if (candidates.Count == 0)
            diagnostics.Add(Blocker("LAYOUT_UNKNOWN", "No import profile exactly matches a worksheet header signature."));
        else if (candidates.Count > 1)
            diagnostics.Add(Blocker("LAYOUT_AMBIGUOUS", "More than one worksheet matches an import profile."));

        return new(
            candidates.Count == 1 ? candidates[0].Profile : null,
            candidates.Count == 1 ? candidates[0].Sheet : null,
            diagnostics);
    }

    private static ImportDiagnostic Blocker(string code, string message, string? sheet = null) =>
        new(code, ImportDiagnosticSeverity.Blocker, message, sheet);
}
