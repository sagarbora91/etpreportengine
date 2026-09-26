-- Phase 5 retires the unused generic master table; keep its audit history.
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
DECLARE @serverPermission nvarchar(max)=N''USE [master]; ''+CASE WHEN @role=''OWNER'' AND @active=1 THEN N''GRANT ALTER ANY LOGIN TO '' ELSE N''REVOKE ALTER ANY LOGIN TO '' END+QUOTENAME(@identity)+N'';'';
EXEC(@serverPermission);
END');
IF OBJECT_ID(N'dbo.controlled_master_values',N'U') IS NOT NULL DROP TABLE dbo.controlled_master_values;
