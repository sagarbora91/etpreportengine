namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// The owner's brand-row master (decision 24; Settings > Stores & masters > Brands and targets): a sales line of alias
/// <c>l</c> on an invoice of alias <c>i</c> maps to the store's brand row whose source brand equals the line's brand code,
/// brand name or cluster (code first, then name, then cluster; then the row order). The DSR brand rows and, since 1.9.8
/// (RA-SALES-03/04, RA-OPS-05), Brand-wise / Brand-Segment Sales share this one fragment so the two reports can never
/// disagree on which lines are mapped. The fragment is an OUTER APPLY named <c>mapped</c>; <c>mapped.row_label</c> is NULL
/// for a line no row claims.
/// </summary>
internal static class BrandRowSql
{
    public const string MappedRowOfL = """
        OUTER APPLY (SELECT TOP(1) r.row_label FROM dbo.brand_row_codes b JOIN dbo.brand_rows r ON r.brand_row_id=b.brand_row_id AND r.store_code=b.store_code
          WHERE b.store_code=i.store_code AND b.source_brand IN(l.source_brand_code,l.source_brand_name,l.brand_segment)
          ORDER BY CASE WHEN b.source_brand=l.source_brand_code THEN 0 WHEN b.source_brand=l.source_brand_name THEN 1 ELSE 2 END,r.sort_order,r.brand_row_id) mapped
        """;
}
