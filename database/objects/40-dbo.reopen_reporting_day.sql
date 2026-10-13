CREATE OR ALTER PROCEDURE dbo.reopen_reporting_day
 @store varchar(30), @date date, @reason nvarchar(500)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51301,'Owner permission is required to reopen a business date.',1;
 IF NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@reason,CHAR(9),' '),CHAR(10),' '),CHAR(13),' '))),'') IS NULL
  THROW 51302,'Enter a reason before reopening the business date.',1;
 UPDATE dbo.daily_reporting_days WITH(UPDLOCK,HOLDLOCK)
  SET status='OPEN',reopened_by=SUSER_SNAME(),reopened_utc=SYSUTCDATETIME(),reopen_reason=@reason
  WHERE store_code=@store AND business_date=@date AND status='LOCKED';
 IF @@ROWCOUNT<>1 THROW 51024,'Only a finalised day can be reopened.',1;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.reopen_reporting_day TO etp_owner;
