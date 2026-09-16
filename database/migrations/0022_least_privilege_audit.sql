SET XACT_ABORT ON;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.record_operational_audit
 @type varchar(40), @outcome varchar(20), @detail nvarchar(200)=NULL, @version nvarchar(40)=N''database''
AS
BEGIN
 SET NOCOUNT ON;
 IF @detail LIKE N''%[0-9:/\]%'' THROW 51310,''Audit details cannot contain paths or identifiers.'',1;
 INSERT dbo.operational_audit(event_type,outcome,safe_detail,application_version,actor_name)
 VALUES(@type,@outcome,@detail,@version,SUSER_SNAME());
END');

-- Staff may propose changes, but cannot choose decision status, decision actor,
-- or an existing approval to attach a new adjustment to.
EXEC(N'CREATE OR ALTER PROCEDURE dbo.submit_approval_request
 @type varchar(max),@subjectType varchar(max),@subject nvarchar(max),@store varchar(max),@date date,@payload nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51313,''Owner or Store Manager permission is required to submit a request.'',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  INSERT dbo.approval_requests(approval_type,subject_type,subject_id,store_code,business_date,request_payload_json,requested_by,status)
  VALUES(@type,@subjectType,@subject,@store,@date,@payload,SUSER_SNAME(),''PENDING'');
  DECLARE @id bigint=SCOPE_IDENTITY();
  EXEC dbo.record_operational_audit ''Approval'',''Succeeded'',N''Approval requested'',N''database'';
  COMMIT TRANSACTION;
  SELECT @id;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.submit_controlled_adjustment
 @store varchar(max),@date date,@type varchar(max),@amount decimal(19,4),@reason nvarchar(max),@document bigint=NULL
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51313,''Owner or Store Manager permission is required to submit an adjustment.'',1;
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

EXEC(N'CREATE OR ALTER PROCEDURE dbo.decide_approval_request @id bigint,@approve bit,@reason nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51315,''Owner permission is required to decide a request.'',1;
 IF @approve IS NULL OR NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@reason,CHAR(9),'' ''),CHAR(10),'' ''),CHAR(13),'' ''))),'''') IS NULL
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

EXEC(N'CREATE OR ALTER TRIGGER dbo.tr_operational_audit_actor ON dbo.operational_audit
 INSTEAD OF INSERT AS
 BEGIN
  SET NOCOUNT ON;
  INSERT dbo.operational_audit(event_utc,event_type,outcome,safe_detail,application_version,actor_name)
  SELECT event_utc,event_type,outcome,safe_detail,application_version,SUSER_SNAME() FROM inserted;
 END');

EXEC(N'CREATE OR ALTER TRIGGER dbo.tr_operational_audit_append_only ON dbo.operational_audit
 INSTEAD OF UPDATE, DELETE AS
 BEGIN THROW 51311,''Audit history is append-only.'',1; END');

EXEC(N'CREATE OR ALTER TRIGGER dbo.tr_daily_reporting_events_append_only ON dbo.daily_reporting_events
 INSTEAD OF UPDATE, DELETE AS
 BEGIN THROW 51311,''Audit history is append-only.'',1; END');

SELECT TOP(0) operational_audit_id,event_utc,event_type,outcome,safe_detail,application_version,actor_name
 INTO dbo.operational_audit_archive FROM dbo.operational_audit;
CREATE UNIQUE INDEX UX_operational_audit_archive ON dbo.operational_audit_archive(operational_audit_id);

EXEC(N'CREATE OR ALTER PROCEDURE dbo.archive_operational_audit @retention_days int=365
AS
BEGIN
 SET NOCOUNT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51312,''Owner permission is required for maintenance.'',1;
 IF @retention_days<30 THROW 51312,''Keep at least thirty days of recent audit history.'',1;
 SET IDENTITY_INSERT dbo.operational_audit_archive ON;
 INSERT dbo.operational_audit_archive(operational_audit_id,event_utc,event_type,outcome,safe_detail,application_version,actor_name)
 SELECT a.operational_audit_id,a.event_utc,a.event_type,a.outcome,a.safe_detail,a.application_version,a.actor_name
 FROM dbo.operational_audit a WHERE a.event_utc<DATEADD(day,-@retention_days,SYSUTCDATETIME())
 AND NOT EXISTS(SELECT 1 FROM dbo.operational_audit_archive h WITH(UPDLOCK,HOLDLOCK) WHERE h.operational_audit_id=a.operational_audit_id);
 SET IDENTITY_INSERT dbo.operational_audit_archive OFF;
END');

EXEC(N'CREATE OR ALTER TRIGGER dbo.tr_operational_audit_archive_append_only ON dbo.operational_audit_archive
 INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51311,''Audit history is append-only.'',1; END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.persist_sales_enrichment
 @report varchar(30),@store varchar(30),@date date,@document nvarchar(100),@product nvarchar(100),@type varchar(30),
 @quantity decimal(19,4),@net decimal(19,4),@cro nvarchar(100),@scheme decimal(19,4),@userDiscount decimal(19,4),
 @pre decimal(19,4),@other decimal(19,4),@activation nvarchar(1000),@discountDetails nvarchar(1000),@lineage bigint
AS BEGIN SET NOCOUNT ON;
DECLARE @matchCount int,@salesLineId bigint;
SELECT @matchCount=COUNT(*),@salesLineId=CASE WHEN COUNT(*)=1 THEN MAX(l.sales_line_id) END
FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
WHERE i.store_code=@store AND i.transaction_date=@date AND i.document_number=@document AND l.product_code=@product;
DECLARE @status varchar(20)=CASE @matchCount WHEN 0 THEN ''Missing'' WHEN 1 THEN ''Matched'' ELSE ''Ambiguous'' END;
INSERT dbo.sales_line_enrichments
  (enrichment_type,store_code,transaction_date,document_number,product_code,source_transaction_type,source_quantity,source_net_value,
   source_cro_number,scheme_discount,user_discount,pre_discount,other_charges,activation_details,user_discount_details,
   matched_sales_line_id,match_status,source_lineage_id)
VALUES(@report,@store,@date,@document,@product,@type,@quantity,@net,@cro,@scheme,@userDiscount,@pre,@other,@activation,@discountDetails,
       @salesLineId,@status,@lineage);
SELECT @status;
END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.refresh_enrichment_matches AS BEGIN SET NOCOUNT ON;
IF OBJECT_ID(N''dbo.sales_line_enrichments'',N''U'') IS NOT NULL
UPDATE e SET matched_sales_line_id=matches.sales_line_id,match_status=matches.match_status
FROM dbo.sales_line_enrichments e
CROSS APPLY
(
  SELECT CASE WHEN COUNT_BIG(*)=1 THEN MAX(l.sales_line_id) END sales_line_id,
         CASE COUNT_BIG(*) WHEN 0 THEN ''Missing'' WHEN 1 THEN ''Matched'' ELSE ''Ambiguous'' END match_status
  FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
  WHERE i.store_code=e.store_code AND i.transaction_date=e.transaction_date
    AND i.document_number=e.document_number AND l.product_code=e.product_code
) matches
WHERE e.match_status<>''Matched'';
END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.configure_application_role @identity nvarchar(200),@role varchar(30),@active bit
AS BEGIN
 SET NOCOUNT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51312,''Owner permission is required to manage access.'',1;
 IF @role NOT IN (''OWNER'',''STORE_MANAGER'',''VIEWER'') OR @active IS NULL THROW 51312,''Select a valid application role.'',1;
IF SUSER_ID(@identity) IS NULL
BEGIN
  DECLARE @createLogin nvarchar(max)=N''CREATE LOGIN ''+QUOTENAME(@identity)+N'' FROM WINDOWS'';
  EXEC(@createLogin);
END;
DECLARE @principal sysname=(SELECT TOP(1) name FROM sys.database_principals WHERE sid=SUSER_SID(@identity));
IF @principal IS NULL
BEGIN
  DECLARE @createUser nvarchar(max)=N''CREATE USER ''+QUOTENAME(@identity)+N'' FOR LOGIN ''+QUOTENAME(@identity);
  EXEC(@createUser);
  SET @principal=@identity;
END;
IF @principal<>N''dbo''
BEGIN
  DECLARE @membership nvarchar(max)=N'''';
  IF IS_ROLEMEMBER(N''etp_owner'',@principal)=1 SET @membership+=N''ALTER ROLE etp_owner DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''etp_store_manager'',@principal)=1 SET @membership+=N''ALTER ROLE etp_store_manager DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''etp_viewer'',@principal)=1 SET @membership+=N''ALTER ROLE etp_viewer DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''etp_automation'',@principal)=1 SET @membership+=N''ALTER ROLE etp_automation DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''db_owner'',@principal)=1 SET @membership+=N''ALTER ROLE db_owner DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''db_datawriter'',@principal)=1 SET @membership+=N''ALTER ROLE db_datawriter DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''db_datareader'',@principal)=1 SET @membership+=N''ALTER ROLE db_datareader DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF IS_ROLEMEMBER(N''db_backupoperator'',@principal)=1 SET @membership+=N''ALTER ROLE db_backupoperator DROP MEMBER ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE DELETE ON SCHEMA::dbo FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.application_users FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.application_user_history FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.controlled_master_values FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.controlled_master_history FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.watch_folder_settings FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.stores FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.schema_migrations FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.report_pack_schedules FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE UPDATE,DELETE ON dbo.operational_audit FROM ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE UPDATE,DELETE ON dbo.automation_runs FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.product_settings'',N''U'') IS NOT NULL SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.product_settings FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.sharing_contacts'',N''U'') IS NOT NULL SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.sharing_contacts FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.kpi_catalogue'',N''U'') IS NOT NULL SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.kpi_catalogue FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.approval_requests'',N''U'') IS NOT NULL SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.approval_requests FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.controlled_adjustments'',N''U'') IS NOT NULL SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.controlled_adjustments FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.accounting_mappings'',N''U'') IS NOT NULL SET @membership+=N''REVOKE INSERT,UPDATE,DELETE ON dbo.accounting_mappings FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.accounting_batches'',N''U'') IS NOT NULL SET @membership+=N''REVOKE UPDATE,DELETE ON dbo.accounting_batches FROM ''+QUOTENAME(@principal)+N'';'';
  IF OBJECT_ID(N''dbo.accounting_entries'',N''U'') IS NOT NULL SET @membership+=N''REVOKE UPDATE,DELETE ON dbo.accounting_entries FROM ''+QUOTENAME(@principal)+N'';'';
  IF @role=''OWNER'' AND @active=1 SET @membership+=N''ALTER ROLE db_owner ADD MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF @role=''STORE_MANAGER'' AND @active=1 SET @membership+=N''ALTER ROLE etp_store_manager ADD MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF @role=''VIEWER'' AND @active=1 SET @membership+=N''ALTER ROLE etp_viewer ADD MEMBER ''+QUOTENAME(@principal)+N'';'';
  IF @role=''OWNER'' AND @active=1 SET @membership+=N''ALTER ROLE etp_owner ADD MEMBER ''+QUOTENAME(@principal)+N'';'';
  SET @membership+=CASE WHEN @active=1 THEN N''REVOKE CONNECT TO '' ELSE N''DENY CONNECT TO '' END+QUOTENAME(@principal)+N'';'';
  SET @membership+=N''REVOKE INSERT ON dbo.operational_audit FROM ''+QUOTENAME(@principal)+N'';'';
  EXEC(@membership);
END;
DECLARE @serverPermission nvarchar(max)=N''USE [master]; ''+CASE WHEN @role=''OWNER'' AND @active=1 THEN N''GRANT ALTER ANY LOGIN TO '' ELSE N''REVOKE ALTER ANY LOGIN TO '' END+QUOTENAME(@identity)+N'';'';
EXEC(@serverPermission);
END');

GRANT SELECT ON SCHEMA::dbo TO etp_viewer;
GRANT SELECT ON SCHEMA::dbo TO etp_store_manager;
DENY INSERT,UPDATE,DELETE ON dbo.operational_audit TO etp_viewer,etp_store_manager;
DENY UPDATE,DELETE ON dbo.daily_reporting_events TO etp_viewer,etp_store_manager;
DENY UPDATE,DELETE ON dbo.import_files TO etp_store_manager;
DENY INSERT,UPDATE,DELETE ON dbo.sales_lines TO etp_store_manager;
DENY INSERT,UPDATE,DELETE ON dbo.sales_invoices TO etp_store_manager;
DENY INSERT,UPDATE,DELETE ON dbo.sales_line_enrichments TO etp_store_manager;
GRANT EXECUTE ON dbo.record_operational_audit TO etp_viewer,etp_store_manager,etp_owner;
GRANT EXECUTE ON dbo.configure_application_role TO etp_owner;
GRANT EXECUTE ON dbo.archive_operational_audit TO etp_owner;

GRANT EXECUTE ON dbo.persist_sales_line TO etp_store_manager;

GRANT EXECUTE ON dbo.persist_sales_invoice_control TO etp_store_manager;

GRANT EXECUTE ON dbo.persist_sales_tender TO etp_store_manager;

GRANT EXECUTE ON dbo.persist_stock_movement TO etp_store_manager;

GRANT EXECUTE ON dbo.persist_stock_snapshot TO etp_store_manager;

GRANT EXECUTE ON dbo.persist_sales_enrichment TO etp_store_manager;

GRANT EXECUTE ON dbo.refresh_enrichment_matches TO etp_store_manager;

GRANT INSERT ON dbo.import_batches TO etp_store_manager;

GRANT INSERT ON dbo.import_profiles TO etp_store_manager;

GRANT INSERT ON dbo.import_files TO etp_store_manager;

GRANT INSERT ON dbo.source_lineage TO etp_store_manager;

GRANT INSERT ON dbo.daily_reporting_days TO etp_store_manager;

GRANT INSERT ON dbo.daily_reporting_events TO etp_store_manager;

GRANT INSERT ON dbo.daily_report_generations TO etp_store_manager;

GRANT INSERT ON dbo.automation_runs TO etp_store_manager;

GRANT INSERT ON dbo.source_documents TO etp_store_manager;

GRANT INSERT ON dbo.document_extractions TO etp_store_manager;

GRANT INSERT ON dbo.report_packages TO etp_store_manager;

GRANT INSERT ON dbo.share_attempts TO etp_store_manager;

GRANT INSERT ON dbo.import_row_outcomes TO etp_store_manager;

GRANT INSERT ON dbo.import_conflicts TO etp_store_manager;

DENY INSERT,UPDATE,DELETE ON dbo.approval_requests TO etp_store_manager,etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.controlled_adjustments TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.submit_approval_request TO etp_store_manager,etp_owner;
GRANT EXECUTE ON dbo.submit_controlled_adjustment TO etp_store_manager,etp_owner;
GRANT EXECUTE ON dbo.decide_approval_request TO etp_owner;

GRANT INSERT,UPDATE ON dbo.manual_operational_inputs TO etp_store_manager;

GRANT INSERT,UPDATE ON dbo.manual_stock_counts TO etp_store_manager;

GRANT INSERT,UPDATE ON dbo.staff_sales_targets TO etp_store_manager;

GRANT INSERT,UPDATE ON dbo.register_entries TO etp_store_manager;

GRANT INSERT,UPDATE ON dbo.data_quality_issues TO etp_store_manager;

GRANT UPDATE ON dbo.import_batches (status,source_row_count,completed_utc,failure_reason) TO etp_store_manager;

GRANT UPDATE ON dbo.daily_reporting_days (status,finalised_by,finalised_utc) TO etp_store_manager;

GRANT UPDATE ON dbo.daily_report_generations (is_final) TO etp_store_manager;

GRANT UPDATE ON dbo.report_pack_schedules (last_business_date,last_run_utc,last_status,last_message) TO etp_store_manager;

GRANT UPDATE ON dbo.source_documents (lifecycle_status,last_status_by,last_status_utc,safe_message) TO etp_store_manager;

GRANT UPDATE ON dbo.document_extractions (review_status,reviewed_by,reviewed_utc,review_reason) TO etp_store_manager;

DECLARE @identity nvarchar(200),@role varchar(30),@active bit;
DECLARE existing_users CURSOR LOCAL FAST_FORWARD FOR
 SELECT u.windows_identity,u.role_code,u.is_active FROM dbo.application_users u
 JOIN sys.database_principals p ON p.sid=SUSER_SID(u.windows_identity) WHERE p.name<>'dbo';
OPEN existing_users; FETCH NEXT FROM existing_users INTO @identity,@role,@active;
WHILE @@FETCH_STATUS=0
BEGIN
 EXEC dbo.configure_application_role @identity,@role,@active;
 FETCH NEXT FROM existing_users INTO @identity,@role,@active;
END;
CLOSE existing_users; DEALLOCATE existing_users;
