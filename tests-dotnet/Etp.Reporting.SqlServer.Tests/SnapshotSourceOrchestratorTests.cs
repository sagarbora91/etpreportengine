using Etp.Reporting.Import.Preflight;
using Etp.Reporting.Import.Profiles;
using Etp.Reporting.Import.Stock;
using Etp.Reporting.Import.Workbooks;
using Etp.Reporting.Infrastructure.SqlServer;

namespace Etp.Reporting.SqlServer.Tests;

// Migration 0041 section D: every snapshot row reaches persist_stock_snapshot with its source and line_seq.
public sealed class SnapshotSourceOrchestratorTests
{
    private const string Hash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public async Task Closing_stock_rows_carry_their_source_and_a_line_seq_per_repeat()
    {
        var capture = new CaptureStore();
        await new StockSqlImportOrchestrator(capture).PersistAsync(ClosingStock(("SNAP-A", 1m), ("SNAP-B", 2m), ("SNAP-A", 1m)));

        var rows = capture.Package!.StockSnapshots;
        Assert.All(rows, row => Assert.Equal(StockSnapshotSources.ClosingStock, row.SourceReportCode));
        Assert.Equal([1, 1, 2], rows.Select(row => row.LineSeq));
        Assert.Equal([2, 3, 4], rows.Select(row => row.Lineage.SourceRowNumber));
    }

    [Fact]
    public async Task BinWise_rows_carry_R010_and_a_line_seq_per_repeat()
    {
        var capture = new CaptureStore();
        var accepted = new MatchedImportEnvelopeFactory().RequireAccepted(BinWise(("SNAP-A", 5m), ("SNAP-A", 5m), ("SNAP-B", 1m)));
        await new EtpFamilySqlImportOrchestrator(capture).PersistAsync(accepted);

        var rows = capture.Package!.StockSnapshots;
        Assert.All(rows, row => Assert.Equal(StockSnapshotSources.BinWise, row.SourceReportCode));
        Assert.All(rows, row => Assert.Equal(StockSnapshotSources.BinWiseLineageRecordType, row.Lineage.SourceRecordType));
        Assert.Equal([1, 2, 1], rows.Select(row => row.LineSeq));
    }

    [Fact]
    public void Line_seq_follows_quantity_and_cost_whatever_the_row_order()
    {
        StockSnapshotPersistence Row(int sourceRow, decimal quantity, decimal? unit) =>
            new("HEMW", new(2026, 8, 25), "SNAP-A", null, null, null, null, null, "LOT-1", null, quantity, unit, null,
                new("Sheet0", sourceRow, "CLOSING_STOCK"));
        var forward = StockSnapshotLines.Assign([Row(2, 2m, 10m), Row(3, 1m, null), Row(4, 1m, 9m)], StockSnapshotSources.ClosingStock);
        var backward = StockSnapshotLines.Assign([Row(2, 1m, 9m), Row(3, 1m, null), Row(4, 2m, 10m)], StockSnapshotSources.ClosingStock);

        Assert.Equal([3, 1, 2], forward.Select(row => row.LineSeq));
        Assert.Equal([2, 1, 3], backward.Select(row => row.LineSeq));
        Assert.Equal(Stored(forward), Stored(backward));
    }

    private static string[] Stored(IEnumerable<StockSnapshotPersistence> rows) =>
        rows.Select(row => $"{row.SourceReportCode}|{row.Quantity}|{row.UnitCost}|{row.LineSeq}").Order(StringComparer.Ordinal).ToArray();

    private static WorkbookSnapshot ClosingStock(params (string Item, decimal Quantity)[] items) => new("202608251500_R011_Closing_Stock.xlsx", 10, Hash,
        [new("Sheet0", 1, StockImportProfiles.ClosingStockHeaders, items.Select((item, i) => new WorkbookRow(i + 2, new object?[]
        {
            "HEMW", "Synthetic", "Retail", "Store", "Region", "State", "City", new DateTime(2026, 8, 25), item.Item, "HSN", "Description",
            "EAN-1", "BR", "Cluster", "U", item.Quantity, 10m, 10m * item.Quantity, "LOT-1", null
        }.Select(value => new WorkbookCell(value)).ToArray())).ToArray())]);

    private static WorkbookSnapshot BinWise(params (string Item, decimal Quantity)[] items)
    {
        var family = EtpReportFamilyRegistry.Resolve("R010");
        var columns = family.Headers.Select(header => family.Columns.Single(column => column.SourceHeader == header)).ToArray();
        WorkbookRow Row(int number, string item, decimal quantity) => new(number, columns.Select(column => new WorkbookCell(column.CanonicalField switch
        {
            "store_code" => "HEMW",
            "itemnumber" => item,
            "lotnumber" => "LOT-1",
            "uid" => null,
            "closingbalance" => quantity,
            "ucp" => 10m,
            "totalucp" => 10m * quantity,
            _ => column.DataType == Etp.Reporting.Domain.Imports.CanonicalDataType.Decimal ? (object)0m : "SYNTHETIC"
        })).ToArray());
        return new("202608251457_R010_BinWise_Stock.xlsx", 10, Hash,
            [new("Data", 1, family.Headers, items.Select((item, i) => Row(i + 2, item.Item, item.Quantity)).ToArray())]);
    }

    private sealed class CaptureStore : ITransactionalImportStore
    {
        public ImportPersistencePackage? Package { get; private set; }
        public Task<long> PersistAsync(ImportPersistencePackage package, CancellationToken cancellationToken = default) { Package = package; return Task.FromResult(42L); }
    }
}
