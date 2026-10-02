using System.Data;
using Etp.Reporting.Import.Stock;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed partial class SqlServerTransactionalImportStore
{
    // Review 1.9.3 (stock-snapshots 1): an identity stored before 0041 holds one row that need not be the chain
    // start. Each per-unit group of the file is matched with the rows its identity already holds, inside the import
    // transaction and after any restatement has removed the previous file's rows (StockLineAlignment).
    private static async Task<IReadOnlyList<StockMovementPersistence>> AlignMovementLinesAsync(SqlConnection c, SqlTransaction t,
        IReadOnlyList<StockMovementPersistence> movements, CancellationToken token)
    {
        if (movements.Count < 2) return movements;
        var aligned = movements.ToArray();
        var groups = Enumerable.Range(0, movements.Count)
            .GroupBy(index => Key(movements[index]), StringComparer.Ordinal)
            .Where(group => group.Count() > 1);
        foreach (var group in groups)
        {
            var indexes = group.ToArray();
            var stored = await StoredLinesAsync(c, t, movements[indexes[0]], token);
            if (stored.Count == 0) continue;
            var lines = StockLineAlignment.Align(indexes.Select(index => Line(movements[index])).ToArray(), stored);
            for (var i = 0; i < indexes.Length; i++) aligned[indexes[i]] = movements[indexes[i]] with { LineSeq = lines[i] };
        }
        return aligned;
    }

    private static string Key(StockMovementPersistence x) => StockLineAlignment.IdentityKey(x.StoreCode, x.InvoiceYear, x.DocumentNumber,
        x.DocumentDate, x.ProductCode, x.SourceTransactionType, x.FromLocation, x.ToLocation);

    private static StockLine Line(StockMovementPersistence x) => new(x.LineSeq, x.OpeningQuantity, x.TransactionQuantity, x.ClosingQuantity);

    // The same identity predicate as persist_stock_movement.
    private static async Task<IReadOnlyList<StockLine>> StoredLinesAsync(SqlConnection c, SqlTransaction t, StockMovementPersistence x,
        CancellationToken token)
    {
        await using var q = Cmd(c, t, """
            SELECT line_seq,opening_quantity,transaction_quantity,closing_quantity FROM dbo.stock_movements
            WHERE store_code=@store AND invoice_year=@year AND document_number=@doc AND document_date=@date AND product_code=@product
              AND source_transaction_type=@type AND from_key=ISNULL(@from,N'') AND to_key=ISNULL(@to,N'')
            """);
        // varchar like the column, so the identity index is sought.
        q.Parameters.Add("@store", SqlDbType.VarChar, 30).Value = x.StoreCode; q.Parameters.AddWithValue("@year", x.InvoiceYear);
        q.Parameters.AddWithValue("@doc", x.DocumentNumber); q.Parameters.AddWithValue("@date", x.DocumentDate);
        q.Parameters.AddWithValue("@product", x.ProductCode); q.Parameters.AddWithValue("@type", x.SourceTransactionType);
        Add(q, "@from", x.FromLocation); Add(q, "@to", x.ToLocation);
        var rows = new List<StockLine>();
        await using var reader = await q.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            rows.Add(new(reader.GetInt32(0), reader.GetDecimal(1), reader.GetDecimal(2), reader.GetDecimal(3)));
        return rows;
    }
}
