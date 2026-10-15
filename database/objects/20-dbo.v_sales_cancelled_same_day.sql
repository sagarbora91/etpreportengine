CREATE OR ALTER VIEW dbo.v_sales_cancelled_same_day AS
-- Decision 41: an invoice that BC (bill cancellation) rows of a current R025 file cancel on its own date.
-- Decision 68 #3 (1.12.1): only a WHOLE-bill cancellation counts. The BC rows name the cancelled invoice (INVREFNO) and
-- carry their own document (INVNUMBER). When that BC document's BC lines take back less quantity than the invoice's INV
-- lines hold, the bill is only partly cancelled and stays an invoice. When no BC document is found in sales_invoices the
-- old rule (whole bill) is kept, so a cancellation is never lost for want of its lines.
WITH bc AS
(
  SELECT DISTINCT i.sales_invoice_id,i.store_code,i.document_number,i.transaction_date,b.sales_invoice_id bc_invoice_id
  FROM dbo.etp_r025 r
  JOIN dbo.import_files f ON f.import_file_id=r.import_file_id AND f.report_code='R025' AND f.is_superseded=0 AND f.data_truth_version=1
  JOIN dbo.sales_invoices i ON i.store_code=CONVERT(varchar(30),LTRIM(RTRIM(r.store_code)))
    AND i.document_number=CONVERT(nvarchar(80),LTRIM(RTRIM(r.reference_invoice_number)))
    AND i.transaction_date=r.transaction_date
  LEFT JOIN dbo.sales_invoices b ON b.store_code=i.store_code
    AND b.document_number=CONVERT(nvarchar(80),LTRIM(RTRIM(r.invoice_number)))
    AND b.transaction_date=r.transaction_date
  WHERE UPPER(LTRIM(RTRIM(r.source_transaction_type)))=N'BC' AND NULLIF(LTRIM(RTRIM(r.reference_invoice_number)),N'') IS NOT NULL
    AND (r.reference_invoice_date IS NULL OR r.reference_invoice_date=r.transaction_date)
),
invoice AS
(
  SELECT sales_invoice_id,store_code,document_number,transaction_date,COUNT(bc_invoice_id) bc_documents
  FROM bc GROUP BY sales_invoice_id,store_code,document_number,transaction_date
)
SELECT c.store_code,c.document_number,c.transaction_date
FROM invoice c
WHERE c.bc_documents=0
   OR -COALESCE((SELECT SUM(l.source_quantity) FROM dbo.sales_lines l
                 WHERE l.sales_invoice_id IN(SELECT x.bc_invoice_id FROM bc x WHERE x.sales_invoice_id=c.sales_invoice_id)
                   AND UPPER(LTRIM(RTRIM(COALESCE(l.source_transaction_type,N''))))=N'BC'),0)
      >=COALESCE((SELECT SUM(l.source_quantity) FROM dbo.sales_lines l
                  WHERE l.sales_invoice_id=c.sales_invoice_id AND UPPER(LTRIM(RTRIM(COALESCE(l.source_transaction_type,N''))))=N'INV'),0)
-- >>> GRANTS
DENY DELETE ON dbo.v_sales_cancelled_same_day TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_sales_cancelled_same_day TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_sales_cancelled_same_day TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_sales_cancelled_same_day TO etp_owner,etp_store_manager,etp_viewer;
