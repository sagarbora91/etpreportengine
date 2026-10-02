-- 1.9.3. Settings > Users could not deactivate an account of a PC that no longer exists.
--
-- After a move to a new PC the old PC's accounts (TFRROWJLTULT009\Sagar on 2 October 2026)
-- stay in dbo.application_users, and their database users came along with the restore. To
-- deactivate one, dbo.configure_application_role first ran CREATE LOGIN ... FROM WINDOWS
-- for it, and Windows on the new PC cannot resolve an account of a machine that is gone:
-- "Windows NT user or group ... not found". The whole save was refused, so the only way
-- out was a SQL administrator's script (Migration 2026-10-02\deactivate-old-pc-users.sql).
--
-- Deactivating removes access; it never needs a login. Now, when @active = 0:
--   * no login is created (and no database user: one that does not exist has no access);
--   * the database user is found by SID, or - for an account Windows can no longer
--     resolve, so no SID - by its name (a Windows user or group, or a user left without
--     a login, the same three kinds the manual script handled);
--   * its role memberships are dropped and CONNECT is denied, exactly as before;
--   * ALTER ANY LOGIN is revoked only from a login that exists (there is nothing to revoke
--     from one that does not, and the REVOKE would fail on the missing name).
-- The application_users row and its history are written by the caller, unchanged, so the
-- audit trail (trg_application_users_history, operational_audit) is the same as for any
-- other change.
--
-- The procedure now also refuses, before it changes any permission, to deactivate or demote
-- the last active Owner (51230, the trigger's own number and text). The trigger still guards
-- the table; this stops a direct call - a SQL administrator's script - from stripping the
-- last Owner's roles while leaving the row active.
--
-- Giving access to an account Windows cannot find still fails, now with 51471 and a message
-- that says what to do instead of SQL Server's 15401.
--
-- Everything else is the body 0036 installed. 0022, 0032 and 0036 are untouched (the
-- migration runner is checksum fail-closed).
--
-- Numbered 0042 on branch fix/workpc-accounts-opsorder: main has 0040 and the import
-- engine branch takes 0041. Renumber when integrating if that changes; the file has no
-- dependency on 0041.

EXEC(N'CREATE OR ALTER PROCEDURE dbo.configure_application_role @identity nvarchar(200),@role varchar(30),@active bit
AS BEGIN
 SET NOCOUNT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51312,''Owner permission is required to manage access.'',1;
 IF @role NOT IN (''OWNER'',''STORE_MANAGER'',''VIEWER'') OR @active IS NULL THROW 51312,''Select a valid application role.'',1;
IF (@active=0 OR @role<>''OWNER'')
   AND EXISTS(SELECT 1 FROM dbo.application_users WHERE windows_identity=@identity AND role_code=''OWNER'' AND is_active=1)
   AND NOT EXISTS(SELECT 1 FROM dbo.application_users WITH(UPDLOCK,HOLDLOCK)
                  WHERE role_code=''OWNER'' AND is_active=1 AND windows_identity<>@identity)
  THROW 51230,''Keep at least one active Owner. Add another Owner before changing this account.'',1;
DECLARE @loginExists bit=CASE WHEN SUSER_ID(@identity) IS NULL THEN 0 ELSE 1 END;
IF @loginExists=0 AND @active=1
BEGIN
  DECLARE @createLogin nvarchar(max)=N''CREATE LOGIN ''+QUOTENAME(@identity)+N'' FROM WINDOWS'';
  BEGIN TRY
    EXEC(@createLogin);
  END TRY
  BEGIN CATCH
    IF ERROR_NUMBER()=15401
    BEGIN
      DECLARE @notFound nvarchar(2048)=N''Windows cannot find the account ''+@identity+N''. Check the name. An account of a PC that no longer exists cannot be given access; it can only be deactivated.'';
      THROW 51471,@notFound,1;
    END;
    THROW;
  END CATCH;
  SET @loginExists=1;
END;
DECLARE @principal sysname=(SELECT TOP(1) name FROM sys.database_principals WHERE sid=SUSER_SID(@identity));
IF @principal IS NULL AND @active=0
  SET @principal=(SELECT TOP(1) name FROM sys.database_principals WHERE name=@identity AND type IN (''U'',''G'',''S''));
IF @principal IS NULL AND @active=1
BEGIN
  DECLARE @createUser nvarchar(max)=N''CREATE USER ''+QUOTENAME(@identity)+N'' FOR LOGIN ''+QUOTENAME(@identity);
  EXEC(@createUser);
  SET @principal=@identity;
END;
IF @principal IS NOT NULL AND @principal<>N''dbo''
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
  IF OBJECT_ID(N''dbo.controlled_master_values'',N''U'') IS NOT NULL SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.controlled_master_values FROM ''+QUOTENAME(@principal)+N'';'';
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
IF @loginExists=1
BEGIN
  DECLARE @serverPermission nvarchar(max)=N''USE [master]; ''+CASE WHEN @role=''OWNER'' AND @active=1 THEN N''GRANT ALTER ANY LOGIN TO '' ELSE N''REVOKE ALTER ANY LOGIN TO '' END+QUOTENAME(@identity)+N'';'';
  EXEC(@serverPermission);
END;
END');
