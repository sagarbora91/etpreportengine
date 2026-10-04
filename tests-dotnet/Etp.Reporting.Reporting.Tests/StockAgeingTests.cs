using Etp.Reporting.Reporting;

namespace Etp.Reporting.Reporting.Tests;

/// <summary>Owner answer Q8 (decision 14): recently received items are NEW, aged by receipt date, not "never sold".</summary>
public sealed class StockAgeingTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 25);
    private static DateOnly DaysAgo(int days) => AsOf.AddDays(-days);

    [Fact]
    public void Zero_quantity_is_zero_stock_whatever_the_dates() =>
        Assert.Equal(StockAgeing.ZeroStock, StockAgeing.Status(0m, DaysAgo(200), DaysAgo(1), AsOf));

    [Fact]
    public void Received_recently_and_never_sold_is_new() =>
        Assert.Equal("NEW", StockAgeing.Status(1m, null, DaysAgo(5), AsOf));

    [Fact]
    public void Received_on_the_date_itself_is_new() =>
        Assert.Equal(StockAgeing.New, StockAgeing.Status(1m, null, AsOf, AsOf));

    [Fact]
    public void Restocked_after_an_old_sale_is_new() =>
        Assert.Equal(StockAgeing.New, StockAgeing.Status(2m, DaysAgo(100), DaysAgo(10), AsOf));

    [Fact]
    public void A_receipt_59_days_old_is_new_and_60_days_old_is_never_sold()
    {
        Assert.Equal(StockAgeing.New, StockAgeing.Status(1m, null, DaysAgo(59), AsOf));
        Assert.Equal(StockAgeing.NeverSold, StockAgeing.Status(1m, null, DaysAgo(60), AsOf));
    }

    [Fact]
    public void Received_long_ago_and_never_sold_is_never_sold_aged_by_the_receipt()
    {
        Assert.Equal(StockAgeing.NeverSold, StockAgeing.Status(1m, null, DaysAgo(120), AsOf));
        Assert.Equal(120, StockAgeing.DaysSince(DaysAgo(120), AsOf));
    }

    [Fact]
    public void No_sale_and_no_receipt_in_the_ledger_stays_never_sold_with_unknown_age()
    {
        Assert.Equal(StockAgeing.NeverSold, StockAgeing.Status(1m, null, null, AsOf));
        Assert.Null(StockAgeing.DaysSince(null, AsOf));
    }

    [Fact]
    public void A_sale_after_the_last_receipt_uses_the_sale_bands()
    {
        Assert.Equal(StockAgeing.Active, StockAgeing.Status(1m, DaysAgo(10), DaysAgo(20), AsOf));
        Assert.Equal(StockAgeing.Watch, StockAgeing.Status(1m, DaysAgo(60), DaysAgo(80), AsOf));
        Assert.Equal(StockAgeing.Slow, StockAgeing.Status(1m, DaysAgo(90), DaysAgo(95), AsOf));
    }

    [Fact]
    public void A_sale_on_the_receipt_day_is_not_new() =>
        Assert.Equal(StockAgeing.Active, StockAgeing.Status(1m, DaysAgo(3), DaysAgo(3), AsOf));

    [Fact]
    public void Restocked_long_ago_after_an_old_sale_keeps_the_sale_band() =>
        Assert.Equal(StockAgeing.Slow, StockAgeing.Status(1m, DaysAgo(200), DaysAgo(70), AsOf));

    [Fact]
    public void No_receipt_keeps_the_old_bands()
    {
        Assert.Equal("SLOW - 90+ DAYS", StockAgeing.Status(1m, DaysAgo(90), null, AsOf));
        Assert.Equal("WATCH - 60+ DAYS", StockAgeing.Status(1m, DaysAgo(60), null, AsOf));
        Assert.Equal("ACTIVE", StockAgeing.Status(-1m, DaysAgo(59), null, AsOf));
    }

    [Fact]
    public void Slow_item_count_leaves_out_new_active_and_zero_stock()
    {
        Assert.False(StockAgeing.IsSlow(1m, StockAgeing.New));
        Assert.False(StockAgeing.IsSlow(1m, StockAgeing.Active));
        Assert.False(StockAgeing.IsSlow(0m, StockAgeing.NeverSold));
        Assert.True(StockAgeing.IsSlow(1m, StockAgeing.NeverSold));
        Assert.True(StockAgeing.IsSlow(1m, StockAgeing.Watch));
        Assert.True(StockAgeing.IsSlow(1m, StockAgeing.Slow));
    }

    [Fact]
    public void Receipt_types_are_the_ledger_inward_types() =>
        Assert.Equal(["Purchase Receipt", "STM Receipt", "Stock Receipt"], StockAgeing.ReceiptTypes);
}
