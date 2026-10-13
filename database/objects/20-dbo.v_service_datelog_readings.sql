CREATE OR ALTER VIEW dbo.v_service_datelog_readings AS
WITH dated AS (
  SELECT 'S003' report_code,import_file_id,trans_date business_date FROM dbo.etp_landing_s003 WHERE trans_date IS NOT NULL GROUP BY import_file_id,trans_date
  UNION ALL SELECT 'S004',import_file_id,billingdate FROM dbo.etp_landing_s004 WHERE billingdate IS NOT NULL GROUP BY import_file_id,billingdate
  UNION ALL SELECT 'S007',import_file_id,grn_date FROM dbo.etp_landing_s007 WHERE grn_date IS NOT NULL GROUP BY import_file_id,grn_date
  UNION ALL SELECT 'S008',import_file_id,grn_date FROM dbo.etp_landing_s008 WHERE grn_date IS NOT NULL GROUP BY import_file_id,grn_date
  UNION ALL SELECT 'S013',import_file_id,stm_date FROM dbo.etp_landing_s013 WHERE stm_date IS NOT NULL GROUP BY import_file_id,stm_date
  UNION ALL SELECT 'S019',import_file_id,repairdate FROM dbo.etp_landing_s019 WHERE repairdate IS NOT NULL GROUP BY import_file_id,repairdate
  UNION ALL SELECT 'S022',import_file_id,invoice_date FROM dbo.etp_landing_s022 WHERE invoice_date IS NOT NULL GROUP BY import_file_id,invoice_date
  UNION ALL SELECT 'S023',import_file_id,transdate FROM dbo.etp_landing_s023 WHERE transdate IS NOT NULL GROUP BY import_file_id,transdate
  UNION ALL SELECT 'S024',import_file_id,transdate FROM dbo.etp_landing_s024 WHERE transdate IS NOT NULL GROUP BY import_file_id,transdate
  UNION ALL SELECT 'S025',import_file_id,transdate FROM dbo.etp_landing_s025 WHERE transdate IS NOT NULL GROUP BY import_file_id,transdate
  UNION ALL SELECT 'S026',import_file_id,transdate FROM dbo.etp_landing_s026 WHERE transdate IS NOT NULL GROUP BY import_file_id,transdate
  UNION ALL SELECT 'S029',import_file_id,repair_date FROM dbo.etp_landing_s029 WHERE repair_date IS NOT NULL GROUP BY import_file_id,repair_date
  UNION ALL SELECT 'S039',import_file_id,transaction_date FROM dbo.etp_landing_s039 WHERE transaction_date IS NOT NULL GROUP BY import_file_id,transaction_date
  UNION ALL SELECT 'S040',import_file_id,transaction_date FROM dbo.etp_landing_s040 WHERE transaction_date IS NOT NULL GROUP BY import_file_id,transaction_date
  UNION ALL SELECT 'S041',import_file_id,transaction_date FROM dbo.etp_landing_s041 WHERE transaction_date IS NOT NULL GROUP BY import_file_id,transaction_date
), win AS (
  SELECT d.report_code,d.import_file_id,COALESCE(f.period_end,f.business_date) snapshot_date,MIN(d.business_date) window_from,MAX(d.business_date) greatest_date
  FROM dated d JOIN dbo.import_files f ON f.import_file_id=d.import_file_id AND f.report_code=d.report_code
  JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
  WHERE f.is_superseded=0 AND f.data_truth_version=1 AND b.status='Completed' AND COALESCE(f.period_end,f.business_date) IS NOT NULL
  GROUP BY d.report_code,d.import_file_id,COALESCE(f.period_end,f.business_date)
), held AS (
  SELECT DISTINCT d.report_code,d.business_date
  FROM dated d JOIN win w ON w.import_file_id=d.import_file_id AND w.report_code=d.report_code
)
SELECT h.report_code,h.business_date,w.import_file_id,w.snapshot_date,
  ROW_NUMBER() OVER(PARTITION BY h.report_code,h.business_date ORDER BY w.snapshot_date DESC,w.import_file_id DESC) reading_rank
FROM held h JOIN win w ON w.report_code=h.report_code
  AND h.business_date BETWEEN w.window_from AND CASE WHEN w.greatest_date>w.snapshot_date THEN w.greatest_date ELSE w.snapshot_date END
-- >>> GRANTS
DENY DELETE ON dbo.v_service_datelog_readings TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_datelog_readings TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_datelog_readings TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_datelog_readings TO etp_owner,etp_store_manager,etp_viewer;
