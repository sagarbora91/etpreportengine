-- 1.12 lane N1 (decision 52; decision 55 = Q1 option a): marks one store-day as a day with no sales. Owner or Store Manager.
-- Refused (plain text, THROW 51980-51989) when: the caller is not Owner / Store Manager; the reason is missing or its code
-- unknown; the store does not exist or is an inactive Retail store (review m-2); the date is after today; the day is finalised (LOCKED: reopen first); ETP already
-- holds a sales invoice for the store-day; a daily entry other than zero is saved for it; it is already marked.
-- Writes, in one transaction: the OPEN daily_reporting_days row when there is none (so the day can be finalised later),
-- the mark (a withdrawn row is marked again in place), its history row (table trigger) and the NoSaleDayMarked audit row.
CREATE OR ALTER PROCEDURE dbo.mark_no_sale_day
 @store varchar(30), @date date, @reason_code varchar(30), @reason nvarchar(500)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_ROLEMEMBER('etp_store_manager'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51980,'Only the Owner or a Store Manager can mark or withdraw a no-sale day.',1;
 SET @store=NULLIF(LTRIM(RTRIM(@store)),'');
 SET @reason=NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@reason,CHAR(9),' '),CHAR(10),' '),CHAR(13),' '))),'');
 IF @reason IS NULL OR @reason_code IS NULL OR @reason_code NOT IN('CLOSED_HOLIDAY','CLOSED_OTHER','OPEN_NO_SALE')
  THROW 51981,'Choose why there were no sales and write a short reason.',1;
 -- Review m-2: an active shop or a Service Centre (an inactive Retail store gets no day row).
 IF @store IS NULL OR @date IS NULL OR NOT EXISTS(SELECT 1 FROM dbo.stores s LEFT JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id
     WHERE s.store_code=@store AND (s.is_active=1 OR u.unit_kind='SERVICE'))
  THROW 51986,'Choose a shop that ETP knows and a business date.',1;
 IF @date>CAST(SYSDATETIME() AS date)
  THROW 51982,'A no-sale day cannot be marked for a date after today.',1;
 DECLARE @by nvarchar(100)=LEFT(COALESCE(SUSER_SNAME(),ORIGINAL_LOGIN()),100);
 BEGIN TRY
  BEGIN TRANSACTION;
  IF NOT EXISTS(SELECT 1 FROM dbo.daily_reporting_days WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND business_date=@date)
   INSERT dbo.daily_reporting_days(store_code,business_date,status) VALUES(@store,@date,'OPEN');
  ELSE IF EXISTS(SELECT 1 FROM dbo.daily_reporting_days WHERE store_code=@store AND business_date=@date AND status='LOCKED')
   THROW 51983,'This business day is finalised. Reopen it before marking or withdrawing a no-sale day.',1;
  IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE store_code=@store AND transaction_date=@date)
   THROW 51984,'ETP already holds sales for this shop on this day, so it cannot be marked as a no-sale day.',1;
  IF EXISTS(SELECT 1 FROM dbo.manual_operational_inputs WHERE store_code=@store AND business_date=@date
     AND (COALESCE(numeric_value,0)<>0 OR NULLIF(LTRIM(RTRIM(text_value)),N'') IS NOT NULL))
   THROW 51985,'A daily entry other than zero is already saved for this shop on this day, so it cannot be marked as a no-sale day.',1;
  DECLARE @withdrawn bit;
  SELECT @withdrawn=CASE WHEN withdrawn_utc IS NULL THEN 0 ELSE 1 END
  FROM dbo.no_sale_days WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND business_date=@date;
  IF @withdrawn=0
   THROW 51987,'This day is already marked as a no-sale day.',1;
  IF @withdrawn IS NULL
   INSERT dbo.no_sale_days(store_code,business_date,reason_code,reason,marked_by,marked_utc)
   VALUES(@store,@date,@reason_code,@reason,@by,SYSUTCDATETIME());
  ELSE
   UPDATE dbo.no_sale_days
   SET reason_code=@reason_code,reason=@reason,marked_by=@by,marked_utc=SYSUTCDATETIME(),
       withdrawn_by=NULL,withdrawn_utc=NULL,withdraw_reason=NULL
   WHERE store_code=@store AND business_date=@date;
  EXEC dbo.record_operational_audit 'NoSaleDayMarked','Succeeded',N'No-sale day marked',N'database';
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
END
-- >>> GRANTS
DENY EXECUTE ON dbo.mark_no_sale_day TO etp_viewer;
GRANT EXECUTE ON dbo.mark_no_sale_day TO etp_owner,etp_store_manager;
