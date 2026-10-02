-- 1.9.3. An Owner can add and change staff in Settings > Users without "Run as administrator".
--
-- REQUIRES 0042_deactivate_unresolvable_accounts (branch fix/workpc-accounts-opsorder). The
-- procedure below is 0042's body with the changes listed here, so the two rewrites do not
-- compete: apply 0042 first. If 0042 is renumbered when the branches are merged, this file
-- must still sort after it. (0042's unit test that no migration after 0036 but 0042
-- redefines the procedure has to admit this file on merge.)
--
-- Why. dbo.configure_application_role ends every change with a server-level GRANT or
-- REVOKE of ALTER ANY LOGIN. SQL Server lets only sysadmin, CONTROL SERVER or a login that
-- holds the permission WITH GRANT OPTION do that. Since 1.9.2 the Owner is sysadmin only
-- through BUILTIN\Administrators, which Windows removes from an unelevated token, and holds
-- plain ALTER ANY LOGIN at most, so an Owner who opened ETP normally got SQL error 4613
-- ("Grantor does not have GRANT permission") on every save (Workpc, 2 October 2026).
-- Sagar's decision, 2 October 2026 (option b): Owners hold ALTER ANY LOGIN WITH GRANT
-- OPTION. That is the Owner's sanctioned power: an Owner can create a SQL Server login for
-- any Windows account and make it an Owner too, without elevation. Non-owners never hold it.
--
-- What changed in the procedure (everything else is 0042's text):
--   * An active OWNER is granted ALTER ANY LOGIN WITH GRANT OPTION (was: plain GRANT).
--   * Anyone else - a demoted or deactivated Owner, a Store Manager, a Viewer - has it
--     revoked WITH CASCADE (was: plain REVOKE). CASCADE is required: SQL Server refuses to
--     revoke a grantable permission without it (error 4611), and it also takes back what that
--     account granted onward, so a demoted Owner leaves no logins behind holding a right
--     that came only from them.
--   * A cascade can take the right from Owners who stay Owners (one Owner made another).
--     After the REVOKE every remaining active Owner with a login that lacks the grant
--     option is granted it again, in the same transaction. If the account making the change
--     lost its own right that way it cannot give it back to itself, so the change is
--     refused (51474) instead.
--   * If the REVOKE left the account any ALTER ANY LOGIN (it was granted by somebody the
--     caller cannot revoke for), the change is refused (51473) rather than leaving a
--     non-owner able to manage logins.
--   * SQL Server never lets a login grant, deny or revoke a permission on itself (error
--     4627, raised as a warning: the statement is skipped and the batch goes on). That is
--     the usual case in setup and in restore-etp-database.ps1, which run as the Owner they
--     provision. Matching by SID (SUSER_SID), the procedure no longer issues that statement:
--       - for an active OWNER it skips the GRANT and PRINTs what is missing when the
--         account does not already hold the grant option. A sysadmin session does not need
--         it, but the same person opening ETP unelevated later does, and only a different
--         SQL administrator can give it (docs\OPERATIONS.md, "Owners and SQL Server logins");
--       - removing your own Owner access while you hold any ALTER ANY LOGIN is refused
--         (51472) before anything changes, because the REVOKE could not take effect.
--   The match is by SID, not by the login the session started with: on Workpc the restore
--   helper, connected through BUILTIN\Administrators, created WORKPC\Sagar's login and the
--   GRANT to that new login in the same session was still skipped as "yourself" (Migration
--   2026-10-02\WORKPC-SETUP-2026-10-02.md, step 7).
--   * 0042's deactivate-by-name fallback (for an account Windows can no longer resolve)
--     now takes a database user only when it is truly orphaned: no server login has its
--     SID and no other active application user resolves to it. A local account renamed
--     from WORKPC\Shop to WORKPC\Sagar keeps its SID and may keep a database user named
--     [WORKPC\Shop]; deactivating the stale WORKPC\Shop row used to strip that live user's
--     roles and deny it CONNECT. Now the stale row is deactivated by the caller as usual
--     and the live user is left alone (security review 1.9.3, F2).
-- Every ETP caller (Settings > Users, setup, the restore helper) runs the procedure in a
-- transaction, so a refusal after the REVOKE leaves nothing changed. A SQL administrator
-- calling it by hand must do the same.
--
-- Existing installs: at the end of this file every active Owner that has a login is granted
-- ALTER ANY LOGIN WITH GRANT OPTION, when the account running the migration can grant it
-- (setup runs migrations elevated). The account running the migration is skipped, for the
-- reason above; setup logs a NOTE when it is an Owner left without the grant option.
--
-- 0022, 0032, 0036 and 0042 are untouched (the migration runner is checksum fail-closed).

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
IF (@active=0 OR @role<>''OWNER'') AND SUSER_SID(@identity)=SUSER_SID()
   AND EXISTS(SELECT 1 FROM sys.server_permissions WHERE class=100 AND grantee_principal_id=SUSER_ID(@identity) AND permission_name=N''ALTER ANY LOGIN'')
  THROW 51472,''You cannot take away your own Owner access. Ask another Owner to change your account.'',1;
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
  SET @principal=(SELECT TOP(1) dp.name FROM sys.database_principals dp WHERE dp.name=@identity AND dp.type IN (''U'',''G'',''S'')
    AND NOT EXISTS(SELECT 1 FROM sys.server_principals sp WHERE sp.sid=dp.sid)
    AND NOT EXISTS(SELECT 1 FROM dbo.application_users u WHERE u.is_active=1 AND u.windows_identity<>@identity AND SUSER_SID(u.windows_identity)=dp.sid));
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
  DECLARE @grantee int=SUSER_ID(@identity);
  DECLARE @self bit=CASE WHEN SUSER_SID(@identity)=SUSER_SID() THEN 1 ELSE 0 END;
  DECLARE @serverPermission nvarchar(max);
  IF @role=''OWNER'' AND @active=1
  BEGIN
    IF @self=0
    BEGIN
      SET @serverPermission=N''USE [master]; GRANT ALTER ANY LOGIN TO ''+QUOTENAME(@identity)+N'' WITH GRANT OPTION;'';
      EXEC(@serverPermission);
    END;
    IF NOT EXISTS(SELECT 1 FROM sys.server_permissions WHERE class=100 AND grantee_principal_id=@grantee AND permission_name=N''ALTER ANY LOGIN'' AND state=''W'')
      PRINT N''ETP: ''+@identity+N'' is an Owner without ALTER ANY LOGIN WITH GRANT OPTION, so it can change users only from an ETP started as administrator. ''
        +CASE WHEN @self=1 THEN N''SQL Server does not let a login grant a permission to itself. '' ELSE N'''' END
        +N''Another SQL administrator must grant it: docs\OPERATIONS.md, Owners and SQL Server logins.'';
  END
  ELSE IF @self=0
  BEGIN
    DECLARE @owners TABLE(principal_id int NOT NULL PRIMARY KEY,login_name sysname NOT NULL,login_sid varbinary(85) NOT NULL,had_grant_option bit NOT NULL);
    INSERT @owners(principal_id,login_name,login_sid,had_grant_option)
    SELECT sp.principal_id,sp.name,sp.sid,
      CASE WHEN EXISTS(SELECT 1 FROM sys.server_permissions p WHERE p.class=100 AND p.grantee_principal_id=sp.principal_id AND p.permission_name=N''ALTER ANY LOGIN'' AND p.state=''W'') THEN 1 ELSE 0 END
    FROM sys.server_principals sp
    WHERE sp.principal_id<>@grantee
      AND sp.principal_id IN(SELECT SUSER_ID(u.windows_identity) FROM dbo.application_users u WHERE u.role_code=''OWNER'' AND u.is_active=1);
    SET @serverPermission=N''USE [master]; REVOKE ALTER ANY LOGIN FROM ''+QUOTENAME(@identity)+N'' CASCADE;'';
    EXEC(@serverPermission);
    IF EXISTS(SELECT 1 FROM sys.server_permissions WHERE class=100 AND grantee_principal_id=@grantee AND permission_name=N''ALTER ANY LOGIN'')
      THROW 51473,''SQL Server kept the right of this account to manage logins (ALTER ANY LOGIN) because another account granted it. Nothing was changed. Start ETP with Run as administrator and save again.'',1;
    IF EXISTS(SELECT 1 FROM @owners o WHERE o.login_sid=SUSER_SID() AND o.had_grant_option=1
              AND NOT EXISTS(SELECT 1 FROM sys.server_permissions p WHERE p.class=100 AND p.grantee_principal_id=o.principal_id AND p.permission_name=N''ALTER ANY LOGIN'' AND p.state=''W''))
      THROW 51474,''This account gave you your own right to manage logins, so taking away its Owner access would take yours too. Nothing was changed. See docs\OPERATIONS.md, Owners and SQL Server logins.'',1;
    DECLARE @regrant nvarchar(max)=N'''';
    SELECT @regrant+=N''GRANT ALTER ANY LOGIN TO ''+QUOTENAME(o.login_name)+N'' WITH GRANT OPTION;'' FROM @owners o
    WHERE o.login_sid<>SUSER_SID()
      AND NOT EXISTS(SELECT 1 FROM sys.server_permissions p WHERE p.class=100 AND p.grantee_principal_id=o.principal_id AND p.permission_name=N''ALTER ANY LOGIN'' AND p.state=''W'');
    IF LEN(@regrant)>0
    BEGIN
      SET @regrant=N''USE [master]; ''+@regrant;
      EXEC(@regrant);
    END;
  END;
END;
END');

-- Existing installs. Every active Owner that has a login gets the grant option, unless it
-- holds it already. Only an account that can grant it does this (setup runs migrations
-- elevated); anyone else leaves it for setup, which logs a NOTE. The account running the
-- migration is skipped: SQL Server would skip it anyway, with a "cannot grant ... to
-- yourself" warning that reads in a log as if the upgrade had failed. Non-owners are not
-- touched: the procedure has always revoked ALTER ANY LOGIN from them.
IF COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),0)=1
   OR COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,N'CONTROL SERVER'),0)=1
   OR EXISTS(SELECT 1 FROM sys.server_permissions p JOIN sys.login_token t ON t.principal_id=p.grantee_principal_id
             WHERE p.class=100 AND p.permission_name=N'ALTER ANY LOGIN' AND p.state='W')
BEGIN
  DECLARE @ownerGrants nvarchar(max)=N'';
  SELECT @ownerGrants+=N'GRANT ALTER ANY LOGIN TO '+QUOTENAME(sp.name)+N' WITH GRANT OPTION;'
  FROM sys.server_principals sp
  WHERE sp.principal_id IN(SELECT SUSER_ID(u.windows_identity) FROM dbo.application_users u WHERE u.role_code='OWNER' AND u.is_active=1)
    AND sp.sid<>SUSER_SID()
    AND NOT EXISTS(SELECT 1 FROM sys.server_permissions p WHERE p.class=100 AND p.grantee_principal_id=sp.principal_id
                   AND p.permission_name=N'ALTER ANY LOGIN' AND p.state='W');
  IF LEN(@ownerGrants)>0
  BEGIN
    SET @ownerGrants=N'USE [master]; '+@ownerGrants;
    EXEC(@ownerGrants);
  END;
END;
