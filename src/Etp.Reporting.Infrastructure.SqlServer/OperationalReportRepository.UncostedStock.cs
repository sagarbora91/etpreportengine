using Etp.Reporting.Reporting;
using Microsoft.Data.SqlClient;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>Items of one store and brand row with a stock line that has a quantity and no cost price (decision 69).</summary>
public sealed record UncostedStockReportRow(string StoreCode, string Brand, int Items);

// 1.12.1 F2 (decision 69). A separate partial file, so the closing-stock query and the value arithmetic stay exactly as they were:
// stock VALUE still leaves an uncosted line out; this only counts what it left out, over the same snapshot choice, the same
// segment and item filters and the same gift-card exclusion as StockInventorySql.
public sealed partial class OperationalReportRepository
{
    /// <summary>
    /// An item counts once when any of its snapshot lines has a non-zero quantity and a NULL total cost (a line with no quantity
    /// adds no value, so it is not "left out"). The brand row is the one Closing Stock/Brand Stock use: the cluster-mapped DSR
    /// brand row, else the brand, else "Unmapped".
    /// </summary>
    public const string UncostedStockSql = $"""
        WITH snap AS
        (
          SELECT e.store_code,MAX(e.snapshot_date) snapshot_date
          FROM dbo.v_stock_snapshots_effective e
          WHERE e.snapshot_date<=@date AND (@latest=1 OR e.snapshot_date=@date)
            AND (@stores IS NULL OR e.store_code IN(SELECT CONVERT(varchar(30),[value]) FROM OPENJSON(@stores)))
          GROUP BY e.store_code
        ),
        items AS
        (
          SELECT s.store_code,s.product_code,
                 COALESCE(NULLIF(LTRIM(RTRIM(s.brand_name)),''),NULLIF(LTRIM(RTRIM(s.brand_code)),'')) brand,
                 NULLIF(LTRIM(RTRIM(s.cluster)),'') cluster
          FROM dbo.v_stock_snapshots_effective s
          JOIN snap d ON d.store_code=s.store_code AND d.snapshot_date=s.snapshot_date
          WHERE s.quantity<>0 AND s.total_cost IS NULL
            AND (@segments IS NULL OR s.cluster IN(SELECT CONVERT(nvarchar(100),[value]) FROM OPENJSON(@segments)))
            AND (@items IS NULL OR s.product_code IN(SELECT CONVERT(nvarchar(80),[value]) FROM OPENJSON(@items)))
            AND NOT {NonMerchandiseSql.StockItemOfS}
          GROUP BY s.store_code,s.product_code,
                   COALESCE(NULLIF(LTRIM(RTRIM(s.brand_name)),''),NULLIF(LTRIM(RTRIM(s.brand_code)),'')),NULLIF(LTRIM(RTRIM(s.cluster)),'')
        )
        SELECT i.store_code,COALESCE(br.row_label,i.brand,N'Unmapped') brand_row,COUNT_BIG(DISTINCT i.product_code) items
        FROM items i
        OUTER APPLY
        (
          SELECT TOP(1) rw.row_label FROM dbo.brand_row_codes b JOIN dbo.brand_rows rw ON rw.brand_row_id=b.brand_row_id AND rw.store_code=b.store_code
          WHERE b.store_code=i.store_code AND b.source_brand=i.cluster
          ORDER BY rw.sort_order,rw.brand_row_id
        ) br
        GROUP BY i.store_code,COALESCE(br.row_label,i.brand,N'Unmapped')
        ORDER BY 1,2;
        """;

    /// <summary>Uncosted items per store and brand row. Same snapshot rule as <see cref="LoadStockInventoryAsync(ReportingQueryScope,bool,CancellationToken)"/>.</summary>
    public async Task<IReadOnlyList<UncostedStockReportRow>> LoadUncostedStockAsync(
        ReportingQueryScope scope, bool latestOnOrBefore, CancellationToken cancellationToken = default)
    {
        scope.Validate();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new SqlCommand(UncostedStockSql, connection);
        command.Parameters.AddWithValue("@date", scope.DateTo);
        command.Parameters.AddWithValue("@latest", latestOnOrBefore);
        command.Parameters.AddWithValue("@stores", Json(scope.StoreCodes));
        command.Parameters.AddWithValue("@segments", Json(scope.BrandSegments));
        command.Parameters.AddWithValue("@items", Json(scope.ItemCodes));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<UncostedStockReportRow>();
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetString(0), reader.GetString(1), checked((int)reader.GetInt64(2))));
        return rows;
    }
}
