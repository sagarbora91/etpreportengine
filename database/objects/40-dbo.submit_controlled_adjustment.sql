CREATE OR ALTER PROCEDURE dbo.submit_controlled_adjustment
 @store varchar(max),@date date,@type varchar(max),@amount decimal(19,4),@reason nvarchar(max),@document bigint=NULL
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51313,'Owner permission is required to submit an adjustment.',1;
 IF dbo.is_blank_text(@reason)=1
  THROW 51314,'Enter an adjustment reason.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  INSERT dbo.approval_requests(approval_type,subject_type,subject_id,store_code,business_date,request_payload_json,requested_by,status)
  VALUES('ADJUSTMENT','ControlledAdjustment',CONCAT(@store,'/',CONVERT(varchar(10),@date,23),'/',@type),@store,@date,
   (SELECT @type adjustmentType,@amount amount,@reason reason FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),SUSER_SNAME(),'PENDING');
  DECLARE @approval bigint=SCOPE_IDENTITY();
  INSERT dbo.controlled_adjustments(store_code,business_date,adjustment_type,amount,reason,source_document_id,approval_request_id,created_by,status)
  VALUES(@store,@date,@type,@amount,@reason,@document,@approval,SUSER_SNAME(),'PENDING');
  DECLARE @id bigint=SCOPE_IDENTITY();
  EXEC dbo.record_operational_audit 'Adjustment','Succeeded',N'Controlled adjustment submitted for Owner approval',N'database';
  COMMIT TRANSACTION;
  SELECT @id;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END
-- >>> GRANTS
DENY EXECUTE ON dbo.submit_controlled_adjustment TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.submit_controlled_adjustment TO etp_owner;
