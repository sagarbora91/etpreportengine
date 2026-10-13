CREATE OR ALTER PROCEDURE diag.merge_error_entries @from_id int,@into_id int,@reason nvarchar(200)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51801,'Only an Owner can merge error register entries.',1;
 IF dbo.is_blank_text(@reason)=1 THROW 51802,'Enter a reason for the merge.',1;
 IF diag.is_safe_text(@reason,200)=0
  THROW 51803,'The reason must be plain text of at most 200 characters, without line breaks.',1;
 IF @from_id IS NULL OR @into_id IS NULL OR @from_id=@into_id
  THROW 51805,'An entry cannot be merged into itself.',1;

 DECLARE @now datetime2(3)=SYSUTCDATETIME();
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @lock int;
  EXEC @lock=sys.sp_getapplock @Resource=N'ETP.ErrorRegister',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
  IF @lock<0 THROW 51806,'The error register is busy. Try again.',1;
  IF NOT EXISTS(SELECT 1 FROM diag.error_register WHERE error_register_id=@from_id AND merged_into_id IS NULL)
     OR NOT EXISTS(SELECT 1 FROM diag.error_register WHERE error_register_id=@into_id AND merged_into_id IS NULL)
   THROW 51805,'Both entries must exist and neither may already be merged.',1;

  UPDATE diag.error_occurrences SET error_register_id=@into_id WHERE error_register_id=@from_id;

  UPDATE t SET occurrence_count=t.occurrence_count+f.occurrence_count,
   first_seen_utc=CASE WHEN f.first_seen_utc<t.first_seen_utc THEN f.first_seen_utc ELSE t.first_seen_utc END,
   last_seen_utc=CASE WHEN f.last_seen_utc>t.last_seen_utc THEN f.last_seen_utc ELSE t.last_seen_utc END
  FROM diag.error_register_versions t
  JOIN diag.error_register_versions f ON f.error_register_id=@from_id AND f.app_version=t.app_version
  WHERE t.error_register_id=@into_id;
  INSERT diag.error_register_versions(error_register_id,app_version,app_version_key,occurrence_count,first_seen_utc,last_seen_utc)
  SELECT @into_id,f.app_version,f.app_version_key,f.occurrence_count,f.first_seen_utc,f.last_seen_utc
  FROM diag.error_register_versions f
  WHERE f.error_register_id=@from_id
    AND NOT EXISTS(SELECT 1 FROM diag.error_register_versions t WHERE t.error_register_id=@into_id AND t.app_version=f.app_version);
  DELETE diag.error_register_versions WHERE error_register_id=@from_id;

  UPDATE t SET
   occurrence_count=t.occurrence_count+f.occurrence_count,
   max_severity=CASE WHEN f.max_severity='Critical' THEN 'Critical' ELSE t.max_severity END,
   first_version=CASE WHEN f.first_seen_utc<t.first_seen_utc THEN f.first_version ELSE t.first_version END,
   first_seen_utc=CASE WHEN f.first_seen_utc<t.first_seen_utc THEN f.first_seen_utc ELSE t.first_seen_utc END,
   last_version=CASE WHEN f.last_seen_utc>t.last_seen_utc THEN f.last_version ELSE t.last_version END,
   latest_stack=CASE WHEN f.last_seen_utc>t.last_seen_utc AND f.latest_stack IS NOT NULL THEN f.latest_stack ELSE t.latest_stack END,
   last_seen_utc=CASE WHEN f.last_seen_utc>t.last_seen_utc THEN f.last_seen_utc ELSE t.last_seen_utc END
  FROM diag.error_register t CROSS JOIN diag.error_register f
  WHERE t.error_register_id=@into_id AND f.error_register_id=@from_id;

  UPDATE diag.error_register SET merged_into_id=@into_id WHERE merged_into_id=@from_id;
  UPDATE diag.error_register SET merged_into_id=@into_id,status_reason=LTRIM(RTRIM(@reason)),
   status_changed_utc=@now,status_changed_by=ORIGINAL_LOGIN()
  WHERE error_register_id=@from_id;
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END
-- >>> GRANTS
GRANT EXECUTE ON diag.merge_error_entries TO etp_owner;
