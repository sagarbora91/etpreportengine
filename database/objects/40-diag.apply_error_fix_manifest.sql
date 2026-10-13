CREATE OR ALTER PROCEDURE diag.apply_error_fix_manifest @manifest nvarchar(max),@running_version varchar(20)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @running bigint=diag.version_key(@running_version);
 IF @running IS NULL THROW 51804,'The fix manifest was refused: the running version is not valid.',1;
 IF @manifest IS NULL OR ISJSON(@manifest)<>1
    OR TRY_CONVERT(int,JSON_VALUE(@manifest,'$.manifest'))IS NULL OR TRY_CONVERT(int,JSON_VALUE(@manifest,'$.manifest'))<>1
    OR COALESCE(LEFT(JSON_QUERY(@manifest,'$.fixes'),1),N'')<>N'['
  THROW 51804,'The fix manifest is not valid.',1;

 DECLARE @lines TABLE(ordinal int NOT NULL PRIMARY KEY,fingerprint char(64) NULL,fixed_in varchar(20) NULL,fixed_key bigint NULL,
   ticket varchar(12) NULL,is_valid bit NOT NULL);
 INSERT @lines(ordinal,fingerprint,fixed_in,fixed_key,ticket,is_valid)
 SELECT CONVERT(int,a.[key]),
   CASE WHEN LEN(j.fingerprint)=64 THEN j.fingerprint END,
   CASE WHEN LEN(j.fixed_in)<=20 THEN j.fixed_in END,
   diag.version_key(CASE WHEN LEN(j.fixed_in)<=20 THEN j.fixed_in END),
   CASE WHEN LEN(j.ticket)<=12 THEN NULLIF(j.ticket,N'') END,
   CASE WHEN a.[type]=5
     AND LEN(j.fingerprint)=64 AND DATALENGTH(j.fingerprint)=128 AND j.fingerprint COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9a-f]%'
     AND diag.version_key(CASE WHEN LEN(j.fixed_in)<=20 THEN j.fixed_in END) IS NOT NULL
     AND (NULLIF(j.ticket,N'') IS NULL OR diag.is_token(j.ticket,12)=1)
   THEN 1 ELSE 0 END
 FROM OPENJSON(@manifest,'$.fixes') a
 OUTER APPLY OPENJSON(CASE WHEN a.[type]=5 THEN a.value ELSE N'{}' END) WITH(
   fingerprint nvarchar(max) '$.fingerprint',fixed_in nvarchar(max) '$.fixedIn',ticket nvarchar(max) '$.ticket') j;
 IF (SELECT COUNT(*) FROM @lines)>5000 OR EXISTS(SELECT 1 FROM @lines WHERE is_valid=0)
  THROW 51804,'The fix manifest is not valid: a line has a bad fingerprint, version or ticket.',1;

 DECLARE @now datetime2(3)=SYSUTCDATETIME(),@lines_count int=(SELECT COUNT(*) FROM @lines),
         @marked int=0,@already int=0,@not_found int=0,@skipped int=0;
 DECLARE @fp char(64),@fixed_in varchar(20),@fixed_key bigint,@ticket varchar(12),@id int,@status varchar(10),@current_key bigint,
         @seen_version varchar(20),@seen_utc datetime2(3);
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @lock int;
  EXEC @lock=sys.sp_getapplock @Resource=N'ETP.ErrorRegister',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
  IF @lock<0 THROW 51806,'The error register is busy. Try again.',1;
  DECLARE fixes CURSOR LOCAL FAST_FORWARD FOR SELECT fingerprint,fixed_in,fixed_key,ticket FROM @lines ORDER BY ordinal;
  OPEN fixes;
  FETCH NEXT FROM fixes INTO @fp,@fixed_in,@fixed_key,@ticket;
  WHILE @@FETCH_STATUS=0
  BEGIN
   SET @id=NULL; SET @status=NULL; SET @current_key=NULL; SET @seen_version=NULL; SET @seen_utc=NULL;
   IF @fixed_key>@running SET @skipped+=1;
   ELSE
   BEGIN
    SELECT @id=COALESCE(merged_into_id,error_register_id) FROM diag.error_register WHERE fingerprint=@fp;
    IF @id IS NULL SET @not_found+=1;
    ELSE
    BEGIN
     SELECT @status=status,@current_key=fixed_in_version_key FROM diag.error_register WHERE error_register_id=@id;
     IF @status='Fixed' OR (@status='Recurred' AND @current_key>=@fixed_key) SET @already+=1;
     ELSE IF @status='Expected' SET @skipped+=1;
     ELSE
     BEGIN
      UPDATE diag.error_register SET status='Fixed',fixed_in_version=@fixed_in,fixed_in_version_key=@fixed_key,
       fixed_ticket=COALESCE(@ticket,fixed_ticket),fixed_utc=@now,status_changed_utc=@now,status_changed_by=N'FixManifest'
      WHERE error_register_id=@id;
      SELECT TOP(1) @seen_version=app_version,@seen_utc=last_seen_utc FROM diag.error_register_versions
      WHERE error_register_id=@id AND app_version_key>=@fixed_key ORDER BY app_version_key DESC,last_seen_utc DESC;
      IF @seen_version IS NOT NULL
       UPDATE diag.error_register SET status='Recurred',recurred_version=@seen_version,recurred_utc=@seen_utc
       WHERE error_register_id=@id;
      SET @marked+=1;
     END;
    END;
   END;
   FETCH NEXT FROM fixes INTO @fp,@fixed_in,@fixed_key,@ticket;
  END;
  CLOSE fixes; DEALLOCATE fixes;
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;

 SELECT @lines_count lines,@marked entries_marked_fixed,@already already_fixed,@not_found not_found,@skipped skipped;
END
-- >>> GRANTS
GRANT EXECUTE ON diag.apply_error_fix_manifest TO etp_automation,etp_owner;
