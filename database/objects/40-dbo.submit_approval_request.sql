CREATE OR ALTER PROCEDURE dbo.submit_approval_request
 @type varchar(max),@subjectType varchar(max),@subject nvarchar(max),@store varchar(max),@date date,@payload nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51313,'Owner permission is required to submit this request.',1;
 IF @type<>'ACCOUNTING_MAPPING' THROW 51554,'Use the adjustment or restatement workflow to create a bound request.',1;
 BEGIN TRY BEGIN TRANSACTION;
 INSERT dbo.approval_requests(approval_type,subject_type,subject_id,store_code,business_date,request_payload_json,requested_by,status)
 VALUES(@type,@subjectType,@subject,@store,@date,@payload,SUSER_SNAME(),'PENDING');
 DECLARE @id bigint=SCOPE_IDENTITY();
 EXEC dbo.record_operational_audit 'Approval','Succeeded',N'Approval requested',N'database';
 COMMIT TRANSACTION; SELECT @id;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
END
-- >>> GRANTS
DENY EXECUTE ON dbo.submit_approval_request TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.submit_approval_request TO etp_owner;
