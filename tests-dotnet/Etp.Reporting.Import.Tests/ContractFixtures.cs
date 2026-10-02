using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Identity;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Staging;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

/// <summary>
/// The synthetic workbooks under <c>tests-dotnet/fixtures/contract/</c>: a workbook and the raw exports its blocks came
/// from, each written as sheets of rows (row 1 first). Tests change a loaded copy to provoke one validation code.
/// </summary>
internal sealed class FixtureBook
{
    public required string FileName { get; set; }
    public required string Sha256 { get; set; }
    public string? SourcePath { get; set; }
    public required List<FixtureSheet> Sheets { get; init; }

    public FixtureSheet Sheet(string name) => Sheets.Single(sheet => string.Equals(sheet.Name, name, StringComparison.OrdinalIgnoreCase));

    public WorkbookSnapshot ToSnapshot() => new(FileName, 1000, Sha256, Sheets.Select(sheet => sheet.ToSheet()).ToArray(), SourcePath);

    /// <summary>The Info row holding <paramref name="key"/> in column A.</summary>
    public List<object?> KeyRow(string key) => Sheet("Info").Rows.First(row => row.Count > 0 && Equals(row[0], key));

    public void SetKey(string key, object? value) => KeyRow(key)[1] = value;

    public void RemoveKey(string key) => Sheet("Info").Rows.Remove(KeyRow(key));

    public void AddKey(string key, string value)
    {
        var info = Sheet("Info").Rows;
        var blank = info.FindIndex(row => row.Count == 0);
        info.Insert(blank, [key, value]);
    }

    public void SetBlock(int blockNo, string column, object? value)
    {
        var info = Sheet("Info").Rows;
        var header = info.First(row => row.Count > 0 && Equals(row[0], "block"));
        var row = info.First(candidate => candidate.Count > 0 && Equals(candidate[0], blockNo.ToString(CultureInfo.InvariantCulture)));
        row[header.IndexOf(column)] = value;
    }

    public string Block(int blockNo, string column)
    {
        var info = Sheet("Info").Rows;
        var header = info.First(row => row.Count > 0 && Equals(row[0], "block"));
        return (string)info.First(candidate => candidate.Count > 0 && Equals(candidate[0], blockNo.ToString(CultureInfo.InvariantCulture)))[header.IndexOf(column)]!;
    }

    public static FixtureBook FromJson(JsonElement element) => new()
    {
        FileName = element.GetProperty("fileName").GetString()!,
        Sha256 = element.GetProperty("sha256").GetString()!,
        Sheets = element.GetProperty("sheets").EnumerateArray().Select(sheet => new FixtureSheet
        {
            Name = sheet.GetProperty("name").GetString()!,
            Rows = sheet.GetProperty("rows").EnumerateArray()
                .Select(row => row.EnumerateArray().Select(cell => (object?)cell.GetString()).ToList()).ToList()
        }).ToList()
    };
}

internal sealed class FixtureSheet
{
    public required string Name { get; set; }
    /// <summary>Every sheet row, row 1 (the header) first. An empty list is a blank row.</summary>
    public required List<List<object?>> Rows { get; init; }

    public WorkbookSheet ToSheet() => new(Name, 1, Rows[0].Select(cell => Convert.ToString(cell, CultureInfo.InvariantCulture) ?? "").ToArray(),
        Rows.Skip(1).Select((row, index) => new WorkbookRow(index + 2, row.Select(cell => new WorkbookCell(cell)).ToArray())).ToArray());
}

/// <summary>A fixture workbook and its raw exports.</summary>
internal sealed record ContractFixture(FixtureBook Workbook, IReadOnlyList<FixtureBook> Raw)
{
    public static ContractFixture Load(string name)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "contract", name));
        using var json = JsonDocument.Parse(stream);
        return new(FixtureBook.FromJson(json.RootElement.GetProperty("workbook")),
            json.RootElement.GetProperty("raw").EnumerateArray().Select(FixtureBook.FromJson).ToArray());
    }

    public IReadOnlyDictionary<string, WorkbookSnapshot> RawBySha() =>
        Raw.ToDictionary(raw => raw.Sha256, raw => raw.ToSnapshot(), StringComparer.Ordinal);
}

/// <summary>The synthetic catalogue of the contract fixtures (R025, R010, S009).</summary>
internal static class ContractCatalogue
{
    private static readonly Lazy<IReadOnlyList<EtpReportFamily>> Families = new(() =>
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", "contract", "catalogue.json"));
        return EtpReportFamilyRegistry.Parse(stream);
    });

    public static EtpReportFamily Family(string code) => Families.Value.Single(family => family.FamilyCode == code);
}

/// <summary>
/// Reads the Info machine section and the <c>ETP_Excluded</c> map as the contract lays them out (contract 3-4), for the
/// tests only: the importer's layout reader is lane P1's. A cell is text when its value is a string.
/// </summary>
internal sealed class TestContractReader : IConsolidationContractReader
{
    public ContractReadResult Read(WorkbookSnapshot workbook)
    {
        var info = workbook.Sheets.FirstOrDefault(sheet => string.Equals(sheet.Name, "Info", StringComparison.OrdinalIgnoreCase));
        if (info is null || info.Headers.Count == 0 || info.Headers[0] != ConsolidationContractLayout.Marker) return ContractReadResult.NotAContract;

        var rows = info.Rows.ToList();
        var index = 0;
        var keys = new List<ContractHeaderKey>();
        for (; index < rows.Count && !Blank(rows[index]); index++)
            keys.Add(new(rows[index].RowNumber, Text(rows[index], 0), Text(rows[index], 1), rows[index].Cells.Skip(1).Take(1).All(IsText)));
        while (index < rows.Count && Blank(rows[index])) index++;
        if (index >= rows.Count)
            return new(true, null, [new(Application.Imports.ImportCodes.Contract.Unreadable, ImportDiagnosticSeverity.Blocker, "No block table.")]);

        var headerRow = rows[index++];
        var columns = headerRow.Cells.Select(cell => Convert.ToString(cell.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
        var blocks = new List<ContractBlockRow>();
        for (; index < rows.Count && !Blank(rows[index]); index++)
        {
            var row = rows[index];
            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var column = 0; column < columns.Length; column++)
                if (columns[column].Length > 0) cells[columns[column]] = Text(row, column);
            blocks.Add(new(row.RowNumber, cells) { AllCellsText = row.Cells.All(IsText) });
        }

        var excluded = workbook.Sheets.FirstOrDefault(sheet => string.Equals(sheet.Name, ConsolidationContractLayout.ExcludedSheet, StringComparison.OrdinalIgnoreCase));
        var contract = new ConsolidationContract(
            new ConsolidationContractHeader(info.Headers.Count > 1 ? info.Headers[1] : "", keys),
            columns, blocks)
        {
            BlockTableHeaderRow = headerRow.RowNumber,
            ExcludedHeader = excluded?.Headers ?? [],
            Excluded = excluded?.Rows.Select(row => new ContractExcludedRow(row.RowNumber,
                excluded.Headers.Select((name, column) => (name, column)).ToDictionary(pair => pair.name, pair => Text(row, pair.column)))).ToArray() ?? []
        };
        return new(true, contract, []);
    }

    private static bool Blank(WorkbookRow row) => row.Cells.All(cell => string.IsNullOrWhiteSpace(Convert.ToString(cell.Value, CultureInfo.InvariantCulture)));

    private static bool IsText(WorkbookCell cell) => cell.Value is null or string;

    private static string Text(WorkbookRow row, int column) =>
        column < row.Cells.Count ? Convert.ToString(row.Cells[column].Value, CultureInfo.InvariantCulture) ?? "" : "";
}

/// <summary>A legacy Info table given by the test (lane P1 reads the real one).</summary>
internal sealed class StubLegacyInfoReader(LegacyInfoTable table) : ILegacyInfoBlockReader
{
    public static StubLegacyInfoReader None { get; } = new(LegacyInfoTable.None);

    public LegacyInfoTable Read(WorkbookSnapshot workbook, WorkbookSheet dataSheet) => table;
}

/// <summary>
/// A canonicaliser following the rules of spec 7.1 closely enough for these tests (lane P2-B owns the real one):
/// sorted <c>len:key:len:value</c> lines, roles from the catalogue, SHA-256 lowercase hex.
/// </summary>
internal sealed class TestCanonicalizer : IFactCanonicalizer
{
    public int ContentHashVersion => 1;

    public string Format(object? value) => value switch
    {
        null => "",
        decimal number => Math.Round(number, 4).ToString("0.####", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        string text => text.Trim(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? ""
    };

    public string Hash(IEnumerable<KeyValuePair<string, object?>> values) => Sha(string.Join('\n', values
        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => $"{pair.Key.Length}:{pair.Key}:{Format(pair.Value).Length}:{Format(pair.Value)}")));

    public CanonicalRow Canonicalize(EtpReportFamily family, IReadOnlyDictionary<string, object?> values)
    {
        var roles = family.Columns.ToDictionary(column => column.CanonicalField, column => column.Role, StringComparer.Ordinal);
        IEnumerable<KeyValuePair<string, object?>> With(params ColumnRole[] wanted) =>
            values.Where(pair => wanted.Contains(roles.GetValueOrDefault(pair.Key, ColumnRole.Fact)));
        var facts = With(ColumnRole.Key, ColumnRole.Fact).ToArray();
        var attributes = With(ColumnRole.Attribute).ToArray();
        return new CanonicalRow(Hash(facts), Hash(attributes), Hash(With(ColumnRole.Descriptive)),
            Hash(values.Where(pair => roles.GetValueOrDefault(pair.Key, ColumnRole.Fact) != ColumnRole.Ignored)),
            facts.ToDictionary(pair => pair.Key, pair => Format(pair.Value)),
            attributes.ToDictionary(pair => pair.Key, pair => Format(pair.Value)));
    }

    public string MultisetHash(IEnumerable<string> rowHashes) => Sha(string.Join('\n', rowHashes.Order(StringComparer.Ordinal)));

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

/// <summary>Stages fixture sheets and projects R025-shaped blocks into documents keyed by FY and invoice number.</summary>
internal static class TestSources
{
    public static readonly TestCanonicalizer Canonicalizer = new();

    public static SourceDescriptionReader Reader(ILegacyInfoBlockReader? legacy = null) =>
        new(new TestContractReader(), legacy ?? StubLegacyInfoReader.None);

    /// <summary>The staged rows of every data sheet of a source, located on their sheet (block numbers come from the rebuild).</summary>
    public static IReadOnlyList<SourceRow> Stage(WorkbookSnapshot workbook, EtpReportFamily family, params string[] sheets)
    {
        var stager = new ImportRowStager();
        var profile = family.CreateProfile();
        var rows = new List<SourceRow>();
        foreach (var name in sheets)
        {
            var sheet = workbook.Sheets.Single(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(name, ConsolidationContractLayout.HistorySheet, StringComparison.OrdinalIgnoreCase))
                sheet = HistorySheetBlockReader.StagingSheet(sheet);
            rows.AddRange(stager.Stage(sheet, profile).Rows.Select(row =>
                new SourceRow(new RowLocator(0, sheet.Name, row.SourceRowNumber), row.Values)));
        }
        return rows;
    }

    public static BlockProjection Project(EtpReportFamily family, string store, SourceBlock block, IReadOnlyList<SourceRow> rows) =>
        new(block.BlockNo, rows
            .GroupBy(row => DocumentKey.ForDocument(family.ReportCode, store, (DateOnly)row.Values["transaction_date"]!, (string)row.Values["invoice_number"]!))
            .Select(group => Observation(family, group.Key, block, group.ToArray()))
            .ToArray(), [], []);

    public static DocumentObservation Observation(EtpReportFamily family, DocumentKey key, SourceBlock block, IReadOnlyList<SourceRow> rows)
    {
        var factRows = rows.Select(row => new FactRow(row.Locator, Canonicalizer.Canonicalize(family, row.Values))).ToArray();
        return new DocumentObservation(key, block.BlockNo, block.ExportTime, (DateOnly?)rows[0].Values.GetValueOrDefault("transaction_date"), factRows,
            Canonicalizer.MultisetHash(factRows.Select(row => row.Canonical.FactRowHash)),
            Canonicalizer.MultisetHash(factRows.Select(row => row.Canonical.AttributeHash)));
    }
}
