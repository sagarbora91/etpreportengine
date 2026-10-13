CREATE OR ALTER PROCEDURE dbo.record_verified_operation
 @operation varchar(32), @backup_sha256 varchar(128)
AS
BEGIN
 SET NOCOUNT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_automation'),0)<>1
    AND COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51341,'The dedicated operations account or an Owner is required.',1;
 IF @operation IS NULL OR NOT
    ((@operation COLLATE Latin1_General_100_BIN2='Backup' AND DATALENGTH(@operation)=6)
     OR (@operation COLLATE Latin1_General_100_BIN2='RestoreDrill' AND DATALENGTH(@operation)=12))
  THROW 51342,'Choose a supported verified operation.',1;
 IF @backup_sha256 IS NULL OR DATALENGTH(@backup_sha256)<>64
    OR @backup_sha256 COLLATE Latin1_General_100_BIN2 LIKE '%[^0-9A-Fa-f]%'
  THROW 51342,'A complete backup verification hash is required.',1;
 INSERT dbo.verified_operation_receipts(operation_type,backup_sha256,recorded_by)
 VALUES(@operation,UPPER(@backup_sha256),SUSER_SNAME());
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.record_verified_operation TO etp_automation,etp_owner;
