using System.Globalization;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Etp.Reporting.Import.Workbooks;

public sealed class OpenXmlWorkbookReader : IWorkbookReader
{
    public async Task<WorkbookSnapshot> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var info = new FileInfo(filePath);
        if (!info.Exists) throw new FileNotFoundException("Workbook not found.", filePath);

        // Excel commonly keeps an export open. Take one in-memory snapshot through a shared-read
        // handle so hashing and parsing see identical bytes without requiring the user to close Excel.
        await using var source = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var snapshot = new MemoryStream(checked((int)Math.Min(source.Length, int.MaxValue)));
        await source.CopyToAsync(snapshot, cancellationToken);
        var bytes = snapshot.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        snapshot.Position = 0;

        using var document = SpreadsheetDocument.Open(snapshot, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("Workbook part is missing.");
        var shared = workbookPart.SharedStringTablePart?.SharedStringTable;
        var dateStyles = DateStyleIndexes(workbookPart);
        var sheets = new List<WorkbookSheet>();
        foreach (var sheet in workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sheet.Id?.Value is null) continue;
            if (workbookPart.GetPartById(sheet.Id.Value) is not WorksheetPart part) continue;

            // Enumerating Row/Cell XML is intentional: ETP files can declare a false A1 worksheet dimension.
            var sourceRows = part.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>().ToArray() ?? [];
            if (sourceRows.Length == 0) continue;
            var materialized = sourceRows.Select(r => ReadRow(r, shared, dateStyles)).ToArray();
            var header = materialized.FirstOrDefault(r => r.Cells.Any(c => !string.IsNullOrWhiteSpace(c.DisplayText)))
                ?? materialized[0];
            var headers = header.Cells.Select(c => c.DisplayText?.Trim() ?? string.Empty).ToArray();
            var rows = materialized.Where(r => r.RowNumber > header.RowNumber && r.Cells.Any(c => c.Value is not null)).ToArray();
            sheets.Add(new WorkbookSheet(sheet.Name?.Value ?? string.Empty, header.RowNumber, headers, rows));
        }
        return new WorkbookSnapshot(info.Name, info.Length, hash, sheets);
    }

    private static WorkbookRow ReadRow(Row row, SharedStringTable? shared, ISet<uint> dateStyles)
    {
        var values = new SortedDictionary<int, WorkbookCell>();
        foreach (var cell in row.Elements<Cell>())
        {
            var index = ColumnIndex(cell.CellReference?.Value);
            var value = ReadValue(cell, shared, dateStyles);
            values[index] = new WorkbookCell(value, Convert.ToString(value, CultureInfo.InvariantCulture));
        }
        var width = values.Count == 0 ? 0 : values.Keys.Max() + 1;
        var cells = Enumerable.Range(0, width).Select(i => values.GetValueOrDefault(i, new WorkbookCell(null))).ToArray();
        return new WorkbookRow(checked((int)(row.RowIndex?.Value ?? 0)), cells);
    }

    private static object? ReadValue(Cell cell, SharedStringTable? shared, ISet<uint> dateStyles)
    {
        if (cell.InlineString is not null) return cell.InlineString.InnerText;
        var raw = cell.CellValue?.InnerText;
        if (raw is null) return null;
        var type = cell.DataType?.Value;
        if (type == CellValues.SharedString && int.TryParse(raw, out var index)) return shared?.ElementAtOrDefault(index)?.InnerText;
        if (type == CellValues.Boolean) return raw == "1";
        if (cell.StyleIndex?.Value is uint style && dateStyles.Contains(style) &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
            return DateTime.FromOADate(serial);
        if (type == CellValues.Number && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
        if (type == CellValues.Date && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)) return date;
        return raw;
    }

    private static HashSet<uint> DateStyleIndexes(WorkbookPart workbookPart)
    {
        var styles = workbookPart.WorkbookStylesPart?.Stylesheet;
        if (styles?.CellFormats is null) return [];
        var custom = styles.NumberingFormats?.Elements<NumberingFormat>()
            .Where(x => x.NumberFormatId?.Value is not null)
            .ToDictionary(x => x.NumberFormatId!.Value, x => x.FormatCode?.Value ?? string.Empty) ?? [];
        var result = new HashSet<uint>(); uint index = 0;
        foreach (var format in styles.CellFormats.Elements<CellFormat>())
        {
            var id = format.NumberFormatId?.Value ?? 0;
            var code = custom.GetValueOrDefault(id, string.Empty).ToLowerInvariant();
            if ((id is >= 14 and <= 22) || (id is >= 45 and <= 47) ||
                (code.Contains('y') && (code.Contains('d') || code.Contains('m')))) result.Add(index);
            index++;
        }
        return result;
    }

    private static int ColumnIndex(string? reference)
    {
        var result = 0;
        foreach (var c in reference ?? string.Empty)
        {
            if (!char.IsLetter(c)) break;
            result = checked(result * 26 + char.ToUpperInvariant(c) - 'A' + 1);
        }
        return Math.Max(0, result - 1);
    }
}
