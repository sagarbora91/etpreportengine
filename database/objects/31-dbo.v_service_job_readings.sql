CREATE OR ALTER VIEW dbo.v_service_job_readings AS
WITH job_rows AS (
  SELECT report_code,import_file_id,job_order_number,status_date FROM dbo.v_service_status_view_rows
  UNION ALL SELECT 'S002',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N''),created_date FROM dbo.etp_landing_s002
  UNION ALL SELECT 'S009',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''),jodate FROM dbo.etp_landing_s009
  UNION ALL SELECT 'S010',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''),jodate FROM dbo.etp_landing_s010
  UNION ALL SELECT 'S011',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N''),srn_date FROM dbo.etp_landing_s011
  UNION ALL SELECT 'S012',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''),srnrepaireddate FROM dbo.etp_landing_s012
  UNION ALL SELECT 'S020',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''),radate FROM dbo.etp_landing_s020
  UNION ALL SELECT 'S021',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''),dcdate FROM dbo.etp_landing_s021
  UNION ALL SELECT 'S030',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_number))),N''),running_test_date FROM dbo.etp_landing_s030
  UNION ALL SELECT 'S036',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N''),created_date FROM dbo.etp_landing_s036
  UNION ALL SELECT 'S037',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N''),created_date FROM dbo.etp_landing_s037
)
SELECT j.job_order_number,j.report_code,w.snapshot_date,j.import_file_id,
  MIN(j.status_date) first_status_date,MAX(j.status_date) status_date,COUNT_BIG(*) line_count
FROM job_rows j JOIN dbo.v_service_reading_windows w ON w.import_file_id=j.import_file_id AND w.report_code=j.report_code
WHERE j.job_order_number IS NOT NULL
GROUP BY j.job_order_number,j.report_code,w.snapshot_date,j.import_file_id
-- >>> GRANTS
DENY DELETE ON dbo.v_service_job_readings TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_job_readings TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_job_readings TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_job_readings TO etp_owner,etp_store_manager,etp_viewer;
