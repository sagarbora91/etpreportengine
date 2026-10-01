using Etp.Reporting.Domain.Imports;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Workbooks;

namespace Etp.Reporting.Import.Tests;

public sealed class ImportPreflightTests
{
    [Fact]
    public void Exact_layout_is_accepted_without_excel_dependency()
    {
        string[] headers = ["Bill Date", "Article"];
        var profile = Profile(headers);
        var workbook = Workbook(headers);

        var result = new ImportPreflight().Inspect(workbook, [profile]);

        Assert.True(result.CanImport);
        Assert.Same(profile, result.Profile);
        Assert.NotNull(result.Sheet);
        Assert.Contains(result.Diagnostics, d => d.Code == "EMPTY_EXPORT");
    }

    [Fact]
    public void Unknown_layout_is_a_blocker_and_is_not_guessed()
    {
        var result = new ImportPreflight().Inspect(Workbook(["Unexpected"]), [Profile(["Expected"])]);

        Assert.False(result.CanImport);
        Assert.Contains(result.Diagnostics, x => x.Code == "LAYOUT_UNKNOWN");
        Assert.Contains(result.Diagnostics, x => x.Code == "REQUIRED_COLUMN_MISSING" && x.ColumnName == "EXPECTED");
        Assert.Contains(result.Diagnostics, x => x.Code == "UNEXPECTED_COLUMN" && x.ColumnName == "UNEXPECTED");
    }

    [Fact]
    public void Previously_imported_hash_is_a_blocker()
    {
        string[] headers = ["Expected"];
        var workbook = Workbook(headers);

        var result = new ImportPreflight().Inspect(workbook, [Profile(headers)], new HashSet<string> { workbook.Sha256 });

        Assert.False(result.CanImport);
        Assert.Contains(result.Diagnostics, x => x.Code == "DUPLICATE_FILE");
    }

    [Fact]
    public void Duplicate_normalized_headers_are_rejected()
    {
        var result = new ImportPreflight().Inspect(Workbook(["Item Code", " item  code "]), []);

        Assert.Contains(result.Diagnostics, x => x.Code == "HEADER_DUPLICATE");
    }

    [Fact]
    public void Exact_repeated_horizontal_layout_is_collapsed_before_profile_matching()
    {
        string[] logicalHeaders = ["Bill Date", "Article"];
        var sheet = new WorkbookSheet("Data", 1, [.. logicalHeaders, .. logicalHeaders],
        [
            new WorkbookRow(2,
            [
                new WorkbookCell("2026-07-01"), new WorkbookCell("SKU-1"),
                new WorkbookCell("2026-07-01"), new WorkbookCell("SKU-1")
            ])
        ]);
        var workbook = new WorkbookSnapshot("repeated.xlsx", 100, new string('a', 64), [sheet]);

        var result = new ImportPreflight().Inspect(workbook, [Profile(logicalHeaders)]);

        Assert.True(result.CanImport);
        Assert.Equal(logicalHeaders, result.Sheet!.Headers);
        Assert.Equal(2, result.Sheet.Rows[0].Cells.Count);
        Assert.Contains(result.Diagnostics, x => x.Code == "REPEATED_LAYOUT_COLLAPSED");
    }

    [Fact]
    public void Repeated_horizontal_layout_with_different_values_is_blocked()
    {
        string[] logicalHeaders = ["Bill Date", "Article"];
        var sheet = new WorkbookSheet("Data", 1, [.. logicalHeaders, .. logicalHeaders],
        [
            new WorkbookRow(2,
            [
                new WorkbookCell("2026-07-01"), new WorkbookCell("SKU-1"),
                new WorkbookCell("2026-07-01"), new WorkbookCell("SKU-2")
            ])
        ]);
        var workbook = new WorkbookSnapshot("mismatch.xlsx", 100, new string('a', 64), [sheet]);

        var result = new ImportPreflight().Inspect(workbook, [Profile(logicalHeaders)]);

        Assert.False(result.CanImport);
        Assert.Contains(result.Diagnostics, x => x.Code == "REPEATED_LAYOUT_MISMATCH");
    }

    [Fact]
    public void Service_centre_snapshot_history_sheet_is_ignored_like_info()
    {
        var family = EtpReportFamilyRegistry.Resolve("S006");
        var workbook = new WorkbookSnapshot("S006_ClosingStock.xlsx", 100, new string('a', 64),
        [
            new WorkbookSheet("Data", 1, family.Headers, []),
            new WorkbookSheet("Info", 1, ["Family ID", "S006"], []),
            new WorkbookSheet("Snapshot History", 1, [.. family.Headers, "Snapshot_As_Of", "SourceFile"], [])
        ]);

        var result = new ImportPreflight().Inspect(workbook, ApprovedImportProfileRegistry.All);

        Assert.True(result.CanImport);
        Assert.Equal("S006", result.Profile!.ReportCode);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "UNEXPECTED_COLUMN");
    }

    [Fact]
    public void Another_extra_sheet_keeps_todays_closest_profile_warnings()
    {
        var family = EtpReportFamilyRegistry.Resolve("S006");
        var workbook = new WorkbookSnapshot("S006_ClosingStock.xlsx", 100, new string('a', 64),
        [
            new WorkbookSheet("Data", 1, family.Headers, []),
            new WorkbookSheet("History", 1, [.. family.Headers, "Snapshot_As_Of", "SourceFile"], [])
        ]);

        var result = new ImportPreflight().Inspect(workbook, ApprovedImportProfileRegistry.All);

        Assert.True(result.CanImport);
        Assert.Contains(result.Diagnostics, d => d.Code == "UNEXPECTED_COLUMN" && d.SheetName == "History");
    }

    private static ImportProfile Profile(string[] headers) => new("SYNTHETIC", "1", "1",
        ImportProfileMatcher.CreateHeaderSignature(headers),
        headers.Select((header, index) => new ImportFieldMapping(header, $"field_{index}", CanonicalDataType.Text, true)));

    private static WorkbookSnapshot Workbook(string[] headers) => new("synthetic.xlsx", 100,
        new string('a', 64), [new WorkbookSheet("Data", 1, headers, [])]);
}
