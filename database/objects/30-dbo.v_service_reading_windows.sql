CREATE OR ALTER VIEW dbo.v_service_reading_windows AS
WITH live AS (
  SELECT f.import_file_id,f.report_code,COALESCE(f.period_end,f.business_date) snapshot_date
  FROM dbo.import_files f JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
  WHERE f.is_superseded=0 AND f.data_truth_version=1 AND b.status='Completed'
    AND f.report_code IN('S002','S003','S004','S006','S007','S008','S009','S010','S011','S012','S013','S014','S015','S016','S017','S018','S019','S020','S021','S022','S023','S024','S025','S026','S029','S030','S031','S032','S033','S034','S035','S036','S037','S039','S040','S041')
), stats AS (
  SELECT 'S002' report_code,import_file_id,COUNT_BIG(*) row_count,CONVERT(date,NULL) least_date,CONVERT(date,NULL) greatest_date FROM dbo.etp_landing_s002 GROUP BY import_file_id
  UNION ALL SELECT 'S003',import_file_id,COUNT_BIG(*),MIN(trans_date),MAX(trans_date) FROM dbo.etp_landing_s003 GROUP BY import_file_id
  UNION ALL SELECT 'S004',import_file_id,COUNT_BIG(*),MIN(billingdate),MAX(billingdate) FROM dbo.etp_landing_s004 GROUP BY import_file_id
  UNION ALL SELECT 'S006',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s006 GROUP BY import_file_id
  UNION ALL SELECT 'S007',import_file_id,COUNT_BIG(*),MIN(grn_date),MAX(grn_date) FROM dbo.etp_landing_s007 GROUP BY import_file_id
  UNION ALL SELECT 'S008',import_file_id,COUNT_BIG(*),MIN(grn_date),MAX(grn_date) FROM dbo.etp_landing_s008 GROUP BY import_file_id
  UNION ALL SELECT 'S009',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s009 GROUP BY import_file_id
  UNION ALL SELECT 'S010',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s010 GROUP BY import_file_id
  UNION ALL SELECT 'S011',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s011 GROUP BY import_file_id
  UNION ALL SELECT 'S012',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s012 GROUP BY import_file_id
  UNION ALL SELECT 'S013',import_file_id,COUNT_BIG(*),MIN(stm_date),MAX(stm_date) FROM dbo.etp_landing_s013 GROUP BY import_file_id
  UNION ALL SELECT 'S014',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s014 GROUP BY import_file_id
  UNION ALL SELECT 'S015',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s015 GROUP BY import_file_id
  UNION ALL SELECT 'S016',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s016 GROUP BY import_file_id
  UNION ALL SELECT 'S017',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s017 GROUP BY import_file_id
  UNION ALL SELECT 'S018',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s018 GROUP BY import_file_id
  UNION ALL SELECT 'S019',import_file_id,COUNT_BIG(*),MIN(repairdate),MAX(repairdate) FROM dbo.etp_landing_s019 GROUP BY import_file_id
  UNION ALL SELECT 'S020',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s020 GROUP BY import_file_id
  UNION ALL SELECT 'S021',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s021 GROUP BY import_file_id
  UNION ALL SELECT 'S022',import_file_id,COUNT_BIG(*),MIN(invoice_date),MAX(invoice_date) FROM dbo.etp_landing_s022 GROUP BY import_file_id
  UNION ALL SELECT 'S023',import_file_id,COUNT_BIG(*),MIN(transdate),MAX(transdate) FROM dbo.etp_landing_s023 GROUP BY import_file_id
  UNION ALL SELECT 'S024',import_file_id,COUNT_BIG(*),MIN(transdate),MAX(transdate) FROM dbo.etp_landing_s024 GROUP BY import_file_id
  UNION ALL SELECT 'S025',import_file_id,COUNT_BIG(*),MIN(transdate),MAX(transdate) FROM dbo.etp_landing_s025 GROUP BY import_file_id
  UNION ALL SELECT 'S026',import_file_id,COUNT_BIG(*),MIN(transdate),MAX(transdate) FROM dbo.etp_landing_s026 GROUP BY import_file_id
  UNION ALL SELECT 'S029',import_file_id,COUNT_BIG(*),MIN(repair_date),MAX(repair_date) FROM dbo.etp_landing_s029 GROUP BY import_file_id
  UNION ALL SELECT 'S030',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s030 GROUP BY import_file_id
  UNION ALL SELECT 'S031',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s031 GROUP BY import_file_id
  UNION ALL SELECT 'S032',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s032 GROUP BY import_file_id
  UNION ALL SELECT 'S033',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s033 GROUP BY import_file_id
  UNION ALL SELECT 'S034',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s034 GROUP BY import_file_id
  UNION ALL SELECT 'S035',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s035 GROUP BY import_file_id
  UNION ALL SELECT 'S036',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s036 GROUP BY import_file_id
  UNION ALL SELECT 'S037',import_file_id,COUNT_BIG(*),CONVERT(date,NULL),CONVERT(date,NULL) FROM dbo.etp_landing_s037 GROUP BY import_file_id
  UNION ALL SELECT 'S039',import_file_id,COUNT_BIG(*),MIN(transaction_date),MAX(transaction_date) FROM dbo.etp_landing_s039 GROUP BY import_file_id
  UNION ALL SELECT 'S040',import_file_id,COUNT_BIG(*),MIN(transaction_date),MAX(transaction_date) FROM dbo.etp_landing_s040 GROUP BY import_file_id
  UNION ALL SELECT 'S041',import_file_id,COUNT_BIG(*),MIN(transaction_date),MAX(transaction_date) FROM dbo.etp_landing_s041 GROUP BY import_file_id
)
SELECT l.import_file_id,l.report_code,r.read_rule,l.snapshot_date,COALESCE(s.row_count,0) row_count,
  s.least_date window_from,
  CASE WHEN s.least_date IS NULL THEN NULL WHEN s.greatest_date>l.snapshot_date THEN s.greatest_date ELSE l.snapshot_date END window_to
FROM live l JOIN dbo.v_service_families r ON r.report_code=l.report_code
LEFT JOIN stats s ON s.import_file_id=l.import_file_id AND s.report_code=l.report_code
WHERE l.snapshot_date IS NOT NULL
-- >>> GRANTS
DENY DELETE ON dbo.v_service_reading_windows TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_reading_windows TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_reading_windows TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_reading_windows TO etp_owner,etp_store_manager,etp_viewer;
