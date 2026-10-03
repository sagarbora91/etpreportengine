using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Etp.Reporting.Import.Profiles;

namespace Etp.Reporting.TestSupport;

/// <summary>
/// The synthetic ImportAudit fixtures under <c>tests-dotnet/fixtures/import-audit/</c>, written to real <c>.xlsx</c> files in
/// a temporary folder at test time (design 12): no binary fixture is kept in the repository. A fixture is JSON:
/// <code>
/// { "fileName": "...xlsx", "folder": "optional sub folder",
///   "sheets": [ { "name": "Data", "family": "R025", "rows": [ { "STORE CODE": "WLMHW", ... } ] },
///               { "name": "Info", "cells": [ [ "Family ID", "R010" ] ] } ] }
/// </code>
/// A <c>family</c> sheet gets the family's full catalogue header row and each row fills the named columns (the rest stay
/// blank); a <c>cells</c> sheet is written as given, row 1 first. JSON numbers become number cells, strings text cells.
/// </summary>
internal static class AuditFixtureWorkbooks
{
    public static string FixtureFolder => Path.Combine(AppContext.BaseDirectory, "fixtures", "import-audit");

    /// <summary>A fresh temporary folder for one test; delete it with <see cref="Delete"/>.</summary>
    public static string NewFolder(string? name = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "etp-import-audit-tests", Guid.NewGuid().ToString("N"), name ?? "input");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static void Delete(string folder)
    {
        try
        {
            var root = Directory.GetParent(folder)?.FullName;
            if (root is not null && Directory.Exists(root)) Directory.Delete(root, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Writes the named fixture into <paramref name="folder"/> (under its own sub folder, if it names one); returns the path.</summary>
    public static string Write(string fixture, string folder, Action<JsonFixture>? change = null)
    {
        var loaded = Load(fixture);
        change?.Invoke(loaded);
        return Write(loaded, folder);
    }

    public static JsonFixture Load(string fixture)
    {
        using var stream = File.OpenRead(Path.Combine(FixtureFolder, fixture));
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        return new JsonFixture
        {
            FileName = root.GetProperty("fileName").GetString()!,
            Folder = root.TryGetProperty("folder", out var sub) ? sub.GetString() : null,
            Sheets = root.GetProperty("sheets").EnumerateArray().Select(ReadSheet).ToList()
        };
    }

    public static string Write(JsonFixture fixture, string folder)
    {
        var target = fixture.Folder is { } sub ? Path.Combine(folder, sub) : folder;
        Directory.CreateDirectory(target);
        var path = Path.Combine(target, fixture.FileName);
        WriteWorkbook(path, fixture.Sheets.Select(sheet => (sheet.Name, sheet.Grid())).ToArray());
        return path;
    }

    /// <summary>
    /// Writes an in-memory synthetic workbook (such as <c>SyntheticConsolidatedWorkbooks</c>) to <paramref name="folder"/> as a
    /// real .xlsx under its own file name, each row at its row number; returns the path.
    /// </summary>
    public static string WriteSnapshot(Etp.Reporting.Import.Workbooks.WorkbookSnapshot workbook, string folder, string? fileName = null)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName ?? workbook.FileName);
        WriteWorkbook(path, workbook.Sheets.Select(sheet =>
        {
            var last = Math.Max(sheet.HeaderRowNumber, sheet.Rows.Count == 0 ? 0 : sheet.Rows.Max(row => row.RowNumber));
            var grid = Enumerable.Range(0, last).Select(_ => (IReadOnlyList<object?>)[]).ToList();
            grid[sheet.HeaderRowNumber - 1] = sheet.Headers.Cast<object?>().ToArray();
            foreach (var row in sheet.Rows) grid[row.RowNumber - 1] = row.Cells.Select(cell => cell.Value switch
            {
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTime time => time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                var value => value
            }).ToArray();
            return (sheet.Name, (IReadOnlyList<IReadOnlyList<object?>>)grid);
        }).ToArray());
        return path;
    }

    /// <summary>Writes sheets of cells (row 1 first) to an .xlsx file. A null cell is left empty.</summary>
    public static void WriteWorkbook(string path, IReadOnlyList<(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows)> sheets)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        var list = new Sheets();
        uint sheetId = 1;
        foreach (var (name, rows) in sheets)
        {
            var part = workbook.AddNewPart<WorksheetPart>();
            var data = new SheetData();
            for (var r = 0; r < rows.Count; r++)
            {
                var row = new Row { RowIndex = (uint)(r + 1) };
                for (var c = 0; c < rows[r].Count; c++)
                {
                    var value = rows[r][c];
                    if (value is null) continue;
                    var reference = Column(c) + (r + 1).ToString(CultureInfo.InvariantCulture);
                    row.Append(value is decimal or double or int or long
                        ? new Cell { CellReference = reference, DataType = CellValues.Number, CellValue = new CellValue(Convert.ToString(value, CultureInfo.InvariantCulture)!) }
                        : new Cell { CellReference = reference, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(Convert.ToString(value, CultureInfo.InvariantCulture)!) { Space = SpaceProcessingModeValues.Preserve }) });
                }
                data.Append(row);
            }
            part.Worksheet = new Worksheet(data);
            list.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = sheetId++, Name = name });
        }
        workbook.Workbook.AppendChild(list);
        workbook.Workbook.Save();
    }

    private static string Column(int index)
    {
        var name = "";
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    private static FixtureSheetSpec ReadSheet(JsonElement sheet)
    {
        var spec = new FixtureSheetSpec { Name = sheet.GetProperty("name").GetString()! };
        if (sheet.TryGetProperty("family", out var family))
        {
            spec.Family = family.GetString();
            spec.Rows = sheet.GetProperty("rows").EnumerateArray().Select(row => row.EnumerateObject()
                .ToDictionary(property => property.Name, property => Value(property.Value), StringComparer.Ordinal)).ToList();
        }
        else
            spec.Cells = sheet.GetProperty("cells").EnumerateArray().Select(row => row.EnumerateArray().Select(Value).ToList()).ToList();
        return spec;
    }

    private static object? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDecimal(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Null => null,
        _ => value.ToString()
    };
}

internal sealed class JsonFixture
{
    public required string FileName { get; set; }
    public string? Folder { get; set; }
    public required List<FixtureSheetSpec> Sheets { get; init; }

    public FixtureSheetSpec Sheet(string name) => Sheets.Single(sheet => string.Equals(sheet.Name, name, StringComparison.OrdinalIgnoreCase));
}

internal sealed class FixtureSheetSpec
{
    public required string Name { get; set; }
    public string? Family { get; set; }
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
    public List<List<object?>> Cells { get; set; } = [];

    /// <summary>The sheet as a grid, row 1 (the header) first.</summary>
    public IReadOnlyList<IReadOnlyList<object?>> Grid()
    {
        if (Family is null) return Cells;
        var headers = EtpReportFamilyRegistry.Resolve(Family).Headers;
        var grid = new List<IReadOnlyList<object?>> { headers.Cast<object?>().ToArray() };
        foreach (var row in Rows)
        {
            foreach (var name in row.Keys)
                if (!headers.Contains(name, StringComparer.Ordinal))
                    throw new InvalidOperationException($"The fixture names column '{name}', which {Family} does not have.");
            grid.Add(headers.Select(header => row.GetValueOrDefault(header)).ToArray());
        }
        return grid;
    }
}
