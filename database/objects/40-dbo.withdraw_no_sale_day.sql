-- 1.12 lane N1 (decision 52): withdraws the no-sale mark of one store-day, with a reason. Owner or Store Manager.
-- Refused (THROW 51980-51989) when the caller is not Owner / Store Manager, the reason is missing, the day is finalised
-- (LOCKED: reopen first) or the day has no current mark. The row stays (withdrawn); history (table trigger) and the
-- NoSaleDayWithdrawn audit row are written in the same transaction. The day then asks for its exports and entries again.
CREATE OR ALTER PROCEDURE dbo.withdraw_no_sale_day
 @store varchar(30), @date date, @reason nvarchar(500)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_ROLEMEMBER('etp_store_manager'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51980,'Only the Owner or a Store Manager can mark or withdraw a no-sale day.',1;
 SET @store=NULLIF(LTRIM(RTRIM(@store)),'');
 SET @reason=NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@reason,CHAR(9),' '),CHAR(10),' '),CHAR(13),' '))),'');
 IF @reason IS NULL
  THROW 51981,'Write a short reason for withdrawing the no-sale day.',1;
 DECLARE @by nvarchar(100)=LEFT(COALESCE(SUSER_SNAME(),ORIGINAL_LOGIN()),100);
 BEGIN TRY
  BEGIN TRANSACTION;
  IF EXISTS(SELECT 1 FROM dbo.daily_reporting_days WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND business_date=@date AND status='LOCKED')
   THROW 51983,'This business day is finalised. Reopen it before marking or withdrawing a no-sale day.',1;
  UPDATE dbo.no_sale_days WITH(UPDLOCK,HOLDLOCK)
  SET withdrawn_by=@by,withdrawn_utc=SYSUTCDATETIME(),withdraw_reason=@reason
  WHERE store_code=@store AND business_date=@date AND withdrawn_utc IS NULL;
  IF @@ROWCOUNT<>1
   THROW 51988,'This day is not marked as a no-sale day, so there is nothing to withdraw.',1;
  EXEC dbo.record_operational_audit 'NoSaleDayWithdrawn','Succeeded',N'No-sale day withdrawn',N'database';
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
END
-- >>> GRANTS
DENY EXECUTE ON dbo.withdraw_no_sale_day TO etp_viewer;
GRANT EXECUTE ON dbo.withdraw_no_sale_day TO etp_owner,etp_store_manager;
