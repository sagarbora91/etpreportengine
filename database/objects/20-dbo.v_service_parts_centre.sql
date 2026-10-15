-- 1.12.1 P8: the Service Centre of each parts invoice line. Only the S007 (invoices created) rows carry a store_code; S008
-- (goods received) rows carry none, so an invoice known only from S008 has no row here (the screen reads it as "Not matched to
-- any centre"). The S007 rows are the latest reading of each invoice, chosen as v_service_parts chooses them. A row names a
-- Service Centre as in v_service_stock_summary_centre; with exactly one Service Centre an unnamed row is that centre's.
-- Join to v_service_parts on invoice_number and item_id.
CREATE OR ALTER VIEW dbo.v_service_parts_centre AS
WITH live AS (
  SELECT f.import_file_id,COALESCE(f.period_end,f.business_date) snapshot_date
  FROM dbo.import_files f JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
  WHERE f.is_superseded=0 AND f.data_truth_version=1 AND b.status='Completed' AND f.report_code='S007'
    AND COALESCE(f.period_end,f.business_date) IS NOT NULL
), centres AS (
  SELECT s.store_code
  FROM dbo.stores s JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id
  WHERE u.unit_kind='SERVICE'
), only_centre AS (
  SELECT CASE WHEN COUNT(*)=1 THEN MIN(store_code) END store_code FROM centres
), created_rows AS (
  SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.invoice_number))),N'') invoice_number,CONVERT(nvarchar(100),s.item_id) item_id,
    w.snapshot_date,s.import_file_id,LEFT(LTRIM(RTRIM(CONVERT(nvarchar(60),s.store_code))),30) row_store_code
  FROM dbo.etp_landing_s007 s JOIN live w ON w.import_file_id=s.import_file_id
), created_ranked AS (
  SELECT c.*,DENSE_RANK() OVER(PARTITION BY c.invoice_number ORDER BY c.snapshot_date DESC,c.import_file_id DESC) reading_rank
  FROM created_rows c WHERE c.invoice_number IS NOT NULL
)
SELECT r.invoice_number,r.item_id,COALESCE(MIN(m.store_code),oc.store_code) service_store_code
FROM created_ranked r
CROSS JOIN only_centre oc
OUTER APPLY (SELECT TOP (1) c.store_code FROM centres c WHERE c.store_code=r.row_store_code ORDER BY c.store_code) m
WHERE r.reading_rank=1
GROUP BY r.invoice_number,r.item_id,oc.store_code
-- >>> GRANTS
DENY DELETE ON dbo.v_service_parts_centre TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_parts_centre TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_parts_centre TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_parts_centre TO etp_owner,etp_store_manager,etp_viewer;
