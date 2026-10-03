using System.IO.Compression;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Audit;
using Etp.Reporting.Import.Batch;
using Etp.Reporting.Import.Documents;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.TestSupport;
using static Etp.Reporting.Import.Tests.SyntheticConsolidatedWorkbooks;

namespace Etp.Reporting.Import.Tests;

/// <summary>ImportAudit <c>inspect</c> (design 12.2): what the engine sees in each workbook, from real .xlsx files.</summary>
public sealed class SourceInspectorTests : IDisposable
{
    private readonly string folder = AuditFixtureWorkbooks.NewFolder();

    public void Dispose() => AuditFixtureWorkbooks.Delete(folder);

    [Fact]
    public async Task A_raw_R025_named_by_minute_is_one_complete_block_timed_to_the_minute()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", folder);

        var run = await new SourceInspector().InspectAsync([folder]);

        var file = Assert.Single(run.Files);
        Assert.Equal(InspectionOutcome.Ready, file.Outcome);
        Assert.Equal("R025", file.ReportCode);
        Assert.Equal("WLMHW", file.Store);
        Assert.Equal(SourceKind.Raw, file.SourceKind);
        var block = Assert.Single(file.Blocks);
        Assert.Equal(BlockCompleteness.Complete, block.Block.Completeness);
        Assert.Equal(BlockOrigin.Raw, block.Block.Origin);
        Assert.Equal(ExportBasis.Minute, block.Block.ExportTime.Basis);
        Assert.Equal(new DateTime(2026, 9, 29, 14, 49, 0), block.Block.ExportTime.Instant);
        Assert.NotNull(block.Identity?.ContentSha256);
        Assert.NotNull(block.Identity?.ExportKey);
        Assert.True(file.Projected);
        Assert.Equal(2, file.Documents.Count);
        Assert.All(file.Documents, document => Assert.Equal(DocumentScope.Document, document.Key.Scope));
        Assert.Equal("202609291449_SDB-VariantwiseSales.xlsx", file.RelativePath);
    }

    [Fact]
    public async Task A_legacy_stacked_R010_is_three_info_blocks_dated_by_their_export_names()
    {
        AuditFixtureWorkbooks.WriteSnapshot(LegacyStackedBinWise(), folder);

        var file = Assert.Single((await new SourceInspector().InspectAsync([folder])).Files);

        Assert.Equal(SourceKind.ConsolidatedLegacy, file.SourceKind);
        Assert.Equal(3, file.Blocks.Count);
        Assert.All(file.Blocks, block => Assert.Equal(BlockOrigin.InfoLegacy, block.Block.Origin));
        Assert.All(file.Blocks, block => Assert.Equal(SnapshotDateBasis.InfoBlock, block.Block.SnapshotDateBasis));
        Assert.Equal([new DateOnly(2026, 7, 2), new DateOnly(2026, 8, 7), new DateOnly(2026, 9, 29)],
            file.Blocks.Select(block => block.Block.SnapshotDate!.Value));
        Assert.Equal([new DateOnly(2026, 7, 2), new DateOnly(2026, 8, 7), new DateOnly(2026, 9, 29)],
            file.SnapshotBlocks.Select(block => block.SnapshotDate).Distinct());
        Assert.Equal(3, file.Documents.Count(document => document.Key.Scope == DocumentScope.Snapshot));
    }

    [Fact]
    public async Task A_HEMW_shaped_R010_is_dated_by_its_block_not_by_its_folder()
    {
        AuditFixtureWorkbooks.WriteSnapshot(LegacySingleBinWise(), Path.Combine(folder, "Consolidated data import package (29 Sep 2026)", "HEMW"));

        var file = Assert.Single((await new SourceInspector().InspectAsync([folder])).Files);

        Assert.Equal(new DateOnly(2026, 9, 7), Assert.Single(file.Blocks).Block.SnapshotDate);
        Assert.Equal(new DateOnly(2026, 9, 7), file.PeriodEnd);
        Assert.Equal(Path.Combine("Consolidated data import package (29 Sep 2026)", "HEMW", "R010_BinWise_Stock.xlsx"), file.RelativePath);
    }

    [Fact]
    public async Task Overlapping_info_ranges_are_unusable_and_the_file_is_one_whole_block()
    {
        var workbook = new WorkbookSnapshot("R010_BinWise_Stock.xlsx", 1, Hash,
        [
            BinWiseData(8),
            LegacyInfo("No dated rows",
            [
                new("202607021446_BinWise-Stock - BinWise-Stock.xlsx", "2:6", 5),
                new("202608071848_BinWise-Stock - BinWise-Stock.xlsx", "5:9", 5)
            ])
        ]);
        AuditFixtureWorkbooks.WriteSnapshot(workbook, Path.Combine(folder, "29 Sep 2026"));

        var file = Assert.Single((await new SourceInspector().InspectAsync([folder])).Files);

        Assert.Contains(file.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.InfoBlocksUnusable);
        Assert.Equal(BlockOrigin.WholeFile, Assert.Single(file.Blocks).Block.Origin);
    }

    [Fact]
    public async Task Zip_entry_dates_from_zip_name_not_temp_folder()
    {
        var raw = AuditFixtureWorkbooks.WriteSnapshot(new WorkbookSnapshot("BinWise-Stock.xlsx", 1, Hash, [BinWiseData(3, "HEMW")]),
            Path.Combine(folder, "staging"));
        var zip = Path.Combine(folder, "HEMW 29 Sep 2026.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) archive.CreateEntryFromFile(raw, "BinWise-Stock.xlsx");

        var run = await new SourceInspector().InspectAsync([zip]);

        var file = Assert.Single(run.Files);
        Assert.Equal(new DateOnly(2026, 9, 29), file.PeriodEnd);
        Assert.Equal(SnapshotDateBasis.Folder, Assert.Single(file.SnapshotBlocks).Basis);
        Assert.Equal(Path.Combine("HEMW 29 Sep 2026", "BinWise-Stock.xlsx"), file.RelativePath);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), file.RelativePath, StringComparison.OrdinalIgnoreCase);
        // The extraction folder is gone once the inspection returns.
        Assert.False(File.Exists(file.SourcePath));
    }

    [Fact]
    public async Task Csv_in_folder_is_counted_not_read()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "service-pack.csv"), "a,b\n1,2\n");

        var run = await new SourceInspector().InspectAsync([folder]);

        Assert.Equal(1, run.SkippedCsvFiles);
        Assert.Single(run.Files);
    }

    [Fact]
    public async Task A_missing_path_is_an_input_refusal()
    {
        var refusal = await Assert.ThrowsAsync<ImportSourceException>(() => new SourceInspector().InspectAsync([Path.Combine(folder, "missing")]));

        Assert.Equal("IMPORT_SOURCE_NOT_FOUND", refusal.Code);
    }

    [Fact]
    public async Task Family_and_store_filters_narrow_the_files()
    {
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", folder);
        AuditFixtureWorkbooks.WriteSnapshot(LegacySingleBinWise(), Path.Combine(folder, "HEMW"));

        Assert.Equal("R010", Assert.Single((await new SourceInspector { Families = ["R010"] }.InspectAsync([folder])).Files).ReportCode);
        Assert.Equal("R025", Assert.Single((await new SourceInspector { Store = "WLMHW" }.InspectAsync([folder])).Files).ReportCode);
    }

    [Fact]
    public async Task Files_come_back_in_the_app_dependency_order()
    {
        AuditFixtureWorkbooks.Write("r030-per-unit-ledger.json", folder);
        AuditFixtureWorkbooks.Write("r025-raw-minute.json", folder);

        var run = await new SourceInspector().InspectAsync([folder]);

        Assert.Equal(["R025", "STOCK_LEDGER"], run.Files.Select(file => file.ReportCode));
    }

    [Fact]
    public async Task A_one_April_return_whose_invoice_year_differs_is_reported_as_information()
    {
        AuditFixtureWorkbooks.Write("r022-period-1.json", folder);

        var file = Assert.Single((await new SourceInspector().InspectAsync([folder])).Files);

        Assert.Equal(InspectionOutcome.Ready, file.Outcome);
        // The preflight and the projector each say so, for the one row.
        var differs = file.Diagnostics.Where(diagnostic => diagnostic.Code == ImportCodes.InvoiceYearDiffers).ToArray();
        Assert.NotEmpty(differs);
        Assert.All(differs, diagnostic => Assert.Equal(1, diagnostic.Occurrences));
        // Keyed by the financial year of its own date (OD-1): 1 Apr 2026 belongs to FY 2027.
        Assert.Contains(file.Documents, document => document.Key.KeyText == "2027|SYN400003");
    }

    [Fact]
    public async Task Legacy_blocks_that_differ_hold_the_document()
    {
        AuditFixtureWorkbooks.Write("r025-legacy-blocks-differ.json", folder);

        var file = Assert.Single((await new SourceInspector().InspectAsync([folder])).Files);

        Assert.Equal(SourceKind.ConsolidatedLegacy, file.SourceKind);
        Assert.Equal(2, file.Blocks.Count);
        Assert.Equal(1, file.HoldsByCode[ImportCodes.LegacyBlocksDiffer]);
        Assert.Contains(file.Diagnostics, diagnostic => diagnostic.Code == ImportCodes.LegacyBlocksDiffer);
    }

    [Fact]
    public async Task An_unreadable_workbook_fails_with_a_code_and_no_exception_text()
    {
        var path = Path.Combine(folder, "202609291449_SDB-VariantwiseSales.xlsx");
        await File.WriteAllTextAsync(path, "ZZSENTINEL-NAME this is not a workbook");

        var file = Assert.Single((await new SourceInspector().InspectAsync([folder])).Files);

        Assert.Equal(InspectionOutcome.Failed, file.Outcome);
        Assert.NotNull(file.FailureCode);
        Assert.All(file.Diagnostics, diagnostic => Assert.DoesNotContain("ZZSENTINEL", diagnostic.Message));
    }
}

/// <summary>
/// The inspector holds the folder import's composition (design 5.1); these pin it against the importer's own preparation,
/// <see cref="MatchedImportEnvelopeFactory"/> over the same bytes, for every synthetic sample.
/// </summary>
public sealed class SourceInspectorParityTests
{
    public static TheoryData<string> Samples()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample"), "*.xlsx").Order(StringComparer.Ordinal))
            data.Add(Path.GetFileName(path));
        return data;
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Staged_rows_scope_and_snapshot_blocks_equal_the_importer(string sample)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "etp-sample", sample);
        var expected = new MatchedImportEnvelopeFactory().Inspect(await new OpenXmlWorkbookReader().ReadAsync(path));

        var file = Assert.Single(await new SourceInspector().InspectFilesAsync([(path, sample)]));

        Assert.Equal(expected.MatchedProfile?.ReportCode, file.ReportCode);
        Assert.Equal(expected.StagedRows, file.StagedRows);
        Assert.Equal(expected.Accepted, file.Accepted is not null);
        Assert.Equal(expected.Diagnostics.Select(diagnostic => diagnostic.Code), file.Diagnostics.Take(expected.Diagnostics.Count).Select(diagnostic => diagnostic.Code));
        if (expected.AcceptedImport is not { } importer) return;
        var inspected = file.Accepted!;
        Assert.Equal(importer.Scope.StoreCode, inspected.Scope.StoreCode);
        Assert.Equal(importer.Scope.PeriodStart, inspected.Scope.PeriodStart);
        Assert.Equal(importer.Scope.PeriodEnd, inspected.Scope.PeriodEnd);
        Assert.Equal(importer.Scope.SnapshotBlocks, inspected.Scope.SnapshotBlocks);
        Assert.Equal(importer.Staging.Rows.Select(row => (row.SourceRowNumber, string.Join("|", row.Values.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")))),
            inspected.Staging.Rows.Select(row => (row.SourceRowNumber, string.Join("|", row.Values.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")))));
    }
}
