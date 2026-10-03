using Etp.Reporting.Import.Stock;

namespace Etp.Reporting.Import.Tests;

public sealed class SnapshotItemSequencerTests
{
    private static readonly DateOnly Day = new(2026, 9, 29);

    [Fact]
    public void Repeated_identical_rows_get_distinct_line_seq()
    {
        var sequence = SnapshotItemSequencer.Assign([Row("ITEM-1", 1, 2), Row("ITEM-1", 1, 3), Row("ITEM-1", 1, 4)]);

        Assert.Equal([1, 2, 3], sequence.LineSeq);
    }

    [Fact]
    public void Rows_are_ordered_by_quantity_unit_cost_total_cost_then_source_row()
    {
        SnapshotItemRow[] rows =
        [
            Row("ITEM-1", 2, 2),
            Row("ITEM-1", 1, 3) with { UnitCost = 10m, TotalCost = 10m },
            Row("ITEM-1", 1, 4) with { UnitCost = null, TotalCost = 5m },
            Row("ITEM-1", 1, 5) with { UnitCost = 10m, TotalCost = null },
            Row("ITEM-1", 1, 6) with { UnitCost = 9m, TotalCost = 9m }
        ];

        // A missing cost sorts first, as SQL Server orders NULL.
        Assert.Equal([5, 4, 1, 3, 2], SnapshotItemSequencer.Assign(rows).LineSeq);
    }

    [Fact]
    public void The_order_of_input_rows_does_not_change_the_stored_multiset()
    {
        SnapshotItemRow[] rows =
        [
            Row("ITEM-1", 1, 2), Row("ITEM-1", 1, 3), Row("ITEM-1", 3, 4), Row("ITEM-2", 1, 5),
            Row("ITEM-1", 2, 6) with { SourceUid = "UID-1" }, Row("ITEM-1", 2, 7) with { SourceUid = "UID-1" },
            Row("ITEM-1", 1, 8) with { UnitCost = 4m }
        ];
        var expected = Stored(rows, SnapshotItemSequencer.Assign(rows));

        var random = new Random(20261001);
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var shuffled = rows.OrderBy(_ => random.Next()).Select((row, index) => row with { SourceRowNumber = index + 2 }).ToArray();
            Assert.Equal(expected, Stored(shuffled, SnapshotItemSequencer.Assign(shuffled)));
        }
    }

    [Fact]
    public void Identity_is_store_date_source_product_and_item_discriminator()
    {
        var baseline = Row("ITEM-1", 1, 2);
        SnapshotItemRow[] rows =
        [
            baseline,
            baseline with { StoreCode = "HEMW" },
            baseline with { SnapshotDate = Day.AddDays(-1) },
            baseline with { SourceReportCode = StockSnapshotSources.BinWise },
            baseline with { ProductCode = "ITEM-2" },
            baseline with { SourceUid = "UID-1" },
            baseline with { BatchNumber = "LOT-1" },
            baseline with { Ean = "8900000000001" }
        ];

        Assert.All(SnapshotItemSequencer.Assign(rows).LineSeq, lineSeq => Assert.Equal(1, lineSeq));
    }

    [Fact]
    public void Item_discriminator_is_uid_then_batch_then_ean_as_the_database_derives_it()
    {
        var uid = Row("ITEM-1", 1, 2) with { SourceUid = "X1", BatchNumber = "LOT-9", Ean = "E" };
        var batch = Row("ITEM-1", 1, 3) with { BatchNumber = "x1 " };
        var ean = Row("ITEM-1", 1, 4) with { Ean = "X1" };

        Assert.Equal("X1", uid.ItemDiscriminator);
        Assert.Equal("", Row("ITEM-1", 1, 5).ItemDiscriminator);
        // Equal under the case-insensitive collation: one identity, so they must not share line_seq 1.
        Assert.Equal([1, 2, 3], SnapshotItemSequencer.Assign([uid, batch, ean]).LineSeq);
    }

    [Fact]
    public void Snapshot_source_comes_from_the_lineage_record_type_when_not_given()
    {
        Assert.Equal("R010", StockSnapshotSources.FromLineageRecordType("R010_SNAPSHOT"));
        Assert.Equal("CLOSING_STOCK", StockSnapshotSources.FromLineageRecordType("CLOSING_STOCK"));
        Assert.Equal("CLOSING_STOCK", StockSnapshotSources.FromLineageRecordType(null));
    }

    private static SnapshotItemRow Row(string product, decimal quantity, int sourceRow) =>
        new("WLMHW", Day, StockSnapshotSources.ClosingStock, product, null, null, null, quantity, 100m, 100m * quantity, "Sheet0", sourceRow);

    private static string[] Stored(SnapshotItemRow[] rows, SnapshotItemSequence sequence) => rows
        .Select((row, index) => $"{row.ProductCode}|{row.ItemDiscriminator}|{row.Quantity}|{row.UnitCost}|{row.TotalCost}|{sequence.LineSeq[index]}")
        .Order(StringComparer.Ordinal).ToArray();
}
