CREATE OR ALTER PROCEDURE dbo.load_database_operational_health
AS
BEGIN
 SET NOCOUNT ON;
 SELECT metrics.size_mb,metrics.max_size_mb,backup_receipt.completed_utc,
   (SELECT COUNT(*) FROM (
      SELECT DISTINCT CAST(a.file_name AS nvarchar(520)) AS problem_key
      FROM dbo.import_attempts a
      WHERE a.recorded_utc>=DATEADD(hour,-24,SYSUTCDATETIME())
        AND (a.outcome='Failed'
             OR a.conflict_rows>0
             OR a.outcome LIKE '%Duplicate%'
             OR a.outcome='Unknown layout')
        AND NOT EXISTS (
          SELECT 1 FROM dbo.import_attempts cleared
          WHERE cleared.file_name=a.file_name
            AND cleared.recorded_utc>a.recorded_utc
            AND cleared.outcome<>'Failed'
            AND cleared.conflict_rows=0
            AND cleared.outcome NOT LIKE '%Duplicate%'
            AND cleared.outcome<>'Unknown layout')
      UNION
      SELECT CAST(CONCAT('batch:',CONVERT(varchar(36),b.import_batch_id)) AS nvarchar(520))
      FROM dbo.import_batches b
      WHERE b.status='Failed'
        AND b.started_utc>=DATEADD(hour,-24,SYSUTCDATETIME())
        AND NOT EXISTS (SELECT 1 FROM dbo.import_files f WHERE f.import_batch_id=b.import_batch_id)
    ) unresolved) AS failed_imports,
   backup_receipt.backup_sha256,drill_receipt.completed_utc AS last_drill_utc,
   drill_receipt.backup_sha256 AS last_drill_backup_sha256,
   (SELECT COUNT(*) FROM dbo.import_batches ob
    WHERE ob.status IN('Pending','Processing') AND ob.started_utc>=DATEADD(hour,-4,SYSUTCDATETIME())) AS open_imports
 FROM
 (SELECT CAST(SUM(size)*8.0/1024.0 AS decimal(18,2)) AS size_mb,
   CAST(CASE WHEN SUM(CASE WHEN max_size=-1 THEN 1 ELSE 0 END)>0 THEN NULL
             ELSE SUM(max_size)*8.0/1024.0 END AS decimal(18,2)) AS max_size_mb
  FROM sys.database_files) metrics
 OUTER APPLY
 (SELECT TOP(1) completed_utc,backup_sha256 FROM dbo.verified_operation_receipts
  WHERE operation_type='Backup' ORDER BY verified_operation_receipt_id DESC) backup_receipt
 OUTER APPLY
 (SELECT TOP(1) completed_utc,backup_sha256 FROM dbo.verified_operation_receipts
  WHERE operation_type='RestoreDrill' ORDER BY verified_operation_receipt_id DESC) drill_receipt;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.load_database_operational_health TO etp_automation,etp_owner,etp_store_manager,etp_viewer;
