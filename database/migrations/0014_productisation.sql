SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE dbo.product_settings
(
    product_setting_id tinyint NOT NULL CONSTRAINT PK_product_settings PRIMARY KEY,
    document_repository_path nvarchar(500) NOT NULL,
    share_folder_path nvarchar(500) NOT NULL,
    ocr_helper_path nvarchar(500) NULL,
    ocr_model_path nvarchar(500) NULL,
    smtp_host nvarchar(255) NULL,
    smtp_port int NULL,
    smtp_use_tls bit NOT NULL CONSTRAINT DF_product_settings_tls DEFAULT(1),
    smtp_from_address nvarchar(320) NULL,
    maximum_attachment_mb int NOT NULL CONSTRAINT DF_product_settings_attachment DEFAULT(20),
    modified_by nvarchar(200) NOT NULL,
    modified_utc datetime2(3) NOT NULL CONSTRAINT DF_product_settings_modified DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    CONSTRAINT CK_product_settings_singleton CHECK(product_setting_id=1),
    CONSTRAINT CK_product_settings_port CHECK(smtp_port IS NULL OR smtp_port BETWEEN 1 AND 65535),
    CONSTRAINT CK_product_settings_attachment CHECK(maximum_attachment_mb BETWEEN 1 AND 100),
    CONSTRAINT CK_product_settings_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0)
);
INSERT dbo.product_settings(product_setting_id,document_repository_path,share_folder_path,modified_by,change_reason)
VALUES(1,N'C:\ProgramData\EtpReporting\Documents',N'C:\ProgramData\EtpReporting\Share',SUSER_SNAME(),N'Safe offline-first product defaults');

CREATE TABLE dbo.source_documents
(
    source_document_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_source_documents PRIMARY KEY,
    original_file_name nvarchar(260) NOT NULL,
    managed_file_path nvarchar(500) NOT NULL,
    source_sha256 char(64) NOT NULL,
    size_bytes bigint NOT NULL,
    source_type varchar(40) NOT NULL,
    document_type varchar(50) NULL,
    store_code varchar(30) NULL,
    business_date date NULL,
    lifecycle_status varchar(20) NOT NULL,
    report_code varchar(30) NULL,
    import_file_id bigint NULL,
    report_generation_id bigint NULL,
    received_by nvarchar(200) NOT NULL,
    received_utc datetime2(3) NOT NULL CONSTRAINT DF_source_documents_received DEFAULT SYSUTCDATETIME(),
    last_status_by nvarchar(200) NOT NULL,
    last_status_utc datetime2(3) NOT NULL CONSTRAINT DF_source_documents_status_utc DEFAULT SYSUTCDATETIME(),
    safe_message nvarchar(500) NULL,
    retention_status varchar(20) NOT NULL CONSTRAINT DF_source_documents_retention DEFAULT('RETAIN'),
    CONSTRAINT UQ_source_documents_hash UNIQUE(source_sha256),
    CONSTRAINT CK_source_documents_hash CHECK(source_sha256 NOT LIKE '%[^0-9a-f]%'),
    CONSTRAINT CK_source_documents_size CHECK(size_bytes>0),
    CONSTRAINT CK_source_documents_source_type CHECK(source_type IN('ETP_WORKBOOK','ZIP','PDF','IMAGE','OTHER')),
    CONSTRAINT CK_source_documents_lifecycle CHECK(lifecycle_status IN('RECEIVED','IDENTIFIED','VALIDATED','IMPORTED','QUARANTINED','DUPLICATE','CONFLICT','SUPERSEDED','ARCHIVED','REVIEW_REQUIRED')),
    CONSTRAINT CK_source_documents_retention CHECK(retention_status IN('RETAIN','LEGAL_HOLD','SUPERSEDED')),
    CONSTRAINT FK_source_documents_import FOREIGN KEY(import_file_id) REFERENCES dbo.import_files(import_file_id),
    CONSTRAINT FK_source_documents_generation FOREIGN KEY(report_generation_id) REFERENCES dbo.daily_report_generations(daily_report_generation_id)
);
CREATE INDEX IX_source_documents_inbox ON dbo.source_documents(lifecycle_status,received_utc DESC) INCLUDE(original_file_name,source_type,document_type,store_code,business_date);
CREATE INDEX IX_source_documents_search ON dbo.source_documents(store_code,business_date,document_type);

CREATE TABLE dbo.document_extractions
(
    document_extraction_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_document_extractions PRIMARY KEY,
    source_document_id bigint NOT NULL,
    extraction_method varchar(30) NOT NULL,
    extraction_version nvarchar(80) NOT NULL,
    page_number int NULL,
    extracted_text nvarchar(max) NOT NULL,
    confidence decimal(5,4) NULL,
    bounding_box_json nvarchar(max) NULL,
    structured_fields_json nvarchar(max) NULL,
    review_status varchar(20) NOT NULL,
    reviewed_by nvarchar(200) NULL,
    reviewed_utc datetime2(3) NULL,
    review_reason nvarchar(500) NULL,
    created_utc datetime2(3) NOT NULL CONSTRAINT DF_document_extractions_created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_document_extractions_document FOREIGN KEY(source_document_id) REFERENCES dbo.source_documents(source_document_id),
    CONSTRAINT CK_document_extractions_method CHECK(extraction_method IN('NATIVE_PDF','PADDLE_OCR','MANUAL','NONE')),
    CONSTRAINT CK_document_extractions_page CHECK(page_number IS NULL OR page_number>0),
    CONSTRAINT CK_document_extractions_confidence CHECK(confidence IS NULL OR confidence BETWEEN 0 AND 1),
    CONSTRAINT CK_document_extractions_bbox CHECK(bounding_box_json IS NULL OR ISJSON(bounding_box_json)=1),
    CONSTRAINT CK_document_extractions_fields CHECK(structured_fields_json IS NULL OR ISJSON(structured_fields_json)=1),
    CONSTRAINT CK_document_extractions_review CHECK(review_status IN('NOT_REQUIRED','REVIEW_REQUIRED','VERIFIED','REJECTED'))
);
CREATE INDEX IX_document_extractions_review ON dbo.document_extractions(review_status,created_utc DESC);

CREATE TABLE dbo.register_entries
(
    register_entry_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_register_entries PRIMARY KEY,
    register_type varchar(40) NOT NULL,
    source_document_id bigint NULL,
    store_code varchar(30) NOT NULL,
    business_date date NOT NULL,
    document_number nvarchar(100) NOT NULL,
    document_date date NULL,
    counterparty nvarchar(200) NULL,
    quantity decimal(19,4) NULL,
    amount decimal(19,4) NULL,
    reference nvarchar(200) NULL,
    received_by nvarchar(200) NULL,
    verification_status varchar(20) NOT NULL,
    remarks nvarchar(1000) NULL,
    created_by nvarchar(200) NOT NULL,
    created_utc datetime2(3) NOT NULL CONSTRAINT DF_register_entries_created DEFAULT SYSUTCDATETIME(),
    modified_by nvarchar(200) NOT NULL,
    modified_utc datetime2(3) NOT NULL CONSTRAINT DF_register_entries_modified DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    CONSTRAINT FK_register_entries_document FOREIGN KEY(source_document_id) REFERENCES dbo.source_documents(source_document_id),
    CONSTRAINT UQ_register_entries_business UNIQUE(register_type,store_code,business_date,document_number),
    CONSTRAINT CK_register_entries_type CHECK(register_type IN('INWARD','OUTWARD','CREDIT_NOTE','SERVICE_RECEIPT','COURIER','STOCK_TRANSFER','EXPENSE','VENDOR_INVOICE')),
    CONSTRAINT CK_register_entries_quantity CHECK(quantity IS NULL OR quantity>=0),
    CONSTRAINT CK_register_entries_verification CHECK(verification_status IN('DRAFT','REVIEW_REQUIRED','VERIFIED','REJECTED')),
    CONSTRAINT CK_register_entries_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0)
);
CREATE INDEX IX_register_entries_search ON dbo.register_entries(store_code,business_date,register_type) INCLUDE(document_number,counterparty,amount,verification_status);

CREATE TABLE dbo.register_entry_history
(
    register_entry_history_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_register_entry_history PRIMARY KEY,
    register_entry_id bigint NOT NULL,
    old_values_json nvarchar(max) NULL,
    new_values_json nvarchar(max) NULL,
    changed_by nvarchar(200) NOT NULL,
    changed_utc datetime2(3) NOT NULL CONSTRAINT DF_register_entry_history_created DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    CONSTRAINT CK_register_entry_history_old CHECK(old_values_json IS NULL OR ISJSON(old_values_json)=1),
    CONSTRAINT CK_register_entry_history_new CHECK(new_values_json IS NULL OR ISJSON(new_values_json)=1)
);
CREATE INDEX IX_register_entry_history_entry ON dbo.register_entry_history(register_entry_id,changed_utc DESC);

CREATE TABLE dbo.import_row_outcomes
(
    import_row_outcome_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_import_row_outcomes PRIMARY KEY,
    import_file_id bigint NOT NULL,
    source_lineage_id bigint NULL,
    business_identity nvarchar(400) NOT NULL,
    outcome varchar(20) NOT NULL,
    content_sha256 char(64) NOT NULL,
    safe_message nvarchar(500) NOT NULL,
    recorded_utc datetime2(3) NOT NULL CONSTRAINT DF_import_row_outcomes_recorded DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_import_row_outcomes_file FOREIGN KEY(import_file_id) REFERENCES dbo.import_files(import_file_id),
    CONSTRAINT FK_import_row_outcomes_lineage FOREIGN KEY(source_lineage_id) REFERENCES dbo.source_lineage(source_lineage_id),
    CONSTRAINT CK_import_row_outcomes_outcome CHECK(outcome IN('NEW','ALREADY_PRESENT','CONFLICT','INVALID')),
    CONSTRAINT CK_import_row_outcomes_hash CHECK(content_sha256 NOT LIKE '%[^0-9a-f]%')
);
CREATE INDEX IX_import_row_outcomes_file ON dbo.import_row_outcomes(import_file_id,outcome);

CREATE TABLE dbo.import_conflicts
(
    import_conflict_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_import_conflicts PRIMARY KEY,
    import_file_id bigint NOT NULL,
    source_lineage_id bigint NULL,
    store_code varchar(30) NULL,
    business_date date NULL,
    report_code varchar(30) NULL,
    business_identity nvarchar(400) NOT NULL,
    existing_content_sha256 char(64) NOT NULL,
    incoming_content_sha256 char(64) NOT NULL,
    safe_difference nvarchar(1000) NOT NULL,
    status varchar(20) NOT NULL CONSTRAINT DF_import_conflicts_status DEFAULT('OPEN'),
    resolution_reason nvarchar(1000) NULL,
    resolved_by nvarchar(200) NULL,
    resolved_utc datetime2(3) NULL,
    created_utc datetime2(3) NOT NULL CONSTRAINT DF_import_conflicts_created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_import_conflicts_file FOREIGN KEY(import_file_id) REFERENCES dbo.import_files(import_file_id),
    CONSTRAINT FK_import_conflicts_lineage FOREIGN KEY(source_lineage_id) REFERENCES dbo.source_lineage(source_lineage_id),
    CONSTRAINT CK_import_conflicts_status CHECK(status IN('OPEN','ACKNOWLEDGED','RESTATEMENT_REQUESTED','RESOLVED','REJECTED')),
    CONSTRAINT CK_import_conflicts_existing_hash CHECK(existing_content_sha256 NOT LIKE '%[^0-9a-f]%'),
    CONSTRAINT CK_import_conflicts_incoming_hash CHECK(incoming_content_sha256 NOT LIKE '%[^0-9a-f]%')
);
CREATE INDEX IX_import_conflicts_queue ON dbo.import_conflicts(status,created_utc DESC) INCLUDE(store_code,business_date,report_code,business_identity);

CREATE TABLE dbo.data_quality_issues
(
    data_quality_issue_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_data_quality_issues PRIMARY KEY,
    issue_key nvarchar(300) NOT NULL,
    category varchar(50) NOT NULL,
    severity varchar(10) NOT NULL,
    store_code varchar(30) NULL,
    business_date date NULL,
    technical_control_status varchar(20) NOT NULL,
    workflow_status varchar(20) NOT NULL,
    safe_summary nvarchar(500) NOT NULL,
    assigned_to nvarchar(200) NULL,
    opened_utc datetime2(3) NOT NULL CONSTRAINT DF_data_quality_issues_opened DEFAULT SYSUTCDATETIME(),
    modified_by nvarchar(200) NOT NULL,
    modified_utc datetime2(3) NOT NULL CONSTRAINT DF_data_quality_issues_modified DEFAULT SYSUTCDATETIME(),
    resolution_reason nvarchar(1000) NULL,
    CONSTRAINT UQ_data_quality_issues_key UNIQUE(issue_key),
    CONSTRAINT CK_data_quality_issues_severity CHECK(severity IN('INFO','WARNING','CRITICAL')),
    CONSTRAINT CK_data_quality_issues_control CHECK(technical_control_status IN('PASS','WARNING','FAIL','PENDING')),
    CONSTRAINT CK_data_quality_issues_workflow CHECK(workflow_status IN('OPEN','ACKNOWLEDGED','RESOLVED','WAIVED'))
);
CREATE INDEX IX_data_quality_issues_queue ON dbo.data_quality_issues(workflow_status,severity,business_date DESC);

CREATE TABLE dbo.approval_requests
(
    approval_request_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_approval_requests PRIMARY KEY,
    approval_type varchar(40) NOT NULL,
    subject_type varchar(50) NOT NULL,
    subject_id nvarchar(100) NOT NULL,
    store_code varchar(30) NULL,
    business_date date NULL,
    request_payload_json nvarchar(max) NOT NULL,
    requested_by nvarchar(200) NOT NULL,
    requested_utc datetime2(3) NOT NULL CONSTRAINT DF_approval_requests_requested DEFAULT SYSUTCDATETIME(),
    status varchar(20) NOT NULL CONSTRAINT DF_approval_requests_status DEFAULT('PENDING'),
    decided_by nvarchar(200) NULL,
    decided_utc datetime2(3) NULL,
    decision_reason nvarchar(1000) NULL,
    CONSTRAINT CK_approval_requests_payload CHECK(ISJSON(request_payload_json)=1),
    CONSTRAINT CK_approval_requests_type CHECK(approval_type IN('RESTATEMENT','REOPEN_DAY','MASTER_MAPPING','ACCOUNTING_MAPPING','ADJUSTMENT','CONTROL_WAIVER')),
    CONSTRAINT CK_approval_requests_status CHECK(status IN('PENDING','APPROVED','REJECTED','CANCELLED'))
);
CREATE INDEX IX_approval_requests_queue ON dbo.approval_requests(status,requested_utc DESC);

CREATE TABLE dbo.controlled_adjustments
(
    controlled_adjustment_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_controlled_adjustments PRIMARY KEY,
    store_code varchar(30) NOT NULL,
    business_date date NOT NULL,
    adjustment_type varchar(50) NOT NULL,
    amount decimal(19,4) NOT NULL,
    reason nvarchar(1000) NOT NULL,
    source_document_id bigint NULL,
    approval_request_id bigint NOT NULL,
    status varchar(20) NOT NULL CONSTRAINT DF_controlled_adjustments_status DEFAULT('PENDING'),
    created_by nvarchar(200) NOT NULL,
    created_utc datetime2(3) NOT NULL CONSTRAINT DF_controlled_adjustments_created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_controlled_adjustments_document FOREIGN KEY(source_document_id) REFERENCES dbo.source_documents(source_document_id),
    CONSTRAINT FK_controlled_adjustments_approval FOREIGN KEY(approval_request_id) REFERENCES dbo.approval_requests(approval_request_id),
    CONSTRAINT CK_controlled_adjustments_reason CHECK(LEN(LTRIM(RTRIM(reason)))>0),
    CONSTRAINT CK_controlled_adjustments_status CHECK(status IN('PENDING','APPROVED','REJECTED','REVERSED'))
);
CREATE INDEX IX_controlled_adjustments_scope ON dbo.controlled_adjustments(store_code,business_date,status);

CREATE TABLE dbo.kpi_catalogue
(
    kpi_code varchar(50) NOT NULL CONSTRAINT PK_kpi_catalogue PRIMARY KEY,
    business_name nvarchar(150) NOT NULL,
    definition nvarchar(1000) NOT NULL,
    formula nvarchar(1000) NOT NULL,
    data_source nvarchar(500) NOT NULL,
    effective_date date NOT NULL,
    version int NOT NULL,
    approval_status varchar(20) NOT NULL,
    approved_by nvarchar(200) NULL,
    is_active bit NOT NULL CONSTRAINT DF_kpi_catalogue_active DEFAULT(1),
    CONSTRAINT CK_kpi_catalogue_version CHECK(version>0),
    CONSTRAINT CK_kpi_catalogue_approval CHECK(approval_status IN('APPROVED','DRAFT','RETIRED'))
);
INSERT dbo.kpi_catalogue(kpi_code,business_name,definition,formula,data_source,effective_date,version,approval_status,approved_by)
VALUES
('NET_SALES',N'Net Sales',N'Primary sales value including GST, with sales returns retaining their negative signs.',N'SUM(R025.NETVALUE)',N'Canonical sales lines sourced from R025 NETVALUE',CONVERT(date,'2026-07-01'),1,'APPROVED',SUSER_SNAME()),
('VOLUME',N'Volume',N'Net item quantity after invoice and sales-return signs.',N'SUM(R025.QUANTITY)',N'Canonical sales lines',CONVERT(date,'2026-07-01'),1,'APPROVED',SUSER_SNAME()),
('INVOICE_COUNT',N'Invoice Count',N'Distinct business documents in the selected scope.',N'COUNT(DISTINCT store + year + document)',N'Canonical sales invoices',CONVERT(date,'2026-07-01'),1,'APPROVED',SUSER_SNAME()),
('TENDER_VARIANCE',N'Tender Variance',N'Revenue control total less eligible tender total.',N'R022 NETVALUE - eligible R022 tender total',N'Canonical invoice controls and eligible tenders',CONVERT(date,'2026-07-01'),1,'APPROVED',SUSER_SNAME()),
('STOCK_VARIANCE',N'Stock Variance',N'Physical closing count less system closing quantity.',N'Physical stock - system stock',N'Manual stock counts and canonical stock facts',CONVERT(date,'2026-07-01'),1,'APPROVED',SUSER_SNAME());

CREATE TABLE dbo.sharing_contacts
(
    sharing_contact_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_sharing_contacts PRIMARY KEY,
    display_name nvarchar(200) NOT NULL,
    contact_role nvarchar(100) NULL,
    email_address nvarchar(320) NULL,
    phone_e164 varchar(20) NULL,
    default_subscriptions nvarchar(500) NULL,
    is_active bit NOT NULL CONSTRAINT DF_sharing_contacts_active DEFAULT(1),
    modified_by nvarchar(200) NOT NULL,
    modified_utc datetime2(3) NOT NULL CONSTRAINT DF_sharing_contacts_modified DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    CONSTRAINT CK_sharing_contacts_destination CHECK(email_address IS NOT NULL OR phone_e164 IS NOT NULL),
    CONSTRAINT CK_sharing_contacts_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0)
);

CREATE TABLE dbo.report_packages
(
    report_package_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_report_packages PRIMARY KEY,
    daily_report_generation_id bigint NOT NULL,
    package_type varchar(20) NOT NULL,
    package_path nvarchar(500) NOT NULL,
    manifest_json nvarchar(max) NOT NULL,
    package_sha256 char(64) NOT NULL,
    package_status varchar(20) NOT NULL,
    created_by nvarchar(200) NOT NULL,
    created_utc datetime2(3) NOT NULL CONSTRAINT DF_report_packages_created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_report_packages_generation FOREIGN KEY(daily_report_generation_id) REFERENCES dbo.daily_report_generations(daily_report_generation_id),
    CONSTRAINT UQ_report_packages_hash UNIQUE(package_sha256),
    CONSTRAINT CK_report_packages_manifest CHECK(ISJSON(manifest_json)=1),
    CONSTRAINT CK_report_packages_hash CHECK(package_sha256 NOT LIKE '%[^0-9a-f]%'),
    CONSTRAINT CK_report_packages_type CHECK(package_type IN('REPORT','CATEGORY','DAILY','COMBINED')),
    CONSTRAINT CK_report_packages_status CHECK(package_status IN('DRAFT','FINAL'))
);

CREATE TABLE dbo.share_attempts
(
    share_attempt_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_share_attempts PRIMARY KEY,
    daily_report_generation_id bigint NOT NULL,
    report_package_id bigint NULL,
    channel varchar(20) NOT NULL,
    destination_safe nvarchar(320) NULL,
    attachment_file_name nvarchar(260) NOT NULL,
    outcome varchar(20) NOT NULL,
    safe_message nvarchar(500) NOT NULL,
    initiated_by nvarchar(200) NOT NULL,
    initiated_utc datetime2(3) NOT NULL CONSTRAINT DF_share_attempts_created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_share_attempts_generation FOREIGN KEY(daily_report_generation_id) REFERENCES dbo.daily_report_generations(daily_report_generation_id),
    CONSTRAINT FK_share_attempts_package FOREIGN KEY(report_package_id) REFERENCES dbo.report_packages(report_package_id),
    CONSTRAINT CK_share_attempts_channel CHECK(channel IN('WHATSAPP','EMAIL','COPY_FILE','OPEN_FOLDER','ZIP_EXPORT')),
    CONSTRAINT CK_share_attempts_outcome CHECK(outcome IN('INITIATED','SUCCEEDED','FAILED','CANCELLED'))
);
CREATE INDEX IX_share_attempts_generation ON dbo.share_attempts(daily_report_generation_id,initiated_utc DESC);

CREATE TABLE dbo.accounting_mappings
(
    accounting_mapping_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_accounting_mappings PRIMARY KEY,
    business_event varchar(50) NOT NULL,
    store_code varchar(30) NULL,
    debit_ledger nvarchar(200) NOT NULL,
    credit_ledger nvarchar(200) NOT NULL,
    narration_template nvarchar(500) NOT NULL,
    cost_centre nvarchar(200) NULL,
    tax_attributes_json nvarchar(max) NULL,
    effective_from date NOT NULL,
    effective_to date NULL,
    version int NOT NULL,
    approval_request_id bigint NOT NULL,
    is_active bit NOT NULL CONSTRAINT DF_accounting_mappings_active DEFAULT(1),
    modified_by nvarchar(200) NOT NULL,
    modified_utc datetime2(3) NOT NULL CONSTRAINT DF_accounting_mappings_modified DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_accounting_mappings_approval FOREIGN KEY(approval_request_id) REFERENCES dbo.approval_requests(approval_request_id),
    CONSTRAINT CK_accounting_mappings_tax CHECK(tax_attributes_json IS NULL OR ISJSON(tax_attributes_json)=1),
    CONSTRAINT CK_accounting_mappings_period CHECK(effective_to IS NULL OR effective_to>=effective_from),
    CONSTRAINT CK_accounting_mappings_version CHECK(version>0)
);
CREATE UNIQUE INDEX UX_accounting_mappings_active ON dbo.accounting_mappings(business_event,store_code,effective_from,version);

CREATE TABLE dbo.accounting_batches
(
    accounting_batch_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_accounting_batches PRIMARY KEY,
    store_code varchar(30) NOT NULL,
    business_date date NOT NULL,
    daily_report_generation_id bigint NOT NULL,
    accounting_generation int NOT NULL,
    debit_total decimal(19,4) NOT NULL,
    credit_total decimal(19,4) NOT NULL,
    status varchar(20) NOT NULL,
    approved_by nvarchar(200) NULL,
    approved_utc datetime2(3) NULL,
    exported_utc datetime2(3) NULL,
    export_sha256 char(64) NULL,
    tally_reference nvarchar(200) NULL,
    created_by nvarchar(200) NOT NULL,
    created_utc datetime2(3) NOT NULL CONSTRAINT DF_accounting_batches_created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_accounting_batches_generation FOREIGN KEY(daily_report_generation_id) REFERENCES dbo.daily_report_generations(daily_report_generation_id),
    CONSTRAINT UQ_accounting_batches_generation UNIQUE(store_code,business_date,accounting_generation),
    CONSTRAINT CK_accounting_batches_generation CHECK(accounting_generation>0),
    CONSTRAINT CK_accounting_batches_status CHECK(status IN('DRAFT','REVIEW','APPROVED','EXPORTED','REJECTED')),
    CONSTRAINT CK_accounting_batches_export_hash CHECK(export_sha256 IS NULL OR export_sha256 NOT LIKE '%[^0-9a-f]%')
);

CREATE TABLE dbo.accounting_entries
(
    accounting_entry_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_accounting_entries PRIMARY KEY,
    accounting_batch_id bigint NOT NULL,
    line_number int NOT NULL,
    business_event varchar(50) NOT NULL,
    ledger_name nvarchar(200) NOT NULL,
    debit_amount decimal(19,4) NOT NULL CONSTRAINT DF_accounting_entries_debit DEFAULT(0),
    credit_amount decimal(19,4) NOT NULL CONSTRAINT DF_accounting_entries_credit DEFAULT(0),
    narration nvarchar(500) NOT NULL,
    cost_centre nvarchar(200) NULL,
    source_reference nvarchar(200) NOT NULL,
    CONSTRAINT FK_accounting_entries_batch FOREIGN KEY(accounting_batch_id) REFERENCES dbo.accounting_batches(accounting_batch_id),
    CONSTRAINT UQ_accounting_entries_line UNIQUE(accounting_batch_id,line_number),
    CONSTRAINT CK_accounting_entries_amount CHECK((debit_amount>0 AND credit_amount=0) OR (credit_amount>0 AND debit_amount=0))
);

EXEC(N'ALTER PROCEDURE dbo.persist_sales_line
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@line nvarchar(80),@product nvarchar(80),@type nvarchar(80)=NULL,@qty decimal(19,4),@gross decimal(19,4)=NULL,@net decimal(19,4)=NULL,@brandcode nvarchar(80)=NULL,@brandname nvarchar(200)=NULL,@segment nvarchar(100)=NULL,@currency char(3),@lineage bigint
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @invoice bigint,@existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
 SET @identity=CONCAT(@store,N''/'',@year,N''/'',@doc,N''/'',@line);
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@date,N''|'',@product,N''|'',ISNULL(@type,N''''),N''|'',@qty,N''|'',ISNULL(@gross,0),N''|'',ISNULL(@net,0),N''|'',ISNULL(@brandcode,N''''),N''|'',ISNULL(@brandname,N''''),N''|'',ISNULL(@segment,N''''),N''|'',@currency)),2));
 SELECT @invoice=sales_invoice_id FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND invoice_year=@year AND document_number=@doc;
 IF @invoice IS NULL BEGIN INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,@doc,@year,@date); SET @invoice=SCOPE_IDENTITY(); END
 ELSE IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice AND transaction_date<>@date)
 BEGIN
   SET @current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(N''invoice-date|'',(SELECT transaction_date FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice))),2));
   INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''The invoice identity already exists with a different date.'');
   INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R025'',@identity,@current,@incoming,N''Invoice date differs. Review and request a controlled restatement.''); RETURN;
 END
 SELECT @existing=sales_line_id,
   @current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@date,N''|'',product_code,N''|'',ISNULL(source_transaction_type,N''''),N''|'',source_quantity,N''|'',ISNULL(source_gross_amount,0),N''|'',ISNULL(source_net_amount,0),N''|'',ISNULL(source_brand_code,N''''),N''|'',ISNULL(source_brand_name,N''''),N''|'',ISNULL(brand_segment,N''''),N''|'',currency_code)),2))
 FROM dbo.sales_lines WHERE sales_invoice_id=@invoice AND line_identifier=@line;
 IF @existing IS NOT NULL
 BEGIN
   IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''ALREADY_PRESENT'',@incoming,N''Identical sales transaction already exists.'');
   ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''Sales transaction identity exists with different content.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R025'',@identity,@current,@incoming,N''One or more protected transaction values differ. Review and request a controlled restatement.''); END
   RETURN;
 END
 INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_brand_code,source_brand_name,brand_segment,currency_code,source_lineage_id) VALUES(@invoice,@line,@product,@type,@qty,@gross,@net,@brandcode,@brandname,@segment,@currency,@lineage);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''NEW'',@incoming,N''New sales transaction imported.'');
END');

EXEC(N'ALTER PROCEDURE dbo.persist_sales_invoice_control
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@type nvarchar(80)=NULL,@qty decimal(19,4),@net decimal(19,4),@currency char(3),@lineage bigint
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @invoice bigint,@existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage; SET @identity=CONCAT(@store,N''/'',@year,N''/'',@doc,N''/CONTROL'');
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@date,N''|'',ISNULL(@type,N''''),N''|'',@qty,N''|'',@net,N''|'',@currency)),2));
 SELECT @invoice=sales_invoice_id FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND invoice_year=@year AND document_number=@doc;
 IF @invoice IS NULL BEGIN INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,@doc,@year,@date); SET @invoice=SCOPE_IDENTITY(); END
 ELSE IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice AND transaction_date<>@date)
 BEGIN SET @current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(N''invoice-date|'',(SELECT transaction_date FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice))),2)); INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''The invoice identity already exists with a different date.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R022'',@identity,@current,@incoming,N''Invoice date differs. Review and request a controlled restatement.''); RETURN; END
 SELECT TOP(1) @existing=sales_invoice_control_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@date,N''|'',ISNULL(source_transaction_type,N''''),N''|'',source_invoice_quantity,N''|'',source_net_value,N''|'',currency_code)),2)) FROM dbo.sales_invoice_controls WHERE sales_invoice_id=@invoice ORDER BY sales_invoice_control_id;
 IF @existing IS NOT NULL BEGIN IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''ALREADY_PRESENT'',@incoming,N''Identical invoice control already exists.''); ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''Invoice control identity exists with different content.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R022'',@identity,@current,@incoming,N''Revenue control values differ. Review and request a controlled restatement.''); END RETURN; END
 INSERT dbo.sales_invoice_controls(sales_invoice_id,source_transaction_type,source_invoice_quantity,source_net_value,currency_code,source_lineage_id) VALUES(@invoice,@type,@qty,@net,@currency,@lineage);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''NEW'',@incoming,N''New invoice control imported.'');
END');

EXEC(N'ALTER PROCEDURE dbo.persist_sales_tender
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@type nvarchar(80),@amount decimal(19,4),@currency char(3),@lineage bigint,@eligible bit=1,@reason nvarchar(200)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @invoice bigint,@existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 IF UPPER(@type)=''PAYMENTTYPE25'' BEGIN SET @eligible=0; SET @reason=COALESCE(@reason,''UNRESOLVED_PAYMENTTYPE25''); END
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage; SET @identity=CONCAT(@store,N''/'',@year,N''/'',@doc,N''/TENDER/'',UPPER(@type));
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@date,N''|'',UPPER(@type),N''|'',@amount,N''|'',@currency,N''|'',@eligible,N''|'',ISNULL(@reason,N''''))),2));
 SELECT @invoice=sales_invoice_id FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND invoice_year=@year AND document_number=@doc;
 IF @invoice IS NULL BEGIN INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,@doc,@year,@date); SET @invoice=SCOPE_IDENTITY(); END
 ELSE IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice AND transaction_date<>@date)
 BEGIN SET @current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(N''invoice-date|'',(SELECT transaction_date FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice))),2)); INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''The invoice identity already exists with a different date.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R022'',@identity,@current,@incoming,N''Invoice date differs. Review and request a controlled restatement.''); RETURN; END
 SELECT TOP(1) @existing=sales_tender_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@date,N''|'',UPPER(tender_type),N''|'',source_amount,N''|'',currency_code,N''|'',is_reporting_eligible,N''|'',ISNULL(exclusion_reason,N''''))),2)) FROM dbo.sales_tenders WHERE sales_invoice_id=@invoice AND UPPER(tender_type)=UPPER(@type) ORDER BY sales_tender_id;
 IF @existing IS NOT NULL BEGIN IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''ALREADY_PRESENT'',@incoming,N''Identical tender transaction already exists.''); ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''Tender identity exists with different content.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R022'',@identity,@current,@incoming,N''Tender values differ. Review and request a controlled restatement.''); END RETURN; END
 INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id,is_reporting_eligible,exclusion_reason) VALUES(@invoice,@type,@amount,@currency,@lineage,@eligible,@reason);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''NEW'',@incoming,N''New tender transaction imported.'');
END');

EXEC(N'CREATE PROCEDURE dbo.persist_stock_movement
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@product nvarchar(80),@type nvarchar(80),@from nvarchar(80)=NULL,@to nvarchar(80)=NULL,@opening decimal(19,4),@transaction decimal(19,4),@closing decimal(19,4),@lineage bigint
AS
BEGIN
 SET NOCOUNT ON; DECLARE @existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage; SET @identity=CONCAT(@store,N''/'',@year,N''/'',@doc,N''/'',@date,N''/'',@product,N''/'',UPPER(@type),N''/'',ISNULL(@from,N''''),N''/'',ISNULL(@to,N''''));
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@opening,N''|'',@transaction,N''|'',@closing)),2));
 SELECT TOP(1) @existing=stock_movement_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(opening_quantity,N''|'',transaction_quantity,N''|'',closing_quantity)),2)) FROM dbo.stock_movements WHERE store_code=@store AND invoice_year=@year AND document_number=@doc AND document_date=@date AND product_code=@product AND source_transaction_type=@type AND ISNULL(from_location,N'''')=ISNULL(@from,N'''') AND ISNULL(to_location,N'''')=ISNULL(@to,N'''') ORDER BY stock_movement_id;
 IF @existing IS NOT NULL BEGIN IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''ALREADY_PRESENT'',@incoming,N''Identical stock movement already exists.''); ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''Stock movement identity exists with different content.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R003'',@identity,@current,@incoming,N''Stock movement values differ. Review and request a controlled restatement.''); END RETURN; END
 INSERT dbo.stock_movements(store_code,document_number,invoice_year,document_date,product_code,source_transaction_type,from_location,to_location,opening_quantity,transaction_quantity,closing_quantity,source_lineage_id) VALUES(@store,@doc,@year,@date,@product,@type,@from,@to,@opening,@transaction,@closing,@lineage);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''NEW'',@incoming,N''New stock movement imported.'');
END');

EXEC(N'CREATE PROCEDURE dbo.persist_stock_snapshot
 @store varchar(30),@date date,@product nvarchar(80),@ean nvarchar(80)=NULL,@brand nvarchar(80)=NULL,@brandname nvarchar(200)=NULL,@cluster nvarchar(100)=NULL,@gender nvarchar(50)=NULL,@batch nvarchar(80)=NULL,@uid nvarchar(100)=NULL,@qty decimal(19,4),@unit decimal(19,4)=NULL,@total decimal(19,4)=NULL,@lineage bigint
AS
BEGIN
 SET NOCOUNT ON; DECLARE @existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage; SET @identity=CONCAT(@store,N''/'',@date,N''/'',@product,N''/'',COALESCE(@uid,@batch,@ean,N''''));
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(ISNULL(@ean,N''''),N''|'',ISNULL(@brand,N''''),N''|'',ISNULL(@brandname,N''''),N''|'',ISNULL(@cluster,N''''),N''|'',ISNULL(@gender,N''''),N''|'',ISNULL(@batch,N''''),N''|'',ISNULL(@uid,N''''),N''|'',@qty,N''|'',ISNULL(@unit,0),N''|'',ISNULL(@total,0))),2));
 SELECT TOP(1) @existing=stock_snapshot_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(ISNULL(ean,N''''),N''|'',ISNULL(brand_code,N''''),N''|'',ISNULL(brand_name,N''''),N''|'',ISNULL(cluster,N''''),N''|'',ISNULL(gender,N''''),N''|'',ISNULL(batch_number,N''''),N''|'',ISNULL(source_uid,N''''),N''|'',quantity,N''|'',ISNULL(unit_cost,0),N''|'',ISNULL(total_cost,0))),2)) FROM dbo.stock_snapshots WHERE store_code=@store AND snapshot_date=@date AND product_code=@product AND COALESCE(source_uid,batch_number,ean,N'''')=COALESCE(@uid,@batch,@ean,N'''') ORDER BY stock_snapshot_id;
 IF @existing IS NOT NULL BEGIN IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''ALREADY_PRESENT'',@incoming,N''Identical stock snapshot row already exists.''); ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''Stock snapshot identity exists with different content.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R001'',@identity,@current,@incoming,N''Closing-stock values differ. Review and request a controlled restatement.''); END RETURN; END
 INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,ean,brand_code,brand_name,cluster,gender,batch_number,source_uid,quantity,unit_cost,total_cost,source_lineage_id) VALUES(@store,@date,@product,@ean,@brand,@brandname,@cluster,@gender,@batch,@uid,@qty,@unit,@total,@lineage);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''NEW'',@incoming,N''New stock snapshot row imported.'');
END');

ALTER TABLE dbo.operational_audit DROP CONSTRAINT CK_operational_audit_type;
ALTER TABLE dbo.operational_audit ADD CONSTRAINT CK_operational_audit_type CHECK
 (event_type IN ('ApplicationStart','SessionStart','ConnectionTest','ImportBatch','ImportFailed','ReportRun','ExportExcel','ExportPdf','DatabaseSetup','SupportPackage',
                 'ManualInput','DayFinalised','DayReopened','ReportPack','Backup','RestoreDrill','ConfigurationChange','MappingProfileChange','Restatement','StockCount','StaffTarget',
                 'UserAdministration','MasterDataChange','AutomationRun','ReportArchive','DocumentIntake','DocumentExtraction','RegisterEntry','ShareInitiated','ReportPackage',
                 'Approval','Adjustment','AccountingBatch','AccountingExport','ImportConflict','IssueWorkflow'));

EXEC(N'CREATE TRIGGER dbo.trg_register_entries_history
ON dbo.register_entries AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.register_entry_history(register_entry_id,old_values_json,new_values_json,changed_by,change_reason)
 SELECT i.register_entry_id,
   CASE WHEN d.register_entry_id IS NULL THEN NULL ELSE (SELECT d.register_type registerType,d.store_code storeCode,d.business_date businessDate,d.document_number documentNumber,d.document_date documentDate,d.counterparty,d.quantity,d.amount,d.reference,d.received_by receivedBy,d.verification_status verificationStatus,d.remarks FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) END,
   (SELECT i.register_type registerType,i.store_code storeCode,i.business_date businessDate,i.document_number documentNumber,i.document_date documentDate,i.counterparty,i.quantity,i.amount,i.reference,i.received_by receivedBy,i.verification_status verificationStatus,i.remarks FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
   i.modified_by,i.change_reason
 FROM inserted i LEFT JOIN deleted d ON d.register_entry_id=i.register_entry_id;
END');

EXEC(N'CREATE TRIGGER dbo.trg_product_evidence_immutable
ON dbo.report_packages AFTER UPDATE,DELETE AS
BEGIN
 THROW 51200,''Report packages are immutable. Create a new package instead.'',1;
END');
EXEC(N'CREATE TRIGGER dbo.trg_share_attempts_immutable
ON dbo.share_attempts AFTER UPDATE,DELETE AS
BEGIN
 THROW 51201,''Share-attempt history is append-only.'',1;
END');
EXEC(N'CREATE TRIGGER dbo.trg_accounting_entries_approved
ON dbo.accounting_entries AFTER INSERT,UPDATE,DELETE AS
BEGIN
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.accounting_batches b ON b.accounting_batch_id=i.accounting_batch_id WHERE b.status IN(''APPROVED'',''EXPORTED''))
    OR EXISTS(SELECT 1 FROM deleted d JOIN dbo.accounting_batches b ON b.accounting_batch_id=d.accounting_batch_id WHERE b.status IN(''APPROVED'',''EXPORTED''))
  THROW 51202,''Approved or exported accounting entries are immutable.'',1;
END');

IF EXISTS(SELECT 1 FROM sys.database_principals WHERE name=N'NT AUTHORITY\SYSTEM')
BEGIN
    DENY INSERT,UPDATE,DELETE ON dbo.product_settings TO [NT AUTHORITY\SYSTEM];
    DENY INSERT,UPDATE,DELETE ON dbo.sharing_contacts TO [NT AUTHORITY\SYSTEM];
    DENY INSERT,UPDATE,DELETE ON dbo.kpi_catalogue TO [NT AUTHORITY\SYSTEM];
    DENY INSERT,UPDATE,DELETE ON dbo.approval_requests TO [NT AUTHORITY\SYSTEM];
    DENY INSERT,UPDATE,DELETE ON dbo.controlled_adjustments TO [NT AUTHORITY\SYSTEM];
    DENY INSERT,UPDATE,DELETE ON dbo.accounting_mappings TO [NT AUTHORITY\SYSTEM];
    DENY INSERT,UPDATE,DELETE ON dbo.accounting_batches TO [NT AUTHORITY\SYSTEM];
    DENY INSERT,UPDATE,DELETE ON dbo.accounting_entries TO [NT AUTHORITY\SYSTEM];
    DENY UPDATE,DELETE ON dbo.source_documents TO [NT AUTHORITY\SYSTEM];
    DENY UPDATE,DELETE ON dbo.document_extractions TO [NT AUTHORITY\SYSTEM];
    DENY UPDATE,DELETE ON dbo.report_packages TO [NT AUTHORITY\SYSTEM];
    DENY UPDATE,DELETE ON dbo.share_attempts TO [NT AUTHORITY\SYSTEM];
    DENY UPDATE,DELETE ON dbo.import_row_outcomes TO [NT AUTHORITY\SYSTEM];
    DENY UPDATE,DELETE ON dbo.import_conflicts TO [NT AUTHORITY\SYSTEM];
END;

COMMIT TRANSACTION;
