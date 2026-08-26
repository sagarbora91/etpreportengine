using System.Text.Json;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public static class SqlReportingQueries
{
    public const string Sales = """
        SELECT i.transaction_date,i.store_code,i.document_number,l.line_identifier,l.product_code,
               COALESCE(l.source_brand_name,l.source_brand_code,p.brand_name),COALESCE(l.brand_segment,p.cluster),l.source_transaction_type,l.source_quantity,
               l.source_gross_amount,l.source_net_amount
        FROM dbo.sales_lines l
        JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
        OUTER APPLY
        (
          SELECT TOP(1) COALESCE(s.brand_name,s.brand_code) brand_name,s.cluster
          FROM dbo.stock_snapshots s
          WHERE s.store_code=i.store_code AND s.product_code=l.product_code
          ORDER BY s.snapshot_date DESC,s.stock_snapshot_id DESC
        ) p
        WHERE i.transaction_date>=@dateFrom AND i.transaction_date<=@dateTo
          AND (@storesJson IS NULL OR i.store_code IN (SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@storesJson)))
          AND (@segmentsJson IS NULL OR COALESCE(l.brand_segment,p.cluster) IN (SELECT CONVERT(nvarchar(100),[value]) FROM OPENJSON(@segmentsJson)))
          AND (@typesJson IS NULL OR l.source_transaction_type IN (SELECT CONVERT(nvarchar(80),[value]) FROM OPENJSON(@typesJson)))
          AND (@itemsJson IS NULL OR l.product_code IN (SELECT CONVERT(nvarchar(80),[value]) FROM OPENJSON(@itemsJson)))
        ORDER BY i.transaction_date,i.store_code,i.document_number,l.line_identifier;
        """;

    public const string Tenders = """
        SELECT i.store_code,i.document_number,t.tender_type,t.source_amount
        FROM dbo.reporting_sales_tenders t
        JOIN dbo.sales_invoices i ON i.sales_invoice_id=t.sales_invoice_id
        WHERE i.transaction_date>=@dateFrom AND i.transaction_date<=@dateTo
          AND (@storesJson IS NULL OR i.store_code IN (SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@storesJson)))
        ORDER BY i.store_code,i.document_number,t.sales_tender_id;
        """;

    public const string InvoiceControls = """
        SELECT i.store_code,i.document_number,c.source_net_value
        FROM dbo.sales_invoice_controls c
        JOIN dbo.sales_invoices i ON i.sales_invoice_id=c.sales_invoice_id
        WHERE i.transaction_date>=@dateFrom AND i.transaction_date<=@dateTo
          AND (@storesJson IS NULL OR i.store_code IN (SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@storesJson)))
        ORDER BY i.store_code,i.document_number,c.sales_invoice_control_id;
        """;

    public const string StockPositions = """
        WITH keys AS
        (
          SELECT DISTINCT m.store_code,m.product_code FROM dbo.stock_movements m
          WHERE m.document_date>=@dateFrom AND m.document_date<=@dateTo
            AND (@storesJson IS NULL OR store_code IN (SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@storesJson)))
            AND (@itemsJson IS NULL OR m.product_code IN (SELECT CONVERT(nvarchar(80),[value]) FROM OPENJSON(@itemsJson)))
            AND EXISTS(SELECT 1 FROM dbo.stock_snapshots s WHERE s.store_code=m.store_code
              AND s.product_code=m.product_code AND s.snapshot_date=@dateTo)
        )
        SELECT k.store_code,k.product_code,
               first_move.opening_quantity source_opening_quantity,
               (SELECT SUM(s.quantity) FROM dbo.stock_snapshots s WHERE s.store_code=k.store_code
                 AND s.product_code=k.product_code AND s.snapshot_date=@dateTo) source_closing_quantity
        FROM keys k
        OUTER APPLY(SELECT TOP(1) m.opening_quantity FROM dbo.stock_movements m
          WHERE m.store_code=k.store_code AND m.product_code=k.product_code
            AND m.document_date>=@dateFrom AND m.document_date<=@dateTo
          ORDER BY m.document_date,m.stock_movement_id) first_move
        ORDER BY k.store_code,k.product_code;
        """;

    public const string StockMovements = """
        SELECT m.store_code,m.product_code,m.source_transaction_type,SUM(m.transaction_quantity) source_signed_quantity
        FROM dbo.stock_movements m
        WHERE m.document_date>=@dateFrom AND m.document_date<=@dateTo
          AND (@storesJson IS NULL OR m.store_code IN (SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@storesJson)))
          AND (@itemsJson IS NULL OR m.product_code IN (SELECT CONVERT(nvarchar(80),[value]) FROM OPENJSON(@itemsJson)))
          AND EXISTS(SELECT 1 FROM dbo.stock_snapshots s WHERE s.store_code=m.store_code
            AND s.product_code=m.product_code AND s.snapshot_date=@dateTo)
        GROUP BY m.store_code,m.product_code,m.source_transaction_type
        ORDER BY m.store_code,m.product_code,m.source_transaction_type;
        """;
}

public sealed class SqlServerReportingQueryRepository(string connectionString) : IReportingQueryRepository
{
    public async Task<IReadOnlyList<SalesQueryRow>> LoadSalesAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default)
    {
        scope.Validate();
        await using var connection = await Open(cancellationToken);
        await using var command = Command(connection, SqlReportingQueries.Sales, scope);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<SalesQueryRow>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetFieldValue<DateOnly>(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), NullableString(reader, 5), NullableString(reader, 6), NullableString(reader, 7),
                reader.GetDecimal(8), NullableDecimal(reader, 9), NullableDecimal(reader, 10)));
        return rows;
    }

    public async Task<IReadOnlyList<TenderQueryRow>> LoadTendersAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default)
    {
        scope.Validate();
        await using var connection = await Open(cancellationToken);
        await using var command = Command(connection, SqlReportingQueries.Tenders, scope);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<TenderQueryRow>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3)));
        return rows;
    }

    public async Task<IReadOnlyList<InvoiceControlQueryRow>> LoadInvoiceControlsAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default)
    {
        scope.Validate();
        await using var connection = await Open(cancellationToken);
        await using var command = Command(connection, SqlReportingQueries.InvoiceControls, scope);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<InvoiceControlQueryRow>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetDecimal(2)));
        return rows;
    }

    public async Task<StockQueryData> LoadStockAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default)
    {
        scope.Validate();
        await using var connection = await Open(cancellationToken);
        var positions = new List<StockPositionQueryRow>();
        await using (var command = Command(connection, SqlReportingQueries.StockPositions, scope))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                positions.Add(new(reader.GetString(0), reader.GetString(1), NullableDecimal(reader, 2), NullableDecimal(reader, 3)));
        var movements = new List<StockMovementQueryRow>();
        await using (var command = Command(connection, SqlReportingQueries.StockMovements, scope))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                movements.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3)));
        return new(positions, movements);
    }

    private async Task<SqlConnection> Open(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("A SQL Server connection string is required.");
        var connection = new SqlConnection(connectionString);
        try { await connection.OpenAsync(token); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static SqlCommand Command(SqlConnection connection, string sql, ReportingQueryScope scope)
    {
        var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@dateFrom", scope.DateFrom);
        command.Parameters.AddWithValue("@dateTo", scope.DateTo);
        command.Parameters.AddWithValue("@storesJson", scope.StoreCodes is { Count: > 0 }
            ? JsonSerializer.Serialize(scope.StoreCodes.Distinct(StringComparer.OrdinalIgnoreCase)) : DBNull.Value);
        command.Parameters.AddWithValue("@segmentsJson", Json(scope.BrandSegments));
        command.Parameters.AddWithValue("@typesJson", Json(scope.TransactionTypes));
        command.Parameters.AddWithValue("@itemsJson", Json(scope.ItemCodes));
        return command;
    }

    private static object Json(IReadOnlyList<string>? values) => values is { Count: > 0 }
        ? JsonSerializer.Serialize(values.Distinct(StringComparer.OrdinalIgnoreCase)) : DBNull.Value;

    private static string? NullableString(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static decimal? NullableDecimal(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);
}
