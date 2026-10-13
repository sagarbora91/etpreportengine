CREATE OR ALTER PROCEDURE dbo.reject_accounting_batch @id bigint,@reason nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1 THROW 51430,'Owner permission is required to reject accounting batches.',1;
 IF dbo.is_blank_text(@reason)=1 OR LEN(@reason)>1000 THROW 51431,'Enter a rejection reason of at most 1000 characters.',1;
 BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int; EXEC @lock=sys.sp_getapplock @Resource='ETP.AccountingReservations',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;
 IF @lock<0 THROW 51451,'Accounting is busy. Try again.',1;
 UPDATE dbo.accounting_batches SET status='REJECTED',rejection_reason=LTRIM(RTRIM(@reason)),rejected_by=SUSER_SNAME(),rejected_utc=SYSUTCDATETIME()
 WHERE accounting_batch_id=@id AND status IN('DRAFT','BLOCKED','APPROVED_READY');
 IF @@ROWCOUNT<>1 THROW 51432,'Only an unexported, unrejected batch can be rejected.',1;
 EXEC dbo.record_operational_audit 'AccountingBatch','Succeeded',N'Accounting batch rejected',N'database';
 COMMIT TRANSACTION;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
END
-- >>> GRANTS
DENY EXECUTE ON dbo.reject_accounting_batch TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.reject_accounting_batch TO etp_owner;
