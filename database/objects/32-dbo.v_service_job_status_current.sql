CREATE OR ALTER VIEW dbo.v_service_job_status_current AS
WITH per_view AS (
  SELECT r.job_order_number,r.report_code,r.snapshot_date,r.import_file_id,r.status_date,
    ROW_NUMBER() OVER(PARTITION BY r.job_order_number,r.report_code ORDER BY r.snapshot_date DESC,r.import_file_id DESC) view_rank
  FROM dbo.v_service_job_readings r JOIN dbo.v_service_families c ON c.report_code=r.report_code AND c.status_label IS NOT NULL
), winners AS (
  SELECT p.job_order_number,p.report_code,p.snapshot_date,p.import_file_id,p.status_date,c.status_label,c.lifecycle_rank,
    COUNT(*) OVER(PARTITION BY p.job_order_number) views_held,
    ROW_NUMBER() OVER(PARTITION BY p.job_order_number ORDER BY p.snapshot_date DESC,c.lifecycle_rank DESC,p.import_file_id DESC) job_rank
  FROM per_view p JOIN dbo.v_service_families c ON c.report_code=p.report_code
  WHERE p.view_rank=1
), details AS (
  SELECT s.report_code,s.import_file_id,s.job_order_number,MIN(s.job_date) job_date,MAX(s.edd) edd,MAX(s.brand) brand,
    MAX(s.model) model,MAX(s.product_category) product_category,MAX(s.customer_name) customer_name,
    SUM(s.spare_value) spare_value,SUM(s.labour_charge) labour_charge,COUNT_BIG(*) line_count
  FROM dbo.v_service_status_view_rows s
  WHERE s.job_order_number IS NOT NULL
  GROUP BY s.report_code,s.import_file_id,s.job_order_number
)
SELECT w.job_order_number,w.report_code status_view,w.status_label,w.lifecycle_rank,w.status_date,
  d.job_date,d.edd,d.brand,d.model,d.product_category,CASE WHEN IS_MEMBER(N'etp_owner')=1 OR IS_MEMBER(N'etp_store_manager')=1 OR IS_MEMBER(N'db_owner')=1 THEN d.customer_name END customer_name,d.spare_value,d.labour_charge,
  CONVERT(int,d.line_count) lines,w.snapshot_date,w.import_file_id,CONVERT(int,w.views_held-1) other_lists
FROM winners w JOIN details d ON d.report_code=w.report_code AND d.import_file_id=w.import_file_id
  AND d.job_order_number=w.job_order_number
WHERE w.job_rank=1
-- >>> GRANTS
DENY DELETE ON dbo.v_service_job_status_current TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_job_status_current TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_job_status_current TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_job_status_current TO etp_owner,etp_store_manager,etp_viewer;
