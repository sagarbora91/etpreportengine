CREATE OR ALTER VIEW dbo.v_service_readings AS
SELECT w.report_code,w.read_rule,w.snapshot_date,w.window_from,w.window_to,w.import_file_id,w.row_count,
  COALESCE(b.completed_utc,b.started_utc) imported_utc,
  CONVERT(varchar(12),CASE WHEN f.original_file_name LIKE 'S[0-9][0-9][0-9][^0-9]%' THEN 'CONSOLIDATED' ELSE 'RAW' END) source_kind,
  CONVERT(bit,CASE WHEN ROW_NUMBER() OVER(PARTITION BY w.report_code
    ORDER BY w.snapshot_date DESC,CASE WHEN w.row_count>0 THEN 1 ELSE 0 END DESC,w.import_file_id DESC)=1 THEN 1 ELSE 0 END) is_latest
FROM dbo.v_service_reading_windows w
JOIN dbo.import_files f ON f.import_file_id=w.import_file_id
JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
-- >>> GRANTS
DENY DELETE ON dbo.v_service_readings TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_readings TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_readings TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_readings TO etp_owner,etp_store_manager,etp_viewer;
