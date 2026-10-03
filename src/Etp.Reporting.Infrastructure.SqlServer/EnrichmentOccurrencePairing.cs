namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// R-WLMHW-01 (1.9.4). The stored <c>match_status</c> of an R013 staff row is decided when the row is imported, by
/// counting the R025 lines with the same store, date, document and item: two lines make every R013 row for that key
/// Ambiguous (dropped from the Staff/CRO report), and nothing stops two R013 rows from both taking a single line (the
/// stale duplicate counted twice). Reports therefore pair R013 rows with sales lines here, live, by occurrence: the
/// nth R013 row for (store, date, document, item) in source order goes to the nth R025 line for that key in sales
/// line order. A sales line takes at most one R013 row; a surplus R013 row reads as Missing.
/// R003 (All Discount Type) carries one row per discount applied to a line, so several R003 rows on one line are
/// correct: R003 keeps its stored status and line.
/// The date and store columns are window partition keys, so a filter on them reaches the enrichment table; the
/// sales-line side is an OUTER APPLY correlated on the R013 row's key, so only that invoice's lines for that item are
/// numbered (IX_sales_invoices_date, IX_sales_lines_report), never the whole sales_lines table.
/// </summary>
internal static class EnrichmentOccurrencePairing
{
    /// <summary>
    /// A derived table (alias it in the query) of every <c>dbo.sales_line_enrichments</c> column plus
    /// <c>effective_sales_line_id</c> and <c>effective_match_status</c> ('Matched', 'Missing' or, for R003 only, 'Ambiguous').
    /// </summary>
    public const string EffectiveEnrichments = """
        (SELECT pe.*,
                CASE WHEN pe.enrichment_type='R013' THEN pl.sales_line_id ELSE pe.matched_sales_line_id END effective_sales_line_id,
                CASE WHEN pe.enrichment_type<>'R013' THEN pe.match_status WHEN pl.sales_line_id IS NULL THEN 'Missing' ELSE 'Matched' END effective_match_status
         FROM (SELECT x.*,ROW_NUMBER() OVER(PARTITION BY x.enrichment_type,x.store_code,x.transaction_date,x.document_number,x.product_code
                                            ORDER BY x.source_lineage_id,x.sales_line_enrichment_id) occurrence
               FROM dbo.sales_line_enrichments x) pe
         OUTER APPLY (SELECT q.sales_line_id
                      FROM (SELECT sl.sales_line_id,ROW_NUMBER() OVER(ORDER BY sl.source_lineage_id,sl.sales_line_id) occurrence
                            FROM dbo.sales_invoices si JOIN dbo.sales_lines sl ON sl.sales_invoice_id=si.sales_invoice_id
                            WHERE pe.enrichment_type='R013' AND si.store_code=pe.store_code AND si.transaction_date=pe.transaction_date
                              AND si.document_number=pe.document_number AND sl.product_code=pe.product_code) q
                      WHERE q.occurrence=pe.occurrence) pl)
        """;
}
