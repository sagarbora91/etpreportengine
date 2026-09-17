-- The Database and recovery block tells the owner "Open Import, then Problems, to see
-- them" beside its failed-import count. That promise could not be kept: the count came
-- from dbo.import_batches while the Problems list is built from dbo.import_attempts.
--
-- Two ways it lied. A file that failed and was retried still sits in import_attempts as
-- a problem, but produced no failed batch, so Problems showed a row while health said
-- zero. And a batch that failed before recording any file counted as a failure with
-- nothing for the owner to open.
--
-- Count what the owner will actually find, using the same rule the Problems list uses:
-- a problem is a failure, a conflict, a duplicate or an unknown layout, and it stops
-- being a problem once a later clean import of the same file supersedes it.
--
-- 0023 introduced this procedure and stays untouched; the migration runner is checksum
-- fail-closed, so a committed migration must remain byte-identical for every database
-- that has already applied it.
EXEC(N'CREATE OR ALTER PROCEDURE dbo.load_database_operational_health
AS
BEGIN
 SET NOCOUNT ON;
 SELECT metrics.size_mb,metrics.max_size_mb,backup_receipt.completed_utc,
   (SELECT COUNT(*) FROM (
      SELECT DISTINCT CAST(a.file_name AS nvarchar(520)) AS problem_key
      FROM dbo.import_attempts a
      WHERE a.recorded_utc>=DATEADD(hour,-24,SYSUTCDATETIME())
        AND (a.outcome=''Failed''
             OR a.conflict_rows>0
             OR a.outcome LIKE ''%Duplicate%''
             OR a.outcome=''Unknown layout'')
        AND NOT EXISTS (
          SELECT 1 FROM dbo.import_attempts cleared
          WHERE cleared.file_name=a.file_name
            AND cleared.recorded_utc>a.recorded_utc
            AND cleared.outcome<>''Failed''
            AND cleared.conflict_rows=0
            AND cleared.outcome NOT LIKE ''%Duplicate%''
            AND cleared.outcome<>''Unknown layout'')
      UNION
      SELECT CAST(CONCAT(''batch:'',CONVERT(varchar(36),b.import_batch_id)) AS nvarchar(520))
      FROM dbo.import_batches b
      WHERE b.status=''Failed''
        AND b.started_utc>=DATEADD(hour,-24,SYSUTCDATETIME())
        AND NOT EXISTS (SELECT 1 FROM dbo.import_files f WHERE f.import_batch_id=b.import_batch_id)
    ) unresolved) AS failed_imports,
   backup_receipt.backup_sha256,drill_receipt.completed_utc AS last_drill_utc,
   drill_receipt.backup_sha256 AS last_drill_backup_sha256
 FROM
 (SELECT CAST(SUM(size)*8.0/1024.0 AS decimal(18,2)) AS size_mb,
   CAST(CASE WHEN SUM(CASE WHEN max_size=-1 THEN 1 ELSE 0 END)>0 THEN NULL
             ELSE SUM(max_size)*8.0/1024.0 END AS decimal(18,2)) AS max_size_mb
  FROM sys.database_files) metrics
 OUTER APPLY
 (SELECT TOP(1) completed_utc,backup_sha256 FROM dbo.verified_operation_receipts
  WHERE operation_type=''Backup'' ORDER BY verified_operation_receipt_id DESC) backup_receipt
 OUTER APPLY
 (SELECT TOP(1) completed_utc,backup_sha256 FROM dbo.verified_operation_receipts
  WHERE operation_type=''RestoreDrill'' ORDER BY verified_operation_receipt_id DESC) drill_receipt;
END');

GRANT EXECUTE ON dbo.load_database_operational_health TO etp_owner,etp_store_manager,etp_viewer,etp_automation;
