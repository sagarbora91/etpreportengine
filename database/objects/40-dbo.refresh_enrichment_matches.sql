CREATE OR ALTER PROCEDURE dbo.refresh_enrichment_matches AS BEGIN SET NOCOUNT ON;
IF OBJECT_ID(N'dbo.sales_line_enrichments',N'U') IS NOT NULL
UPDATE e SET matched_sales_line_id=matches.sales_line_id,match_status=matches.match_status
FROM dbo.sales_line_enrichments e
CROSS APPLY
(
  SELECT CASE WHEN COUNT_BIG(*)=1 THEN MAX(l.sales_line_id) END sales_line_id,
         CASE COUNT_BIG(*) WHEN 0 THEN 'Missing' WHEN 1 THEN 'Matched' ELSE 'Ambiguous' END match_status
  FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
  WHERE i.store_code=e.store_code AND i.transaction_date=e.transaction_date
    AND i.document_number=e.document_number AND l.product_code=e.product_code
) matches
WHERE e.match_status<>'Matched';
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.refresh_enrichment_matches TO etp_store_manager;
