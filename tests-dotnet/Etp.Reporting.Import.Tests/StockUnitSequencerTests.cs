using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;
using Etp.Reporting.Import.Stock;

namespace Etp.Reporting.Import.Tests;

public sealed class StockUnitSequencerTests
{
    private static readonly DateOnly Day = new(2026, 9, 6);

    [Fact]
    public void Shuffled_groups_with_openings_0_1_3_4_2_get_the_same_line_seq()
    {
        // The package's per-unit receipts: file order is not the running-balance order.
        decimal[] receipts = [0, 1, 3, 4, 2];
        // Per-unit sales of another document run downwards: the chain starts at the highest opening.
        decimal[] sales = [2, 4, 1, 3];
        var rows = receipts.Select(opening => Row("STM-9", "STM Receipt", opening, 1))
            .Concat(sales.Select(opening => Row("INV-7", "INV", opening, -1)))
            .ToArray();

        var expected = Sequence(rows);
        Assert.Equal([1, 2, 4, 5, 3], expected.Take(5).Select(x => x.LineSeq));
        Assert.Equal([3, 1, 4, 2], expected.Skip(5).Select(x => x.LineSeq));

        var random = new Random(20261001);
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var shuffled = rows.OrderBy(_ => random.Next()).Select((row, index) => row with { SourceRowNumber = index + 2 }).ToArray();
            var sequence = StockUnitSequencer.Assign(shuffled);
            Assert.Empty(sequence.Repeats);
            for (var i = 0; i < shuffled.Length; i++)
                Assert.Equal(LineSeqOf(expected, shuffled[i]), sequence.LineSeq[i]);
        }
    }

    [Fact]
    public void Chain_start_is_line_seq_one_for_receipts_and_issues()
    {
        var sequence = StockUnitSequencer.Assign([Row("A", "STM Receipt", 7, 1, 10), Row("A", "STM Receipt", 6, 1, 11),
            Row("B", "INV", 5, -1, 12), Row("B", "INV", 6, -1, 13)]);

        Assert.Equal([2, 1, 2, 1], sequence.LineSeq);
    }

    [Fact]
    public void Exact_repeats_are_kept_with_consecutive_line_seq_and_a_warning()
    {
        StockUnitRow[] rows =
        [
            Row("STM-9", "STM Receipt", 3, 1, 2),
            Row("STM-9", "STM Receipt", 5, 1, 3),
            Row("STM-9", "STM Receipt", 3, 1, 4),
            Row("STM-9", "STM Receipt", 4, 1, 5),
            Row("STM-9", "STM Receipt", 3, 1, 6)
        ];

        var sequence = StockUnitSequencer.Assign(rows);

        Assert.Equal([1, 5, 2, 4, 3], sequence.LineSeq);
        var repeat = Assert.Single(sequence.Repeats);
        Assert.Equal(3, repeat.Occurrences);
        Assert.Equal(1, repeat.FirstLineSeq);
        Assert.Equal([2, 4, 6], repeat.SourceRows);
        Assert.Equal("STM-9 2026-09-06 ITEM-1", repeat.DocumentRef);
        var warning = Assert.Single(sequence.Diagnostics);
        Assert.Equal(ImportCodes.StockRowRepeated, warning.Code);
        Assert.Equal(ImportDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(3, warning.Occurrences);
        Assert.Equal(2, warning.RowNumber);
        Assert.Equal("Sheet0", warning.SheetName);
        Assert.Contains("rows 2, 4, 6", warning.Message);
        Assert.DoesNotContain("STM-9", warning.Message);

        // Identical rows are interchangeable: any row order stores the same (row content, line_seq) multiset.
        var stored = Stored(rows, sequence);
        var reversed = rows.AsEnumerable().Reverse().Select((row, index) => row with { SourceRowNumber = index + 2 }).ToArray();
        Assert.Equal(stored, Stored(reversed, StockUnitSequencer.Assign(reversed)));
        Assert.Equal(3, Assert.Single(StockUnitSequencer.Assign(reversed).Repeats).Occurrences);
    }

    [Fact]
    public void Rows_differing_only_in_attributes_are_repeats_and_ordered_by_reference()
    {
        var first = Row("STM-9", "STM Receipt", 3, 1, 2) with { RefDocumentNumber = "REF-B" };
        var second = Row("STM-9", "STM Receipt", 3, 1, 3) with { RefDocumentNumber = "REF-A" };

        Assert.Equal([2, 1], StockUnitSequencer.Assign([first, second]).LineSeq);
        Assert.Equal([1, 2], StockUnitSequencer.Assign([second with { SourceRowNumber = 2 }, first with { SourceRowNumber = 3 }]).LineSeq);
        Assert.Equal(2, Assert.Single(StockUnitSequencer.Assign([first, second]).Repeats).Occurrences);
    }

    [Fact]
    public void Identity_is_compared_as_the_case_insensitive_database_compares_it()
    {
        StockUnitRow[] rows =
        [
            Row("stm-9", "STM Receipt", 0, 1, 2) with { FromLocation = null },
            Row("STM-9 ", "stm receipt", 1, 1, 3) with { FromLocation = "" },
            Row("STM-9", "STM Receipt", 2, 1, 4) with { FromLocation = "  ", StoreCode = "wlmhw" }
        ];

        Assert.Equal([1, 2, 3], StockUnitSequencer.Assign(rows).LineSeq);
    }

    [Fact]
    public void Each_identity_part_starts_its_own_chain()
    {
        var baseline = Row("STM-9", "STM Receipt", 0, 1, 2);
        StockUnitRow[] rows =
        [
            baseline,
            baseline with { StoreCode = "HEMW" },
            baseline with { FinancialYear = 2026 },
            baseline with { DocumentNumber = "STM-10" },
            baseline with { DocumentDate = Day.AddDays(1) },
            baseline with { ProductCode = "ITEM-2" },
            baseline with { SourceTransactionType = "Stock Receipt" },
            baseline with { FromLocation = "WAREHOUSE" },
            baseline with { ToLocation = "COUNTER" }
        ];

        var sequence = StockUnitSequencer.Assign(rows);

        Assert.All(sequence.LineSeq, lineSeq => Assert.Equal(1, lineSeq));
        Assert.Empty(sequence.Repeats);
    }

    [Fact]
    public void Generic_overload_reads_any_row_shape()
    {
        var rows = new[] { (Opening: 2m, Row: 2), (Opening: 1m, Row: 3) };

        var sequence = StockUnitSequencer.Assign(rows, x => Row("STM-9", "STM Receipt", x.Opening, 1, x.Row));

        Assert.Equal([2, 1], sequence.LineSeq);
        Assert.Empty(StockUnitSequencer.Assign(Array.Empty<StockUnitRow>()).LineSeq);
    }

    private static StockUnitRow Row(string document, string type, decimal opening, decimal transaction, int sourceRow = 2) =>
        new("WLMHW", 2027, document, Day, "ITEM-1", type, null, "STORE", opening, transaction, opening + transaction, "Sheet0", sourceRow);

    private static (StockUnitRow Row, int LineSeq)[] Sequence(StockUnitRow[] rows)
    {
        var numbered = rows.Select((row, index) => row with { SourceRowNumber = index + 2 }).ToArray();
        var sequence = StockUnitSequencer.Assign(numbered);
        return numbered.Select((row, index) => (row, sequence.LineSeq[index])).ToArray();
    }

    private static int LineSeqOf((StockUnitRow Row, int LineSeq)[] expected, StockUnitRow row) =>
        expected.Single(x => x.Row.DocumentNumber == row.DocumentNumber && x.Row.OpeningQuantity == row.OpeningQuantity).LineSeq;

    private static string[] Stored(StockUnitRow[] rows, StockUnitSequence sequence) => rows
        .Select((row, index) => $"{row.DocumentNumber}|{row.OpeningQuantity}|{row.TransactionQuantity}|{row.ClosingQuantity}|{sequence.LineSeq[index]}")
        .Order(StringComparer.Ordinal).ToArray();
}
