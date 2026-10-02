using Etp.Reporting.Import.Stock;

namespace Etp.Reporting.Import.Tests;

// Review 1.9.3, stock-snapshots finding 1: a per-unit identity stored before 0041 keeps one row on line 1.
public sealed class StockLineAlignmentTests
{
    [Fact]
    public void Stored_row_that_is_not_the_chain_start_keeps_its_line_and_the_rest_take_free_lines()
    {
        // Document 300000003 of the package: openings 1,2,0,3,4 in file order, chain start 0. Before 0041 the first
        // row in file order (opening 1) was stored, and the backfill left it on line 1.
        var incoming = Receipts((1, 2), (2, 3), (0, 1), (3, 4), (4, 5));

        var lines = StockLineAlignment.Align(incoming, [Receipt(1, 1)]);

        // Opening 1 meets its stored row on line 1; the other four are new, on lines 2-5 in chain order.
        Assert.Equal([1, 3, 2, 4, 5], lines);
    }

    [Fact]
    public void Issue_chain_is_aligned_the_same_way()
    {
        // Document 100001262: openings 4,6,2,5,3 in file order, chain start 6 (an issue runs downwards).
        var incoming = new[] { (4m, 3), (6m, 1), (2m, 5), (5m, 2), (3m, 4) }.Select(x => Issue(x.Item1, x.Item2)).ToArray();

        var lines = StockLineAlignment.Align(incoming, [Issue(4m, 1)]);

        Assert.Equal([1, 2, 5, 3, 4], lines);
    }

    [Fact]
    public void Lines_stay_as_they_are_when_the_stored_rows_agree_with_them()
    {
        var incoming = Receipts((0, 1), (1, 2), (2, 3));
        Assert.Equal([1, 2, 3], StockLineAlignment.Align(incoming, [Receipt(0, 1), Receipt(1, 2)]));
        Assert.Equal([1, 2, 3], StockLineAlignment.Align(incoming, []));
        // An identical repeat collapsed before 0041 is restored by the line the file gives it.
        var repeats = new[] { new StockLine(1, 1, 1, 2), new StockLine(2, 1, 1, 2) };
        Assert.Equal([1, 2], StockLineAlignment.Align(repeats, [new StockLine(1, 1, 1, 2)]));
    }

    [Fact]
    public void A_stored_row_the_file_does_not_contain_stays_a_conflict()
    {
        var incoming = Receipts((0, 1), (1, 2), (2, 3));
        // Opening 7 is in no incoming row: the values really differ, so line 1 is compared and conflicts.
        Assert.Equal([1, 2, 3], StockLineAlignment.Align(incoming, [Receipt(7, 1)]));
        // One stored row matches, the other does not: nothing is moved.
        Assert.Equal([1, 2, 3], StockLineAlignment.Align(incoming, [Receipt(1, 1), Receipt(9, 2)]));
    }

    [Fact]
    public void A_superset_export_keeps_the_stored_lines_and_adds_the_new_unit()
    {
        // Stored after 0041 as lines 1 and 2; a later export adds a unit before them.
        var incoming = Receipts((0, 1), (1, 2), (2, 3));
        Assert.Equal([3, 1, 2], StockLineAlignment.Align(incoming, [Receipt(1, 1), Receipt(2, 2)]));
    }

    [Fact]
    public void Quantities_compare_at_the_stored_four_decimal_places()
    {
        var incoming = new[] { new StockLine(1, 0m, 1m, 1m), new StockLine(2, 1.00004m, 1m, 2.00004m) };
        Assert.Equal([2, 1], StockLineAlignment.Align(incoming, [new StockLine(1, 1.0000m, 1m, 2.0000m)]));
    }

    [Fact]
    public void Identity_key_ignores_case_trailing_spaces_and_a_missing_location()
    {
        var day = new DateOnly(2026, 9, 6);
        Assert.Equal(
            StockLineAlignment.IdentityKey("wlmhw ", 2027, "doc-1", day, "item", "stm receipt", null, "LOC"),
            StockLineAlignment.IdentityKey("WLMHW", 2027, "DOC-1", day, "ITEM", "STM Receipt", "", "loc "));
        Assert.NotEqual(
            StockLineAlignment.IdentityKey("WLMHW", 2027, "DOC-1", day, "ITEM", "STM Receipt", null, "A"),
            StockLineAlignment.IdentityKey("WLMHW", 2027, "DOC-1", day, "ITEM", "STM Receipt", "A", null));
    }

    private static StockLine Receipt(decimal opening, int line) => new(line, opening, 1m, opening + 1m);

    private static StockLine Issue(decimal opening, int line) => new(line, opening, -1m, opening - 1m);

    private static StockLine[] Receipts(params (decimal Opening, int Line)[] rows) => rows.Select(x => Receipt(x.Opening, x.Line)).ToArray();
}
