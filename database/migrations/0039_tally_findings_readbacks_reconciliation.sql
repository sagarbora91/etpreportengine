-- Phase 7 records for validation findings, read-backs and reconciliation runs (master plan tasks 7, 9 and 10;
-- appendix S7-5 to S7-7). Additive and Owner-only, like 0038. E-IMMUTABLE = 51573.
-- tally_reconciliation_tolerances (S7-7) waits for its approval type; until then the engine uses no tolerance.

CREATE TABLE dbo.accounting_validation_findings(
 finding_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_accounting_validation_findings PRIMARY KEY,
 accounting_batch_id bigint NOT NULL CONSTRAINT FK_accounting_validation_findings_batch REFERENCES dbo.accounting_batches(accounting_batch_id),
 accounting_voucher_id bigint NULL,
 rule_id varchar(30) NOT NULL,
 rule_version int NOT NULL,
 severity varchar(5) NOT NULL,
 subject nvarchar(200) NULL,
 observed nvarchar(500) NULL,
 expected nvarchar(500) NULL,
 explanation nvarchar(1000) NOT NULL,
 corrective_action nvarchar(500) NULL,
 waived_by nvarchar(100) NULL,
 waived_utc datetime2(3) NULL,
 waiver_reason nvarchar(1000) NULL,
 created_utc datetime2(3) NOT NULL CONSTRAINT DF_accounting_validation_findings_utc DEFAULT(SYSUTCDATETIME()),
 CONSTRAINT FK_accounting_validation_findings_voucher FOREIGN KEY(accounting_voucher_id,accounting_batch_id) REFERENCES dbo.accounting_vouchers(accounting_voucher_id,accounting_batch_id),
 CONSTRAINT CK_accounting_validation_findings_severity CHECK(severity IN('PASS','WARN','FAIL')),
 CONSTRAINT CK_accounting_validation_findings_rule CHECK(LEN(rule_id)>0 AND rule_version>0),
 CONSTRAINT CK_accounting_validation_findings_explanation CHECK(LEN(LTRIM(RTRIM(explanation)))>0),
 -- Only a warning can be accepted, and only with who, when and why.
 CONSTRAINT CK_accounting_validation_findings_waiver CHECK((waived_by IS NULL AND waived_utc IS NULL AND waiver_reason IS NULL)
  OR (severity='WARN' AND waived_by IS NOT NULL AND waived_utc IS NOT NULL AND waiver_reason IS NOT NULL AND LEN(LTRIM(RTRIM(waiver_reason)))>0)));
CREATE INDEX IX_accounting_validation_findings_batch ON dbo.accounting_validation_findings(accounting_batch_id,finding_id);

CREATE TABLE dbo.tally_readbacks(
 tally_readback_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_tally_readbacks PRIMARY KEY,
 tally_profile_id int NOT NULL CONSTRAINT FK_tally_readbacks_profile REFERENCES dbo.tally_profiles(tally_profile_id),
 accounting_batch_id bigint NULL CONSTRAINT FK_tally_readbacks_batch REFERENCES dbo.accounting_batches(accounting_batch_id),
 source varchar(12) NOT NULL,
 company_name_reported nvarchar(200) NULL,
 from_date date NOT NULL,
 to_date date NOT NULL,
 voucher_count int NOT NULL,
 is_complete bit NOT NULL,
 incomplete_reason varchar(30) NULL,
 request_artifact_id bigint NULL CONSTRAINT FK_tally_readbacks_request REFERENCES dbo.tally_artifacts(tally_artifact_id),
 response_artifact_id bigint NOT NULL CONSTRAINT FK_tally_readbacks_response REFERENCES dbo.tally_artifacts(tally_artifact_id),
 adapter_version varchar(20) NOT NULL,
 acquired_utc datetime2(3) NOT NULL CONSTRAINT DF_tally_readbacks_utc DEFAULT(SYSUTCDATETIME()),
 acquired_by nvarchar(100) NOT NULL CONSTRAINT DF_tally_readbacks_by DEFAULT(ORIGINAL_LOGIN()),
 CONSTRAINT CK_tally_readbacks_source CHECK(source IN('HTTP','MANUAL_FILE')),
 CONSTRAINT CK_tally_readbacks_dates CHECK(from_date<=to_date),
 CONSTRAINT CK_tally_readbacks_count CHECK(voucher_count>=0),
 CONSTRAINT CK_tally_readbacks_reason CHECK(incomplete_reason IN('WRONG_COMPANY','COMPANY_NOT_REPORTED','COMPANY_NOT_OPEN','TALLY_ERROR','TRUNCATED','NOT_XML')),
 CONSTRAINT CK_tally_readbacks_complete CHECK((is_complete=1 AND incomplete_reason IS NULL) OR (is_complete=0 AND incomplete_reason IS NOT NULL)));

EXEC(N'CREATE TABLE dbo.tally_actual_vouchers(
 tally_actual_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_tally_actual_vouchers PRIMARY KEY,
 tally_readback_id bigint NOT NULL CONSTRAINT FK_tally_actual_vouchers_readback REFERENCES dbo.tally_readbacks(tally_readback_id),
 voucher_index int NOT NULL,
 voucher_type nvarchar(50) NULL,
 voucher_date date NULL,
 voucher_number nvarchar(100) NULL,
 reference nvarchar(200) NULL,
 narration nvarchar(1000) NULL,
 correspondence_key nvarchar(200) NULL,
 tally_guid nvarchar(100) NULL,
 tally_master_id bigint NULL,
 tally_alter_id bigint NULL,
 is_cancelled bit NULL,
 is_optional bit NULL,
 total_amount decimal(19,4) NULL,
 fragment_sha256 char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 CONSTRAINT UQ_tally_actual_vouchers_index UNIQUE(tally_readback_id,voucher_index),
 CONSTRAINT CK_tally_actual_vouchers_hash CHECK(LEN(fragment_sha256)=64 AND fragment_sha256 NOT LIKE ''%[^0-9a-f]%''));
CREATE INDEX IX_tally_actual_vouchers_key ON dbo.tally_actual_vouchers(tally_readback_id,correspondence_key);

CREATE TABLE dbo.tally_actual_ledger_entries(
 tally_actual_entry_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_tally_actual_ledger_entries PRIMARY KEY,
 tally_actual_id bigint NOT NULL CONSTRAINT FK_tally_actual_ledger_entries_voucher REFERENCES dbo.tally_actual_vouchers(tally_actual_id),
 line_number int NOT NULL,
 ledger_name nvarchar(200) NOT NULL,
 amount decimal(19,4) NULL,
 is_deemed_positive bit NULL,
 quantity_text nvarchar(50) NULL,
 rate_text nvarchar(50) NULL,
 quantity decimal(19,3) NULL,
 unit nvarchar(20) NULL,
 stock_item nvarchar(200) NULL,
 CONSTRAINT UQ_tally_actual_ledger_entries_line UNIQUE(tally_actual_id,line_number));

CREATE TABLE dbo.tally_reconciliation_runs(
 run_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_tally_reconciliation_runs PRIMARY KEY,
 accounting_batch_id bigint NOT NULL CONSTRAINT FK_tally_reconciliation_runs_batch REFERENCES dbo.accounting_batches(accounting_batch_id),
 tally_readback_id bigint NOT NULL CONSTRAINT FK_tally_reconciliation_runs_readback REFERENCES dbo.tally_readbacks(tally_readback_id),
 rule_set_version varchar(20) NOT NULL,
 outcome varchar(40) NOT NULL,
 summary_json nvarchar(max) NOT NULL,
 evidence_artifact_id bigint NOT NULL,
 run_by nvarchar(100) NOT NULL CONSTRAINT DF_tally_reconciliation_runs_by DEFAULT(ORIGINAL_LOGIN()),
 run_utc datetime2(3) NOT NULL CONSTRAINT DF_tally_reconciliation_runs_utc DEFAULT(SYSUTCDATETIME()),
 CONSTRAINT FK_tally_reconciliation_runs_evidence FOREIGN KEY(evidence_artifact_id,accounting_batch_id) REFERENCES dbo.tally_artifacts(tally_artifact_id,accounting_batch_id),
 CONSTRAINT CK_tally_reconciliation_runs_outcome CHECK(outcome IN(''RECONCILED'',''RECONCILED_WITH_ACCEPTED_WARNINGS'',''FAILED_RECONCILIATION'',''RECONCILIATION_INCOMPLETE'')),
 CONSTRAINT CK_tally_reconciliation_runs_summary CHECK(ISJSON(summary_json)=1));

CREATE TABLE dbo.tally_reconciliation_differences(
 difference_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_tally_reconciliation_differences PRIMARY KEY,
 run_id bigint NOT NULL CONSTRAINT FK_tally_reconciliation_differences_run REFERENCES dbo.tally_reconciliation_runs(run_id),
 accounting_voucher_id bigint NULL CONSTRAINT FK_tally_reconciliation_differences_voucher REFERENCES dbo.accounting_vouchers(accounting_voucher_id),
 tally_actual_id bigint NULL CONSTRAINT FK_tally_reconciliation_differences_actual REFERENCES dbo.tally_actual_vouchers(tally_actual_id),
 check_level varchar(20) NOT NULL,
 difference_type varchar(30) NOT NULL,
 severity varchar(5) NOT NULL,
 value_a nvarchar(500) NULL,
 value_b nvarchar(500) NULL,
 value_c nvarchar(500) NULL,
 delta decimal(19,4) NULL,
 rule_id varchar(40) NOT NULL,
 rule_version int NOT NULL,
 match_rationale nvarchar(500) NOT NULL,
 evidence_reference nvarchar(400) NOT NULL,
 required_action nvarchar(500) NOT NULL,
 accepted_by nvarchar(100) NULL,
 accepted_utc datetime2(3) NULL,
 accepted_reason nvarchar(1000) NULL,
 CONSTRAINT CK_tally_reconciliation_differences_level CHECK(check_level IN(''COVERAGE'',''HEADER'',''ACCOUNTING'',''TAX'',''TENDER'',''INVENTORY'',''AGGREGATE'',''INTEGRITY'')),
 CONSTRAINT CK_tally_reconciliation_differences_type CHECK(difference_type IN(''MISSING'',''EXTRA'',''DUPLICATE'',''WRONG_COMPANY'',''AMOUNT_MISMATCH'',''TAX_MISMATCH'',''LEDGER_MISMATCH'',''QUANTITY_MISMATCH'',''STATUS_MISMATCH'',''SOURCE_CHANGED'',''ACTUAL_CHANGED'',''AMBIGUOUS_MATCH'',''NOT_VERIFIABLE'')),
 CONSTRAINT CK_tally_reconciliation_differences_severity CHECK(severity IN(''WARN'',''FAIL'')),
 CONSTRAINT CK_tally_reconciliation_differences_acceptance CHECK((accepted_by IS NULL AND accepted_utc IS NULL AND accepted_reason IS NULL)
  OR (severity=''WARN'' AND accepted_by IS NOT NULL AND accepted_utc IS NOT NULL AND accepted_reason IS NOT NULL AND LEN(LTRIM(RTRIM(accepted_reason)))>0)));
CREATE INDEX IX_tally_reconciliation_differences_run ON dbo.tally_reconciliation_differences(run_id,difference_id);');

-- Append-only records.
EXEC(N'CREATE TRIGGER dbo.trg_tally_readbacks_immutable ON dbo.tally_readbacks AFTER UPDATE,DELETE AS
BEGIN THROW 51573,''Tally read-backs cannot be changed or deleted.'',1; END');
EXEC(N'CREATE TRIGGER dbo.trg_tally_actual_vouchers_immutable ON dbo.tally_actual_vouchers AFTER UPDATE,DELETE AS
BEGIN THROW 51573,''Vouchers read back from Tally cannot be changed or deleted.'',1; END');
EXEC(N'CREATE TRIGGER dbo.trg_tally_actual_ledger_entries_immutable ON dbo.tally_actual_ledger_entries AFTER UPDATE,DELETE AS
BEGIN THROW 51573,''Ledger lines read back from Tally cannot be changed or deleted.'',1; END');
EXEC(N'CREATE TRIGGER dbo.trg_tally_reconciliation_runs_immutable ON dbo.tally_reconciliation_runs AFTER UPDATE,DELETE AS
BEGIN THROW 51573,''Reconciliation runs cannot be changed or deleted.'',1; END');

-- A finding or difference is never deleted; its acceptance columns may be filled once and nothing else changes.
EXEC(N'CREATE TRIGGER dbo.trg_accounting_validation_findings_guard ON dbo.accounting_validation_findings AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM inserted) THROW 51573,''Validation findings cannot be deleted.'',1;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.finding_id=d.finding_id
  WHERE d.waived_by IS NOT NULL OR i.waived_by IS NULL
   OR i.accounting_batch_id<>d.accounting_batch_id OR ISNULL(i.accounting_voucher_id,-1)<>ISNULL(d.accounting_voucher_id,-1)
   OR i.rule_id<>d.rule_id OR i.rule_version<>d.rule_version OR i.severity<>d.severity
   OR ISNULL(i.subject,N'''')<>ISNULL(d.subject,N'''') OR ISNULL(i.observed,N'''')<>ISNULL(d.observed,N'''') OR ISNULL(i.expected,N'''')<>ISNULL(d.expected,N'''')
   OR i.explanation<>d.explanation OR ISNULL(i.corrective_action,N'''')<>ISNULL(d.corrective_action,N'''') OR i.created_utc<>d.created_utc)
  THROW 51573,''A validation finding can only be accepted, and only once.'',1;
END');

EXEC(N'CREATE TRIGGER dbo.trg_tally_reconciliation_differences_guard ON dbo.tally_reconciliation_differences AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM inserted) THROW 51573,''Reconciliation differences cannot be deleted.'',1;
 IF EXISTS(SELECT 1 FROM deleted d JOIN inserted i ON i.difference_id=d.difference_id
  WHERE d.accepted_by IS NOT NULL OR i.accepted_by IS NULL
   OR i.run_id<>d.run_id OR ISNULL(i.accounting_voucher_id,-1)<>ISNULL(d.accounting_voucher_id,-1) OR ISNULL(i.tally_actual_id,-1)<>ISNULL(d.tally_actual_id,-1)
   OR i.check_level<>d.check_level OR i.difference_type<>d.difference_type OR i.severity<>d.severity
   OR ISNULL(i.value_a,N'''')<>ISNULL(d.value_a,N'''') OR ISNULL(i.value_b,N'''')<>ISNULL(d.value_b,N'''') OR ISNULL(i.value_c,N'''')<>ISNULL(d.value_c,N'''')
   OR ISNULL(i.delta,0)<>ISNULL(d.delta,0) OR (i.delta IS NULL AND d.delta IS NOT NULL) OR (i.delta IS NOT NULL AND d.delta IS NULL)
   OR i.rule_id<>d.rule_id OR i.rule_version<>d.rule_version OR i.match_rationale<>d.match_rationale
   OR i.evidence_reference<>d.evidence_reference OR i.required_action<>d.required_action)
  THROW 51573,''A reconciliation difference can only be accepted, and only once.'',1;
END');

DENY SELECT,INSERT,UPDATE,DELETE ON dbo.accounting_validation_findings TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.tally_readbacks TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.tally_actual_vouchers TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.tally_actual_ledger_entries TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.tally_reconciliation_runs TO etp_store_manager,etp_viewer;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.tally_reconciliation_differences TO etp_store_manager,etp_viewer;
