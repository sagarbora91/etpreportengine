SET XACT_ABORT ON;

-- Keep retired request history readable; no new unused request kinds may be submitted.
UPDATE dbo.approval_requests SET status='CANCELLED',decided_by=SUSER_SNAME(),decided_utc=SYSUTCDATETIME(),decision_reason=N'Retired unused request type.'
 WHERE approval_type IN('REOPEN_DAY','MASTER_MAPPING','CONTROL_WAIVER') AND status='PENDING';

CREATE TABLE dbo.import_restatement_approvals(
 approval_request_id bigint NOT NULL PRIMARY KEY REFERENCES dbo.approval_requests(approval_request_id),
 previous_import_file_id bigint NOT NULL REFERENCES dbo.import_files(import_file_id),
 replacement_sha256 char(64) NOT NULL,
 report_code varchar(30) NOT NULL,
 store_code varchar(30) NOT NULL,
 period_start date NOT NULL,
 period_end date NOT NULL,
 request_reason nvarchar(500) NOT NULL,
 binding_hash binary(32) NOT NULL UNIQUE,
 applied_import_file_id bigint NULL REFERENCES dbo.import_files(import_file_id));

EXEC(N'CREATE OR ALTER PROCEDURE dbo.save_register_entry
 @type varchar(30),@document bigint=NULL,@store varchar(30),@date date,@number nvarchar(100),
 @documentDate date=NULL,@counterparty nvarchar(200)=NULL,@quantity decimal(19,4)=NULL,@amount decimal(19,4)=NULL,
 @reference nvarchar(200)=NULL,@received nvarchar(100)=NULL,@verification varchar(20),@remarks nvarchar(1000)=NULL,@reason nvarchar(500)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @owner bit=CASE WHEN IS_ROLEMEMBER(''etp_owner'')=1 OR IS_SRVROLEMEMBER(''sysadmin'')=1 THEN 1 ELSE 0 END;
 IF @owner=0 AND COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 THROW 51550,''Owner or Store Manager permission is required.'',1;
 IF NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@reason,CHAR(9),'' ''),CHAR(10),'' ''),CHAR(13),'' ''))),'''') IS NULL THROW 51551,''Enter a register change reason.'',1;
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

EXEC(N'CREATE OR ALTER PROCEDURE dbo.submit_approval_request
 @type varchar(max),@subjectType varchar(max),@subject nvarchar(max),@store varchar(max),@date date,@payload nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51313,''Owner permission is required to submit this request.'',1;
 IF @type<>''ACCOUNTING_MAPPING'' THROW 51554,''Use the adjustment or restatement workflow to create a bound request.'',1;
 BEGIN TRY BEGIN TRANSACTION;
 INSERT dbo.approval_requests(approval_type,subject_type,subject_id,store_code,business_date,request_payload_json,requested_by,status)
 VALUES(@type,@subjectType,@subject,@store,@date,@payload,SUSER_SNAME(),''PENDING'');
 DECLARE @id bigint=SCOPE_IDENTITY();
 EXEC dbo.record_operational_audit ''Approval'',''Succeeded'',N''Approval requested'',N''database'';
 COMMIT TRANSACTION; SELECT @id;
 END TRY BEGIN CATCH IF XACT_STATE()<>0 ROLLBACK TRANSACTION; THROW; END CATCH;
END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.submit_controlled_adjustment
 @store varchar(max),@date date,@type varchar(max),@amount decimal(19,4),@reason nvarchar(max),@document bigint=NULL
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51313,''Owner permission is required to submit an adjustment.'',1;
 IF NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@reason,CHAR(9),'' ''),CHAR(10),'' ''),CHAR(13),'' ''))),'''') IS NULL
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
 IF NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@reason,CHAR(9),'' ''),CHAR(10),'' ''),CHAR(13),'' ''))),'''') IS NULL THROW 51039,''A restatement reason is required.'',1;
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
 IF LEN(LTRIM(RTRIM(@reason)))=0 THROW 51039,''A restatement reason is required.'',1;
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

EXEC(N'CREATE OR ALTER PROCEDURE dbo.prepare_import_restatement
 @previous_file_id bigint,@replacement_file_id bigint,@user nvarchar(100),@reason nvarchar(500)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51421,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 DECLARE @id bigint;
 SELECT @id=r.approval_request_id FROM dbo.import_restatement_approvals r WITH(UPDLOCK,HOLDLOCK)
 JOIN dbo.approval_requests a ON a.approval_request_id=r.approval_request_id
 JOIN dbo.import_files fresh ON fresh.import_file_id=@replacement_file_id
 JOIN dbo.import_files old ON old.import_file_id=r.previous_import_file_id
 JOIN dbo.import_batches b ON b.import_batch_id=fresh.import_batch_id
 WHERE r.previous_import_file_id=@previous_file_id AND r.applied_import_file_id IS NULL AND a.status=''APPROVED''
  AND r.replacement_sha256=fresh.source_sha256 AND r.store_code=fresh.store_code AND r.report_code=fresh.report_code
  AND r.period_start=COALESCE(fresh.period_start,fresh.business_date) AND r.period_end=COALESCE(fresh.period_end,fresh.business_date)
  AND r.request_reason=@reason COLLATE Latin1_General_100_BIN2 AND old.is_superseded=0 AND fresh.is_superseded=0
  AND fresh.data_truth_version=1 AND b.status=''Processing'' AND @previous_file_id<>@replacement_file_id;
 IF @id IS NULL THROW 51556,''This exact replacement and reason require Owner approval before import. Request approval and retry after the decision.'',1;
 UPDATE dbo.import_restatement_approvals SET applied_import_file_id=@replacement_file_id WHERE approval_request_id=@id;
 EXEC dbo.replace_import_facts_internal @previous_file_id,@replacement_file_id,@user,@reason;
END');

DENY INSERT,UPDATE,DELETE ON dbo.register_entries TO etp_store_manager,etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.import_restatement_approvals TO etp_store_manager,etp_viewer,etp_owner;
GRANT SELECT ON dbo.import_restatement_approvals TO etp_owner,etp_store_manager;
GRANT EXECUTE ON dbo.save_register_entry TO etp_store_manager,etp_owner;
GRANT EXECUTE ON dbo.request_import_restatement TO etp_store_manager,etp_owner;
GRANT EXECUTE ON dbo.prepare_import_restatement TO etp_store_manager,etp_owner;
DENY EXECUTE ON dbo.replace_import_facts_internal TO etp_store_manager,etp_viewer,etp_owner;
DENY EXECUTE ON dbo.submit_controlled_adjustment TO etp_store_manager,etp_viewer;
DENY EXECUTE ON dbo.submit_approval_request TO etp_store_manager,etp_viewer;
DENY EXECUTE ON dbo.save_register_entry TO etp_viewer;
DENY EXECUTE ON dbo.request_import_restatement TO etp_viewer;
DENY EXECUTE ON dbo.prepare_import_restatement TO etp_viewer;
