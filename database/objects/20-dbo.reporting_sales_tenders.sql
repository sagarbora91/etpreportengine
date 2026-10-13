CREATE OR ALTER VIEW dbo.reporting_sales_tenders AS
 SELECT sales_tender_id,sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id
 FROM dbo.sales_tenders WHERE is_reporting_eligible=1
-- >>> GRANTS
