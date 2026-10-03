-- 1.9.3. Phase 5 re-audit (26 Sep 2026), defect 3: the SQL reason checks trimmed spaces only.
--
-- 0029's CK_accounting_batches_approval_reason and 0033's approval check in trg_accounting_batch_states
-- used LTRIM/RTRIM, which remove the space character (U+0020) and nothing else, so an approval reason of
-- tabs or non-breaking spaces passed. The rejection, decision, register, adjustment and restatement checks
-- (0022, 0033, 0035) also mapped tab, CR and LF to spaces, but still let a non-breaking space or any other
-- Unicode space through, and 0035's replace_import_facts_internal trimmed spaces only.
--
-- The app checks every one of these reasons with string.IsNullOrWhiteSpace, so a reason made only of white
-- space reached SQL only from a direct SQL call. SQL now refuses exactly what the app refuses:
-- dbo.is_blank_text is 1 for NULL and for text made only of the characters .NET's char.IsWhiteSpace counts as
-- white space (the 25 code points listed in the function, the same set string.IsNullOrWhiteSpace and
-- string.Trim use). Zero-width characters such as U+200B and U+FEFF are not white space in .NET, so they are
-- not here either.
--
-- Redefined here, each with only its reason check changed (MigrationTests compare each body with the one it
-- replaces) and its error number and message kept:
--   CK_accounting_batches_approval_reason (0029)   547, the constraint's name
--   dbo.trg_accounting_batch_states (0033)         51457
--   dbo.reject_accounting_batch (0033)             51431
--   dbo.decide_approval_request (0022)             51314
--   dbo.save_register_entry (0035)                 51551
--   dbo.submit_controlled_adjustment (0035)        51314
--   dbo.request_import_restatement (0035)          51039
--   dbo.replace_import_facts_internal (0035)       51039
-- dbo.prepare_import_restatement is unchanged: it passes the reason to replace_import_facts_internal, which
-- now refuses a blank one, including one approved before this migration.
-- Grants are unchanged: CREATE OR ALTER keeps each object's permissions. dbo.is_blank_text needs no grant;
-- the dbo-owned constraint, trigger and procedures reach it through ownership chaining.
--
-- 0044 follows 0043 (Owner grant option, another 1.9.3 branch); the contiguous-numbering guard in
-- MigrationTests passes once both are merged.
SET XACT_ABORT ON;

-- >>> PRECHECK_BLANK_APPROVAL_REASON begin
-- The new constraint must hold for every existing batch, so a reason made only of white space refuses the
-- upgrade before any pending migration applies, and nothing changes. A pre-check may not use EXEC and a
-- database from before 0029 has no approval_reason column, so the column is read by name through FOR JSON:
-- naming it in the query would not compile there. The code points are dbo.is_blank_text's.
IF OBJECT_ID(N'dbo.accounting_batches',N'U') IS NOT NULL AND COL_LENGTH(N'dbo.accounting_batches',N'approval_reason') IS NOT NULL
BEGIN
  DECLARE @blank_reason_count int, @blank_reason_first bigint, @blank_reason_message nvarchar(2048);
  SELECT @blank_reason_count=COUNT(*), @blank_reason_first=MIN(b.accounting_batch_id)
  FROM dbo.accounting_batches b WITH(UPDLOCK,HOLDLOCK)
  CROSS APPLY(SELECT JSON_VALUE((SELECT b.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),N'$.approval_reason') AS reason) r
  WHERE r.reason IS NOT NULL AND NOT EXISTS(
    SELECT 1 FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) h(d)
    CROSS JOIN (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) t(d)
    CROSS JOIN (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) u(d)
    WHERE h.d*100+t.d*10+u.d<LEN(r.reason+N'x')-1
      AND UNICODE(SUBSTRING(r.reason,h.d*100+t.d*10+u.d+1,1)) NOT IN(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288));
  IF @blank_reason_count>0
  BEGIN
    SET @blank_reason_message=CONCAT(N'Some accounting batches have an approval reason made only of spaces, tabs, line breaks or other white space: ',
      @blank_reason_count,CASE WHEN @blank_reason_count=1 THEN N' batch' ELSE N' batches' END,N', the first is accounting batch ',@blank_reason_first,
      N'. Nothing was changed. A SQL administrator must record each batch''s real approval reason before ETP can be updated.');
    THROW 51562,@blank_reason_message,1;
  END;
END;
-- <<< PRECHECK_BLANK_APPROVAL_REASON end

IF OBJECT_ID(N'dbo.is_blank_text',N'FN') IS NULL
EXEC(N'CREATE FUNCTION dbo.is_blank_text(@value nvarchar(max))
RETURNS bit WITH SCHEMABINDING
AS
BEGIN
 -- 1 for NULL and for text made only of the characters char.IsWhiteSpace counts as white space in .NET:
 -- U+0009-U+000D, U+0020, U+0085, U+00A0, U+1680, U+2000-U+200A, U+2028, U+2029, U+202F, U+205F, U+3000.
 IF @value IS NULL RETURN 1;
 DECLARE @i bigint=1,@n bigint=DATALENGTH(@value)/2;
 WHILE @i<=@n
 BEGIN
  IF UNICODE(SUBSTRING(@value,@i,1)) NOT IN(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)
   RETURN 0;
  SET @i+=1;
 END;
 RETURN 1;
END');

IF OBJECT_ID(N'dbo.CK_accounting_batches_approval_reason',N'C') IS NOT NULL
 ALTER TABLE dbo.accounting_batches DROP CONSTRAINT CK_accounting_batches_approval_reason;
EXEC(N'ALTER TABLE dbo.accounting_batches WITH CHECK ADD CONSTRAINT CK_accounting_batches_approval_reason
 CHECK (approval_reason IS NULL OR dbo.is_blank_text(approval_reason)=0);');

EXEC(N'CREATE OR ALTER TRIGGER dbo.trg_accounting_batch_states ON dbo.accounting_batches AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.accounting_batch_id=d.accounting_batch_id WHERE i.accounting_batch_id IS NULL)
 THROW 51454,''Accounting history cannot be deleted. Reject an unexported batch with a reason.'',1;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.accounting_batch_id=d.accounting_batch_id
 WHERE d.status IN(''REJECTED'',''EXPORTED_AWAITING_IMPORT'') AND i.status<>d.status)
 THROW 51455,''A rejected or exported batch cannot change status.'',1;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.accounting_batch_id=d.accounting_batch_id
 WHERE i.status<>d.status AND NOT((d.status IN(''DRAFT'',''BLOCKED'') AND i.status IN(''DRAFT'',''BLOCKED'',''APPROVED_READY'',''REJECTED'')) OR (d.status=''APPROVED_READY'' AND i.status IN(''REJECTED'',''EXPORTED_AWAITING_IMPORT''))))
 THROW 51456,''This accounting status change is not allowed.'',1;
 IF EXISTS(SELECT 1 FROM inserted WHERE status=''APPROVED_READY'' AND (debit_total<>credit_total OR dbo.is_blank_text(approval_reason)=1 OR blocking_reason IS NOT NULL))
 THROW 51457,''A balanced, unblocked batch and an approval reason are required.'',1;
 UPDATE r SET is_active=0 FROM dbo.accounting_batch_invoices r JOIN inserted i ON i.accounting_batch_id=r.accounting_batch_id WHERE i.status=''REJECTED'';
END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.reject_accounting_batch @id bigint,@reason nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1 THROW 51430,''Owner permission is required to reject accounting batches.'',1;
 IF dbo.is_blank_text(@reason)=1 OR LEN(@reason)>1000 THROW 51431,''Enter a rejection reason of at most 1000 characters.'',1;
 BEGIN TRY
 BEGIN TRANSACTION;
 DECLARE @lock int; EXEC @lock=sys.sp_getapplock @Resource=''ETP.AccountingReservations'',@LockMode=''Exclusive'',@LockOwner=''Transaction'',@LockTimeout=15000;
 IF @lock<0 THROW 51451,''Accounting is busy. Try again.'',1;
 UPDATE dbo.accounting_batches SET status=''REJECTED'',rejection_reason=LTRIM(RTRIM(@reason)),rejected_by=SUSER_SNAME(),rejected_utc=SYSUTCDATETIME()
 WHERE accounting_batch_id=@id AND status IN(''DRAFT'',''BLOCKED'',''APPROVED_READY'');
 IF @@ROWCOUNT<>1 THROW 51432,''Only an unexported, unrejected batch can be rejected.'',1;
 EXEC dbo.record_operational_audit ''AccountingBatch'',''Succeeded'',N''Accounting batch rejected'',N''database'';
 COMMIT TRANSACTION;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.decide_approval_request @id bigint,@approve bit,@reason nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51315,''Owner permission is required to decide a request.'',1;
 IF @approve IS NULL OR dbo.is_blank_text(@reason)=1
  THROW 51314,''Choose a decision and enter its reason.'',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @status varchar(20)=CASE @approve WHEN 1 THEN ''APPROVED'' ELSE ''REJECTED'' END;
  UPDATE dbo.approval_requests SET status=@status,decided_by=SUSER_SNAME(),decided_utc=SYSUTCDATETIME(),decision_reason=@reason
   WHERE approval_request_id=@id AND status=''PENDING'';
  IF @@ROWCOUNT<>1 THROW 51211,''The approval is no longer pending.'',1;
  UPDATE dbo.controlled_adjustments SET status=@status WHERE approval_request_id=@id AND status=''PENDING'';
  EXEC dbo.record_operational_audit ''Approval'',''Succeeded'',N''Approval decided'',N''database'';
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.save_register_entry
 @type varchar(30),@document bigint=NULL,@store varchar(30),@date date,@number nvarchar(100),
 @documentDate date=NULL,@counterparty nvarchar(200)=NULL,@quantity decimal(19,4)=NULL,@amount decimal(19,4)=NULL,
 @reference nvarchar(200)=NULL,@received nvarchar(100)=NULL,@verification varchar(20),@remarks nvarchar(1000)=NULL,@reason nvarchar(500)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @owner bit=CASE WHEN IS_ROLEMEMBER(''etp_owner'')=1 OR IS_SRVROLEMEMBER(''sysadmin'')=1 THEN 1 ELSE 0 END;
 IF @owner=0 AND COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 THROW 51550,''Owner or Store Manager permission is required.'',1;
 IF dbo.is_blank_text(@reason)=1 THROW 51551,''Enter a register change reason.'',1;
 IF NULLIF(LTRIM(RTRIM(@store)),'''') IS NULL OR NULLIF(LTRIM(RTRIM(@number)),'''') IS NULL THROW 51551,''Enter a store and document number.'',1;
 IF @verification NOT IN(''DRAFT'',''VERIFIED'') THROW 51551,''Choose Draft or Verified.'',1;
 BEGIN TRY
 BEGIN TRANSACTION;
 IF EXISTS(SELECT 1 FROM dbo.daily_reporting_days WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND business_date=@date AND status=''LOCKED'')
   THROW 51210,''This business day is finalised. Reopen it before changing the register.'',1;
 DECLARE @id bigint,@oldStatus varchar(20);
 SELECT @id=register_entry_id,@oldStatus=verification_status FROM dbo.register_entries WITH(UPDLOCK,HOLDLOCK)
  WHERE register_type=@type AND store_code=@store AND business_date=@date AND document_number=@number;
 IF @owner=0 AND (@verification<>''DRAFT'' OR COALESCE(@oldStatus,''DRAFT'')=''VERIFIED'') THROW 51552,''Only the Owner can verify or change a verified register entry.'',1;
 IF @verification=''VERIFIED'' AND (@id IS NULL OR @oldStatus NOT IN(''DRAFT'',''REVIEW_REQUIRED'')) THROW 51553,''Save a draft before verifying the entry.'',1;
 IF @id IS NULL BEGIN
  INSERT dbo.register_entries(register_type,source_document_id,store_code,business_date,document_number,document_date,counterparty,quantity,amount,reference,received_by,verification_status,remarks,created_by,modified_by,change_reason)
  VALUES(@type,@document,@store,@date,@number,@documentDate,@counterparty,@quantity,@amount,@reference,@received,@verification,@remarks,SUSER_SNAME(),SUSER_SNAME(),@reason);
  SET @id=SCOPE_IDENTITY();
 END ELSE
  UPDATE dbo.register_entries SET source_document_id=@document,document_date=@documentDate,counterparty=@counterparty,quantity=@quantity,amount=@amount,reference=@reference,
   received_by=@received,verification_status=@verification,remarks=@remarks,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME(),change_reason=@reason WHERE register_entry_id=@id;
 EXEC dbo.record_operational_audit ''RegisterEntry'',''Succeeded'',N''Register entry saved'',N''database'';
 COMMIT TRANSACTION;
 SELECT @id;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.submit_controlled_adjustment
 @store varchar(max),@date date,@type varchar(max),@amount decimal(19,4),@reason nvarchar(max),@document bigint=NULL
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51313,''Owner permission is required to submit an adjustment.'',1;
 IF dbo.is_blank_text(@reason)=1
  THROW 51314,''Enter an adjustment reason.'',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  INSERT dbo.approval_requests(approval_type,subject_type,subject_id,store_code,business_date,request_payload_json,requested_by,status)
  VALUES(''ADJUSTMENT'',''ControlledAdjustment'',CONCAT(@store,''/'',CONVERT(varchar(10),@date,23),''/'',@type),@store,@date,
   (SELECT @type adjustmentType,@amount amount,@reason reason FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),SUSER_SNAME(),''PENDING'');
  DECLARE @approval bigint=SCOPE_IDENTITY();
  INSERT dbo.controlled_adjustments(store_code,business_date,adjustment_type,amount,reason,source_document_id,approval_request_id,created_by,status)
  VALUES(@store,@date,@type,@amount,@reason,@document,@approval,SUSER_SNAME(),''PENDING'');
  DECLARE @id bigint=SCOPE_IDENTITY();
  EXEC dbo.record_operational_audit ''Adjustment'',''Succeeded'',N''Controlled adjustment submitted for Owner approval'',N''database'';
  COMMIT TRANSACTION;
  SELECT @id;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.request_import_restatement
 @previous bigint,@hash char(64),@report varchar(30),@store varchar(30),@start date,@end date,@reason nvarchar(500)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51313,''Owner or Store Manager permission is required to request a restatement.'',1;
 IF dbo.is_blank_text(@reason)=1 THROW 51039,''A restatement reason is required.'',1;
 IF @hash LIKE ''%[^0-9a-f]%'' OR LEN(@hash)<>64 OR @start IS NULL OR @end IS NULL OR @start>@end THROW 51555,''The replacement source identity is invalid.'',1;
 DECLARE @binding binary(32)=HASHBYTES(''SHA2_256'',(SELECT @previous previousFile,@hash sha256,@report report,@store store,@start periodStart,@end periodEnd,@reason reason FOR JSON PATH,WITHOUT_ARRAY_WRAPPER));
 BEGIN TRY BEGIN TRANSACTION;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files WITH(UPDLOCK,HOLDLOCK) WHERE import_file_id=@previous AND is_superseded=0 AND store_code=@store AND report_code=@report
   AND @start<=COALESCE(period_start,business_date) AND @end>=COALESCE(period_end,business_date))
  THROW 51555,''The previous current import does not match the replacement store, report or date range.'',1;
 DECLARE @id bigint;
 SELECT @id=approval_request_id FROM dbo.import_restatement_approvals WITH(UPDLOCK,HOLDLOCK) WHERE binding_hash=@binding;
 IF @id IS NULL BEGIN
  INSERT dbo.approval_requests(approval_type,subject_type,subject_id,store_code,business_date,request_payload_json,requested_by,status)
  VALUES(''RESTATEMENT'',''ImportFile'',CONVERT(nvarchar(100),@previous),@store,@end,
   (SELECT @previous previousImportFileId,@hash replacementSha256,@report reportCode,@start periodStart,@end periodEnd,@reason reason FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),SUSER_SNAME(),''PENDING'');
  SET @id=SCOPE_IDENTITY();
  INSERT dbo.import_restatement_approvals(approval_request_id,previous_import_file_id,replacement_sha256,report_code,store_code,period_start,period_end,request_reason,binding_hash)
  VALUES(@id,@previous,@hash,@report,@store,@start,@end,@reason,@binding);
  EXEC dbo.record_operational_audit ''Approval'',''Succeeded'',N''Restatement requested; current facts retained'',N''database'';
 END;
 COMMIT TRANSACTION;
 SELECT approval_request_id,status FROM dbo.approval_requests WHERE approval_request_id=@id;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.replace_import_facts_internal
 @previous_file_id bigint,@replacement_file_id bigint,@user nvarchar(100),@reason nvarchar(500)
AS
BEGIN
 SET NOCOUNT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  AND NOT EXISTS(SELECT 1 FROM dbo.import_restatement_approvals r JOIN dbo.approval_requests a ON a.approval_request_id=r.approval_request_id WHERE r.previous_import_file_id=@previous_file_id AND r.applied_import_file_id=@replacement_file_id AND a.status=''APPROVED'')
  THROW 51421,''Owner permission is required for corrective restatement.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 SET @user=SUSER_SNAME();
 IF dbo.is_blank_text(@reason)=1 THROW 51039,''A restatement reason is required.'',1;
 DECLARE @store varchar(30),@date date,@report varchar(30),@newStore varchar(30),@newDate date,@newReport varchar(30),@restatement bigint,@start date,@end date,@newStart date,@newEnd date;
 SELECT @store=store_code,@date=business_date,@report=report_code,@start=COALESCE(period_start,business_date),@end=COALESCE(period_end,business_date) FROM dbo.import_files WITH(UPDLOCK,HOLDLOCK)
  WHERE import_file_id=@previous_file_id AND is_superseded=0;
 SELECT @newStore=store_code,@newDate=business_date,@newReport=report_code,@newStart=COALESCE(period_start,business_date),@newEnd=COALESCE(period_end,business_date) FROM dbo.import_files WHERE import_file_id=@replacement_file_id;
 IF @report IS NULL THROW 51040,''The previous current import file was not found.'',1;
 IF @store<>@newStore OR (@newStart>@start OR @newEnd<@end) OR @report<>@newReport THROW 51041,''A restatement must replace the same store and report type and cover the previous date range.'',1;
 IF EXISTS(SELECT 1 FROM dbo.daily_reporting_days WHERE store_code=@store AND business_date BETWEEN @newStart AND @newEnd AND status=''LOCKED'')
   THROW 51042,''Reopen the finalised business date before applying a restatement.'',1;

 INSERT dbo.import_restatements(store_code,business_date,report_code,previous_import_file_id,replacement_import_file_id,requested_by,reason,impact_summary)
 VALUES(@store,@date,@report,@previous_file_id,@replacement_file_id,@user,@reason,N''Previous canonical facts archived; replacement facts become the only current reporting generation.'');
 SET @restatement=SCOPE_IDENTITY();

 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''SalesLine'',l.source_lineage_id,
   (SELECT l.sales_line_id,l.sales_invoice_id,l.line_identifier,l.product_code,l.source_transaction_type,l.source_quantity,l.source_gross_amount,l.source_net_amount,l.source_tax_amount,l.source_brand_code,l.source_brand_name,l.brand_segment,l.currency_code FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_lines l JOIN dbo.source_lineage s ON s.source_lineage_id=l.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''InvoiceControl'',c.source_lineage_id,
   (SELECT c.sales_invoice_control_id,c.sales_invoice_id,c.source_transaction_type,c.source_invoice_quantity,c.source_net_value,c.currency_code FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_invoice_controls c JOIN dbo.source_lineage s ON s.source_lineage_id=c.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''Tender'',t.source_lineage_id,
   (SELECT t.sales_tender_id,t.sales_invoice_id,t.tender_type,t.source_amount,t.currency_code,t.is_reporting_eligible,t.exclusion_reason FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_tenders t JOIN dbo.source_lineage s ON s.source_lineage_id=t.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''StockMovement'',m.source_lineage_id,
   (SELECT m.stock_movement_id,m.store_code,m.document_number,m.invoice_year,m.document_date,m.product_code,m.source_transaction_type,m.from_location,m.to_location,m.opening_quantity,m.transaction_quantity,m.closing_quantity FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.stock_movements m JOIN dbo.source_lineage s ON s.source_lineage_id=m.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''StockSnapshot'',p.source_lineage_id,
   (SELECT p.stock_snapshot_id,p.store_code,p.snapshot_date,p.product_code,p.ean,p.brand_code,p.brand_name,p.cluster,p.gender,p.batch_number,p.source_uid,p.quantity,p.unit_cost,p.total_cost FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.stock_snapshots p JOIN dbo.source_lineage s ON s.source_lineage_id=p.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''SalesEnrichment'',e.source_lineage_id,
   (SELECT e.sales_line_enrichment_id,e.enrichment_type,e.store_code,e.transaction_date,e.document_number,e.product_code,e.source_transaction_type,e.source_quantity,e.source_net_value,e.source_gross_value,e.content_key,e.invoice_year,e.staff_name,e.source_cro_number,e.scheme_discount,e.user_discount,e.pre_discount,e.other_charges,e.activation_details,e.user_discount_details,e.match_status FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_line_enrichments e JOIN dbo.source_lineage s ON s.source_lineage_id=e.source_lineage_id WHERE s.import_file_id=@previous_file_id;

 UPDATE e SET matched_sales_line_id=NULL,match_status=''Missing''
 FROM dbo.sales_line_enrichments e JOIN dbo.sales_lines l ON l.sales_line_id=e.matched_sales_line_id
 JOIN dbo.source_lineage s ON s.source_lineage_id=l.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE e FROM dbo.sales_line_enrichments e JOIN dbo.source_lineage s ON s.source_lineage_id=e.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE l FROM dbo.sales_lines l JOIN dbo.source_lineage s ON s.source_lineage_id=l.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE c FROM dbo.sales_invoice_controls c JOIN dbo.source_lineage s ON s.source_lineage_id=c.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE t FROM dbo.sales_tenders t JOIN dbo.source_lineage s ON s.source_lineage_id=t.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE m FROM dbo.stock_movements m JOIN dbo.source_lineage s ON s.source_lineage_id=m.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE p FROM dbo.stock_snapshots p JOIN dbo.source_lineage s ON s.source_lineage_id=p.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 UPDATE dbo.import_files SET is_superseded=1,superseded_by_import_file_id=@replacement_file_id,superseded_utc=SYSUTCDATETIME(),superseded_by=@user,restatement_reason=@reason
 WHERE import_file_id=@previous_file_id;
END');
