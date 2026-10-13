CREATE OR ALTER VIEW dbo.v_service_job_revision AS
SELECT COUNT_BIG(*) files,MAX(f.import_file_id) last_import_file_id,
  CHECKSUM_AGG(CHECKSUM(f.import_file_id,f.is_superseded,f.data_truth_version,b.status,f.period_end,f.business_date)) state
FROM dbo.import_files f JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
WHERE f.report_code IN('S002','S003','S004','S006','S007','S008','S009','S010','S011','S012','S013','S014','S015','S016','S017','S018','S019','S020','S021','S022','S023','S024','S025','S026','S029','S030','S031','S032','S033','S034','S035','S036','S037','S039','S040','S041')
-- >>> GRANTS
DENY DELETE ON dbo.v_service_job_revision TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_job_revision TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_job_revision TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_job_revision TO etp_owner,etp_store_manager,etp_viewer;
