namespace Etp.Reporting.Infrastructure.SqlServer.Tests;

/// <summary>
/// 1.9.8 (RA-SALES-03/04, RA-OPS-05): Brand-wise Sales maps a line to the owner's brand row with the very fragment the
/// DSR uses, so the two can never disagree. The SQL shape is pinned here; the figures on a database are pinned by the
/// integration suite and were checked read-only on live for Sep 2026 (198-BRANDS-DONE.md).
/// </summary>
public sealed class BrandRowSqlTests
{
    [Fact]
    public void Brand_row_fragment_matches_code_then_name_then_cluster_within_the_invoice_store()
    {
        var sql = BrandRowSql.MappedRowOfL;

        Assert.StartsWith("OUTER APPLY (SELECT TOP(1) r.row_label FROM dbo.brand_row_codes b JOIN dbo.brand_rows r ON r.brand_row_id=b.brand_row_id AND r.store_code=b.store_code", sql.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("WHERE b.store_code=i.store_code AND b.source_brand IN(l.source_brand_code,l.source_brand_name,l.brand_segment)", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY CASE WHEN b.source_brand=l.source_brand_code THEN 0 WHEN b.source_brand=l.source_brand_name THEN 1 ELSE 2 END,r.sort_order,r.brand_row_id) mapped", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Sales_query_returns_the_brand_row_as_its_thirteenth_column_without_changing_the_line_filter()
    {
        var sql = SqlReportingQueries.Sales;

        Assert.Contains("l.source_gross_amount,l.source_net_amount,i.invoice_year,mapped.row_label", sql, StringComparison.Ordinal);
        Assert.Contains(BrandRowSql.MappedRowOfL.Trim(), sql, StringComparison.Ordinal);
        // The row is a lookup, never a filter: an unmapped line stays in the report as an Unmapped row.
        Assert.DoesNotContain("mapped.row_label IS", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE i.transaction_date>=@dateFrom AND i.transaction_date<=@dateTo", sql, StringComparison.Ordinal);
        // The source brand and cluster columns are unchanged, so the Unmapped split and the segment filter read what they did.
        Assert.Contains("COALESCE(l.source_brand_name,l.source_brand_code,p.brand_name),COALESCE(l.brand_segment,p.cluster)", sql, StringComparison.Ordinal);
    }
}
