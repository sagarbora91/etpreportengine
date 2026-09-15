SET XACT_ABORT ON;
IF DATABASE_PRINCIPAL_ID(N'etp_owner') IS NULL EXEC(N'CREATE ROLE etp_owner AUTHORIZATION dbo');
IF DATABASE_PRINCIPAL_ID(N'etp_store_manager') IS NULL EXEC(N'CREATE ROLE etp_store_manager AUTHORIZATION dbo');
IF DATABASE_PRINCIPAL_ID(N'etp_viewer') IS NULL EXEC(N'CREATE ROLE etp_viewer AUTHORIZATION dbo');

-- SQL administrators retain the documented break-glass path. Application Owners
-- use etp_owner membership; a client flag or elevated Windows token is irrelevant.
EXEC(N'CREATE OR ALTER TRIGGER dbo.tr_daily_reporting_day_lock ON dbo.daily_reporting_days
AFTER UPDATE, DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted WHERE status=''LOCKED'')
 BEGIN
  IF UPDATE(store_code) OR UPDATE(business_date)
   THROW 51300,''A locked business date cannot be moved.'',1;
  IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.store_code=d.store_code AND i.business_date=d.business_date
    WHERE d.status=''LOCKED'' AND i.store_code IS NULL)
   THROW 51300,''A locked business date cannot be deleted.'',1;
  IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.store_code=d.store_code AND i.business_date=d.business_date
    WHERE d.status=''LOCKED'' AND i.status<>''LOCKED'')
  BEGIN
   IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
    THROW 51301,''Owner permission is required to reopen a business date.'',1;
   IF NOT UPDATE(reopen_reason) OR EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.store_code=d.store_code AND i.business_date=d.business_date
      WHERE d.status=''LOCKED'' AND (i.status<>''OPEN'' OR NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(i.reopen_reason,CHAR(9),'' ''),CHAR(10),'' ''),CHAR(13),'' ''))),'''') IS NULL))
    THROW 51302,''Enter a reason before reopening the business date.'',1;
   INSERT dbo.daily_reporting_events(store_code,business_date,event_type,performed_by,reason)
    SELECT i.store_code,i.business_date,''DayReopened'',SUSER_SNAME(),i.reopen_reason FROM deleted d
    JOIN inserted i ON i.store_code=d.store_code AND i.business_date=d.business_date
    WHERE d.status=''LOCKED'' AND i.status=''OPEN'';
  END;
 END;
END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.reopen_reporting_day
 @store varchar(30), @date date, @reason nvarchar(500)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51301,''Owner permission is required to reopen a business date.'',1;
 IF NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@reason,CHAR(9),'' ''),CHAR(10),'' ''),CHAR(13),'' ''))),'''') IS NULL
  THROW 51302,''Enter a reason before reopening the business date.'',1;
 UPDATE dbo.daily_reporting_days WITH(UPDLOCK,HOLDLOCK)
  SET status=''OPEN'',reopened_by=SUSER_SNAME(),reopened_utc=SYSUTCDATETIME(),reopen_reason=@reason
  WHERE store_code=@store AND business_date=@date AND status=''LOCKED'';
 IF @@ROWCOUNT<>1 THROW 51024,''Only a finalised day can be reopened.'',1;
END');
GRANT EXECUTE ON dbo.reopen_reporting_day TO etp_owner;

DECLARE @principal sysname, @sql nvarchar(max);
DECLARE owners CURSOR LOCAL FAST_FORWARD FOR
 SELECT p.name FROM dbo.application_users u JOIN sys.database_principals p ON p.sid=SUSER_SID(u.windows_identity)
 WHERE u.role_code='OWNER' AND u.is_active=1 AND p.name<>'dbo';
OPEN owners; FETCH NEXT FROM owners INTO @principal;
WHILE @@FETCH_STATUS=0
BEGIN
 IF IS_ROLEMEMBER(N'etp_owner',@principal)<>1
 BEGIN SET @sql=N'ALTER ROLE etp_owner ADD MEMBER '+QUOTENAME(@principal); EXEC(@sql); END;
 FETCH NEXT FROM owners INTO @principal;
END;
CLOSE owners; DEALLOCATE owners;

-- Keep the authority event separate from client-entered descriptions. Other
-- event types retain their existing payload but are stamped by SQL.
EXEC(N'CREATE OR ALTER TRIGGER dbo.tr_daily_reporting_events_actor ON dbo.daily_reporting_events
INSTEAD OF INSERT AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted WHERE event_type=''DayReopened'')
  AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51301,''Owner permission is required for a day reopening event.'',1;
 INSERT dbo.daily_reporting_events(store_code,business_date,event_type,performed_by,reason,event_utc)
 SELECT store_code,business_date,event_type,SUSER_SNAME(),reason,SYSUTCDATETIME() FROM inserted;
END');
