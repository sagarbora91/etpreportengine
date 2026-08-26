SET XACT_ABORT ON;
BEGIN TRANSACTION;

;WITH file_scope AS
(
    SELECT f.import_file_id,
           CASE
             WHEN EXISTS(SELECT 1 FROM dbo.source_lineage l WHERE l.import_file_id=f.import_file_id AND l.source_record_type='R025_SALES_LINE') THEN 'R025'
             WHEN EXISTS(SELECT 1 FROM dbo.source_lineage l WHERE l.import_file_id=f.import_file_id AND l.source_record_type LIKE 'R022[_]%') THEN 'R022'
             WHEN EXISTS(SELECT 1 FROM dbo.source_lineage l WHERE l.import_file_id=f.import_file_id AND l.source_record_type='CLOSING_STOCK') THEN 'CLOSING_STOCK'
             WHEN EXISTS(SELECT 1 FROM dbo.source_lineage l WHERE l.import_file_id=f.import_file_id AND l.source_record_type='STOCK_LEDGER') THEN 'STOCK_LEDGER'
           END report_code,
           COALESCE
           (
             (SELECT MAX(i.store_code) FROM dbo.sales_invoice_controls c JOIN dbo.sales_invoices i ON i.sales_invoice_id=c.sales_invoice_id JOIN dbo.source_lineage l ON l.source_lineage_id=c.source_lineage_id WHERE l.import_file_id=f.import_file_id),
             (SELECT MAX(i.store_code) FROM dbo.sales_lines s JOIN dbo.sales_invoices i ON i.sales_invoice_id=s.sales_invoice_id JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id WHERE l.import_file_id=f.import_file_id),
             (SELECT MAX(s.store_code) FROM dbo.stock_snapshots s JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id WHERE l.import_file_id=f.import_file_id),
             (SELECT MAX(m.store_code) FROM dbo.stock_movements m JOIN dbo.source_lineage l ON l.source_lineage_id=m.source_lineage_id WHERE l.import_file_id=f.import_file_id)
           ) store_code,
           COALESCE
           (
             (SELECT MAX(i.transaction_date) FROM dbo.sales_invoice_controls c JOIN dbo.sales_invoices i ON i.sales_invoice_id=c.sales_invoice_id JOIN dbo.source_lineage l ON l.source_lineage_id=c.source_lineage_id WHERE l.import_file_id=f.import_file_id),
             (SELECT MAX(i.transaction_date) FROM dbo.sales_lines s JOIN dbo.sales_invoices i ON i.sales_invoice_id=s.sales_invoice_id JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id WHERE l.import_file_id=f.import_file_id),
             (SELECT MAX(s.snapshot_date) FROM dbo.stock_snapshots s JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id WHERE l.import_file_id=f.import_file_id),
             (SELECT MAX(m.document_date) FROM dbo.stock_movements m JOIN dbo.source_lineage l ON l.source_lineage_id=m.source_lineage_id WHERE l.import_file_id=f.import_file_id)
           ) business_date
    FROM dbo.import_files f
)
UPDATE f SET
    report_code=COALESCE(f.report_code,s.report_code),
    store_code=COALESCE(f.store_code,s.store_code),
    business_date=COALESCE(f.business_date,s.business_date),
    source_report_date=COALESCE(f.source_report_date,s.business_date),
    imported_by=COALESCE(f.imported_by,N'LEGACY_IMPORT')
FROM dbo.import_files f
JOIN file_scope s ON s.import_file_id=f.import_file_id
WHERE f.report_code IS NULL OR f.store_code IS NULL OR f.business_date IS NULL OR f.source_report_date IS NULL OR f.imported_by IS NULL;

COMMIT TRANSACTION;
