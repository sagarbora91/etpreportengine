CREATE OR ALTER PROCEDURE dbo.decide_approval_request @id bigint,@approve bit,@reason nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51315,'Owner permission is required to decide a request.',1;
 IF @approve IS NULL OR dbo.is_blank_text(@reason)=1
  THROW 51314,'Choose a decision and enter its reason.',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @status varchar(20)=CASE @approve WHEN 1 THEN 'APPROVED' ELSE 'REJECTED' END;
  UPDATE dbo.approval_requests SET status=@status,decided_by=SUSER_SNAME(),decided_utc=SYSUTCDATETIME(),decision_reason=@reason
   WHERE approval_request_id=@id AND status='PENDING';
  IF @@ROWCOUNT<>1 THROW 51211,'The approval is no longer pending.',1;
  UPDATE dbo.controlled_adjustments SET status=@status WHERE approval_request_id=@id AND status='PENDING';
  EXEC dbo.record_operational_audit 'Approval','Succeeded',N'Approval decided',N'database';
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.decide_approval_request TO etp_owner;
