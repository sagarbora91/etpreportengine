SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.import_files ADD
    is_superseded bit NOT NULL CONSTRAINT DF_import_files_superseded DEFAULT(0),
    superseded_by_import_file_id bigint NULL,
    superseded_utc datetime2(3) NULL,
    superseded_by nvarchar(100) NULL,
    restatement_reason nvarchar(500) NULL;
EXEC(N'ALTER TABLE dbo.import_files ADD CONSTRAINT FK_import_files_replacement
    FOREIGN KEY(superseded_by_import_file_id) REFERENCES dbo.import_files(import_file_id)');
EXEC(N'ALTER TABLE dbo.import_files ADD CONSTRAINT CK_import_files_superseded
    CHECK((is_superseded=0 AND superseded_by_import_file_id IS NULL AND superseded_utc IS NULL AND superseded_by IS NULL AND restatement_reason IS NULL)
       OR (is_superseded=1 AND superseded_by_import_file_id IS NOT NULL AND superseded_utc IS NOT NULL AND superseded_by IS NOT NULL AND restatement_reason IS NOT NULL))');
EXEC(N'CREATE INDEX IX_import_files_current_scope ON dbo.import_files(store_code,business_date,report_code,is_superseded)');

CREATE TABLE dbo.import_restatements
(
    import_restatement_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_import_restatements PRIMARY KEY,
    store_code varchar(30) NOT NULL,
    business_date date NOT NULL,
    report_code varchar(30) NOT NULL,
    previous_import_file_id bigint NOT NULL,
    replacement_import_file_id bigint NOT NULL,
    requested_by nvarchar(100) NOT NULL,
    requested_utc datetime2(3) NOT NULL CONSTRAINT DF_import_restatements_requested DEFAULT SYSUTCDATETIME(),
    reason nvarchar(500) NOT NULL,
    impact_summary nvarchar(500) NOT NULL,
    CONSTRAINT FK_import_restatements_previous FOREIGN KEY(previous_import_file_id) REFERENCES dbo.import_files(import_file_id),
    CONSTRAINT FK_import_restatements_replacement FOREIGN KEY(replacement_import_file_id) REFERENCES dbo.import_files(import_file_id),
    CONSTRAINT UQ_import_restatements_replacement UNIQUE(replacement_import_file_id),
    CONSTRAINT CK_import_restatements_reason CHECK(LEN(LTRIM(RTRIM(reason)))>0)
);
CREATE INDEX IX_import_restatements_scope ON dbo.import_restatements(store_code,business_date,report_code,requested_utc DESC);

CREATE TABLE dbo.restatement_fact_archive
(
    restatement_fact_archive_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_restatement_fact_archive PRIMARY KEY,
    import_restatement_id bigint NOT NULL,
    fact_type varchar(40) NOT NULL,
    previous_source_lineage_id bigint NOT NULL,
    fact_json nvarchar(max) NOT NULL,
    archived_utc datetime2(3) NOT NULL CONSTRAINT DF_restatement_fact_archive_utc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_restatement_fact_archive_restatement FOREIGN KEY(import_restatement_id) REFERENCES dbo.import_restatements(import_restatement_id),
    CONSTRAINT CK_restatement_fact_archive_json CHECK(ISJSON(fact_json)=1)
);
CREATE INDEX IX_restatement_fact_archive_restatement ON dbo.restatement_fact_archive(import_restatement_id,fact_type);

CREATE TABLE dbo.manual_stock_counts
(
    manual_stock_count_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_manual_stock_counts PRIMARY KEY,
    store_code varchar(30) NOT NULL,
    business_date date NOT NULL,
    inventory_group_code nvarchar(100) NOT NULL,
    display_quantity decimal(19,4) NULL,
    backstock_quantity decimal(19,4) NULL,
    defective_quantity decimal(19,4) NULL,
    y_location_quantity decimal(19,4) NULL,
    counted_physical_quantity decimal(19,4) NULL,
    remarks nvarchar(500) NULL,
    entered_by nvarchar(100) NOT NULL,
    entered_utc datetime2(3) NOT NULL CONSTRAINT DF_manual_stock_counts_entered DEFAULT SYSUTCDATETIME(),
    modified_by nvarchar(100) NOT NULL,
    modified_utc datetime2(3) NOT NULL CONSTRAINT DF_manual_stock_counts_modified DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    row_version rowversion NOT NULL,
    CONSTRAINT UQ_manual_stock_counts_scope UNIQUE(store_code,business_date,inventory_group_code),
    CONSTRAINT CK_manual_stock_counts_group CHECK(LEN(LTRIM(RTRIM(inventory_group_code)))>0),
    CONSTRAINT CK_manual_stock_counts_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0),
    CONSTRAINT CK_manual_stock_counts_value CHECK(display_quantity IS NOT NULL OR backstock_quantity IS NOT NULL OR defective_quantity IS NOT NULL OR y_location_quantity IS NOT NULL OR counted_physical_quantity IS NOT NULL)
);

CREATE TABLE dbo.manual_stock_count_history
(
    manual_stock_count_history_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_manual_stock_count_history PRIMARY KEY,
    manual_stock_count_id bigint NOT NULL,
    store_code varchar(30) NOT NULL,
    business_date date NOT NULL,
    inventory_group_code nvarchar(100) NOT NULL,
    old_values_json nvarchar(max) NULL,
    new_values_json nvarchar(max) NULL,
    changed_by nvarchar(100) NOT NULL,
    changed_utc datetime2(3) NOT NULL CONSTRAINT DF_manual_stock_count_history_changed DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    CONSTRAINT CK_manual_stock_count_history_old_json CHECK(old_values_json IS NULL OR ISJSON(old_values_json)=1),
    CONSTRAINT CK_manual_stock_count_history_new_json CHECK(new_values_json IS NULL OR ISJSON(new_values_json)=1)
);
CREATE INDEX IX_manual_stock_count_history_scope ON dbo.manual_stock_count_history(store_code,business_date,changed_utc DESC);

CREATE TABLE dbo.staff_sales_targets
(
    staff_sales_target_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_staff_sales_targets PRIMARY KEY,
    store_code varchar(30) NOT NULL,
    cro_number nvarchar(80) NOT NULL,
    period_start date NOT NULL,
    period_end date NOT NULL,
    target_sales decimal(19,4) NOT NULL,
    entered_by nvarchar(100) NOT NULL,
    entered_utc datetime2(3) NOT NULL CONSTRAINT DF_staff_sales_targets_entered DEFAULT SYSUTCDATETIME(),
    modified_by nvarchar(100) NOT NULL,
    modified_utc datetime2(3) NOT NULL CONSTRAINT DF_staff_sales_targets_modified DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    row_version rowversion NOT NULL,
    CONSTRAINT UQ_staff_sales_targets_scope UNIQUE(store_code,cro_number,period_start,period_end),
    CONSTRAINT CK_staff_sales_targets_period CHECK(period_end>=period_start),
    CONSTRAINT CK_staff_sales_targets_cro CHECK(LEN(LTRIM(RTRIM(cro_number)))>0),
    CONSTRAINT CK_staff_sales_targets_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0)
);

CREATE TABLE dbo.staff_sales_target_history
(
    staff_sales_target_history_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_staff_sales_target_history PRIMARY KEY,
    staff_sales_target_id bigint NOT NULL,
    store_code varchar(30) NOT NULL,
    cro_number nvarchar(80) NOT NULL,
    period_start date NOT NULL,
    period_end date NOT NULL,
    old_target_sales decimal(19,4) NULL,
    new_target_sales decimal(19,4) NULL,
    changed_by nvarchar(100) NOT NULL,
    changed_utc datetime2(3) NOT NULL CONSTRAINT DF_staff_sales_target_history_changed DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL
);
CREATE INDEX IX_staff_sales_target_history_scope ON dbo.staff_sales_target_history(store_code,period_start,period_end,changed_utc DESC);

CREATE TABLE dbo.daily_report_generations
(
    daily_report_generation_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_daily_report_generations PRIMARY KEY,
    store_code varchar(30) NOT NULL,
    business_date date NOT NULL,
    generation_number int NOT NULL,
    content_sha256 char(64) NOT NULL,
    control_json nvarchar(max) NOT NULL,
    generated_by nvarchar(100) NOT NULL,
    generated_utc datetime2(3) NOT NULL CONSTRAINT DF_daily_report_generations_utc DEFAULT SYSUTCDATETIME(),
    is_final bit NOT NULL CONSTRAINT DF_daily_report_generations_final DEFAULT(0),
    supersedes_generation_id bigint NULL,
    CONSTRAINT UQ_daily_report_generations_number UNIQUE(store_code,business_date,generation_number),
    CONSTRAINT FK_daily_report_generations_previous FOREIGN KEY(supersedes_generation_id) REFERENCES dbo.daily_report_generations(daily_report_generation_id),
    CONSTRAINT CK_daily_report_generations_hash CHECK(content_sha256 NOT LIKE '%[^0-9a-f]%'),
    CONSTRAINT CK_daily_report_generations_json CHECK(ISJSON(control_json)=1)
);
CREATE INDEX IX_daily_report_generations_scope ON dbo.daily_report_generations(store_code,business_date,generation_number DESC);
CREATE INDEX IX_daily_report_generations_hash ON dbo.daily_report_generations(store_code,business_date,content_sha256);

ALTER TABLE dbo.operational_audit ADD actor_name nvarchar(100) NULL;
ALTER TABLE dbo.operational_audit DROP CONSTRAINT CK_operational_audit_type;
ALTER TABLE dbo.operational_audit ADD CONSTRAINT CK_operational_audit_type CHECK
 (event_type IN ('ApplicationStart','SessionStart','ConnectionTest','ImportBatch','ImportFailed','ReportRun','ExportExcel','ExportPdf','DatabaseSetup','SupportPackage',
                 'ManualInput','DayFinalised','DayReopened','ReportPack','Backup','RestoreDrill','ConfigurationChange','MappingProfileChange','Restatement','StockCount','StaffTarget'));

EXEC(N'CREATE TRIGGER dbo.trg_import_profiles_audit
ON dbo.import_profiles AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.operational_audit(event_type,outcome,safe_detail,application_version,actor_name)
 VALUES(''MappingProfileChange'',''Succeeded'',N''Import profile definition changed'',N''database'',ORIGINAL_LOGIN());
END');

EXEC(N'CREATE TRIGGER dbo.trg_manual_stock_counts_audit_lock
ON dbo.manual_stock_counts AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS
 (
   SELECT 1 FROM (SELECT store_code,business_date FROM inserted UNION SELECT store_code,business_date FROM deleted) x
   JOIN dbo.daily_reporting_days d ON d.store_code=x.store_code AND d.business_date=x.business_date
   WHERE d.status=''LOCKED''
 ) THROW 51036,''The business date is finalised. Reopen it before changing physical stock counts.'',1;

 INSERT dbo.manual_stock_count_history
   (manual_stock_count_id,store_code,business_date,inventory_group_code,old_values_json,new_values_json,changed_by,change_reason)
 SELECT COALESCE(i.manual_stock_count_id,d.manual_stock_count_id),COALESCE(i.store_code,d.store_code),COALESCE(i.business_date,d.business_date),
        COALESCE(i.inventory_group_code,d.inventory_group_code),
        CASE WHEN d.manual_stock_count_id IS NULL THEN NULL ELSE
          (SELECT d.display_quantity [display],d.backstock_quantity backstock,d.defective_quantity defective,d.y_location_quantity yLocation,d.counted_physical_quantity countedPhysical,d.remarks remarks FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) END,
        CASE WHEN i.manual_stock_count_id IS NULL THEN NULL ELSE
          (SELECT i.display_quantity [display],i.backstock_quantity backstock,i.defective_quantity defective,i.y_location_quantity yLocation,i.counted_physical_quantity countedPhysical,i.remarks remarks FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) END,
        COALESCE(i.modified_by,d.modified_by),COALESCE(i.change_reason,d.change_reason)
 FROM inserted i FULL OUTER JOIN deleted d ON d.manual_stock_count_id=i.manual_stock_count_id;
END');

EXEC(N'CREATE TRIGGER dbo.trg_staff_sales_targets_audit_lock
ON dbo.staff_sales_targets AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS
 (
   SELECT 1 FROM (SELECT store_code,period_start,period_end FROM inserted UNION SELECT store_code,period_start,period_end FROM deleted) x
   JOIN dbo.daily_reporting_days d ON d.store_code=x.store_code AND d.business_date BETWEEN x.period_start AND x.period_end
   WHERE d.status=''LOCKED''
 ) THROW 51037,''A covered business date is finalised. Reopen it before changing the staff target.'',1;

 INSERT dbo.staff_sales_target_history
   (staff_sales_target_id,store_code,cro_number,period_start,period_end,old_target_sales,new_target_sales,changed_by,change_reason)
 SELECT COALESCE(i.staff_sales_target_id,d.staff_sales_target_id),COALESCE(i.store_code,d.store_code),COALESCE(i.cro_number,d.cro_number),
        COALESCE(i.period_start,d.period_start),COALESCE(i.period_end,d.period_end),d.target_sales,i.target_sales,
        COALESCE(i.modified_by,d.modified_by),COALESCE(i.change_reason,d.change_reason)
 FROM inserted i FULL OUTER JOIN deleted d ON d.staff_sales_target_id=i.staff_sales_target_id;
END');

EXEC(N'CREATE TRIGGER dbo.trg_sales_invoices_protect_locked
ON dbo.sales_invoices AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS
 (
   SELECT 1 FROM (SELECT store_code,transaction_date FROM inserted UNION SELECT store_code,transaction_date FROM deleted) x
   JOIN dbo.daily_reporting_days d ON d.store_code=x.store_code AND d.business_date=x.transaction_date
   WHERE d.status=''LOCKED''
 ) THROW 51038,''The business date is finalised. Reopen it before changing invoice identity.'',1;
END');

EXEC(N'CREATE TRIGGER dbo.trg_source_lineage_protect_locked
ON dbo.source_lineage AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS
 (
   SELECT 1 FROM (SELECT import_file_id FROM inserted UNION SELECT import_file_id FROM deleted) x
   JOIN dbo.import_files f ON f.import_file_id=x.import_file_id
   JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date=f.business_date
   WHERE d.status=''LOCKED''
 ) THROW 51043,''The business date is finalised. Reopen it before changing source lineage.'',1;
END');

EXEC(N'CREATE TRIGGER dbo.trg_daily_report_generations_immutable
ON dbo.daily_report_generations AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM inserted) THROW 51044,''Report generations cannot be deleted.'',1;
 IF EXISTS
 (
   SELECT 1 FROM inserted i JOIN deleted d ON d.daily_report_generation_id=i.daily_report_generation_id
   WHERE d.is_final<>0 OR i.is_final<>1 OR
         i.store_code<>d.store_code OR i.business_date<>d.business_date OR i.generation_number<>d.generation_number OR
         i.content_sha256<>d.content_sha256 OR i.control_json<>d.control_json OR i.generated_by<>d.generated_by OR
         i.generated_utc<>d.generated_utc OR ISNULL(i.supersedes_generation_id,-1)<>ISNULL(d.supersedes_generation_id,-1)
 ) THROW 51045,''A report generation is immutable except for its one-way finalisation flag.'',1;
END');

EXEC(N'CREATE TRIGGER dbo.trg_restatement_archive_immutable
ON dbo.restatement_fact_archive AFTER UPDATE,DELETE AS
BEGIN
 THROW 51046,''Restatement fact archives are immutable.'',1;
END');

EXEC(N'CREATE TRIGGER dbo.trg_import_restatements_immutable
ON dbo.import_restatements AFTER UPDATE,DELETE AS
BEGIN
 THROW 51047,''Applied import restatements are immutable.'',1;
END');

EXEC(N'ALTER TRIGGER dbo.trg_import_files_protect_locked
ON dbo.import_files AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS
 (
   SELECT 1 FROM (SELECT store_code,business_date FROM inserted UNION SELECT store_code,business_date FROM deleted) x
   JOIN dbo.daily_reporting_days d ON d.store_code=x.store_code AND d.business_date=x.business_date
   WHERE d.status=''LOCKED''
 ) THROW 51021,''The business date is finalised. Reopen it before importing a restatement.'',1;
END');

EXEC(N'CREATE PROCEDURE dbo.prepare_import_restatement
 @previous_file_id bigint,@replacement_file_id bigint,@user nvarchar(100),@reason nvarchar(500)
AS
BEGIN
 SET NOCOUNT ON;
 IF LEN(LTRIM(RTRIM(@reason)))=0 THROW 51039,''A restatement reason is required.'',1;
 DECLARE @store varchar(30),@date date,@report varchar(30),@newStore varchar(30),@newDate date,@newReport varchar(30),@restatement bigint;
 SELECT @store=store_code,@date=business_date,@report=report_code FROM dbo.import_files WITH(UPDLOCK,HOLDLOCK)
  WHERE import_file_id=@previous_file_id AND is_superseded=0;
 SELECT @newStore=store_code,@newDate=business_date,@newReport=report_code FROM dbo.import_files WHERE import_file_id=@replacement_file_id;
 IF @report IS NULL THROW 51040,''The previous current import file was not found.'',1;
 IF @store<>@newStore OR @date<>@newDate OR @report<>@newReport THROW 51041,''A restatement must replace the same store, business date and report type.'',1;
 IF EXISTS(SELECT 1 FROM dbo.daily_reporting_days WHERE store_code=@store AND business_date=@date AND status=''LOCKED'')
   THROW 51042,''Reopen the finalised business date before applying a restatement.'',1;

 INSERT dbo.import_restatements(store_code,business_date,report_code,previous_import_file_id,replacement_import_file_id,requested_by,reason,impact_summary)
 VALUES(@store,@date,@report,@previous_file_id,@replacement_file_id,@user,@reason,N''Previous canonical facts archived; replacement facts become the only current reporting generation.'');
 SET @restatement=SCOPE_IDENTITY();

 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''SalesLine'',l.source_lineage_id,
   (SELECT l.sales_line_id,l.sales_invoice_id,l.line_identifier,l.product_code,l.source_transaction_type,l.source_quantity,l.source_gross_amount,l.source_net_amount,l.source_brand_code,l.source_brand_name,l.brand_segment,l.currency_code FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
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
   (SELECT e.sales_line_enrichment_id,e.enrichment_type,e.store_code,e.transaction_date,e.document_number,e.product_code,e.source_transaction_type,e.source_quantity,e.source_net_value,e.source_cro_number,e.scheme_discount,e.user_discount,e.pre_discount,e.other_charges,e.activation_details,e.user_discount_details,e.match_status FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
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

COMMIT TRANSACTION;
