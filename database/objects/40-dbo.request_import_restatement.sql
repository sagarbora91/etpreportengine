CREATE OR ALTER PROCEDURE dbo.request_import_restatement
 @previous bigint,@hash char(64),@report varchar(30),@store varchar(30),@start date,@end date,@reason nvarchar(500)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_store_manager'),0)<>1 AND COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51313,'Owner or Store Manager permission is required to request a restatement.',1;
 IF dbo.is_blank_text(@reason)=1 THROW 51039,'A restatement reason is required.',1;
 IF @hash LIKE '%[^0-9a-f]%' OR LEN(@hash)<>64 OR @start IS NULL OR @end IS NULL OR @start>@end THROW 51555,'The replacement source identity is invalid.',1;
 DECLARE @binding binary(32)=HASHBYTES('SHA2_256',(SELECT @previous previousFile,@hash sha256,@report report,@store store,@start periodStart,@end periodEnd,@reason reason FOR JSON PATH,WITHOUT_ARRAY_WRAPPER));
 BEGIN TRY BEGIN TRANSACTION;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files WITH(UPDLOCK,HOLDLOCK) WHERE import_file_id=@previous AND is_superseded=0 AND store_code=@store AND report_code=@report
   AND @start<=COALESCE(period_start,business_date) AND @end>=COALESCE(period_end,business_date))
  THROW 51555,'The previous current import does not match the replacement store, report or date range.',1;
 DECLARE @id bigint;
 SELECT @id=approval_request_id FROM dbo.import_restatement_approvals WITH(UPDLOCK,HOLDLOCK) WHERE binding_hash=@binding;
 IF @id IS NULL BEGIN
  INSERT dbo.approval_requests(approval_type,subject_type,subject_id,store_code,business_date,request_payload_json,requested_by,status)
  VALUES('RESTATEMENT','ImportFile',CONVERT(nvarchar(100),@previous),@store,@end,
   (SELECT @previous previousImportFileId,@hash replacementSha256,@report reportCode,@start periodStart,@end periodEnd,@reason reason FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),SUSER_SNAME(),'PENDING');
  SET @id=SCOPE_IDENTITY();
  INSERT dbo.import_restatement_approvals(approval_request_id,previous_import_file_id,replacement_sha256,report_code,store_code,period_start,period_end,request_reason,binding_hash)
  VALUES(@id,@previous,@hash,@report,@store,@start,@end,@reason,@binding);
  EXEC dbo.record_operational_audit 'Approval','Succeeded',N'Restatement requested; current facts retained',N'database';
 END;
 COMMIT TRANSACTION;
 SELECT approval_request_id,status FROM dbo.approval_requests WHERE approval_request_id=@id;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
END
-- >>> GRANTS
DENY EXECUTE ON dbo.request_import_restatement TO etp_viewer;
GRANT EXECUTE ON dbo.request_import_restatement TO etp_owner,etp_store_manager;
