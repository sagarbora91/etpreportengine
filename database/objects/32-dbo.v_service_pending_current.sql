CREATE OR ALTER VIEW dbo.v_service_pending_current AS
WITH state_rows AS (
  SELECT 'S009' report_code,import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N'') job_order_number,jodate job_date,CONVERT(nvarchar(200),brand) brand,
    CONVERT(nvarchar(200),variantnumber) model,CONVERT(nvarchar(200),customername) customer_name,CONVERT(nvarchar(200),pendingstore) pending_store,
    edd,CONVERT(nvarchar(60),jostatus) jo_status,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(200),sparerequired))),N'') spare_required,indentdate indent_date,CONVERT(date,NULL) repair_date
  FROM dbo.etp_landing_s009
  UNION ALL SELECT 'S010',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''),jodate,CONVERT(nvarchar(200),brand),
    CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),customername),CONVERT(nvarchar(200),pendingstore),
    CONVERT(date,NULL),CONVERT(nvarchar(60),jostatus),NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(200),sparerequired))),N''),indentdate,repairdate
  FROM dbo.etp_landing_s010
), state_lists AS (
  SELECT s.report_code,s.job_order_number,MIN(s.job_date) job_date,MAX(s.brand) brand,MAX(s.model) model,
    MAX(s.customer_name) customer_name,MAX(s.pending_store) pending_store,r.snapshot_date,r.import_file_id,
    MAX(s.edd) edd,MAX(s.jo_status) jo_status,MAX(s.spare_required) spare_required,MAX(s.indent_date) indent_date,MAX(s.repair_date) repair_date,CONVERT(nvarchar(80),NULL) srn_to_status
  FROM state_rows s JOIN dbo.v_service_readings r ON r.import_file_id=s.import_file_id AND r.report_code=s.report_code AND r.is_latest=1
  WHERE s.job_order_number IS NOT NULL
  GROUP BY s.report_code,s.job_order_number,r.snapshot_date,r.import_file_id
), srn_winners AS (
  SELECT job_order_number,snapshot_date,import_file_id,
    ROW_NUMBER() OVER(PARTITION BY job_order_number ORDER BY snapshot_date DESC,import_file_id DESC) job_rank
  FROM dbo.v_service_job_readings WHERE report_code='S011'
), srn_lists AS (
  SELECT 'S011' report_code,w.job_order_number,MIN(s.joborder_date) job_date,MAX(CONVERT(nvarchar(200),s.brand)) brand,
    CONVERT(nvarchar(200),NULL) model,MAX(CONVERT(nvarchar(200),s.customer_name)) customer_name,MAX(CONVERT(nvarchar(200),s.to_store)) pending_store,
    w.snapshot_date,w.import_file_id,
    CONVERT(date,NULL) edd,CONVERT(nvarchar(60),NULL) jo_status,CONVERT(nvarchar(200),NULL) spare_required,CONVERT(date,NULL) indent_date,CONVERT(date,NULL) repair_date,
    MAX(CONVERT(nvarchar(80),s.to_status)) srn_to_status
  FROM srn_winners w JOIN dbo.etp_landing_s011 s ON s.import_file_id=w.import_file_id
    AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.joborder_number))),N'')=w.job_order_number
  WHERE w.job_rank=1
    AND s.srn_received_date IS NULL AND s.repaired_date IS NULL AND COALESCE(CONVERT(nvarchar(80),s.to_status),N'') NOT LIKE '%Received%'
  GROUP BY w.job_order_number,w.snapshot_date,w.import_file_id
), lists AS (
  SELECT * FROM state_lists UNION ALL SELECT * FROM srn_lists
)
SELECT f.pending_list [list],l.report_code,f.list_label,l.job_order_number,l.job_date,
  CASE WHEN l.job_date IS NULL THEN NULL ELSE DATEDIFF(day,l.job_date,l.snapshot_date) END age_days,
  l.brand,l.model,CASE WHEN IS_MEMBER(N'etp_owner')=1 OR IS_MEMBER(N'etp_store_manager')=1 OR IS_MEMBER(N'db_owner')=1 THEN l.customer_name END customer_name,l.pending_store,l.snapshot_date,l.import_file_id,
  l.edd,l.jo_status,l.spare_required,l.indent_date,l.repair_date,l.srn_to_status
FROM lists l JOIN dbo.v_service_families f ON f.report_code=l.report_code
-- >>> GRANTS
DENY DELETE ON dbo.v_service_pending_current TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_pending_current TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_pending_current TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_pending_current TO etp_owner,etp_store_manager,etp_viewer;
