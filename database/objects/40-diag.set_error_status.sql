CREATE OR ALTER PROCEDURE diag.set_error_status @error_register_id int,@status varchar(10),@fixed_in_version varchar(20)=NULL,
 @ticket varchar(12)=NULL,@reason nvarchar(200)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51801,'Only an Owner can change the status of an error register entry.',1;
 IF @status IS NULL OR @status COLLATE Latin1_General_100_BIN2 NOT IN('Seen','Expected','New','Fixed')
  THROW 51805,'This status change is not allowed.',1;
 IF @status IN('Expected','Fixed') AND dbo.is_blank_text(@reason)=1
  THROW 51802,'Enter a reason for this status.',1;
 IF dbo.is_blank_text(@reason)=0 AND diag.is_safe_text(@reason,200)=0
  THROW 51803,'The reason must be plain text of at most 200 characters, without line breaks.',1;
 IF NULLIF(@ticket,'') IS NOT NULL AND diag.is_token(@ticket,12)=0
  THROW 51803,'The ticket id is not valid.',1;
 DECLARE @key bigint=diag.version_key(@fixed_in_version);
 IF @status='Fixed' AND @key IS NULL
  THROW 51803,'The fixed-in version is not valid.',1;

 DECLARE @now datetime2(3)=SYSUTCDATETIME(),@current varchar(10),@merged int,@seen_version varchar(20),@seen_utc datetime2(3);
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @lock int;
  EXEC @lock=sys.sp_getapplock @Resource=N'ETP.ErrorRegister',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
  IF @lock<0 THROW 51806,'The error register is busy. Try again.',1;
  SELECT @current=status,@merged=merged_into_id FROM diag.error_register WHERE error_register_id=@error_register_id;
  IF @current IS NULL OR @merged IS NOT NULL
   THROW 51805,'This error register entry was not found, or it was merged into another entry.',1;
  IF NOT((@status='Seen' AND @current IN('New','Seen','Recurred'))
      OR (@status='Expected' AND @current IN('New','Seen'))
      OR (@status='New' AND @current='Expected')
      OR (@status='Fixed' AND @current IN('New','Seen','Recurred')))
   THROW 51805,'This status change is not allowed.',1;

  UPDATE diag.error_register SET
   status=@status,
   status_reason=CASE WHEN dbo.is_blank_text(@reason)=1 THEN NULL ELSE LTRIM(RTRIM(@reason)) END,
   fixed_ticket=CASE WHEN @status IN('Seen','Fixed') AND NULLIF(@ticket,'') IS NOT NULL THEN @ticket ELSE fixed_ticket END,
   fixed_in_version=CASE WHEN @status='Fixed' THEN @fixed_in_version ELSE fixed_in_version END,
   fixed_in_version_key=CASE WHEN @status='Fixed' THEN @key ELSE fixed_in_version_key END,
   fixed_utc=CASE WHEN @status='Fixed' THEN @now ELSE fixed_utc END,
   status_changed_utc=@now,
   status_changed_by=ORIGINAL_LOGIN()
  WHERE error_register_id=@error_register_id;

  IF @status='Fixed'
  BEGIN
   SELECT TOP(1) @seen_version=app_version,@seen_utc=last_seen_utc FROM diag.error_register_versions
   WHERE error_register_id=@error_register_id AND app_version_key>=@key ORDER BY app_version_key DESC,last_seen_utc DESC;
   IF @seen_version IS NOT NULL
    UPDATE diag.error_register SET status='Recurred',recurred_version=@seen_version,recurred_utc=@seen_utc
    WHERE error_register_id=@error_register_id;
  END;
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END
-- >>> GRANTS
GRANT EXECUTE ON diag.set_error_status TO etp_owner;
