using System.Globalization;
using Etp.Reporting.Application.Imports;
using Etp.Reporting.Import.Diagnostics;

namespace Etp.Reporting.Import.Stock;

/// <summary>The identity and running-balance fields of one stock-ledger (R030) row, as the sequencer reads them.</summary>
public readonly record struct StockUnitRow(
    string StoreCode,
    int FinancialYear,
    string DocumentNumber,
    DateOnly DocumentDate,
    string ProductCode,
    string SourceTransactionType,
    string? FromLocation,
    string? ToLocation,
    decimal OpeningQuantity,
    decimal TransactionQuantity,
    decimal ClosingQuantity,
    string SheetName,
    int SourceRowNumber)
{
    public string? RefDocumentNumber { get; init; }
    public DateOnly? RefDocumentDate { get; init; }
}

/// <summary>
/// Rows equal on every Key and Fact field: all kept, with consecutive <c>line_seq</c> from <see cref="FirstLineSeq"/>.
/// <see cref="DocumentRef"/> holds document number, date and product code only.
/// </summary>
public sealed record StockRowRepeat(string DocumentRef, int FirstLineSeq, string SheetName, IReadOnlyList<int> SourceRows)
{
    public int Occurrences => SourceRows.Count;
}

/// <summary><see cref="LineSeq"/>[i] is the <c>line_seq</c> of input row i.</summary>
public sealed record StockUnitSequence(IReadOnlyList<int> LineSeq, IReadOnlyList<StockRowRepeat> Repeats)
{
    /// <summary>One <c>STOCK_ROW_REPEATED</c> warning per run of identical rows, with its row numbers and count.</summary>
    public IReadOnlyList<ImportDiagnostic> Diagnostics => Repeats.Select(repeat => new ImportDiagnostic(
        ImportCodes.StockRowRepeated, ImportDiagnosticSeverity.Warning,
        $"{repeat.Occurrences} identical stock-ledger rows (rows {RowList(repeat.SourceRows)}) were all kept, each with its own line. A per-unit row normally moves the balance; check the export.",
        repeat.SheetName, repeat.SourceRows[0])
    {
        DocumentRef = repeat.DocumentRef,
        Occurrences = repeat.Occurrences
    }).ToArray();

    // Issue messages are stored in 500 characters; ten row numbers locate the run.
    private static string RowList(IReadOnlyList<int> rows) => rows.Count <= 10
        ? string.Join(", ", rows)
        : $"{string.Join(", ", rows.Take(10))} and {rows.Count - 10} more";
}

/// <summary>
/// Gives each stock-ledger row its <c>line_seq</c> inside its identity (store, financial year, document, date, product,
/// type, from, to), in running-balance order (spec 7.2, StockUnitChain): opening quantity, descending when the
/// transaction quantity is negative and ascending otherwise; then closing quantity the same way; then
/// REF_DOCUMENTNUMBER, REF_DOCUMENTDATE, sheet and sheet row. The chain start is <c>line_seq</c> 1 whatever the row
/// order of the file, and rows equal on every Key and Fact field are kept with consecutive numbers and a
/// <c>STOCK_ROW_REPEATED</c> warning. Identity text is compared as SQL Server compares it under the database's
/// case-insensitive collation (case and trailing spaces ignored; a missing location equals a blank one), so two
/// rows can never get the same <c>UX_stock_movements_identity</c> key. Shared by both planners.
/// </summary>
public static class StockUnitSequencer
{
    public static StockUnitSequence Assign<T>(IReadOnlyList<T> rows, Func<T, StockUnitRow> describe)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(describe);
        return Assign(rows.Select(describe).ToArray());
    }

    public static StockUnitSequence Assign(IReadOnlyList<StockUnitRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var lineSeq = new int[rows.Count];
        var repeats = new List<StockRowRepeat>();
        foreach (var group in Enumerable.Range(0, rows.Count).GroupBy(index => IdentityKey(rows[index]), StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(index => rows[index], ChainOrder.Instance).ToArray();
            for (var position = 0; position < ordered.Length; position++) lineSeq[ordered[position]] = position + 1;
            for (var start = 0; start < ordered.Length;)
            {
                var end = start + 1;
                while (end < ordered.Length && SameQuantities(rows[ordered[start]], rows[ordered[end]])) end++;
                if (end - start > 1)
                {
                    var first = rows[ordered[start]];
                    repeats.Add(new(
                        $"{first.DocumentNumber.Trim()} {first.DocumentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {first.ProductCode.Trim()}",
                        start + 1, first.SheetName, ordered[start..end].Select(index => rows[index].SourceRowNumber).ToArray()));
                }
                start = end;
            }
        }
        return new(lineSeq, repeats.OrderBy(repeat => repeat.SheetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(repeat => repeat.SourceRows[0]).ToArray());
    }

    private static bool SameQuantities(StockUnitRow a, StockUnitRow b) =>
        a.OpeningQuantity == b.OpeningQuantity && a.TransactionQuantity == b.TransactionQuantity && a.ClosingQuantity == b.ClosingQuantity;

    private static string IdentityKey(StockUnitRow row) => string.Join('\u001f',
        SqlText(row.StoreCode), row.FinancialYear.ToString(CultureInfo.InvariantCulture), SqlText(row.DocumentNumber),
        row.DocumentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), SqlText(row.ProductCode),
        SqlText(row.SourceTransactionType), SqlText(row.FromLocation), SqlText(row.ToLocation));

    // Equal under Latin1_General_CI_AS for the identifiers ETP exports, which are ASCII.
    internal static string SqlText(string? value) => (value ?? "").TrimEnd().ToUpperInvariant();

    private sealed class ChainOrder : IComparer<StockUnitRow>
    {
        public static ChainOrder Instance { get; } = new();

        public int Compare(StockUnitRow a, StockUnitRow b)
        {
            var result = Directed(a, a.OpeningQuantity).CompareTo(Directed(b, b.OpeningQuantity));
            if (result == 0) result = Directed(a, a.ClosingQuantity).CompareTo(Directed(b, b.ClosingQuantity));
            // Keeps identical rows next to each other even when the transaction signs of one identity differ.
            if (result == 0) result = a.OpeningQuantity.CompareTo(b.OpeningQuantity);
            if (result == 0) result = a.TransactionQuantity.CompareTo(b.TransactionQuantity);
            if (result == 0) result = a.ClosingQuantity.CompareTo(b.ClosingQuantity);
            if (result == 0) result = string.CompareOrdinal(SqlText(a.RefDocumentNumber), SqlText(b.RefDocumentNumber));
            if (result == 0) result = Nullable.Compare(a.RefDocumentDate, b.RefDocumentDate);
            if (result == 0) result = string.Compare(a.SheetName, b.SheetName, StringComparison.OrdinalIgnoreCase);
            if (result == 0) result = a.SourceRowNumber.CompareTo(b.SourceRowNumber);
            return result;
        }

        private static decimal Directed(StockUnitRow row, decimal quantity) => row.TransactionQuantity < 0 ? -quantity : quantity;
    }
}
