CREATE OR ALTER PROCEDURE dbo.archive_operational_audit @retention_days int=365
AS
BEGIN
 SET NOCOUNT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51312,'Owner permission is required for maintenance.',1;
 IF @retention_days<30 THROW 51312,'Keep at least thirty days of recent audit history.',1;
 SET IDENTITY_INSERT dbo.operational_audit_archive ON;
 INSERT dbo.operational_audit_archive(operational_audit_id,event_utc,event_type,outcome,safe_detail,application_version,actor_name)
 SELECT a.operational_audit_id,a.event_utc,a.event_type,a.outcome,a.safe_detail,a.application_version,a.actor_name
 FROM dbo.operational_audit a WHERE a.event_utc<DATEADD(day,-@retention_days,SYSUTCDATETIME())
 AND NOT EXISTS(SELECT 1 FROM dbo.operational_audit_archive h WITH(UPDLOCK,HOLDLOCK) WHERE h.operational_audit_id=a.operational_audit_id);
 SET IDENTITY_INSERT dbo.operational_audit_archive OFF;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.archive_operational_audit TO etp_owner;
