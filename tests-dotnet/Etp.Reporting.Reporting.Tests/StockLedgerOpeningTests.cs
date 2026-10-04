using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

// Report audit 3 Oct 2026: R-HEMW-02 (HEMW FIX-02) and R-WLMHW-05 (WLMHW FIX-05). The rows of one day are stored in
// export order, which is not time order, so the opening must come from the day's chain, not from the first row stored.
public sealed class StockLedgerOpeningTests
{
    private static readonly DateOnly May11 = new(2026, 5, 11);

    [Fact]
    public void Return_stored_before_the_earlier_sale_takes_the_balance_carried_into_the_day()
    {
        // ME3219I: received 16 Aug 2025; on 11 May 2026 the SR at 17:39 is stored before the INV at 12:21.
        var rows = new[]
        {
            Row(new(2025, 8, 16), 1, 0m, 1m),
            Row(May11, 2, 0m, 1m),
            Row(May11, 3, 1m, 0m)
        };

        Assert.Equal(1m, StockLedgerOpening.Resolve(rows, May11));
        Assert.Equal(1m, StockLedgerOpening.Resolve(rows, new(2026, 4, 1)));
    }

    [Fact]
    public void Period_starting_before_the_first_movement_takes_the_start_of_the_first_days_chain()
    {
        // CECRA26801: two STM receipts on 26 Sep 2024, the second (1 -> 2) stored first.
        var rows = new[]
        {
            Row(new(2024, 9, 26), 1, 1m, 2m),
            Row(new(2024, 9, 26), 2, 0m, 1m),
            Row(new(2025, 4, 17), 3, 2m, 0m)
        };

        Assert.Equal(0m, StockLedgerOpening.Resolve(rows, new(2024, 9, 16)));
        Assert.Equal(2m, StockLedgerOpening.Resolve(rows, new(2024, 10, 1)));
    }

    [Fact]
    public void Bill_cancellation_stored_before_the_invoices_of_another_document_starts_from_the_true_opening()
    {
        // 77196SL03 on 29 Aug: sold (1 -> 0), the bill cancelled (0 -> 1) and re-billed (1 -> 0); the BC row is stored first.
        var day = new DateOnly(2026, 8, 29);
        var rows = new[] { Row(day, 1, 0m, 1m), Row(day, 2, 1m, 0m), Row(day, 3, 1m, 0m) };

        Assert.Equal(1m, StockLedgerOpening.Resolve(rows, day));
        Assert.Equal(1m, StockLedgerOpening.Resolve(rows, new(2026, 7, 1)));
    }

    [Fact]
    public void Ledger_that_starts_mid_life_keeps_its_first_opening()
    {
        // The Titan ledger starts on 1 Jul 2026 with stock already on hand.
        var rows = new[] { Row(new(2026, 7, 3), 1, 3m, 2m) };

        Assert.Equal(3m, StockLedgerOpening.Resolve(rows, new(2026, 7, 1)));
    }

    [Fact]
    public void A_closed_loop_day_before_the_period_carries_the_last_known_end()
    {
        // NU95352WM01: sold on 10 Aug; on 14 Aug the INV is stored before its Purchase Receipt (a closed loop 0 -> 1 -> 0).
        var rows = new[]
        {
            Row(new(2026, 7, 3), 1, 0m, 1m),
            Row(new(2026, 8, 10), 2, 1m, 0m),
            Row(new(2026, 8, 14), 3, 1m, 0m),
            Row(new(2026, 8, 14), 4, 0m, 1m),
            Row(new(2026, 8, 20), 5, 0m, 1m)
        };

        Assert.Equal(0m, StockLedgerOpening.Resolve(rows, new(2026, 8, 20)));
        Assert.Equal(0m, StockLedgerOpening.Resolve(rows, new(2026, 8, 14)));
    }

    [Fact]
    public void Only_closed_loops_use_the_opening_that_agrees_with_the_reported_closing()
    {
        // NTC4040PP06 (Titan): on hand before the ledger starts, sold and returned on 7 Aug, sale stored first.
        var titan = new[] { Row(new(2026, 8, 7), 1, 1m, 0m), Row(new(2026, 8, 7), 2, 0m, 1m) };
        // G1372 (Helios): received and sold on one day, sale stored first, sold out at the To date.
        var helios = new[] { Row(new(2025, 1, 9), 1, 1m, 0m), Row(new(2025, 1, 9), 2, 0m, 1m) };

        Assert.Equal(1m, StockLedgerOpening.Resolve(titan, new(2026, 7, 1), reportedClosing: 1m));
        Assert.Equal(0m, StockLedgerOpening.Resolve(helios, new(2024, 9, 16), reportedClosing: 0m));
        // With no closing to compare, the first row as stored decides.
        Assert.Equal(1m, StockLedgerOpening.Resolve(helios, new(2024, 9, 16)));
    }

    [Fact]
    public void A_later_day_settles_a_closed_loop_so_a_real_gap_still_fails()
    {
        var rows = new[]
        {
            Row(new(2026, 8, 7), 1, 1m, 0m),
            Row(new(2026, 8, 7), 2, 0m, 1m),
            Row(new(2026, 9, 2), 3, 0m, 1m)
        };

        // The 2 Sep receipt starts from 0, so the item was at 0 throughout August whatever the snapshot says.
        Assert.Equal(0m, StockLedgerOpening.Resolve(rows, new(2026, 8, 1), reportedClosing: 1m));
    }

    [Fact]
    public void No_rows_give_no_opening()
    {
        Assert.Null(StockLedgerOpening.Resolve([], May11));
    }

    [Fact]
    public void Variance_is_zero_for_a_day_whose_rows_are_stored_in_reverse_time_order()
    {
        // The whole check, as HEMW FIX-02 asks: ME3219I for a period starting 11 May 2026, closing 1.
        var opening = StockLedgerOpening.Resolve(
            [Row(new(2025, 8, 16), 1, 0m, 1m), Row(May11, 2, 0m, 1m), Row(May11, 3, 1m, 0m)], May11, 1m);
        var result = new StockReconciliationService().Reconcile(
            [new("HEMW", "ME3219I", opening!.Value, 1m)],
            [new("HEMW", "ME3219I", "SR", 1m, true), new("HEMW", "ME3219I", "INV", -1m, true)],
            RetailReportingPolicy.Stock);

        Assert.Equal(ReconciliationStatus.Passed, result.Status);
        Assert.Equal(0m, Assert.Single(result.Items).Variance);
    }

    private static StockLedgerRow Row(DateOnly date, long id, decimal opening, decimal closing) => new(date, 1, id, opening, closing);
}
