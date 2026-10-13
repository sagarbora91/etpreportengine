CREATE OR ALTER PROCEDURE diag.set_health_check_known @check_id varchar(10),@finding_code varchar(60),@until date,@reason nvarchar(200)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51822,'Only an Owner can mark a health check warning as known.',1;
 DECLARE @today date=CONVERT(date,SYSDATETIME());
 IF diag.is_check_id(@check_id)=0 OR diag.is_code(@finding_code,60)=0
    OR @until IS NULL OR @until<@today OR @until>DATEADD(day,90,@today)
    OR diag.is_safe_text(@reason,200)=0
  THROW 51823,'Enter a reason (plain text, at most 200 characters) and a date from today up to 90 days ahead.',1;
 DECLARE @now datetime2(3)=SYSUTCDATETIME();
 BEGIN TRY
  BEGIN TRANSACTION;
  UPDATE diag.health_check_known WITH(UPDLOCK,HOLDLOCK)
  SET until_date=@until,reason=LTRIM(RTRIM(@reason)),set_by=ORIGINAL_LOGIN(),set_utc=@now
  WHERE check_id=@check_id AND finding_code=@finding_code;
  IF @@ROWCOUNT=0
   INSERT diag.health_check_known(check_id,finding_code,until_date,reason,set_by,set_utc)
   VALUES(@check_id,@finding_code,@until,LTRIM(RTRIM(@reason)),ORIGINAL_LOGIN(),@now);
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END
-- >>> GRANTS
GRANT EXECUTE ON diag.set_health_check_known TO etp_owner;
