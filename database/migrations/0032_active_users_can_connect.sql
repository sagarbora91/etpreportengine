-- P4-14. Settings > Users could not produce a working Store Manager or Viewer.
--
-- configure_application_role ended an active user's provisioning with REVOKE CONNECT. The
-- intent is clear - clear any DENY CONNECT left from an earlier deactivation - but REVOKE
-- removes a GRANT as well as a DENY, and CREATE USER grants CONNECT implicitly. So every
-- active Store Manager and Viewer was left with no CONNECT at all and could not enter the
-- database. Owners were spared only because db_owner implies CONNECT. Nothing grants
-- CONNECT to any role, so there was no second path in.
--
-- 0022 then ran every existing user through the procedure, which is what locked out
-- NT AUTHORITY\SYSTEM - the account the scheduled Automated Operations and Daily Backup
-- tasks run as. 0012 had given it CONNECT through CREATE USER; the first database to be
-- migrated past 0022 lost it, silently.
--
-- GRANT is the statement that was meant: it gives CONNECT, and it replaces a DENY rather
-- than stacking beside one. Deactivation still DENYs. This is the only change to the
-- procedure; its body is otherwise byte-for-byte the one 0022 installed.
--
-- 0022 is untouched. The migration runner is checksum fail-closed, so a committed migration
-- has to stay byte-identical for every database that has already applied it.

EXEC(N'CREATE OR ALTER PROCEDURE dbo.configure_application_role @identity nvarchar(200),@role varchar(30),@active bit
AS BEGIN
 SET NOCOUNT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51312,''Owner permission is required to manage access.'',1;
 IF @role NOT IN (''OWNER'',''STORE_MANAGER'',''VIEWER'') OR @active IS NULL THROW 51312,''Select a valid application role.'',1;
IF SUSER_ID(@identity) IS NULL
BEGIN
  DECLARE @createLogin nvarchar(max)=N''CREATE LOGIN ''+QUOTENAME(@identity)+N'' FROM WINDOWS'';
  EXEC(@createLogin);
END;
DECLARE @principal sysname=(SELECT TOP(1) name FROM sys.database_principals WHERE sid=SUSER_SID(@identity));
IF @principal IS NULL
BEGIN
  DECLARE @createUser nvarchar(max)=N''CREATE USER ''+QUOTENAME(@identity)+N'' FOR LOGIN ''+QUOTENAME(@identity);
  EXEC(@createUser);
  SET @principal=@identity;
END;
IF @principal<>N''dbo''
BEGIN
  DECLARE @membership nvarchar(max)=N'''';
  IF IS_ROLEMEMBER(N''etp_owner'',@principal)=1 SET @membership+=N''ALTER ROLE etp_owner DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''etp_store_manager'',@principal)=1 SET @membership+=N''ALTER ROLE etp_store_manager DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''etp_viewer'',@principal)=1 SET @membership+=N''ALTER ROLE etp_viewer DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''etp_automation'',@principal)=1 SET @membership+=N''ALTER ROLE etp_automation DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''db_owner'',@principal)=1 SET @membership+=N''ALTER ROLE db_owner DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''db_datawriter'',@principal)=1 SET @membership+=N''ALTER ROLE db_datawriter DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''db_datareader'',@principal)=1 SET @membership+=N''ALTER ROLE db_datareader DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''db_backupoperator'',@principal)=1 SET @membership+=N''ALTER ROLE db_backupoperator DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE DELETE ON SCHEMA::dbo FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.application_users FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.application_user_history FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.controlled_master_values FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.controlled_master_history FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.watch_folder_settings FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.stores FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.schema_migrations FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.report_pack_schedules FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE UPDATE,DELETE ON dbo.operational_audit FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE UPDATE,DELETE ON dbo.automation_runs FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.product_settings'',N''U'') IS NOT NULL SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.product_settings FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.sharing_contacts'',N''U'') IS NOT NULL SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.sharing_contacts FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.kpi_catalogue'',N''U'') IS NOT NULL SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.kpi_catalogue FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.approval_requests'',N''U'') IS NOT NULL SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.approval_requests FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.controlled_adjustments'',N''U'') IS NOT NULL SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.controlled_adjustments FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.accounting_mappings'',N''U'') IS NOT NULL SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.accounting_mappings FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.accounting_batches'',N''U'') IS NOT NULL SET @membership+=N''REVOKE UPDATE,DELETE ON dbo.accounting_batches FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.accounting_entries'',N''U'') IS NOT NULL SET @membership+=N''REVOKE UPDATE,DELETE ON dbo.accounting_entries FROM ''+QUOTENAME(@principal)+N'';'';
  IF @role=''OWNER'' AND @active=1 SET @membership+=N''ALTER ROLE db_owner ADD MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF @role=''STORE_MANAGER'' AND @active=1 SET @membership+=N''ALTER ROLE etp_store_manager ADD MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF @role=''VIEWER'' AND @active=1 SET @membership+=N''ALTER ROLE etp_viewer ADD MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF @role=''OWNER'' AND @active=1 SET @membership+=N''ALTER ROLE etp_owner ADD MEMBER ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=CASE WHEN @active=1 THEN N''GRANT CONNECT TO '' ELSE N''DENY CONNECT TO '' END+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT ON dbo.operational_audit FROM ''+QUOTENAME(@principal)+N'';'';
  EXEC(@membership);
END;
DECLARE @serverPermission nvarchar(max)=N''USE [master]; ''+CASE WHEN @role=''OWNER'' AND @active=1 THEN N''GRANT ALTER ANY LOGIN TO '' ELSE N''REVOKE ALTER ANY LOGIN TO '' END+QUOTENAME(@identity)+N'';'';
EXEC(@serverPermission);
END');

-- Give back CONNECT to every active application user the old procedure locked out. An
-- inactive user keeps its DENY; a user whose login no longer exists is left alone
-- (SUSER_SID resolves a Windows name whether or not SQL has a login for it, so the login
-- is checked separately). The account running this migration is skipped: it is plainly
-- connected already, and SQL ignores a GRANT to yourself with a "cannot grant ... to
-- yourself" warning that would only mislead whoever reads the upgrade log.
DECLARE @repair nvarchar(max) = N'';
SELECT @repair += N'GRANT CONNECT TO ' + QUOTENAME(dp.name) + N';'
FROM dbo.application_users u
JOIN sys.database_principals dp ON dp.sid = SUSER_SID(u.windows_identity)
WHERE u.is_active = 1
  AND SUSER_ID(u.windows_identity) IS NOT NULL
  AND dp.name <> N'dbo'
  AND dp.principal_id <> DATABASE_PRINCIPAL_ID()
  AND NOT EXISTS (SELECT 1 FROM sys.database_permissions p
                  WHERE p.grantee_principal_id = dp.principal_id
                    AND p.permission_name = N'CONNECT'
                    AND p.state IN ('G','W'));
IF LEN(@repair) > 0 EXEC (@repair);
