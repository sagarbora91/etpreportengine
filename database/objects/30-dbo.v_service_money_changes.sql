CREATE OR ALTER VIEW dbo.v_service_money_changes AS
WITH ranked AS (
  SELECT report_code,business_date,import_file_id,snapshot_date,reading_rank,
    MAX(CASE WHEN reading_rank=2 THEN import_file_id END) OVER(PARTITION BY report_code,business_date) second_import_file_id,
    MAX(CASE WHEN reading_rank=2 THEN snapshot_date END) OVER(PARTITION BY report_code,business_date) second_snapshot_date
  FROM dbo.v_service_datelog_readings WHERE report_code IN('S003','S004') AND reading_rank<=2
), amounts AS (
  SELECT 'S003' report_code,import_file_id,trans_date business_date,SUM(netamount_incl_tax) amount
  FROM dbo.etp_landing_s003 WHERE trans_date IS NOT NULL GROUP BY import_file_id,trans_date
  UNION ALL
  SELECT 'S004',import_file_id,billingdate,SUM(totalamount)
  FROM dbo.etp_landing_s004 WHERE billingdate IS NOT NULL GROUP BY import_file_id,billingdate
), restated AS (
  SELECT f.superseded_by_import_file_id current_import_file_id,f.import_file_id,f.report_code,
    COALESCE(f.period_end,f.business_date) snapshot_date,MIN(a.business_date) window_from,MAX(a.business_date) greatest_date
  FROM dbo.import_files f JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
  JOIN amounts a ON a.report_code=f.report_code AND a.import_file_id=f.import_file_id
  WHERE f.is_superseded=1 AND f.data_truth_version=1 AND b.status='Completed' AND f.report_code IN('S003','S004')
    AND COALESCE(f.period_end,f.business_date) IS NOT NULL
  GROUP BY f.superseded_by_import_file_id,f.import_file_id,f.report_code,COALESCE(f.period_end,f.business_date)
), matched AS (
  SELECT c.report_code,c.business_date,c.import_file_id current_import_file_id,c.snapshot_date current_snapshot_date,
    c.second_import_file_id,c.second_snapshot_date,x.import_file_id restated_import_file_id,x.snapshot_date restated_snapshot_date,
    ROW_NUMBER() OVER(PARTITION BY c.report_code,c.business_date ORDER BY x.import_file_id DESC) restated_rank
  FROM ranked c LEFT JOIN restated x ON x.current_import_file_id=c.import_file_id AND x.report_code=c.report_code
    AND c.business_date BETWEEN x.window_from AND CASE WHEN x.greatest_date>x.snapshot_date THEN x.greatest_date ELSE x.snapshot_date END
  WHERE c.reading_rank=1
), previous AS (
  SELECT report_code,business_date,current_import_file_id,current_snapshot_date,
    COALESCE(restated_import_file_id,second_import_file_id) import_file_id,COALESCE(restated_snapshot_date,second_snapshot_date) snapshot_date
  FROM matched
  WHERE restated_rank=1 AND (restated_import_file_id IS NOT NULL OR second_import_file_id IS NOT NULL)
), pairs AS (
  SELECT v.report_code,v.business_date,v.snapshot_date previous_snapshot_date,v.import_file_id previous_import_file_id,
    COALESCE(pa.amount,0) previous_amount,v.current_snapshot_date,v.current_import_file_id,
    COALESCE(ca.amount,0) current_amount
  FROM previous v
  LEFT JOIN amounts ca ON ca.report_code=v.report_code AND ca.import_file_id=v.current_import_file_id AND ca.business_date=v.business_date
  LEFT JOIN amounts pa ON pa.report_code=v.report_code AND pa.import_file_id=v.import_file_id AND pa.business_date=v.business_date
)
SELECT report_code,business_date,previous_snapshot_date,previous_import_file_id,previous_amount,
  current_snapshot_date,current_import_file_id,current_amount,current_amount-previous_amount difference
FROM pairs WHERE current_amount<>previous_amount
-- >>> GRANTS
DENY DELETE ON dbo.v_service_money_changes TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_money_changes TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_money_changes TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_money_changes TO etp_owner,etp_store_manager,etp_viewer;
