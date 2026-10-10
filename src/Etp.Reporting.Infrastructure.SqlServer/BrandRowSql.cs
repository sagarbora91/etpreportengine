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

    /// <summary>
    /// The Brand column of the Invoice Source Drill-down (1.9.8, RA-SALES-04 remainder): the line's brand row by
    /// <see cref="MappedRowOfL"/>, or "Unmapped: " and the export's brand (the distributor for Helios) when no row claims
    /// it, with the same prefix Brand-wise Sales uses, so a drilled line reads the brand its Brand-wise row reads.
    /// Needs <see cref="MappedRowOfL"/> in the FROM clause.
    /// </summary>
    public const string BrandRowOrUnmappedOfL =
        $"COALESCE(mapped.row_label,N'{Etp.Reporting.Reporting.SalesReportingService.UnmappedPrefix}'+COALESCE(l.source_brand_name,l.source_brand_code))";
}
