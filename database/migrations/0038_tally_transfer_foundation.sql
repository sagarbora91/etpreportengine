-- Phase 7 Tally transfer foundation (master plan Phase 7 tasks 1-4, schema only).
-- Additive: no existing row, status value, constraint or trigger is changed. The Phase 5 status list,
-- one-active-batch-per-store-day index and invoice reservations stay exactly as they are.
-- New tables are Owner-only, like the Phase 5 accounting tables. THROW numbers 51570-51579 (PHASE-7-REPORT.md).

CREATE TABLE dbo.tally_profiles(
 tally_profile_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_tally_profiles PRIMARY KEY,
 profile_code varchar(30) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT UQ_tally_profiles_code UNIQUE,
 company_name nvarchar(200) NOT NULL,
 environment varchar(10) NOT NULL,
 endpoint_url nvarchar(200) NULL,
 default_delivery_mode varchar(10) NOT NULL CONSTRAINT DF_tally_profiles_delivery DEFAULT('FILE'),
 payload_format varchar(10) NOT NULL CONSTRAINT DF_tally_profiles_format DEFAULT('XML'),
 voucher_granularity varchar(20) NOT NULL,
 party_policy varchar(20) NOT NULL,
 single_party_ledger nvarchar(200) NULL,
 tender_model varchar(20) NOT NULL,
 posting_model varchar(20) NOT NULL,
 voucher_view varchar(30) NOT NULL,
 posting_from_date date NULL,
 posting_to_date date NULL,
 tally_build_label nvarchar(100) NULL,
 company_identity_json nvarchar(max) NULL,
 is_enabled bit NOT NULL CONSTRAINT DF_tally_profiles_enabled DEFAULT(1),
 production_enabled_utc datetime2(3) NULL,
 production_enabled_by nvarchar(100) NULL,
 modified_by nvarchar(100) NOT NULL CONSTRAINT DF_tally_profiles_modified_by DEFAULT(ORIGINAL_LOGIN()),
 modified_utc datetime2(3) NOT NULL CONSTRAINT DF_tally_profiles_modified_utc DEFAULT(SYSUTCDATETIME()),
 change_reason nvarchar(500) NOT NULL,
 CONSTRAINT UQ_tally_profiles_environment UNIQUE(tally_profile_id,environment),
 CONSTRAINT CK_tally_profiles_code CHECK(LEN(profile_code) BETWEEN 1 AND 30 AND profile_code NOT LIKE '%[^A-Z0-9_-]%' AND LEFT(profile_code,1) LIKE '[A-Z0-9]'),
 CONSTRAINT CK_tally_profiles_company CHECK(LEN(LTRIM(RTRIM(company_name)))>0),
 CONSTRAINT CK_tally_profiles_environment CHECK(environment IN('TEST','PRODUCTION')),
 -- Only this PC's Tally: http://127.0.0.1:<port>/ or http://localhost:<port>/, port of one to five digits.
 CONSTRAINT CK_tally_profiles_endpoint CHECK(endpoint_url IS NULL OR
  ((LEFT(endpoint_url,17)='http://127.0.0.1:' OR LEFT(endpoint_url,17)='http://localhost:')
   AND RIGHT(endpoint_url,1)='/' AND LEN(endpoint_url) BETWEEN 19 AND 23
   AND CASE WHEN LEN(endpoint_url) BETWEEN 19 AND 23 THEN SUBSTRING(endpoint_url,18,LEN(endpoint_url)-18) ELSE 'x' END NOT LIKE '%[^0-9]%')),
 CONSTRAINT CK_tally_profiles_delivery CHECK(default_delivery_mode IN('FILE','HTTP')),
 CONSTRAINT CK_tally_profiles_format CHECK(payload_format IN('XML','JSON')),
 CONSTRAINT CK_tally_profiles_granularity CHECK(voucher_granularity IN('PER_INVOICE','DAILY_SUMMARY')),
 CONSTRAINT CK_tally_profiles_party CHECK(party_policy IN('NAMED_LEDGERS','SINGLE_LEDGER')),
 CONSTRAINT CK_tally_profiles_tender CHECK(tender_model IN('IN_VOUCHER','CLEARING_LEDGER')),
 CONSTRAINT CK_tally_profiles_posting CHECK(posting_model IN('ACCOUNTING_ONLY','INVENTORY')),
 CONSTRAINT CK_tally_profiles_view CHECK(voucher_view IN('ACCOUNTING','INVOICE')),
 CONSTRAINT CK_tally_profiles_party_tender CHECK(NOT(party_policy='NAMED_LEDGERS' AND tender_model='IN_VOUCHER')),
 CONSTRAINT CK_tally_profiles_single_party CHECK(party_policy<>'SINGLE_LEDGER' OR (single_party_ledger IS NOT NULL AND LEN(LTRIM(RTRIM(single_party_ledger)))>0)),
 CONSTRAINT CK_tally_profiles_window CHECK(posting_from_date IS NULL OR posting_to_date IS NULL OR posting_from_date<=posting_to_date),
 CONSTRAINT CK_tally_profiles_identity CHECK(company_identity_json IS NULL OR ISJSON(company_identity_json)=1),
 CONSTRAINT CK_tally_profiles_production CHECK(production_enabled_utc IS NULL OR production_enabled_by IS NOT NULL),
 CONSTRAINT CK_tally_profiles_production_environment CHECK(production_enabled_utc IS NULL OR environment='PRODUCTION'),
 CONSTRAINT CK_tally_profiles_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0));

-- D12: a store is bound to at most one TEST and one PRODUCTION company.
CREATE TABLE dbo.tally_profile_stores(
 tally_profile_id int NOT NULL,
 environment varchar(10) NOT NULL,
 store_code varchar(30) NOT NULL CONSTRAINT FK_tally_profile_stores_store REFERENCES dbo.stores(store_code),
 CONSTRAINT PK_tally_profile_stores PRIMARY KEY(tally_profile_id,store_code),
 CONSTRAINT FK_tally_profile_stores_profile FOREIGN KEY(tally_profile_id,environment) REFERENCES dbo.tally_profiles(tally_profile_id,environment),
 CONSTRAINT UQ_tally_profile_stores_store UNIQUE(store_code,environment));

ALTER TABLE dbo.accounting_batches ADD
 tally_profile_id int NULL CONSTRAINT FK_accounting_batches_tally_profile REFERENCES dbo.tally_profiles(tally_profile_id),
 batch_kind varchar(20) NOT NULL CONSTRAINT DF_accounting_batches_kind DEFAULT('DAY_JOURNAL'),
 selection_json nvarchar(max) NULL,
 selection_version int NOT NULL CONSTRAINT DF_accounting_batches_selection_version DEFAULT(1),
 mapping_version_set_json nvarchar(max) NULL,
 manifest_sha256 char(64) COLLATE Latin1_General_100_BIN2 NULL;

ALTER TABLE dbo.product_settings ADD tally_evidence_root nvarchar(400) NOT NULL
 CONSTRAINT DF_product_settings_tally_evidence_root DEFAULT(N'C:\ProgramData\EtpReporting\TallyEvidence');

-- Later statements reference the new columns, so they compile in their own batches.
EXEC(N'ALTER TABLE dbo.accounting_batches ADD
 CONSTRAINT CK_accounting_batches_kind CHECK(batch_kind IN(''DAY_JOURNAL'',''SALES_VOUCHERS'')),
 CONSTRAINT CK_accounting_batches_kind_profile CHECK(batch_kind=''DAY_JOURNAL'' OR tally_profile_id IS NOT NULL),
 CONSTRAINT CK_accounting_batches_selection CHECK(selection_json IS NULL OR ISJSON(selection_json)=1),
 CONSTRAINT CK_accounting_batches_selection_version CHECK(selection_version>0),
 CONSTRAINT CK_accounting_batches_mapping_versions CHECK(mapping_version_set_json IS NULL OR ISJSON(mapping_version_set_json)=1),
 CONSTRAINT CK_accounting_batches_manifest CHECK(manifest_sha256 IS NULL OR (LEN(manifest_sha256)=64 AND manifest_sha256 NOT LIKE ''%[^0-9a-f]%''));
ALTER TABLE dbo.product_settings ADD CONSTRAINT CK_product_settings_tally_evidence_root CHECK(LEN(LTRIM(RTRIM(tally_evidence_root)))>0);

CREATE TABLE dbo.accounting_vouchers(
 accounting_voucher_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_accounting_vouchers PRIMARY KEY,
 accounting_batch_id bigint NOT NULL CONSTRAINT FK_accounting_vouchers_batch REFERENCES dbo.accounting_batches(accounting_batch_id),
 voucher_sequence int NOT NULL,
 component_role varchar(20) NOT NULL,
 voucher_type nvarchar(50) NOT NULL,
 store_code varchar(30) NOT NULL,
 invoice_year int NOT NULL,
 document_number nvarchar(80) NOT NULL,
 revision int NOT NULL CONSTRAINT DF_accounting_vouchers_revision DEFAULT(1),
 sales_invoice_id bigint NULL CONSTRAINT FK_accounting_vouchers_invoice REFERENCES dbo.sales_invoices(sales_invoice_id),
 voucher_date date NOT NULL,
 expected_total decimal(19,4) NOT NULL,
 correspondence_key nvarchar(200) NOT NULL,
 source_sha256 char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 plan_sha256 char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 voucher_status varchar(30) NOT NULL CONSTRAINT DF_accounting_vouchers_status DEFAULT(''PLANNED''),
 blocked_reason nvarchar(1000) NULL,
 CONSTRAINT UQ_accounting_vouchers_sequence UNIQUE(accounting_batch_id,voucher_sequence),
 CONSTRAINT UQ_accounting_vouchers_batch UNIQUE(accounting_voucher_id,accounting_batch_id),
 CONSTRAINT CK_accounting_vouchers_sequence CHECK(voucher_sequence>0),
 CONSTRAINT CK_accounting_vouchers_role CHECK(component_role IN(''SALES'',''CREDIT_NOTE'',''RECEIPT'')),
 CONSTRAINT CK_accounting_vouchers_revision CHECK(revision>0),
 CONSTRAINT CK_accounting_vouchers_year CHECK(invoice_year BETWEEN 1000 AND 9999),
 CONSTRAINT CK_accounting_vouchers_key CHECK(correspondence_key=CONCAT(''ETP:'',store_code,'':'',invoice_year,'':'',document_number,'':'',component_role,'':'',revision)),
 -- KEY_UNSAFE numbers may be stored only as blocked or excluded vouchers, never sent.
 CONSTRAINT CK_accounting_vouchers_key_safe CHECK(voucher_status IN(''BLOCKED'',''EXCLUDED'')
  OR (document_number NOT LIKE ''%[:|]%'' AND document_number NOT LIKE ''%[ ''+CHAR(9)+CHAR(10)+CHAR(13)+'']%'')),
 CONSTRAINT CK_accounting_vouchers_source_hash CHECK(LEN(source_sha256)=64 AND source_sha256 NOT LIKE ''%[^0-9a-f]%''),
 CONSTRAINT CK_accounting_vouchers_plan_hash CHECK(LEN(plan_sha256)=64 AND plan_sha256 NOT LIKE ''%[^0-9a-f]%''),
 CONSTRAINT CK_accounting_vouchers_status CHECK(voucher_status IN(''PLANNED'',''BLOCKED'',''EXCLUDED'',''EXPORTED'',''SUBMITTED'',''OUTCOME_UNKNOWN'',''ACTUAL_LOCATED'',''RECONCILED'',''RECONCILED_WITH_WARNINGS'',''DIFFERENCE'',''CANCELLED'')),
 CONSTRAINT CK_accounting_vouchers_blocked CHECK(voucher_status<>''BLOCKED'' OR (blocked_reason IS NOT NULL AND LEN(LTRIM(RTRIM(blocked_reason)))>0)));

ALTER TABLE dbo.accounting_entries ADD
 accounting_voucher_id bigint NULL,
 tax_rate decimal(5,2) NULL,
 quantity decimal(19,3) NULL,
 stock_item nvarchar(200) NULL;');

-- An entry that belongs to a voucher must belong to the same batch as that voucher.
EXEC(N'ALTER TABLE dbo.accounting_entries ADD CONSTRAINT FK_accounting_entries_voucher
 FOREIGN KEY(accounting_voucher_id,accounting_batch_id) REFERENCES dbo.accounting_vouchers(accounting_voucher_id,accounting_batch_id);
CREATE INDEX IX_accounting_entries_voucher ON dbo.accounting_entries(accounting_voucher_id) WHERE accounting_voucher_id IS NOT NULL;

CREATE TABLE dbo.accounting_voucher_reservations(
 reservation_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_accounting_voucher_reservations PRIMARY KEY,
 tally_profile_id int NOT NULL CONSTRAINT FK_accounting_voucher_reservations_profile REFERENCES dbo.tally_profiles(tally_profile_id),
 store_code varchar(30) NOT NULL,
 invoice_year int NOT NULL,
 document_number nvarchar(80) NOT NULL,
 component_role varchar(20) NOT NULL,
 revision int NOT NULL CONSTRAINT DF_accounting_voucher_reservations_revision DEFAULT(1),
 accounting_voucher_id bigint NOT NULL CONSTRAINT FK_accounting_voucher_reservations_voucher REFERENCES dbo.accounting_vouchers(accounting_voucher_id),
 reserved_by nvarchar(100) NOT NULL CONSTRAINT DF_accounting_voucher_reservations_by DEFAULT(ORIGINAL_LOGIN()),
 reserved_utc datetime2(3) NOT NULL CONSTRAINT DF_accounting_voucher_reservations_utc DEFAULT(SYSUTCDATETIME()),
 released_utc datetime2(3) NULL,
 release_reason nvarchar(500) NULL,
 CONSTRAINT CK_accounting_voucher_reservations_role CHECK(component_role IN(''SALES'',''CREDIT_NOTE'',''RECEIPT'')),
 CONSTRAINT CK_accounting_voucher_reservations_revision CHECK(revision>0),
 CONSTRAINT CK_accounting_voucher_reservations_release CHECK((released_utc IS NULL AND release_reason IS NULL)
  OR (released_utc IS NOT NULL AND release_reason IS NOT NULL AND LEN(LTRIM(RTRIM(release_reason)))>0)));
CREATE UNIQUE INDEX UX_accounting_voucher_reservations_active ON dbo.accounting_voucher_reservations
 (tally_profile_id,store_code,invoice_year,document_number,component_role,revision) WHERE released_utc IS NULL;

CREATE TABLE dbo.tally_artifacts(
 tally_artifact_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_tally_artifacts PRIMARY KEY,
 accounting_batch_id bigint NOT NULL CONSTRAINT FK_tally_artifacts_batch REFERENCES dbo.accounting_batches(accounting_batch_id),
 artifact_kind varchar(20) NOT NULL,
 relative_path nvarchar(400) NOT NULL,
 sha256 char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 byte_length bigint NOT NULL,
 created_by nvarchar(100) NOT NULL CONSTRAINT DF_tally_artifacts_by DEFAULT(ORIGINAL_LOGIN()),
 created_utc datetime2(3) NOT NULL CONSTRAINT DF_tally_artifacts_utc DEFAULT(SYSUTCDATETIME()),
 CONSTRAINT UQ_tally_artifacts_path UNIQUE(accounting_batch_id,relative_path),
 CONSTRAINT UQ_tally_artifacts_batch UNIQUE(tally_artifact_id,accounting_batch_id),
 CONSTRAINT CK_tally_artifacts_kind CHECK(artifact_kind IN(''SOURCE_SNAPSHOT'',''EXPECTED_POSTINGS'',''VALIDATION'',''PAYLOAD_XML'',''HTTP_REQUEST'',''HTTP_RESPONSE'',''READBACK_XML'',''RECONCILIATION'',''RECOVERY_PLAN'',''APPROVAL'',''MANIFEST'')),
 CONSTRAINT CK_tally_artifacts_path CHECK(LEN(relative_path)>0 AND relative_path NOT LIKE ''%..%'' AND relative_path NOT LIKE ''%:%'' AND LEFT(relative_path,1) NOT IN(''\'',''/'')),
 CONSTRAINT CK_tally_artifacts_hash CHECK(LEN(sha256)=64 AND sha256 NOT LIKE ''%[^0-9a-f]%''),
 CONSTRAINT CK_tally_artifacts_length CHECK(byte_length>0));
CREATE INDEX IX_tally_artifacts_hash ON dbo.tally_artifacts(sha256);

CREATE TABLE dbo.tally_attempts(
 tally_attempt_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_tally_attempts PRIMARY KEY,
 accounting_batch_id bigint NOT NULL CONSTRAINT FK_tally_attempts_batch REFERENCES dbo.accounting_batches(accounting_batch_id),
 tally_profile_id int NOT NULL CONSTRAINT FK_tally_attempts_profile REFERENCES dbo.tally_profiles(tally_profile_id),
 payload_artifact_id bigint NOT NULL,
 delivery_mode varchar(10) NOT NULL,
 payload_format varchar(10) NOT NULL,
 attempt_status varchar(30) NOT NULL,
 http_status int NULL,
 response_artifact_id bigint NULL,
 tally_diagnostics_json nvarchar(max) NULL,
 started_utc datetime2(3) NOT NULL CONSTRAINT DF_tally_attempts_started_utc DEFAULT(SYSUTCDATETIME()),
 completed_utc datetime2(3) NULL,
 started_by nvarchar(100) NOT NULL CONSTRAINT DF_tally_attempts_started_by DEFAULT(ORIGINAL_LOGIN()),
 CONSTRAINT FK_tally_attempts_payload FOREIGN KEY(payload_artifact_id,accounting_batch_id) REFERENCES dbo.tally_artifacts(tally_artifact_id,accounting_batch_id),
 CONSTRAINT FK_tally_attempts_response FOREIGN KEY(response_artifact_id,accounting_batch_id) REFERENCES dbo.tally_artifacts(tally_artifact_id,accounting_batch_id),
 CONSTRAINT CK_tally_attempts_delivery CHECK(delivery_mode IN(''FILE'',''HTTP'')),
 CONSTRAINT CK_tally_attempts_format CHECK(payload_format IN(''XML'',''JSON'')),
 CONSTRAINT CK_tally_attempts_status CHECK(attempt_status IN(''RECORDED'',''FILE_WRITTEN'',''SENT'',''RESPONDED'',''TIMED_OUT'',''TRANSPORT_FAILED'',''CANCELLED'')),
 CONSTRAINT CK_tally_attempts_diagnostics CHECK(tally_diagnostics_json IS NULL OR ISJSON(tally_diagnostics_json)=1));

CREATE TABLE dbo.accounting_status_history(
 history_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_accounting_status_history PRIMARY KEY,
 subject_type varchar(10) NOT NULL,
 subject_id bigint NOT NULL,
 from_status varchar(40) NULL,
 to_status varchar(40) NOT NULL,
 actor nvarchar(100) NOT NULL CONSTRAINT DF_accounting_status_history_actor DEFAULT(ORIGINAL_LOGIN()),
 reason nvarchar(1000) NULL,
 evidence_artifact_id bigint NULL CONSTRAINT FK_accounting_status_history_evidence REFERENCES dbo.tally_artifacts(tally_artifact_id),
 changed_utc datetime2(3) NOT NULL CONSTRAINT DF_accounting_status_history_utc DEFAULT(SYSUTCDATETIME()),
 CONSTRAINT CK_accounting_status_history_subject CHECK(subject_type IN(''BATCH'',''VOUCHER'')));
CREATE INDEX IX_accounting_status_history_subject ON dbo.accounting_status_history(subject_type,subject_id,history_id);

-- Every existing batch gets one starting row so its history is never silently empty.
INSERT dbo.accounting_status_history(subject_type,subject_id,from_status,to_status,actor,reason)
SELECT ''BATCH'',accounting_batch_id,NULL,status,N''database update 0038'',N''Status when status history began; earlier changes are in the operational audit.''
FROM dbo.accounting_batches;');

-- Append-only tables (E-IMMUTABLE = 51573).
EXEC(N'CREATE TRIGGER dbo.trg_accounting_status_history_immutable ON dbo.accounting_status_history AFTER UPDATE,DELETE AS
BEGIN THROW 51573,''Accounting status history cannot be changed or deleted.'',1; END');

EXEC(N'CREATE TRIGGER dbo.trg_tally_artifacts_immutable ON dbo.tally_artifacts AFTER UPDATE,DELETE AS
BEGIN THROW 51573,''Tally evidence records cannot be changed or deleted.'',1; END');

-- An attempt may only advance its outcome fields, and only from RECORDED or SENT.
EXEC(N'CREATE TRIGGER dbo.trg_tally_attempts_guard ON dbo.tally_attempts AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM inserted)
 BEGIN
  IF EXISTS(SELECT 1 FROM deleted) THROW 51573,''Tally attempts cannot be deleted.'',1;
  RETURN;
 END;
 IF NOT EXISTS(SELECT 1 FROM deleted)
 BEGIN
  IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.accounting_batches b ON b.accounting_batch_id=i.accounting_batch_id
   WHERE b.tally_profile_id IS NULL OR b.tally_profile_id<>i.tally_profile_id)
   THROW 51579,''A Tally attempt must use the Tally company of its batch.'',1;
  RETURN;
 END;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.tally_attempt_id=d.tally_attempt_id
  WHERE d.attempt_status NOT IN(''RECORDED'',''SENT'')
   OR i.accounting_batch_id<>d.accounting_batch_id OR i.tally_profile_id<>d.tally_profile_id
   OR i.payload_artifact_id<>d.payload_artifact_id OR i.delivery_mode<>d.delivery_mode
   OR i.payload_format<>d.payload_format OR i.started_utc<>d.started_utc OR i.started_by<>d.started_by
   OR NOT((d.attempt_status=''RECORDED'' AND i.attempt_status IN(''RECORDED'',''FILE_WRITTEN'',''SENT'',''TRANSPORT_FAILED'',''CANCELLED''))
       OR (d.attempt_status=''SENT'' AND i.attempt_status IN(''SENT'',''RESPONDED'',''TIMED_OUT'',''TRANSPORT_FAILED''))))
  THROW 51573,''A finished Tally attempt cannot be changed, and an open one can only record its outcome.'',1;
END');

-- A reservation is created only for a PLANNED voucher of a Tally batch, never deleted,
-- and released at most once.
EXEC(N'CREATE TRIGGER dbo.trg_accounting_voucher_reservations_guard ON dbo.accounting_voucher_reservations AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM inserted)
 BEGIN
  IF EXISTS(SELECT 1 FROM deleted) THROW 51573,''Tally invoice reservations cannot be deleted.'',1;
  RETURN;
 END;
 IF EXISTS(SELECT 1 FROM deleted)
 BEGIN
  IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.reservation_id=d.reservation_id
   WHERE d.released_utc IS NOT NULL OR i.released_utc IS NULL
    OR i.tally_profile_id<>d.tally_profile_id OR i.store_code<>d.store_code OR i.invoice_year<>d.invoice_year
    OR i.document_number<>d.document_number OR i.component_role<>d.component_role OR i.revision<>d.revision
    OR i.accounting_voucher_id<>d.accounting_voucher_id OR i.reserved_by<>d.reserved_by OR i.reserved_utc<>d.reserved_utc)
   THROW 51579,''A Tally invoice reservation can only be released, and only once.'',1;
  RETURN;
 END;
 IF EXISTS(SELECT 1 FROM inserted r
  LEFT JOIN dbo.accounting_vouchers v ON v.accounting_voucher_id=r.accounting_voucher_id
  LEFT JOIN dbo.accounting_batches b ON b.accounting_batch_id=v.accounting_batch_id
  WHERE v.accounting_voucher_id IS NULL OR v.voucher_status<>''PLANNED'' OR r.released_utc IS NOT NULL
   OR b.tally_profile_id IS NULL OR b.tally_profile_id<>r.tally_profile_id
   OR r.store_code<>v.store_code OR r.invoice_year<>v.invoice_year OR r.document_number<>v.document_number
   OR r.component_role<>v.component_role OR r.revision<>v.revision)
  THROW 51579,''A Tally invoice reservation must match a planned voucher of a Tally batch.'',1;
END');

-- Vouchers of a decided batch are immutable, except their reconciliation status and reason.
EXEC(N'CREATE TRIGGER dbo.trg_accounting_vouchers_approved ON dbo.accounting_vouchers AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted) AND EXISTS(SELECT 1 FROM deleted)
  AND NOT(UPDATE(accounting_batch_id) OR UPDATE(voucher_sequence) OR UPDATE(component_role) OR UPDATE(voucher_type)
   OR UPDATE(store_code) OR UPDATE(invoice_year) OR UPDATE(document_number) OR UPDATE(revision) OR UPDATE(sales_invoice_id)
   OR UPDATE(voucher_date) OR UPDATE(expected_total) OR UPDATE(correspondence_key) OR UPDATE(source_sha256) OR UPDATE(plan_sha256))
  RETURN;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.accounting_batches b ON b.accounting_batch_id=i.accounting_batch_id WHERE b.status NOT IN(''DRAFT'',''BLOCKED''))
 OR EXISTS(SELECT 1 FROM deleted d JOIN dbo.accounting_batches b ON b.accounting_batch_id=d.accounting_batch_id WHERE b.status NOT IN(''DRAFT'',''BLOCKED''))
  THROW 51212,''Decided accounting vouchers are immutable.'',1;
END');

-- One history row per status change, written by the database so no code path can skip it.
-- A caller may pass a reason through SESSION_CONTEXT(N'etp.status_reason'); approval and rejection
-- reuse the reason the batch already records.
EXEC(N'CREATE TRIGGER dbo.trg_accounting_batches_status_history ON dbo.accounting_batches AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.accounting_status_history(subject_type,subject_id,from_status,to_status,reason)
 SELECT ''BATCH'',i.accounting_batch_id,d.status,i.status,
  CASE WHEN i.status=''REJECTED'' THEN i.rejection_reason
       WHEN i.status=''APPROVED_READY'' THEN i.approval_reason
       ELSE CONVERT(nvarchar(1000),SESSION_CONTEXT(N''etp.status_reason'')) END
 FROM inserted i LEFT JOIN deleted d ON d.accounting_batch_id=i.accounting_batch_id
 WHERE d.accounting_batch_id IS NULL OR d.status<>i.status;

 -- Rule 1 of plan task 2: a rejected batch that never reached Tally frees its invoices.
 UPDATE r SET released_utc=SYSUTCDATETIME(),release_reason=N''Batch rejected before anything was sent to Tally.''
 FROM dbo.accounting_voucher_reservations r
 JOIN dbo.accounting_vouchers v ON v.accounting_voucher_id=r.accounting_voucher_id
 JOIN inserted i ON i.accounting_batch_id=v.accounting_batch_id
 JOIN deleted d ON d.accounting_batch_id=i.accounting_batch_id
 WHERE i.status=''REJECTED'' AND d.status<>''REJECTED'' AND r.released_utc IS NULL
  AND NOT EXISTS(SELECT 1 FROM dbo.tally_attempts a WHERE a.accounting_batch_id=i.accounting_batch_id);
END');

EXEC(N'CREATE TRIGGER dbo.trg_accounting_vouchers_status_history ON dbo.accounting_vouchers AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.accounting_status_history(subject_type,subject_id,from_status,to_status,reason)
 SELECT ''VOUCHER'',i.accounting_voucher_id,d.voucher_status,i.voucher_status,
  COALESCE(CASE WHEN i.voucher_status=''BLOCKED'' THEN i.blocked_reason END,CONVERT(nvarchar(1000),SESSION_CONTEXT(N''etp.status_reason'')))
 FROM inserted i LEFT JOIN deleted d ON d.accounting_voucher_id=i.accounting_voucher_id
 WHERE d.accounting_voucher_id IS NULL OR d.voucher_status<>i.voucher_status;

 -- Rule 2 of plan task 2: a voucher blocked or excluded before any attempt frees its invoice.
 UPDATE r SET released_utc=SYSUTCDATETIME(),
  release_reason=LEFT(COALESCE(NULLIF(LTRIM(RTRIM(i.blocked_reason)),N''''),CASE WHEN i.voucher_status=''EXCLUDED'' THEN N''Voucher excluded before anything was sent to Tally.'' ELSE N''Voucher blocked before anything was sent to Tally.'' END),500)
 FROM dbo.accounting_voucher_reservations r
 JOIN inserted i ON i.accounting_voucher_id=r.accounting_voucher_id
 JOIN deleted d ON d.accounting_voucher_id=i.accounting_voucher_id
 WHERE i.voucher_status IN(''BLOCKED'',''EXCLUDED'') AND d.voucher_status NOT IN(''BLOCKED'',''EXCLUDED'') AND r.released_utc IS NULL
  AND NOT EXISTS(SELECT 1 FROM dbo.tally_attempts a WHERE a.accounting_batch_id=i.accounting_batch_id);
END');

-- Owner-only, like the Phase 5 accounting tables: Store Managers and Viewers inherit SELECT on dbo
-- (0022), so every new table is denied to them until a later task grants exactly what it needs.
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.tally_profiles TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.tally_profile_stores TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.accounting_vouchers TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.accounting_voucher_reservations TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.accounting_status_history TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.tally_artifacts TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.tally_attempts TO etp_store_manager,etp_viewer;
