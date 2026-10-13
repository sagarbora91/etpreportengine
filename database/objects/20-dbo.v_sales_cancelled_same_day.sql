CREATE OR ALTER VIEW dbo.v_sales_cancelled_same_day AS
SELECT DISTINCT i.store_code,i.document_number,i.transaction_date
FROM dbo.etp_r025 r
JOIN dbo.import_files f ON f.import_file_id=r.import_file_id AND f.report_code='R025' AND f.is_superseded=0 AND f.data_truth_version=1
JOIN dbo.sales_invoices i ON i.store_code=CONVERT(varchar(30),LTRIM(RTRIM(r.store_code)))
  AND i.document_number=CONVERT(nvarchar(80),LTRIM(RTRIM(r.reference_invoice_number)))
  AND i.transaction_date=r.transaction_date
WHERE UPPER(LTRIM(RTRIM(r.source_transaction_type)))=N'BC' AND NULLIF(LTRIM(RTRIM(r.reference_invoice_number)),N'') IS NOT NULL
  AND (r.reference_invoice_date IS NULL OR r.reference_invoice_date=r.transaction_date)
-- >>> GRANTS
DENY DELETE ON dbo.v_sales_cancelled_same_day TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_sales_cancelled_same_day TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_sales_cancelled_same_day TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_sales_cancelled_same_day TO etp_owner,etp_store_manager,etp_viewer;
