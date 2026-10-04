namespace Etp.Reporting.Reporting;

/// <summary>
/// The movement status of one closing-stock item (owner answer Q8, decision 14): a recently received item is NEW, aged by
/// its receipt date, not "never sold". Receipts are the stock ledger's inward movements (Purchase Receipt, STM Receipt,
/// Stock Receipt) on or before the snapshot date.
/// </summary>
public static class StockAgeing
{
    public const string ZeroStock = "ZERO STOCK";
    public const string New = "NEW";
    public const string NeverSold = "NEVER SOLD";
    public const string Slow = "SLOW - 90+ DAYS";
    public const string Watch = "WATCH - 60+ DAYS";
    public const string Active = "ACTIVE";

    /// <summary>An item received fewer days ago than this is NEW. It reuses the WATCH band (60 days).</summary>
    public const int NewWindowDays = 60;
    public const int WatchDays = 60;
    public const int SlowDays = 90;

    /// <summary>The ledger's inward movement types that count as a receipt.</summary>
    public static IReadOnlyList<string> ReceiptTypes { get; } = ["Purchase Receipt", "STM Receipt", "Stock Receipt"];

    /// <summary>
    /// <list type="bullet">
    /// <item>Quantity 0: ZERO STOCK.</item>
    /// <item>The latest receipt is after the last sale (or there is no sale) and fewer than 60 days old: NEW.</item>
    /// <item>No sale: NEVER SOLD (aged by the receipt when there is one; age unknown when the ledger has no receipt).</item>
    /// <item>Otherwise the bands on days since the last sale: 90+ SLOW, 60+ WATCH, else ACTIVE.</item>
    /// </list>
    /// </summary>
    public static string Status(decimal quantity, DateOnly? lastSale, DateOnly? lastReceipt, DateOnly asOf)
    {
        if (quantity == 0) return ZeroStock;
        if (lastReceipt is { } receipt && (lastSale is null || receipt > lastSale) && Days(receipt, asOf) < NewWindowDays) return New;
        if (lastSale is not { } sale) return NeverSold;
        var days = Days(sale, asOf);
        return days >= SlowDays ? Slow : days >= WatchDays ? Watch : Active;
    }

    /// <summary>Whole days from <paramref name="from"/> to <paramref name="asOf"/>, or null without a date.</summary>
    public static int? DaysSince(DateOnly? from, DateOnly asOf) => from is { } date ? Days(date, asOf) : null;

    /// <summary>True for a status the Brand Stock "Slow Items" count includes: not ACTIVE, not NEW, not zero stock.</summary>
    public static bool IsSlow(decimal quantity, string status) => quantity != 0 && status is not (Active or New or ZeroStock);

    private static int Days(DateOnly from, DateOnly asOf) => asOf.DayNumber - from.DayNumber;
}
