using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Preflight;

public sealed record WorkbookLayoutNormalization(
    WorkbookSheet? Sheet,
    IReadOnlyList<ImportDiagnostic> Diagnostics);

public static class WorkbookLayoutNormalizer
{
    public static WorkbookLayoutNormalization Normalize(WorkbookSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (sheet.Headers.Count < 4 || sheet.Headers.Count % 2 != 0)
            return new(sheet, []);

        var width = sheet.Headers.Count / 2;
        var leftHeaders = sheet.Headers.Take(width).ToArray();
        var rightHeaders = sheet.Headers.Skip(width).Take(width).ToArray();
        if (!HeadersEqual(leftHeaders, rightHeaders))
            return new(sheet, []);

        foreach (var row in sheet.Rows)
        {
            var cells = Pad(row.Cells, width * 2);
            if (!cells.Take(width).SequenceEqual(cells.Skip(width).Take(width)))
            {
                return new(null,
                [
                    new ImportDiagnostic(
                        "REPEATED_LAYOUT_MISMATCH",
                        ImportDiagnosticSeverity.Blocker,
                        "The worksheet repeats its header layout horizontally, but the corresponding row values differ.",
                        sheet.Name,
                        row.RowNumber)
                ]);
            }
        }

        var rows = sheet.Rows
            .Select(row => new WorkbookRow(row.RowNumber, Pad(row.Cells, width).Take(width).ToArray()))
            .ToArray();
        var normalized = new WorkbookSheet(sheet.Name, sheet.HeaderRowNumber, leftHeaders, rows);
        return new(normalized,
        [
            new ImportDiagnostic(
                "REPEATED_LAYOUT_COLLAPSED",
                ImportDiagnosticSeverity.Information,
                "An exact duplicated horizontal layout was validated and collapsed.",
                sheet.Name)
        ]);
    }

    private static bool HeadersEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Select(ImportProfile.NormalizeHeader)
            .SequenceEqual(right.Select(ImportProfile.NormalizeHeader), StringComparer.Ordinal);

    private static IReadOnlyList<WorkbookCell> Pad(IReadOnlyList<WorkbookCell> cells, int size)
    {
        if (cells.Count >= size) return cells;
        var padded = cells.ToList();
        while (padded.Count < size) padded.Add(new WorkbookCell(null));
        return padded;
    }
}
