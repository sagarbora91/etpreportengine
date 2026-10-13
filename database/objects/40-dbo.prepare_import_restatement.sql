CREATE OR ALTER PROCEDURE dbo.prepare_import_restatement
 @previous_file_id bigint,@replacement_file_id bigint,@user nvarchar(100),@reason nvarchar(500)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_store_manager'),0)<>1 AND COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51421,'Owner or Store Manager permission is required.',1;
 IF @@TRANCOUNT=0 THROW 51422,'Import writes require an enclosing transaction.',1;
 DECLARE @id bigint;
 SELECT @id=r.approval_request_id FROM dbo.import_restatement_approvals r WITH(UPDLOCK,HOLDLOCK)
 JOIN dbo.approval_requests a ON a.approval_request_id=r.approval_request_id
 JOIN dbo.import_files fresh ON fresh.import_file_id=@replacement_file_id
 JOIN dbo.import_files old ON old.import_file_id=r.previous_import_file_id
 JOIN dbo.import_batches b ON b.import_batch_id=fresh.import_batch_id
 WHERE r.previous_import_file_id=@previous_file_id AND r.applied_import_file_id IS NULL AND a.status='APPROVED'
  AND r.replacement_sha256=fresh.source_sha256 AND r.store_code=fresh.store_code AND r.report_code=fresh.report_code
  AND r.period_start=COALESCE(fresh.period_start,fresh.business_date) AND r.period_end=COALESCE(fresh.period_end,fresh.business_date)
  AND r.request_reason=@reason COLLATE Latin1_General_100_BIN2 AND old.is_superseded=0 AND fresh.is_superseded=0
  AND fresh.data_truth_version=1 AND b.status='Processing' AND @previous_file_id<>@replacement_file_id;
 IF @id IS NULL THROW 51556,'This exact replacement and reason require Owner approval before import. Request approval and retry after the decision.',1;
 UPDATE dbo.import_restatement_approvals SET applied_import_file_id=@replacement_file_id WHERE approval_request_id=@id;
 EXEC dbo.replace_import_facts_internal @previous_file_id,@replacement_file_id,@user,@reason;
END
-- >>> GRANTS
DENY EXECUTE ON dbo.prepare_import_restatement TO etp_viewer;
GRANT EXECUTE ON dbo.prepare_import_restatement TO etp_owner,etp_store_manager;
