CREATE OR ALTER PROCEDURE dbo.save_register_entry
 @type varchar(30),@document bigint=NULL,@store varchar(30),@date date,@number nvarchar(100),
 @documentDate date=NULL,@counterparty nvarchar(200)=NULL,@quantity decimal(19,4)=NULL,@amount decimal(19,4)=NULL,
 @reference nvarchar(200)=NULL,@received nvarchar(100)=NULL,@verification varchar(20),@remarks nvarchar(1000)=NULL,@reason nvarchar(500)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @owner bit=CASE WHEN IS_ROLEMEMBER('etp_owner')=1 OR IS_SRVROLEMEMBER('sysadmin')=1 THEN 1 ELSE 0 END;
 IF @owner=0 AND COALESCE(IS_ROLEMEMBER('etp_store_manager'),0)<>1 THROW 51550,'Owner or Store Manager permission is required.',1;
 IF dbo.is_blank_text(@reason)=1 THROW 51551,'Enter a register change reason.',1;
 IF NULLIF(LTRIM(RTRIM(@store)),'') IS NULL OR NULLIF(LTRIM(RTRIM(@number)),'') IS NULL THROW 51551,'Enter a store and document number.',1;
 IF @verification NOT IN('DRAFT','VERIFIED') THROW 51551,'Choose Draft or Verified.',1;
 BEGIN TRY
 BEGIN TRANSACTION;
 IF EXISTS(SELECT 1 FROM dbo.daily_reporting_days WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND business_date=@date AND status='LOCKED')
   THROW 51210,'This business day is finalised. Reopen it before changing the register.',1;
 DECLARE @id bigint,@oldStatus varchar(20);
 SELECT @id=register_entry_id,@oldStatus=verification_status FROM dbo.register_entries WITH(UPDLOCK,HOLDLOCK)
  WHERE register_type=@type AND store_code=@store AND business_date=@date AND document_number=@number;
 IF @owner=0 AND (@verification<>'DRAFT' OR COALESCE(@oldStatus,'DRAFT')='VERIFIED') THROW 51552,'Only the Owner can verify or change a verified register entry.',1;
 IF @verification='VERIFIED' AND (@id IS NULL OR @oldStatus NOT IN('DRAFT','REVIEW_REQUIRED')) THROW 51553,'Save a draft before verifying the entry.',1;
 IF @id IS NULL BEGIN
  INSERT dbo.register_entries(register_type,source_document_id,store_code,business_date,document_number,document_date,counterparty,quantity,amount,reference,received_by,verification_status,remarks,created_by,modified_by,change_reason)
  VALUES(@type,@document,@store,@date,@number,@documentDate,@counterparty,@quantity,@amount,@reference,@received,@verification,@remarks,SUSER_SNAME(),SUSER_SNAME(),@reason);
  SET @id=SCOPE_IDENTITY();
 END ELSE
  UPDATE dbo.register_entries SET source_document_id=@document,document_date=@documentDate,counterparty=@counterparty,quantity=@quantity,amount=@amount,reference=@reference,
   received_by=@received,verification_status=@verification,remarks=@remarks,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason WHERE register_entry_id=@id;
 EXEC dbo.record_operational_audit 'RegisterEntry','Succeeded',N'Register entry saved',N'database';
 COMMIT TRANSACTION;
 SELECT @id;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
END
-- >>> GRANTS
DENY EXECUTE ON dbo.save_register_entry TO etp_viewer;
GRANT EXECUTE ON dbo.save_register_entry TO etp_owner,etp_store_manager;
