using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Sources;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

public sealed class ContractUnderPlanner1Tests
{
    private const string Hash = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

    [Fact]
    public async Task Contract_workbook_dates_R010_blocks_and_ignores_excluded_sheet()
    {
        // Three stacked snapshots; ETP_Excluded maps two rows, and Snapshot History carries R010-shaped rows, neither of which
        // planner 1 imports: only the eight Data rows are packaged.
        var accepted = new MatchedImportEnvelopeFactory(["WLMHW"]).RequireAccepted(ContractBinWise());
        var capture = new CaptureStore();

        await new EtpFamilySqlImportOrchestrator(capture).PersistAsync(accepted, accepted.Scope.PeriodEnd, "WLMHW", "tester");

        Assert.Equal("Data", accepted.MatchedSheet.Name);
        Assert.Equal("WLMHW", accepted.Scope.StoreCode);
        var package = capture.Package!;
        Assert.Equal(8, package.StockSnapshots.Count);
        Assert.All(package.StockSnapshots, snapshot => Assert.Equal("Data", snapshot.Lineage.SheetName));
        Assert.Equal(
            [
                (2, new DateOnly(2026, 7, 2)), (3, new DateOnly(2026, 7, 2)), (4, new DateOnly(2026, 7, 2)),
                (5, new DateOnly(2026, 8, 7)), (6, new DateOnly(2026, 8, 7)), (7, new DateOnly(2026, 8, 7)),
                (8, new DateOnly(2026, 9, 29)), (9, new DateOnly(2026, 9, 29))
            ],
            package.StockSnapshots.Select(snapshot => (snapshot.Lineage.SourceRowNumber, snapshot.SnapshotDate)));
        Assert.Equal((new DateOnly(2026, 7, 2), new DateOnly(2026, 9, 29)), (package.Batch.PeriodStart, package.Batch.PeriodEnd));
        Assert.Equal((new DateOnly(2026, 7, 2), new DateOnly(2026, 9, 29)), (package.File.PeriodStart, package.File.PeriodEnd));
    }

    [Fact]
    public async Task Stacked_R010_blocks_get_their_dates()
    {
        // IF-020: a WLMHW-shaped legacy stacked R010 (tier 2), kept in a folder named for the latest snapshot only.
        var accepted = new MatchedImportEnvelopeFactory(["WLMHW"]).RequireAccepted(LegacyStackedBinWise());
        var capture = new CaptureStore();

        await new EtpFamilySqlImportOrchestrator(capture).PersistAsync(accepted, accepted.Scope.PeriodEnd, "WLMHW", "tester");

        Assert.All(accepted.Scope.SnapshotBlocks, block => Assert.Equal(SnapshotDateBasis.InfoBlock, block.Basis));
        var package = capture.Package!;
        Assert.Equal(
            [
                (2, new DateOnly(2026, 7, 2)), (3, new DateOnly(2026, 7, 2)), (4, new DateOnly(2026, 7, 2)),
                (5, new DateOnly(2026, 8, 7)), (6, new DateOnly(2026, 8, 7)), (7, new DateOnly(2026, 8, 7)),
                (8, new DateOnly(2026, 9, 29)), (9, new DateOnly(2026, 9, 29))
            ],
            package.StockSnapshots.Select(snapshot => (snapshot.Lineage.SourceRowNumber, snapshot.SnapshotDate)));
        Assert.Equal((new DateOnly(2026, 7, 2), new DateOnly(2026, 9, 29)), (package.Batch.PeriodStart, package.Batch.PeriodEnd));
    }

    [Fact]
    public async Task Partly_dated_contract_R010_beside_a_dated_sibling_is_refused_not_given_the_sibling_date()
    {
        var persistence = new CapturePersistence();
        var reader = new Reader(path => path.EndsWith("R010.xlsx")
            ? ContractBinWise(secondBlockDate: "") with { FileName = path, SourcePath = path, Sha256 = new string('f', 64) }
            : ClosingStock(new DateOnly(2026, 9, 29)) with { FileName = path });

        var summary = await new FolderImportService(persistence, reader).RunFilesAsync([@"F:\pack\R010.xlsx", @"F:\pack\R011.xlsx"], new("tester"));

        var refused = Assert.Single(summary.Files, file => file.ReportCode == "R010");
        Assert.Equal("Failed", refused.Status);
        Assert.Contains(refused.Diagnostics!, issue => issue.Code == ImportCodes.SnapshotDateUnknown && issue.SourceRow == 5);
        Assert.DoesNotContain(persistence.Requests, request => request.AcceptedImport.ProfileIdentity.ReportCode == "R010");
    }

    [Fact]
    public async Task Folder_import_of_a_contract_R010_uses_the_contract_store_and_block_dates()
    {
        var persistence = new CapturePersistence();
        var summary = await new FolderImportService(persistence, new Reader(_ => ContractBinWise(dataStore: null)), knownStores: [])
            .RunFilesAsync(["R010_BinWise_Stock.xlsx"], new("tester"));

        var file = Assert.Single(summary.Files);
        Assert.Equal("Imported", file.Status);
        Assert.Equal(("WLMHW", new DateOnly(2026, 7, 2), new DateOnly(2026, 9, 29)), (file.StoreCode, file.PeriodStart, file.PeriodEnd));
        Assert.Equal(new DateOnly(2026, 9, 29), Assert.Single(persistence.Requests).ExpectedBusinessDate);
    }

    [Fact]
    public async Task Multi_date_closing_stock_refused_with_SNAPSHOT_MULTIPLE_DATES()
    {
        var persistence = new CapturePersistence();
        var summary = await new FolderImportService(persistence, new Reader(_ => ClosingStock(new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 29))))
            .RunFilesAsync(["R011_Closing_Stock.xlsx"], new("tester"));

        var file = Assert.Single(summary.Files);
        Assert.Equal("Failed", file.Status);
        var issue = Assert.Single(file.Diagnostics!, issue => issue.Code == ImportCodes.SnapshotMultipleDates);
        Assert.Equal(ImportIssueSeverity.Blocker, issue.Severity);
        Assert.Contains("2026-08-25, 2026-09-29", issue.Message);
        Assert.Empty(persistence.Requests);
    }

    [Fact]
    public async Task Single_date_closing_stock_still_imports()
    {
        var persistence = new CapturePersistence();
        var summary = await new FolderImportService(persistence, new Reader(_ => ClosingStock(new DateOnly(2026, 9, 29), new DateOnly(2026, 9, 29))))
            .RunFilesAsync(["R011_Closing_Stock.xlsx"], new("tester"));

        Assert.Equal("Imported", Assert.Single(summary.Files).Status);
    }

    [Fact]
    public async Task Undated_R010_takes_its_siblings_end_date_only_when_they_agree()
    {
        var persistence = new CapturePersistence();
        var reader = new Reader(path => path.EndsWith("R010.xlsx") ? BinWise(path) : ClosingStock(new DateOnly(2026, 9, 29)) with { FileName = path });
        var agreed = await new FolderImportService(persistence, reader).RunFilesAsync([@"F:\pack\R010.xlsx", @"F:\pack\R011.xlsx"], new("tester"));

        var dated = Assert.Single(agreed.Files, file => file.ReportCode == "R010");
        Assert.Equal("Imported", dated.Status);
        Assert.Equal(new DateOnly(2026, 9, 29), dated.PeriodEnd);
        Assert.Contains(dated.Diagnostics!, issue => issue.Code == ImportCodes.SnapshotDateFromSiblings && issue.Severity == ImportIssueSeverity.Warning);
        Assert.DoesNotContain(dated.Diagnostics!, issue => issue.Code == ImportCodes.SnapshotDateFromFolder);

        var disagreeing = new Reader(path => path.EndsWith("R010.xlsx") ? BinWise(path)
            : ClosingStock(path.EndsWith("a.xlsx") ? new DateOnly(2026, 9, 28) : new DateOnly(2026, 9, 29)) with { FileName = path, Sha256 = path.EndsWith("a.xlsx") ? new string('e', 64) : Hash });
        var refused = await new FolderImportService(new CapturePersistence(), disagreeing)
            .RunFilesAsync([@"F:\pack\R010.xlsx", @"F:\pack\R011a.xlsx", @"F:\pack\R011b.xlsx"], new("tester"));
        var ambiguous = Assert.Single(refused.Files, file => file.ReportCode == "R010");
        Assert.Equal("Failed", ambiguous.Status);
        Assert.Contains("end on different dates", ambiguous.Message);

        var alone = await new FolderImportService(new CapturePersistence(), new Reader(BinWise)).RunFilesAsync([@"F:\pack\R010.xlsx"], new("tester"));
        Assert.Equal("Failed", Assert.Single(alone.Files).Status);
        Assert.Contains("snapshot date could not be found", alone.Files[0].Message);
    }

    [Fact]
    public async Task Undated_R010_dated_only_by_the_override_records_that_basis()
    {
        var persistence = new CapturePersistence();
        var summary = await new FolderImportService(persistence, new Reader(BinWise)).RunFilesAsync([@"F:\pack\R010.xlsx"],
            new("tester", RestatementEnabled: true, RestatementReason: "Owner dated it", OverrideBusinessDate: new DateOnly(2026, 9, 29)));

        var file = Assert.Single(summary.Files);
        Assert.Equal("Imported", file.Status);
        Assert.Contains(file.Diagnostics!, issue => issue.Code == ImportCodes.SnapshotDateFromOverride && issue.Severity == ImportIssueSeverity.Warning);
        Assert.DoesNotContain(file.Diagnostics!, issue => issue.Code == ImportCodes.SnapshotDateFromSiblings);
    }

    private static WorkbookSnapshot BinWise(string path) => new(Path.GetFileName(path), 1, new string('f', 64),
        [BinWiseData(2, "WLMHW")]);

    private static WorkbookSheet BinWiseData(int rows, string? store)
    {
        var headers = EtpReportFamilyRegistry.Resolve("R010").Headers;
        return new("Data", 1, headers, Enumerable.Range(2, rows).Select(number => new WorkbookRow(number, headers.Select(header => new WorkbookCell(header switch
        {
            "STORE CODE" => store, "ITEMNUMBER" => $"SYN-ITEM-{number:0000}", "CLOSINGBALANCE" => 1m, _ => null
        })).ToArray())).ToArray());
    }

    private static WorkbookRow Row(int number, params string[] cells) => new(number, cells.Select(value => new WorkbookCell(value, value)).ToArray());

    private static string[] Block(int block, int first, int last, string date) =>
        [block.ToString(), "Data", first.ToString(), last.ToString(), (last - first + 1).ToString(), $"20260{block}011200_BinWise-Stock.xlsx", "xlsx",
         new string('a', 63) + block, date.Length == 0 ? "" : $"{date}T12:00", "", "", "none", date, (last - first + 1).ToString(), "0", "0", "complete", "Snapshot retained"];

    private static readonly string[] LegacyHeader =
        ["Package", "Source file", "Raw rows", "Rows retained", "Rows excluded", "Period from", "Period to", "Data row block", "Disposition"];

    private static string[] LegacyBlock(string sourceFile, int first, int last) =>
        ["Synthetic pack", sourceFile, (last - first + 1).ToString(), (last - first + 1).ToString(), "0", "", "", $"{first}:{last}", "Snapshot retained"];

    private static WorkbookSnapshot LegacyStackedBinWise() => new("R010_BinWise_Stock.xlsx", 1, Hash,
    [
        BinWiseData(8, "WLMHW"),
        new("Info", 1, ["ETP Consolidation - R010 BinWise-Stock"],
        [
            Row(2, "Updated by pack 01/07-29/09/2026", "Rule: snapshot"), Row(4, "Family ID", "R010"), Row(5, "Coverage", "No dated rows"),
            Row(7, LegacyHeader),
            Row(8, LegacyBlock("202607021446_BinWise-Stock - BinWise-Stock.xlsx", 2, 4)),
            Row(9, LegacyBlock("202608071848_BinWise-Stock - BinWise-Stock.xlsx", 5, 7)),
            Row(10, LegacyBlock("202609291433_BinWise-Stock - BinWise-Stock.xlsx", 8, 9)),
            Row(12, "Previous build 2026-12-31 noted by hand.")
        ])
    ], @"F:\Consolidated data import package (29 Sep 2026)\Retail\WLMHW\R010_BinWise_Stock.xlsx");

    private static WorkbookSnapshot ContractBinWise(string? dataStore = "WLMHW", string secondBlockDate = "2026-08-07")
    {
        (string, string)[] keys =
        [
            ("family_code", "R010"), ("store_code", "WLMHW"), ("business_unit", "RETAIL"), ("rule", "snapshot"), ("data_sheet", "Data"),
            ("header_row", "1"), ("data_rows", "8"), ("excluded_sheet", "ETP_Excluded"), ("excluded_rows", "2"), ("block_count", "3"),
            ("built_at", "2026-10-05T10:15:00+05:30"), ("builder", "synthetic-builder 2.0")
        ];
        var rows = keys.Select((key, index) => Row(index + 2, key.Item1, key.Item2)).ToList();
        var header = keys.Length + 3;
        rows.Add(Row(header, [.. ConsolidationContractLayout.BlockTableColumns]));
        rows.Add(Row(header + 1, Block(1, 2, 4, "2026-07-02")));
        rows.Add(Row(header + 2, Block(2, 5, 7, secondBlockDate)));
        rows.Add(Row(header + 3, Block(3, 8, 9, "2026-09-29")));
        rows.Add(Row(header + 5, "Notes: the 2026-12-31 build replaced 20261230."));
        var info = new WorkbookSheet("Info", 1, ["etp_contract", "1"], rows);
        var excluded = new WorkbookSheet("ETP_Excluded", 1, ["block", "sheet", "row"], [Row(2, "2", "Data", "3"), Row(3, "3", "Data", "6")]);
        // Real data headers, so only the preflight skip (not a header mismatch) keeps planner 1 off this sheet.
        var history = BinWiseData(3, "WLMHW") with { Name = ConsolidationContractLayout.HistorySheet };
        return new("R010_BinWise_Stock.xlsx", 1, Hash, [history, BinWiseData(8, dataStore), info, excluded],
            @"F:\Consolidated data import package (31 Dec 2026)\R010_BinWise_Stock.xlsx");
    }

    private static WorkbookSnapshot ClosingStock(params DateOnly[] dates) => new("R011_Closing_Stock.xlsx", 1, Hash,
    [
        new("Closing Stock", 1, StockImportProfiles.ClosingStockHeaders, dates.Select((date, index) => new WorkbookRow(index + 2,
            new object?[] { "WLMHW", "Store", "Retail", "Store", "Region", "State", "City", date.ToDateTime(TimeOnly.MinValue), $"ITEM-{index}", "HSN",
                "Description", "EAN", "BR", "Cluster", "U", 3m, 10m, 30m, null, null }.Select(value => new WorkbookCell(value)).ToArray())).ToArray())
    ]);

    private sealed class CaptureStore : ITransactionalImportStore
    {
        public ImportPersistencePackage? Package { get; private set; }
        public Task<long> PersistAsync(ImportPersistencePackage package, CancellationToken cancellationToken = default)
        {
            Package = package;
            return Task.FromResult(42L);
        }
    }

    private sealed class Reader(Func<string, WorkbookSnapshot> read) : IWorkbookReader
    {
        public Task<WorkbookSnapshot> ReadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(read(path));
    }

    private sealed class CapturePersistence : IImportPersistenceUseCase<MatchedImportEnvelope>
    {
        public List<ImportPersistenceRequest<MatchedImportEnvelope>> Requests { get; } = [];
        public Task<bool> ExistsByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<long?> FindCurrentImportFileIdAsync(string report, string store, DateOnly date, CancellationToken cancellationToken = default) => Task.FromResult<long?>(null);
        public Task PrepareRestatementAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ImportPersistenceResult> PersistAsync(ImportPersistenceRequest<MatchedImportEnvelope> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ImportPersistenceResult(request.AcceptedImport.ProfileIdentity.ReportCode, request.AcceptedImport.Staging.Rows.Count)
                { Status = "Imported" });
        }
        public Task<ImportRowOutcome> LoadOutcomeByHashAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult(new ImportRowOutcome(0, 0, 0, 0));
    }
}
