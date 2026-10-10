using Etp.Reporting.Application.Imports;
using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

/// <summary>
/// IF-025: an R010 BinWise export re-saved in Excel (a filtered copy of <c>Sheet0</c> as <c>Sheet2</c>, pivots as
/// <c>Sheet1</c> and <c>Sheet3</c>) was refused as LAYOUT_AMBIGUOUS plus the pivots' missing columns, which the folder
/// import shows as "Unknown layout". It is now refused with one diagnostic naming the matching sheets. Synthetic workbooks
/// only: the R010 header is the catalogue's, every value is made up.
/// </summary>
public sealed class EditedAfterExportPreflightTests
{
    private static readonly ImportProfile[] AllProfiles = EtpReportFamilyRegistry.Families.Select(family => family.CreateProfile()).ToArray();
    private static readonly EtpReportFamily R010 = EtpReportFamilyRegistry.Families.Single(family => family.FamilyCode == "R010");
    private static readonly string[] LayoutCodes = ["LAYOUT_AMBIGUOUS", "LAYOUT_UNKNOWN", "REQUIRED_COLUMN_MISSING", "UNEXPECTED_COLUMN"];

    [Fact]
    public void Edited_BinWise_workbook_is_refused_naming_the_matching_and_the_pivot_sheets_not_as_an_unknown_layout()
    {
        var workbook = Workbook(BinWise("Sheet0", 3), Pivot("Sheet1", "BRAND"), BinWise("Sheet2", 1), Pivot("Sheet3", "CLUSTER"));

        var result = new ImportPreflight().Inspect(workbook, AllProfiles);

        Assert.False(result.CanImport);
        Assert.Null(result.Profile);
        Assert.Null(result.Sheet);
        var refusal = Assert.Single(result.Diagnostics, x => x.Code == ImportCodes.WorkbookEditedAfterExport);
        Assert.Equal(ImportDiagnosticSeverity.Blocker, refusal.Severity);
        Assert.Equal("This workbook was edited after export: sheets Sheet0, Sheet2 match the R010 layout; sheets Sheet1, Sheet3 are not ETP export sheets. " +
            "Re-export it from ETP and import the new file; nothing from this workbook was imported.", refusal.Message);
        // Nothing that makes the folder import or the audit call it "Unknown layout" (FolderImportService, SourceInspector).
        Assert.DoesNotContain(result.Diagnostics, x => LayoutCodes.Contains(x.Code));
    }

    [Fact]
    public void Export_sheet_with_pivot_sheets_beside_it_is_refused_too_never_imported_in_part()
    {
        var result = new ImportPreflight().Inspect(Workbook(BinWise("Sheet0", 2), Pivot("Sheet1", "BRAND")), AllProfiles);

        Assert.False(result.CanImport);
        Assert.Null(result.Profile);
        var refusal = Assert.Single(result.Diagnostics, x => x.Code == ImportCodes.WorkbookEditedAfterExport);
        Assert.StartsWith("This workbook was edited after export: sheet Sheet0 matches the R010 layout; sheet Sheet1 is not an ETP export sheet.", refusal.Message);
        Assert.DoesNotContain(result.Diagnostics, x => LayoutCodes.Contains(x.Code));
    }

    [Fact]
    public void Two_copies_of_the_export_sheet_without_pivots_are_named_as_edited()
    {
        var result = new ImportPreflight().Inspect(Workbook(BinWise("Sheet0", 2), BinWise("Sheet2", 1)), AllProfiles);

        Assert.False(result.CanImport);
        Assert.StartsWith("This workbook was edited after export: sheets Sheet0, Sheet2 match the R010 layout. Re-export",
            Assert.Single(result.Diagnostics, x => x.Code == ImportCodes.WorkbookEditedAfterExport).Message);
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == "LAYOUT_AMBIGUOUS");
    }

    [Fact]
    public void The_unedited_export_still_imports()
    {
        var result = new ImportPreflight().Inspect(Workbook(BinWise("Sheet0", 2)), AllProfiles);

        Assert.True(result.CanImport);
        Assert.Equal(R010.ReportCode, result.Profile!.ReportCode);
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == ImportCodes.WorkbookEditedAfterExport);
    }

    [Fact]
    public void Sheets_matching_different_layouts_stay_ambiguous()
    {
        string[] first = ["Bill Date", "Article"], second = ["Bin", "Quantity"];
        var workbook = new WorkbookSnapshot("synthetic.xlsx", 100, new string('a', 64),
            [new WorkbookSheet("One", 1, first, []), new WorkbookSheet("Two", 1, second, [])]);

        var result = new ImportPreflight().Inspect(workbook, [Profile("FIRST", first), Profile("SECOND", second)]);

        Assert.Contains(result.Diagnostics, x => x.Code == "LAYOUT_AMBIGUOUS");
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == ImportCodes.WorkbookEditedAfterExport);
    }

    [Fact]
    public void An_extra_sheet_that_only_adds_warnings_changes_nothing()
    {
        string[] headers = ["Bill Date", "Article"];
        var workbook = new WorkbookSnapshot("synthetic.xlsx", 100, new string('a', 64),
            [new WorkbookSheet("Data", 1, headers, []), new WorkbookSheet("Notes", 1, [.. headers, "Remark"], [])]);

        var result = new ImportPreflight().Inspect(workbook, [Profile("FIRST", headers)]);

        Assert.True(result.CanImport);
        Assert.Contains(result.Diagnostics, x => x.Code == "UNEXPECTED_COLUMN");
        Assert.DoesNotContain(result.Diagnostics, x => x.Code == ImportCodes.WorkbookEditedAfterExport);
    }

    [Fact]
    public void History_keeps_the_template_without_sheet_names()
    {
        var message = new ImportPreflight().Inspect(Workbook(BinWise("Sheet0", 1), Pivot("Sheet1", "BRAND")), AllProfiles)
            .Diagnostics.Single(x => x.Code == ImportCodes.WorkbookEditedAfterExport).Message;

        var stored = ImportDiagnosticCatalogue.SafeMessage(ImportCodes.WorkbookEditedAfterExport, message);

        Assert.Equal(ImportDiagnosticCatalogue.Template(ImportCodes.WorkbookEditedAfterExport), stored);
        Assert.DoesNotContain("Sheet", stored, StringComparison.Ordinal);
        Assert.Contains("Re-export it from ETP", stored, StringComparison.Ordinal);
    }

    private static WorkbookSnapshot Workbook(params WorkbookSheet[] sheets) =>
        new("202610031040_BinWise-Stock - BinWise-Stock.xlsx", 1000, new string('b', 64), sheets);

    private static WorkbookSheet BinWise(string name, int rows) => new(name, 1, R010.Headers,
        Enumerable.Range(2, rows).Select(row => new WorkbookRow(row, R010.Headers.Select((_, column) => new WorkbookCell($"SYN{row}-{column}")).ToArray())).ToArray());

    // An Excel pivot as the reader sees it: a two-column header and a few total rows.
    private static WorkbookSheet Pivot(string name, string by) => new(name, 1, ["Row Labels", "Sum of RETAILBIN"],
        [new WorkbookRow(2, [new WorkbookCell($"SYNTHETIC {by}"), new WorkbookCell(1m)]), new WorkbookRow(3, [new WorkbookCell("Grand Total"), new WorkbookCell(1m)])]);

    private static ImportProfile Profile(string code, string[] headers) => new(code, "1", "1",
        ImportProfileMatcher.CreateHeaderSignature(headers),
        headers.Select((header, index) => new ImportFieldMapping(header, $"field_{index}", CanonicalDataType.Text, true)));
}
