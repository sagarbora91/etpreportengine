namespace Etp.Reporting.Reporting;

/// <summary>One stock ledger (R030) row of one store and item, as stored: opening + transaction = closing.</summary>
public sealed record StockLedgerRow(DateOnly DocumentDate, int LineSeq, long SequenceId, decimal OpeningQuantity, decimal ClosingQuantity);

/// <summary>
/// The ledger balance of one store and item at the start of a date (report audit 3 Oct 2026, R-HEMW-02 / R-WLMHW-05).
/// <para>
/// The rows of one day are not stored in time order: the export lists a later movement first on some days (an SR at
/// 17:39 before the INV at 12:21, a BC before the INV rows of another document). So the opening is never "the first row
/// as stored". Each day's rows form a chain (each row's opening is the previous row's closing), and the start and end of
/// that chain are found from the rows themselves: the start is the quantity that is an opening one more time than it is
/// a closing, the end the quantity that is a closing one more time than it is an opening.
/// </para>
/// <list type="number">
/// <item>The closing of the last day before <c>from</c> whose chain end is known (the ledger balance carried into the period).</item>
/// <item>Otherwise the opening of the first day on or after <c>from</c> whose chain start is known; the days between are closed loops
/// that leave the balance unchanged.</item>
/// <item>Otherwise every day of the item is a closed loop (for example sold and returned on one day), so the rows cannot tell the
/// opening: the opening on the first day from <c>from</c> that equals the reported closing, when one does; else the first row as stored.</item>
/// </list>
/// The ledger need not start at store opening (the Titan ledger starts on 1 Jul 2026), so the opening is never assumed to be 0.
/// </summary>
public static class StockLedgerOpening
{
    /// <param name="rows">Every stored ledger row of one store and item, any order and any date.</param>
    /// <param name="from">The first day of the period.</param>
    /// <param name="reportedClosing">The closing-stock quantity the period is compared with, used only to settle a closed loop.</param>
    /// <returns>The balance at the start of <paramref name="from"/>, or null when there are no rows.</returns>
    public static decimal? Resolve(IEnumerable<StockLedgerRow> rows, DateOnly from, decimal? reportedClosing = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var days = rows.GroupBy(row => row.DocumentDate)
            .OrderBy(day => day.Key)
            .Select(day => new LedgerDay(day.Key, day.OrderBy(row => row.LineSeq).ThenBy(row => row.SequenceId).ToArray()))
            .ToArray();
        if (days.Length == 0) return null;

        if (days.LastOrDefault(day => day.Date < from && day.End is not null) is { } before)
            return before.End;
        if (days.FirstOrDefault(day => day.Date >= from && day.Start is not null) is { } after)
            return after.Start;

        var loop = days.FirstOrDefault(day => day.Date >= from) ?? days[^1];
        return reportedClosing is { } closing && loop.Rows.Any(row => row.OpeningQuantity == closing)
            ? closing
            : loop.Rows[0].OpeningQuantity;
    }

    private sealed class LedgerDay
    {
        public LedgerDay(DateOnly date, IReadOnlyList<StockLedgerRow> rows)
        {
            Date = date;
            Rows = rows;
            var balance = new Dictionary<decimal, int>();
            foreach (var row in rows)
            {
                balance[row.OpeningQuantity] = balance.GetValueOrDefault(row.OpeningQuantity) + 1;
                balance[row.ClosingQuantity] = balance.GetValueOrDefault(row.ClosingQuantity) - 1;
            }
            // A broken chain can leave more than one candidate; the row order as stored decides between them.
            Start = rows.Where(row => balance[row.OpeningQuantity] > 0).Select(row => (decimal?)row.OpeningQuantity).FirstOrDefault();
            End = rows.Where(row => balance[row.ClosingQuantity] < 0).Select(row => (decimal?)row.ClosingQuantity).LastOrDefault();
        }

        public DateOnly Date { get; }
        public IReadOnlyList<StockLedgerRow> Rows { get; }
        public decimal? Start { get; }
        public decimal? End { get; }
    }
}
