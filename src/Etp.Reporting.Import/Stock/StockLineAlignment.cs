using System.Globalization;

namespace Etp.Reporting.Import.Stock;

/// <summary>One stock-ledger row of an identity: its <c>line_seq</c> and the three quantities that persist_stock_movement compares.</summary>
public readonly record struct StockLine(int LineSeq, decimal Opening, decimal Transaction, decimal Closing)
{
    // persist_stock_movement compares decimal(19,4) values; SQL Server rounds a wider value half away from zero.
    internal (decimal, decimal, decimal) Content => (Round(Opening), Round(Transaction), Round(Closing));

    private static decimal Round(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Matches an incoming per-unit group with the rows its identity already holds, before persist_stock_movement
/// matches them by <c>line_seq</c> (review 1.9.3, stock-snapshots finding 1).
/// <para>
/// Before migration 0041 an identity kept one row: the first row of the file, which need not be the chain start.
/// 0041 leaves that row at <c>line_seq</c> 1, but <see cref="StockUnitSequencer"/> gives line 1 to the chain start,
/// so an import of the full group would log the chain start as a CONFLICT and insert the stored row's twin again.
/// When the stored rows sit on lines whose incoming row differs, and every stored row is matched by a distinct
/// incoming row with the same quantities, each matched incoming row takes its stored row's line (ALREADY_PRESENT)
/// and the other incoming rows take the free lines in chain order (NEW). Otherwise the lines stay as they are,
/// so a group whose stored values really differ is still a CONFLICT.
/// </para>
/// </summary>
public static class StockLineAlignment
{
    /// <summary>The identity of a stock-ledger row as SQL Server compares it (see <see cref="StockUnitSequencer"/>).</summary>
    public static string IdentityKey(string storeCode, int financialYear, string documentNumber, DateOnly documentDate,
        string productCode, string sourceTransactionType, string? fromLocation, string? toLocation) => string.Join('\u001f',
        StockUnitSequencer.SqlText(storeCode), financialYear.ToString(CultureInfo.InvariantCulture), StockUnitSequencer.SqlText(documentNumber),
        documentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StockUnitSequencer.SqlText(productCode),
        StockUnitSequencer.SqlText(sourceTransactionType), StockUnitSequencer.SqlText(fromLocation), StockUnitSequencer.SqlText(toLocation));

    /// <summary>The <c>line_seq</c> to persist for each incoming row of one identity, in input order.</summary>
    public static int[] Align(IReadOnlyList<StockLine> incoming, IReadOnlyList<StockLine> stored)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(stored);
        var unchanged = incoming.Select(row => row.LineSeq).ToArray();
        if (stored.Count == 0) return unchanged;
        var byLine = Enumerable.Range(0, incoming.Count).ToLookup(index => incoming[index].LineSeq);
        if (!stored.Any(row => byLine[row.LineSeq].Any(index => incoming[index].Content != row.Content))) return unchanged;

        var assigned = new int?[incoming.Count];
        var pending = new List<StockLine>();
        // A stored row keeps the incoming row on its own line when they agree.
        foreach (var row in stored.OrderBy(row => row.LineSeq))
        {
            var same = byLine[row.LineSeq].Where(index => assigned[index] is null && incoming[index].Content == row.Content).ToArray();
            if (same.Length > 0) assigned[same[0]] = row.LineSeq;
            else pending.Add(row);
        }
        foreach (var row in pending)
        {
            var match = Enumerable.Range(0, incoming.Count)
                .Where(index => assigned[index] is null && incoming[index].Content == row.Content)
                .OrderBy(index => incoming[index].LineSeq).ThenBy(index => index).Take(1).ToArray();
            // A stored row the file does not contain: its values differ, which only a restatement settles.
            if (match.Length == 0) return unchanged;
            assigned[match[0]] = row.LineSeq;
        }

        var taken = stored.Select(row => row.LineSeq).ToHashSet();
        var next = 1;
        foreach (var index in Enumerable.Range(0, incoming.Count).Where(index => assigned[index] is null)
                     .OrderBy(index => incoming[index].LineSeq).ThenBy(index => index).ToArray())
        {
            while (taken.Contains(next)) next++;
            assigned[index] = next;
            taken.Add(next);
        }
        return assigned.Select(line => line!.Value).ToArray();
    }
}
