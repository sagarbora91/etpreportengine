SET XACT_ABORT ON;

IF DATABASE_PRINCIPAL_ID(N'etp_automation') IS NULL
 EXEC(N'CREATE ROLE etp_automation AUTHORIZATION dbo');

-- Only the dedicated operations identity and Owners attest verified receipts.
-- Generic audit events are intentionally not a source of trusted backup health.
CREATE TABLE dbo.verified_operation_receipts
(
 verified_operation_receipt_id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
 operation_type varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
 completed_utc datetime2(7) NOT NULL CONSTRAINT DF_verified_operation_receipts_completed DEFAULT SYSUTCDATETIME(),
 backup_sha256 varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 recorded_by nvarchar(128) NOT NULL,
 CONSTRAINT CK_verified_operation_receipts_type CHECK(operation_type IN ('Backup','RestoreDrill')),
 CONSTRAINT CK_verified_operation_receipts_hash CHECK(DATALENGTH(backup_sha256)=64 AND backup_sha256 NOT LIKE '%[^0-9A-F]%')
);
CREATE INDEX IX_verified_operation_receipts_latest
 ON dbo.verified_operation_receipts(operation_type,verified_operation_receipt_id DESC)
 INCLUDE(completed_utc,backup_sha256);

EXEC(N'CREATE OR ALTER TRIGGER dbo.tr_verified_operation_receipts_append_only
 ON dbo.verified_operation_receipts INSTEAD OF UPDATE,DELETE AS
 BEGIN THROW 51340,''Verified operations history is append-only.'',1; END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.record_verified_operation
 @operation varchar(32), @backup_sha256 varchar(128)
AS
BEGIN
 SET NOCOUNT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_automation''),0)<>1
    AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51341,''The dedicated operations account or an Owner is required.'',1;
 IF @operation IS NULL OR NOT
    ((@operation COLLATE Latin1_General_100_BIN2=''Backup'' AND DATALENGTH(@operation)=6)
     OR (@operation COLLATE Latin1_General_100_BIN2=''RestoreDrill'' AND DATALENGTH(@operation)=12))
  THROW 51342,''Choose a supported verified operation.'',1;
 IF @backup_sha256 IS NULL OR DATALENGTH(@backup_sha256)<>64
    OR @backup_sha256 COLLATE Latin1_General_100_BIN2 LIKE ''%[^0-9A-Fa-f]%''
  THROW 51342,''A complete backup verification hash is required.'',1;
 INSERT dbo.verified_operation_receipts(operation_type,backup_sha256,recorded_by)
 VALUES(@operation,UPPER(@backup_sha256),SUSER_SNAME());
END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.load_database_operational_health
AS
BEGIN
 SET NOCOUNT ON;
 SELECT metrics.size_mb,metrics.max_size_mb,backup_receipt.completed_utc,
   (SELECT COUNT(*) FROM dbo.import_batches
    WHERE status=''Failed'' AND started_utc>=DATEADD(hour,-24,SYSUTCDATETIME())) AS failed_imports,
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

DENY SELECT,INSERT,UPDATE,DELETE ON dbo.verified_operation_receipts TO etp_store_manager,etp_viewer,etp_automation;
GRANT EXECUTE ON dbo.record_verified_operation TO etp_owner,etp_automation;
GRANT EXECUTE ON dbo.load_database_operational_health TO etp_owner,etp_store_manager,etp_viewer,etp_automation;
