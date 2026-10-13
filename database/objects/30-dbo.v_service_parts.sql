CREATE OR ALTER VIEW dbo.v_service_parts AS
WITH live AS (
  SELECT f.import_file_id,f.report_code,COALESCE(f.period_end,f.business_date) snapshot_date
  FROM dbo.import_files f JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
  WHERE f.is_superseded=0 AND f.data_truth_version=1 AND b.status='Completed'
    AND f.report_code IN('S002','S003','S004','S006','S007','S008','S009','S010','S011','S012','S013','S014','S015','S016','S017','S018','S019','S020','S021','S022','S023','S024','S025','S026','S029','S030','S031','S032','S033','S034','S035','S036','S037','S039','S040','S041')
    AND COALESCE(f.period_end,f.business_date) IS NOT NULL
), created_rows AS (
  SELECT s.import_file_id,w.snapshot_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.invoice_number))),N'') invoice_number,s.invoice_date,
    CONVERT(nvarchar(100),s.item_id) item_id,s.shipped_quantity,s.received_quantity,s.net_amount,CONVERT(nvarchar(40),s.status) exported_status,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.grn_no))),N'') grn_number,s.grn_date,s.received_date,CONVERT(nvarchar(60),s.from_location) from_location
  FROM dbo.etp_landing_s007 s JOIN live w ON w.import_file_id=s.import_file_id AND w.report_code='S007'
), created_ranked AS (
  SELECT c.*,DENSE_RANK() OVER(PARTITION BY c.invoice_number ORDER BY c.snapshot_date DESC,c.import_file_id DESC) reading_rank
  FROM created_rows c WHERE c.invoice_number IS NOT NULL
), created AS (
  SELECT invoice_number,item_id,MAX(snapshot_date) snapshot_date,MIN(invoice_date) invoice_date,SUM(shipped_quantity) shipped_quantity,SUM(received_quantity) received_quantity,
    SUM(net_amount) net_amount,MAX(exported_status) exported_status,MAX(grn_number) grn_number,MAX(grn_date) grn_date,MAX(received_date) received_date,MAX(from_location) from_location
  FROM created_ranked WHERE reading_rank=1 GROUP BY invoice_number,item_id
), received AS (
  SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.invoice_number))),N'') invoice_number,CONVERT(nvarchar(100),s.item_id) item_id,MAX(w.snapshot_date) snapshot_date,
    MIN(s.invoice_date) invoice_date,SUM(s.shipped_quantity) shipped_quantity,SUM(s.received_quantity) received_quantity,SUM(s.net_amount) net_amount,
    MAX(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.grn_no))),N'')) grn_number,MAX(s.grn_date) grn_date,MAX(s.received_date) received_date,MAX(CONVERT(nvarchar(60),s.from_location)) from_location
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s008 s ON w.report_code='S008' AND w.reading_rank=1
    AND w.import_file_id=s.import_file_id AND w.business_date=s.grn_date
  WHERE NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.invoice_number))),N'') IS NOT NULL
  GROUP BY NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.invoice_number))),N''),CONVERT(nvarchar(100),s.item_id)
), snap AS (
  SELECT MAX(snapshot_date) as_at FROM live
)
SELECT COALESCE(c.invoice_number,r.invoice_number) invoice_number,COALESCE(c.invoice_date,r.invoice_date) invoice_date,COALESCE(c.item_id,r.item_id) item_id,
  COALESCE(c.shipped_quantity,r.shipped_quantity) shipped_quantity,COALESCE(r.received_quantity,CASE WHEN c.grn_date IS NOT NULL THEN c.received_quantity END) received_quantity,
  COALESCE(c.net_amount,r.net_amount) net_amount,COALESCE(r.grn_number,c.grn_number) grn_number,COALESCE(r.grn_date,c.grn_date) grn_date,COALESCE(r.received_date,c.received_date) received_date,
  CONVERT(varchar(12),CASE WHEN r.invoice_number IS NOT NULL OR c.grn_date IS NOT NULL THEN 'Received' ELSE 'Open' END) status,c.exported_status,
  COALESCE(c.from_location,r.from_location) from_location,
  CASE WHEN r.invoice_number IS NOT NULL OR c.grn_date IS NOT NULL
       THEN DATEDIFF(day,COALESCE(c.invoice_date,r.invoice_date),COALESCE(r.grn_date,c.grn_date,r.received_date,c.received_date))
       ELSE DATEDIFF(day,c.invoice_date,snap.as_at) END days_open,
  CASE WHEN c.snapshot_date IS NULL OR r.snapshot_date>c.snapshot_date THEN r.snapshot_date ELSE c.snapshot_date END snapshot_date,snap.as_at
FROM created c FULL OUTER HASH JOIN received r ON r.invoice_number=c.invoice_number AND r.item_id=c.item_id
CROSS JOIN snap
-- >>> GRANTS
DENY DELETE ON dbo.v_service_parts TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_parts TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_parts TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_parts TO etp_owner,etp_store_manager,etp_viewer;
