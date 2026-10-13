-- 1.12 wave 1, lane D2 "collections engine" (12 Oct 2026). 112-WAVE-PLAN D-b, master plan Phase 8a tasks 1-4; decision 44 #7.
-- (The plan numbers this 0062; the 1.11.4 patch took 0061, so every 1.12 migration moved up by one.)
--
-- Collections masters, Owner-configured statement layouts and the bank / provider statement lines. Line-by-line matching,
-- obligations, cases and periods (8b-8d) are NOT here: they wait for the sample exports (D19) and the windows and approvers
-- (D20/D21). Nothing in this file guesses a bank or provider format; a new bank is a statement_layouts row, not code.
--
--  1. bank_accounts: the last four digits only (no column for a full account number exists anywhere); account_key is the only
--     identifier used in folder paths. takes_cash_deposits marks the account(s) the shop's cash goes to (rule R-DEP-1).
--  2. collection_providers: the D11 modes; seeds AIRPAY, PHONEPE, CARD_ACQUIRER (BANK), PAYTM (inactive), CREDIT_NOTE and
--     GIFT_CARD (NONE). statement_kind and granularity stay UNKNOWN until D19.
--  3. provider_merchant_ids: a settlement line reaches a store only through a confirmed merchant / terminal id, never a name.
--  4. collection_policies: shipped EMPTY. No window means nothing ever becomes overdue (D20 default).
--  5. tender_modes.provider_key and is_till_cash, with trg_tender_modes_provider_mode: a code's mode (Service prefix ignored)
--     must be its provider's mode. A row whose mode was changed on this PC from the seed keeps provider_key NULL here.
--     ROUND_OFF stays is_till_cash=0 until a fixture check shows otherwise (master plan 8a task 1).
--  6. statement_layouts: one Owner-made column map (JSON, keys validated by Etp.Reporting.Import.Collections.StatementLayout)
--     per bank account or provider, found by the header signature (ImportProfileMatcher.CreateHeaderSignature). At most one
--     active layout per bank account, so one statement can never be imported twice under two formats.
--  7. bank_statement_lines and provider_settlement_lines: SHA-256 line_identity (computed in C#, LineIdentity.cs), unique per
--     account / provider, so a re-import adds nothing and a superset adds only its new lines. Lineage through import_files
--     (store_code NULL, report_code BANK_STATEMENT / PROVIDER_STATEMENT) and source_lineage. No day-lock trigger: banking facts
--     are dated by the bank and arrive after the day is finalised. import_files' lock trigger joins on store_code, which is
--     NULL here, so it never fires for these files and their supersede passes; that trigger is not altered.
--  8. product_settings.collections_evidence_root: where the imported statement files are kept (by SHA-256).
--  9. collections_setup_history: every change to 1-4 and 6 (trigger, row JSON before and after, who, reason).
-- 10. Permissions. Store Manager and Viewer write none of these tables: the Owner (db_owner) edits the masters, and statement
--     files go in only through dbo.collections_import_statement (an object file, EXECUTE to Store Manager and Owner).
--     PII: a bank narration and a UPI payer name / VPA carry customer names, and raw_json carries every cell, so they are
--     DENY SELECT to Store Manager and Viewer. 0059's pattern scan does not match these names, hence explicit denies here and
--     CollectionsPermissionSqlTests' own scan.
--
-- THROW numbers: lane block 51950-51959 (this file: 51950, 51957).
-- 0001-0062 are not edited. The migration runner applies this entire script in one transaction, so it has no BEGIN or COMMIT
-- and no GO; statements that must start a batch, or that read a column added here to an existing table, run through EXEC.
-- Grants change: ExpectedRolePermissions.json gains the DENY rows below at the gate (regenerated there).
SET XACT_ABORT ON;

-- 1. Bank accounts.
CREATE TABLE dbo.bank_accounts(
 bank_account_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_bank_accounts PRIMARY KEY,
 account_key varchar(30) NOT NULL CONSTRAINT UQ_bank_accounts_key UNIQUE,
 display_name nvarchar(100) NOT NULL,
 bank_name nvarchar(100) NOT NULL,
 account_last4 char(4) NULL,
 store_code varchar(30) NULL CONSTRAINT FK_bank_accounts_store REFERENCES dbo.stores(store_code),
 currency_code char(3) NOT NULL CONSTRAINT DF_bank_accounts_currency DEFAULT('INR'),
 takes_cash_deposits bit NOT NULL CONSTRAINT DF_bank_accounts_cash DEFAULT(0),
 active bit NOT NULL CONSTRAINT DF_bank_accounts_active DEFAULT(1),
 modified_by nvarchar(100) NOT NULL CONSTRAINT DF_bank_accounts_by DEFAULT(ORIGINAL_LOGIN()),
 modified_utc datetime2(3) NOT NULL CONSTRAINT DF_bank_accounts_utc DEFAULT(SYSUTCDATETIME()),
 change_reason nvarchar(500) NOT NULL,
 CONSTRAINT CK_bank_accounts_key CHECK(LEN(account_key)>0 AND account_key COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_]%'),
 CONSTRAINT CK_bank_accounts_last4 CHECK(account_last4 IS NULL OR account_last4 COLLATE Latin1_General_100_BIN2 LIKE '[0-9][0-9][0-9][0-9]'),
 -- No run of five digits in a name, so a full account number cannot be typed into one.
 CONSTRAINT CK_bank_accounts_names CHECK(LEN(LTRIM(RTRIM(display_name)))>0 AND LEN(LTRIM(RTRIM(bank_name)))>0
   AND display_name NOT LIKE N'%[0-9][0-9][0-9][0-9][0-9]%' AND bank_name NOT LIKE N'%[0-9][0-9][0-9][0-9][0-9]%'),
 CONSTRAINT CK_bank_accounts_currency CHECK(currency_code COLLATE Latin1_General_100_BIN2 LIKE '[A-Z][A-Z][A-Z]'),
 CONSTRAINT CK_bank_accounts_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0));

-- 2. Providers (the D11 modes verbatim).
CREATE TABLE dbo.collection_providers(
 provider_key varchar(30) NOT NULL CONSTRAINT PK_collection_providers PRIMARY KEY,
 display_name nvarchar(100) NOT NULL,
 mode varchar(30) NOT NULL,
 settles_via varchar(10) NOT NULL,
 statement_kind varchar(25) NOT NULL CONSTRAINT DF_collection_providers_kind DEFAULT('UNKNOWN'),
 granularity varchar(20) NOT NULL CONSTRAINT DF_collection_providers_granularity DEFAULT('UNKNOWN'),
 bank_account_id int NULL CONSTRAINT FK_collection_providers_account REFERENCES dbo.bank_accounts(bank_account_id),
 rounding_tolerance decimal(19,4) NULL,
 active bit NOT NULL CONSTRAINT DF_collection_providers_active DEFAULT(1),
 modified_by nvarchar(100) NOT NULL CONSTRAINT DF_collection_providers_by DEFAULT(ORIGINAL_LOGIN()),
 modified_utc datetime2(3) NOT NULL CONSTRAINT DF_collection_providers_utc DEFAULT(SYSUTCDATETIME()),
 change_reason nvarchar(500) NOT NULL,
 CONSTRAINT CK_collection_providers_key CHECK(LEN(provider_key)>0 AND provider_key COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_]%'),
 CONSTRAINT CK_collection_providers_mode CHECK(mode IN('Cash','Card','UPI','CN','Gift Card','Bank','TC','Service Cash','Service Card','Service UPI')),
 CONSTRAINT CK_collection_providers_settles CHECK(settles_via IN('BANK','NONE')),
 CONSTRAINT CK_collection_providers_statement_kind CHECK(statement_kind IN('GROSS_WITH_DEDUCTIONS','NET','UNKNOWN')),
 CONSTRAINT CK_collection_providers_granularity CHECK(granularity IN('TRANSACTION','SETTLEMENT_SUMMARY','UNKNOWN')),
 CONSTRAINT CK_collection_providers_tolerance CHECK(rounding_tolerance IS NULL OR rounding_tolerance>=0),
 CONSTRAINT CK_collection_providers_name CHECK(LEN(LTRIM(RTRIM(display_name)))>0),
 CONSTRAINT CK_collection_providers_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0));

INSERT dbo.collection_providers(provider_key,display_name,mode,settles_via,active,change_reason) VALUES
 ('AIRPAY',N'Airpay','UPI','BANK',1,N'Seeded by 0063 (master plan 8a; D19 confirms the providers)'),
 ('PHONEPE',N'PhonePe','UPI','BANK',1,N'Seeded by 0063 (master plan 8a; D19 confirms the providers)'),
 ('CARD_ACQUIRER',N'Card acquirer','Card','BANK',1,N'Seeded by 0063 (master plan 8a; D19 confirms the providers)'),
 ('PAYTM',N'Paytm','UPI','BANK',0,N'Seeded by 0063 (master plan 8a; D19 confirms the providers)'),
 ('CREDIT_NOTE',N'Credit note','CN','NONE',1,N'Seeded by 0063 (master plan 8a; D19 confirms the providers)'),
 ('GIFT_CARD',N'Gift card','Gift Card','NONE',1,N'Seeded by 0063 (master plan 8a; D19 confirms the providers)');

-- 3. Merchant and terminal ids: the only way a settlement line reaches a store.
CREATE TABLE dbo.provider_merchant_ids(
 provider_merchant_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_provider_merchant_ids PRIMARY KEY,
 provider_key varchar(30) NOT NULL CONSTRAINT FK_provider_merchant_ids_provider REFERENCES dbo.collection_providers(provider_key),
 merchant_id nvarchar(50) NOT NULL,
 terminal_id nvarchar(50) NULL,
 store_code varchar(30) NOT NULL CONSTRAINT FK_provider_merchant_ids_store REFERENCES dbo.stores(store_code),
 active bit NOT NULL CONSTRAINT DF_provider_merchant_ids_active DEFAULT(1),
 modified_by nvarchar(100) NOT NULL CONSTRAINT DF_provider_merchant_ids_by DEFAULT(ORIGINAL_LOGIN()),
 modified_utc datetime2(3) NOT NULL CONSTRAINT DF_provider_merchant_ids_utc DEFAULT(SYSUTCDATETIME()),
 change_reason nvarchar(500) NOT NULL,
 CONSTRAINT UQ_provider_merchant_ids UNIQUE(provider_key,merchant_id,terminal_id),
 CONSTRAINT CK_provider_merchant_ids_values CHECK(LEN(LTRIM(RTRIM(merchant_id)))>0 AND (terminal_id IS NULL OR LEN(LTRIM(RTRIM(terminal_id)))>0)),
 CONSTRAINT CK_provider_merchant_ids_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0));

-- 4. Settlement rules: shipped empty (D20). No default window, threshold or tolerance exists in code either.
CREATE TABLE dbo.collection_policies(
 policy_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_collection_policies PRIMARY KEY,
 store_code varchar(30) NULL CONSTRAINT FK_collection_policies_store REFERENCES dbo.stores(store_code),
 mode varchar(30) NOT NULL,
 provider_key varchar(30) NULL CONSTRAINT FK_collection_policies_provider REFERENCES dbo.collection_providers(provider_key),
 settlement_window_days int NULL,
 overdue_after_days int NULL,
 cash_tolerance decimal(19,4) NULL,
 effective_from date NOT NULL,
 effective_to date NULL,
 modified_by nvarchar(100) NOT NULL CONSTRAINT DF_collection_policies_by DEFAULT(ORIGINAL_LOGIN()),
 modified_utc datetime2(3) NOT NULL CONSTRAINT DF_collection_policies_utc DEFAULT(SYSUTCDATETIME()),
 change_reason nvarchar(500) NOT NULL,
 CONSTRAINT CK_collection_policies_mode CHECK(mode IN('Cash','Card','UPI','CN','Gift Card','Bank','TC','Service Cash','Service Card','Service UPI')),
 CONSTRAINT CK_collection_policies_days CHECK((settlement_window_days IS NULL OR settlement_window_days BETWEEN 0 AND 90)
   AND (overdue_after_days IS NULL OR overdue_after_days BETWEEN 0 AND 365)),
 CONSTRAINT CK_collection_policies_tolerance CHECK(cash_tolerance IS NULL OR cash_tolerance>=0),
 CONSTRAINT CK_collection_policies_dates CHECK(effective_to IS NULL OR effective_to>=effective_from),
 CONSTRAINT CK_collection_policies_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0));

-- 5. Tender codes point at their provider.
ALTER TABLE dbo.tender_modes ADD
 provider_key varchar(30) NULL CONSTRAINT FK_tender_modes_provider REFERENCES dbo.collection_providers(provider_key),
 is_till_cash bit NOT NULL CONSTRAINT DF_tender_modes_till_cash DEFAULT(0);

-- Only where the code's mode is still the provider's mode (a mode the Owner changed keeps NULL; the D19 list names the rest).
EXEC(N'UPDATE t SET provider_key=x.provider_key
FROM dbo.tender_modes t
JOIN (VALUES(N''PAYMENTTYPE25'',''AIRPAY''),(N''PHONEPE'',''PHONEPE''),(N''PAYMENTTYPE20'',''PHONEPE''),(N''PAYTM'',''PAYTM''),
            (N''CARD'',''CARD_ACQUIRER''),(N''SERVICE_CARD'',''CARD_ACQUIRER''),(N''CREDITNOTE_REDEEM'',''CREDIT_NOTE''),
            (N''ISSUED_CREDITNOTE'',''CREDIT_NOTE''),(N''GIFTCARD'',''GIFT_CARD'')) x(source_tender_code,provider_key)
  ON x.source_tender_code=t.source_tender_code
JOIN dbo.collection_providers p ON p.provider_key=x.provider_key
WHERE CASE WHEN t.mode LIKE ''Service %'' THEN STUFF(t.mode,1,8,'''') ELSE t.mode END=p.mode;
UPDATE dbo.tender_modes SET is_till_cash=1 WHERE source_tender_code IN(N''CASH'',N''CASH_REFUND'') AND mode=''Cash'';');

EXEC(N'CREATE TRIGGER dbo.trg_tender_modes_provider_mode ON dbo.tender_modes AFTER INSERT,UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.collection_providers p ON p.provider_key=i.provider_key
           WHERE CASE WHEN i.mode LIKE ''Service %'' THEN STUFF(i.mode,1,8,'''') ELSE i.mode END<>p.mode)
  THROW 51950,''The provider settles a different payment mode.'',1;
END');

EXEC(N'CREATE TRIGGER dbo.trg_collection_providers_mode ON dbo.collection_providers AFTER UPDATE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM inserted i JOIN dbo.tender_modes t ON t.provider_key=i.provider_key
           WHERE CASE WHEN t.mode LIKE ''Service %'' THEN STUFF(t.mode,1,8,'''') ELSE t.mode END<>i.mode)
  THROW 51950,''The provider settles a different payment mode.'',1;
END');

-- 6. Statement layouts (Owner-configured column maps).
CREATE TABLE dbo.statement_layouts(
 layout_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_statement_layouts PRIMARY KEY,
 layout_key varchar(30) NOT NULL CONSTRAINT UQ_statement_layouts_key UNIQUE,
 display_name nvarchar(100) NOT NULL,
 layout_kind varchar(10) NOT NULL,
 bank_account_id int NULL CONSTRAINT FK_statement_layouts_account REFERENCES dbo.bank_accounts(bank_account_id),
 provider_key varchar(30) NULL CONSTRAINT FK_statement_layouts_provider REFERENCES dbo.collection_providers(provider_key),
 header_signature_sha256 char(64) NOT NULL,
 sheet_name nvarchar(100) NULL,
 header_row int NOT NULL CONSTRAINT DF_statement_layouts_header_row DEFAULT(1),
 skip_footer_rows int NOT NULL CONSTRAINT DF_statement_layouts_footer DEFAULT(0),
 mapping_json nvarchar(max) NOT NULL,
 version int NOT NULL CONSTRAINT DF_statement_layouts_version DEFAULT(1),
 active bit NOT NULL CONSTRAINT DF_statement_layouts_active DEFAULT(1),
 modified_by nvarchar(100) NOT NULL CONSTRAINT DF_statement_layouts_by DEFAULT(ORIGINAL_LOGIN()),
 modified_utc datetime2(3) NOT NULL CONSTRAINT DF_statement_layouts_utc DEFAULT(SYSUTCDATETIME()),
 change_reason nvarchar(500) NOT NULL,
 CONSTRAINT CK_statement_layouts_key CHECK(LEN(layout_key)>0 AND layout_key COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^A-Z0-9_]%'),
 CONSTRAINT CK_statement_layouts_kind CHECK((layout_kind='BANK' AND bank_account_id IS NOT NULL AND provider_key IS NULL)
   OR (layout_kind='PROVIDER' AND provider_key IS NOT NULL AND bank_account_id IS NULL)),
 CONSTRAINT CK_statement_layouts_signature CHECK(LEN(header_signature_sha256)=64 AND header_signature_sha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
 CONSTRAINT CK_statement_layouts_rows CHECK(header_row BETWEEN 1 AND 1000 AND skip_footer_rows BETWEEN 0 AND 1000),
 CONSTRAINT CK_statement_layouts_mapping CHECK(ISJSON(mapping_json)=1),
 CONSTRAINT CK_statement_layouts_version CHECK(version>=1),
 CONSTRAINT CK_statement_layouts_name CHECK(LEN(LTRIM(RTRIM(display_name)))>0),
 CONSTRAINT CK_statement_layouts_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0),
 CONSTRAINT UQ_statement_layouts_signature UNIQUE(layout_kind,header_signature_sha256,bank_account_id,provider_key));
CREATE UNIQUE INDEX UX_statement_layouts_one_active_per_account ON dbo.statement_layouts(bank_account_id)
 WHERE layout_kind='BANK' AND active=1;

-- 7a. Bank statement lines. Amounts are positive; the column says credit or debit.
CREATE TABLE dbo.bank_statement_lines(
 bank_line_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_bank_statement_lines PRIMARY KEY,
 bank_account_id int NOT NULL CONSTRAINT FK_bank_statement_lines_account REFERENCES dbo.bank_accounts(bank_account_id),
 layout_id int NOT NULL CONSTRAINT FK_bank_statement_lines_layout REFERENCES dbo.statement_layouts(layout_id),
 import_file_id bigint NOT NULL CONSTRAINT FK_bank_statement_lines_file REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL CONSTRAINT FK_bank_statement_lines_lineage REFERENCES dbo.source_lineage(source_lineage_id),
 value_date date NULL,
 posting_date date NULL,
 line_date AS COALESCE(value_date,posting_date) PERSISTED,
 narration nvarchar(500) NULL,
 bank_reference nvarchar(100) NULL,
 debit_amount decimal(19,4) NULL,
 credit_amount decimal(19,4) NULL,
 running_balance decimal(19,4) NULL,
 raw_json nvarchar(max) NOT NULL,
 line_identity char(64) NOT NULL,
 is_current bit NOT NULL CONSTRAINT DF_bank_statement_lines_current DEFAULT(1),
 classification varchar(25) NOT NULL CONSTRAINT DF_bank_statement_lines_class DEFAULT('UNCLASSIFIED'),
 classified_by nvarchar(100) NULL,
 classified_utc datetime2(3) NULL,
 classification_reason nvarchar(500) NULL,
 CONSTRAINT UQ_bank_statement_lines_lineage UNIQUE(source_lineage_id),
 CONSTRAINT UQ_bank_statement_lines_identity UNIQUE(bank_account_id,line_identity),
 CONSTRAINT CK_bank_statement_lines_date CHECK(value_date IS NOT NULL OR posting_date IS NOT NULL),
 CONSTRAINT CK_bank_statement_lines_amount CHECK((credit_amount IS NOT NULL AND credit_amount>0 AND debit_amount IS NULL)
   OR (debit_amount IS NOT NULL AND debit_amount>0 AND credit_amount IS NULL)),
 CONSTRAINT CK_bank_statement_lines_raw CHECK(ISJSON(raw_json)=1),
 CONSTRAINT CK_bank_statement_lines_identity CHECK(LEN(line_identity)=64 AND line_identity COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
 CONSTRAINT CK_bank_statement_lines_class CHECK(classification IN('UNCLASSIFIED','UPI_SETTLEMENT','CARD_SETTLEMENT','CASH_DEPOSIT',
   'CUSTOMER_TRANSFER','CHARGEBACK_DEBIT','BANK_CHARGE','REVERSAL','NON_SALES','INTERNAL_TRANSFER')),
 CONSTRAINT CK_bank_statement_lines_classified CHECK(classification='UNCLASSIFIED'
   OR (classified_by IS NOT NULL AND classified_utc IS NOT NULL AND classification_reason IS NOT NULL)));
CREATE INDEX IX_bank_statement_lines_date ON dbo.bank_statement_lines(bank_account_id,line_date)
 INCLUDE(credit_amount,debit_amount,is_current,classification,bank_reference);
CREATE INDEX IX_bank_statement_lines_file ON dbo.bank_statement_lines(import_file_id);

-- 7b. Provider settlement lines. Amounts are absolute; line_kind gives the direction (only ADJUSTMENT keeps the file's sign).
CREATE TABLE dbo.provider_settlement_lines(
 provider_line_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_provider_settlement_lines PRIMARY KEY,
 provider_key varchar(30) NOT NULL CONSTRAINT FK_provider_settlement_lines_provider REFERENCES dbo.collection_providers(provider_key),
 layout_id int NOT NULL CONSTRAINT FK_provider_settlement_lines_layout REFERENCES dbo.statement_layouts(layout_id),
 import_file_id bigint NOT NULL CONSTRAINT FK_provider_settlement_lines_file REFERENCES dbo.import_files(import_file_id),
 source_lineage_id bigint NOT NULL CONSTRAINT FK_provider_settlement_lines_lineage REFERENCES dbo.source_lineage(source_lineage_id),
 store_code varchar(30) NULL CONSTRAINT FK_provider_settlement_lines_store REFERENCES dbo.stores(store_code),
 merchant_id nvarchar(50) NULL,
 terminal_id nvarchar(50) NULL,
 provider_txn_reference nvarchar(100) NULL,
 invoice_reference nvarchar(80) NULL,
 approval_code nvarchar(30) NULL,
 txn_datetime datetime2(0) NULL,
 txn_business_date date NULL,
 settlement_date date NULL,
 settlement_id nvarchar(100) NULL,
 bank_reference nvarchar(100) NULL,
 provider_status nvarchar(30) NULL,
 is_failed bit NOT NULL CONSTRAINT DF_provider_settlement_lines_failed DEFAULT(0),
 line_kind varchar(15) NOT NULL CONSTRAINT DF_provider_settlement_lines_kind DEFAULT('UNKNOWN'),
 gross_amount decimal(19,4) NULL,
 fee_amount decimal(19,4) NULL,
 tax_on_fee_amount decimal(19,4) NULL,
 net_amount decimal(19,4) NULL,
 payer_name nvarchar(200) NULL,
 payer_vpa nvarchar(200) NULL,
 raw_json nvarchar(max) NOT NULL,
 line_identity char(64) NOT NULL,
 is_current bit NOT NULL CONSTRAINT DF_provider_settlement_lines_current DEFAULT(1),
 CONSTRAINT UQ_provider_settlement_lines_lineage UNIQUE(source_lineage_id),
 CONSTRAINT UQ_provider_settlement_lines_identity UNIQUE(provider_key,line_identity),
 CONSTRAINT CK_provider_settlement_lines_kind CHECK(line_kind IN('SALE','REFUND','CHARGEBACK','FEE','TAX_ON_FEE','HOLDBACK','RELEASE','ADJUSTMENT','UNKNOWN')),
 CONSTRAINT CK_provider_settlement_lines_amount CHECK(gross_amount IS NOT NULL OR net_amount IS NOT NULL),
 CONSTRAINT CK_provider_settlement_lines_signs CHECK(line_kind='ADJUSTMENT'
   OR ((gross_amount IS NULL OR gross_amount>=0) AND (net_amount IS NULL OR net_amount>=0))),
 CONSTRAINT CK_provider_settlement_lines_fees CHECK((fee_amount IS NULL OR fee_amount>=0) AND (tax_on_fee_amount IS NULL OR tax_on_fee_amount>=0)),
 CONSTRAINT CK_provider_settlement_lines_date CHECK(txn_business_date IS NOT NULL OR settlement_date IS NOT NULL),
 CONSTRAINT CK_provider_settlement_lines_raw CHECK(ISJSON(raw_json)=1),
 CONSTRAINT CK_provider_settlement_lines_identity CHECK(LEN(line_identity)=64 AND line_identity COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'));
CREATE INDEX IX_provider_settlement_lines_store_date ON dbo.provider_settlement_lines(store_code,txn_business_date)
 INCLUDE(provider_key,line_kind,is_failed,is_current,gross_amount,net_amount);
CREATE INDEX IX_provider_settlement_lines_file ON dbo.provider_settlement_lines(import_file_id);

-- 8. Where statement files are kept.
ALTER TABLE dbo.product_settings ADD collections_evidence_root nvarchar(400) NOT NULL
 CONSTRAINT DF_product_settings_collections_evidence_root DEFAULT(N'C:\ProgramData\EtpReporting\CollectionsEvidence');
EXEC(N'ALTER TABLE dbo.product_settings ADD CONSTRAINT CK_product_settings_collections_evidence_root
 CHECK(LEN(LTRIM(RTRIM(collections_evidence_root)))>0)');

-- 9. History of the masters and layouts (append-only).
CREATE TABLE dbo.collections_setup_history(
 history_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_collections_setup_history PRIMARY KEY,
 table_name varchar(40) NOT NULL,
 row_key nvarchar(100) NOT NULL,
 action char(1) NOT NULL,
 old_json nvarchar(max) NULL,
 new_json nvarchar(max) NULL,
 changed_by nvarchar(100) NOT NULL CONSTRAINT DF_collections_setup_history_by DEFAULT(ORIGINAL_LOGIN()),
 changed_utc datetime2(3) NOT NULL CONSTRAINT DF_collections_setup_history_utc DEFAULT(SYSUTCDATETIME()),
 change_reason nvarchar(500) NOT NULL,
 CONSTRAINT CK_collections_setup_history_action CHECK(action IN('I','U','D')),
 CONSTRAINT CK_collections_setup_history_json CHECK((old_json IS NULL OR ISJSON(old_json)=1) AND (new_json IS NULL OR ISJSON(new_json)=1)));
CREATE INDEX IX_collections_setup_history_row ON dbo.collections_setup_history(table_name,row_key,changed_utc DESC);

EXEC(N'CREATE TRIGGER dbo.trg_collections_setup_history_immutable ON dbo.collections_setup_history AFTER UPDATE,DELETE AS
BEGIN
 THROW 51957,''Collections setup history cannot be changed.'',1;
END');

EXEC(N'CREATE TRIGGER dbo.trg_bank_accounts_history ON dbo.bank_accounts AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.collections_setup_history(table_name,row_key,action,old_json,new_json,change_reason)
 SELECT ''bank_accounts'',CONVERT(nvarchar(100),k.id),
  CASE WHEN NOT EXISTS(SELECT 1 FROM deleted d WHERE d.bank_account_id=k.id) THEN ''I''
       WHEN NOT EXISTS(SELECT 1 FROM inserted i WHERE i.bank_account_id=k.id) THEN ''D'' ELSE ''U'' END,
  (SELECT * FROM deleted d WHERE d.bank_account_id=k.id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  (SELECT * FROM inserted i WHERE i.bank_account_id=k.id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  COALESCE((SELECT i.change_reason FROM inserted i WHERE i.bank_account_id=k.id),(SELECT d.change_reason FROM deleted d WHERE d.bank_account_id=k.id))
 FROM (SELECT bank_account_id id FROM inserted UNION SELECT bank_account_id FROM deleted) k;
END');

EXEC(N'CREATE TRIGGER dbo.trg_collection_providers_history ON dbo.collection_providers AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.collections_setup_history(table_name,row_key,action,old_json,new_json,change_reason)
 SELECT ''collection_providers'',CONVERT(nvarchar(100),k.id),
  CASE WHEN NOT EXISTS(SELECT 1 FROM deleted d WHERE d.provider_key=k.id) THEN ''I''
       WHEN NOT EXISTS(SELECT 1 FROM inserted i WHERE i.provider_key=k.id) THEN ''D'' ELSE ''U'' END,
  (SELECT * FROM deleted d WHERE d.provider_key=k.id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  (SELECT * FROM inserted i WHERE i.provider_key=k.id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  COALESCE((SELECT i.change_reason FROM inserted i WHERE i.provider_key=k.id),(SELECT d.change_reason FROM deleted d WHERE d.provider_key=k.id))
 FROM (SELECT provider_key id FROM inserted UNION SELECT provider_key FROM deleted) k;
END');

EXEC(N'CREATE TRIGGER dbo.trg_provider_merchant_ids_history ON dbo.provider_merchant_ids AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.collections_setup_history(table_name,row_key,action,old_json,new_json,change_reason)
 SELECT ''provider_merchant_ids'',CONVERT(nvarchar(100),k.id),
  CASE WHEN NOT EXISTS(SELECT 1 FROM deleted d WHERE d.provider_merchant_id=k.id) THEN ''I''
       WHEN NOT EXISTS(SELECT 1 FROM inserted i WHERE i.provider_merchant_id=k.id) THEN ''D'' ELSE ''U'' END,
  (SELECT * FROM deleted d WHERE d.provider_merchant_id=k.id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  (SELECT * FROM inserted i WHERE i.provider_merchant_id=k.id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  COALESCE((SELECT i.change_reason FROM inserted i WHERE i.provider_merchant_id=k.id),(SELECT d.change_reason FROM deleted d WHERE d.provider_merchant_id=k.id))
 FROM (SELECT provider_merchant_id id FROM inserted UNION SELECT provider_merchant_id FROM deleted) k;
END');

EXEC(N'CREATE TRIGGER dbo.trg_collection_policies_history ON dbo.collection_policies AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.collections_setup_history(table_name,row_key,action,old_json,new_json,change_reason)
 SELECT ''collection_policies'',CONVERT(nvarchar(100),k.id),
  CASE WHEN NOT EXISTS(SELECT 1 FROM deleted d WHERE d.policy_id=k.id) THEN ''I''
       WHEN NOT EXISTS(SELECT 1 FROM inserted i WHERE i.policy_id=k.id) THEN ''D'' ELSE ''U'' END,
  (SELECT * FROM deleted d WHERE d.policy_id=k.id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  (SELECT * FROM inserted i WHERE i.policy_id=k.id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  COALESCE((SELECT i.change_reason FROM inserted i WHERE i.policy_id=k.id),(SELECT d.change_reason FROM deleted d WHERE d.policy_id=k.id))
 FROM (SELECT policy_id id FROM inserted UNION SELECT policy_id FROM deleted) k;
END');

EXEC(N'CREATE TRIGGER dbo.trg_statement_layouts_history ON dbo.statement_layouts AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.collections_setup_history(table_name,row_key,action,old_json,new_json,change_reason)
 SELECT ''statement_layouts'',CONVERT(nvarchar(100),k.id),
  CASE WHEN NOT EXISTS(SELECT 1 FROM deleted d WHERE d.layout_id=k.id) THEN ''I''
       WHEN NOT EXISTS(SELECT 1 FROM inserted i WHERE i.layout_id=k.id) THEN ''D'' ELSE ''U'' END,
  (SELECT * FROM deleted d WHERE d.layout_id=k.id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  (SELECT * FROM inserted i WHERE i.layout_id=k.id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),
  COALESCE((SELECT i.change_reason FROM inserted i WHERE i.layout_id=k.id),(SELECT d.change_reason FROM deleted d WHERE d.layout_id=k.id))
 FROM (SELECT layout_id id FROM inserted UNION SELECT layout_id FROM deleted) k;
END');

-- The seed rows above were inserted before the history trigger existed; record them once so the history is complete.
EXEC(N'INSERT dbo.collections_setup_history(table_name,row_key,action,old_json,new_json,change_reason)
SELECT ''collection_providers'',CONVERT(nvarchar(100),p.provider_key),''I'',NULL,
 (SELECT * FROM dbo.collection_providers x WHERE x.provider_key=p.provider_key FOR JSON PATH,WITHOUT_ARRAY_WRAPPER),p.change_reason
FROM dbo.collection_providers p;');

-- 10. Permissions. Masters, layouts, lines and history: no direct writes by Store Manager or Viewer.
DENY INSERT,UPDATE,DELETE ON dbo.bank_accounts TO etp_store_manager,etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.collection_providers TO etp_store_manager,etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.provider_merchant_ids TO etp_store_manager,etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.collection_policies TO etp_store_manager,etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.statement_layouts TO etp_store_manager,etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.bank_statement_lines TO etp_store_manager,etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.provider_settlement_lines TO etp_store_manager,etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.collections_setup_history TO etp_store_manager,etp_viewer;

-- PII (customer names in UPI narrations and payer fields; raw_json holds every cell): Owner only.
DENY SELECT ON dbo.bank_statement_lines(narration,raw_json) TO etp_store_manager,etp_viewer;
DENY SELECT ON dbo.provider_settlement_lines(payer_name,payer_vpa,raw_json) TO etp_store_manager,etp_viewer;
-- The history keeps whole rows of the masters; mapping_json can carry sample header text. Owner only, like the setup screen's edits.
DENY SELECT ON dbo.collections_setup_history TO etp_viewer;
