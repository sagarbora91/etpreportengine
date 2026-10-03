using System.Text.Json;
using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

public static class SqlReportingQueries
{
    public const string Sales = """
        SELECT i.transaction_date,i.store_code,i.document_number,l.line_identifier,l.product_code,
               COALESCE(l.source_brand_name,l.source_brand_code,p.brand_name),COALESCE(l.brand_segment,p.cluster),l.source_transaction_type,l.source_quantity,
               l.source_gross_amount,l.source_net_amount,i.invoice_year
        FROM dbo.sales_lines l
        JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
        OUTER APPLY
        (
          SELECT TOP(1) COALESCE(s.brand_name,s.brand_code) brand_name,s.cluster
          FROM dbo.v_stock_snapshots_effective s
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

    /// <summary>Source tender code given to the TC tender read from R020 (decision 13 Q3).</summary>
    public const string R020TcTenderCode = "TC_R020";

    /// <summary>
    /// TC tenders Titan's R022 does not carry: R020 CHEQUEAMOUNT with a blank AGENCYNAME, taken only for an invoice whose
    /// R022 tenders fall short of its NetValue by exactly that amount, so R022 cover is never counted twice (decision 13 Q3, Titan audit FIX-03).
    /// Columns: sales_invoice_id, tender_type, source_amount.
    /// </summary>
    public const string R020TcTenders = """
        SELECT i.sales_invoice_id,CONVERT(nvarchar(80),N'TC_R020') tender_type,tc.amount source_amount
        FROM
        (
          SELECT rf.store_code,CONVERT(nvarchar(80),LTRIM(RTRIM(r.invnumber))) invnumber,r.invdate,MAX(r.chequeamount) amount
          FROM dbo.etp_r020 r JOIN dbo.import_files rf ON rf.import_file_id=r.import_file_id AND rf.report_code='R020' AND rf.is_superseded=0
          WHERE NULLIF(LTRIM(RTRIM(r.agencyname)),N'') IS NULL AND r.chequeamount<>0 AND r.invdate IS NOT NULL AND r.invnumber IS NOT NULL
          GROUP BY rf.store_code,CONVERT(nvarchar(80),LTRIM(RTRIM(r.invnumber))),r.invdate
        ) tc
        JOIN dbo.sales_invoices i ON i.store_code=tc.store_code AND i.document_number=tc.invnumber AND i.transaction_date=tc.invdate
        WHERE tc.amount=(SELECT SUM(c.source_net_value) FROM dbo.sales_invoice_controls c WHERE c.sales_invoice_id=i.sales_invoice_id)
                       -COALESCE((SELECT SUM(x.source_amount) FROM dbo.reporting_sales_tenders x WHERE x.sales_invoice_id=i.sales_invoice_id),0)
        """;

    /// <summary>Reporting tenders plus the R020 TC tenders. Columns: sales_tender_id (null for R020), sales_invoice_id, tender_type, source_amount.</summary>
    public const string EffectiveTenders = """
        SELECT sales_tender_id,sales_invoice_id,tender_type,source_amount FROM dbo.reporting_sales_tenders
        UNION ALL
        SELECT CONVERT(bigint,NULL),tc.sales_invoice_id,tc.tender_type,tc.source_amount FROM (
        """ + R020TcTenders + """
        ) tc
        """;

    /// <summary>Maps a source tender code to its cash book mode; TC_R020 is TC unless tender_modes says otherwise.</summary>
    public const string TenderModeExpression = "COALESCE(m.mode,CASE WHEN t.tender_type=N'TC_R020' THEN 'TC' END,CONCAT('Unmapped: ',t.tender_type))";

    public const string Tenders = """
        SELECT i.store_code,i.document_number,
        """ + TenderModeExpression + """
        ,t.source_amount,i.invoice_year
        FROM (
        """ + EffectiveTenders + """
        ) t
        JOIN dbo.sales_invoices i ON i.sales_invoice_id=t.sales_invoice_id
        LEFT JOIN dbo.tender_modes m ON m.source_tender_code=t.tender_type AND m.active=1
        WHERE i.transaction_date>=@dateFrom AND i.transaction_date<=@dateTo
          AND (@storesJson IS NULL OR i.store_code IN (SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@storesJson)))
        ORDER BY i.store_code,i.document_number,t.sales_tender_id;
        """;

    public const string InvoiceControls = """
        SELECT i.store_code,i.document_number,c.source_net_value,i.invoice_year
        FROM dbo.sales_invoice_controls c
        JOIN dbo.sales_invoices i ON i.sales_invoice_id=c.sales_invoice_id
        WHERE i.transaction_date>=@dateFrom AND i.transaction_date<=@dateTo
          AND (@storesJson IS NULL OR i.store_code IN (SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@storesJson)))
        ORDER BY i.store_code,i.document_number,c.sales_invoice_control_id;
        """;

    public const string TenderCoverageGaps = """
        WITH sales_days AS
        (
          SELECT DISTINCT i.store_code,i.transaction_date
          FROM dbo.sales_lines l
          JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
          JOIN dbo.source_lineage sl ON sl.source_lineage_id=l.source_lineage_id
          JOIN dbo.import_files sf ON sf.import_file_id=sl.import_file_id AND sf.is_superseded=0
          WHERE i.transaction_date>=@dateFrom AND i.transaction_date<=@dateTo
            AND (@storesJson IS NULL OR i.store_code IN (SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@storesJson)))
        )
        SELECT d.store_code,d.transaction_date
        FROM sales_days d
        WHERE NOT EXISTS(SELECT 1 FROM dbo.import_files f WHERE f.store_code=d.store_code AND f.report_code='R022'
          AND f.is_superseded=0 AND f.data_truth_version=1
          AND d.transaction_date BETWEEN COALESCE(f.period_start,f.business_date) AND COALESCE(f.period_end,f.business_date))
        ORDER BY d.store_code,d.transaction_date;
        """;

    public const string StockPositions = """
        WITH keys AS
        (
          SELECT DISTINCT m.store_code,m.product_code FROM dbo.stock_movements m
          WHERE m.document_date>=@dateFrom AND m.document_date<=@dateTo
            AND (@storesJson IS NULL OR store_code IN (SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@storesJson)))
            AND (@itemsJson IS NULL OR m.product_code IN (SELECT CONVERT(nvarchar(80),[value]) FROM OPENJSON(@itemsJson)))
            AND EXISTS(SELECT 1 FROM dbo.v_stock_snapshots_effective s WHERE s.store_code=m.store_code
              AND s.product_code=m.product_code AND s.snapshot_date=@dateTo)
        )
        SELECT k.store_code,k.product_code,
               first_move.opening_quantity source_opening_quantity,
               (SELECT SUM(s.quantity) FROM dbo.v_stock_snapshots_effective s WHERE s.store_code=k.store_code
                 AND s.product_code=k.product_code AND s.snapshot_date=@dateTo) source_closing_quantity
        FROM keys k
        OUTER APPLY(SELECT TOP(1) m.opening_quantity FROM dbo.stock_movements m
          WHERE m.store_code=k.store_code AND m.product_code=k.product_code
            AND m.document_date>=@dateFrom AND m.document_date<=@dateTo
          ORDER BY m.document_date,m.line_seq,m.stock_movement_id) first_move
        ORDER BY k.store_code,k.product_code;
        """;

    public const string StockMovements = """
        SELECT m.store_code,m.product_code,m.source_transaction_type,SUM(m.transaction_quantity) source_signed_quantity
        FROM dbo.stock_movements m
        WHERE m.document_date>=@dateFrom AND m.document_date<=@dateTo
          AND (@storesJson IS NULL OR m.store_code IN (SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@storesJson)))
          AND (@itemsJson IS NULL OR m.product_code IN (SELECT CONVERT(nvarchar(80),[value]) FROM OPENJSON(@itemsJson)))
          AND EXISTS(SELECT 1 FROM dbo.v_stock_snapshots_effective s WHERE s.store_code=m.store_code
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
                reader.GetDecimal(8), NullableDecimal(reader, 9), NullableDecimal(reader, 10), reader.GetInt32(11)));
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
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.GetInt32(4)));
        return rows;
    }

    public async Task<IReadOnlyList<TenderCoverageGapRow>> LoadTenderCoverageGapsAsync(ReportingQueryScope scope, CancellationToken cancellationToken = default)
    {
        scope.Validate();
        await using var connection = await Open(cancellationToken);
        await using var command = Command(connection, SqlReportingQueries.TenderCoverageGaps, scope);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<TenderCoverageGapRow>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.GetFieldValue<DateOnly>(1)));
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
            rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetDecimal(2), reader.GetInt32(3)));
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
        var connection = new SqlConnection(LocalSqlConnectionPolicy.Validate(connectionString));
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
