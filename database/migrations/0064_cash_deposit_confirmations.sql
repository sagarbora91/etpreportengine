-- 1.12 wave 1, lane D2 "collections engine" (12 Oct 2026). 112-WAVE-PLAN D-b (the plan's 0063; renumbered +1 for 1.11.4's 0061).
--
-- Cash deposits have two states (closes PvB R4):
--   SENT     - the daily sheet's CASH_DEPOSIT value in dbo.manual_operational_inputs, read through the object view
--              dbo.v_cash_deposits_sent. There is no second place to type it.
--   CREDITED - a row here: the bank credited it. Either the Owner accepts an R-DEP-1 proposal (same amount, a credit line on a
--              cash-deposit account within 0-3 days; bank_line_id set, proposal_rule 'R-DEP-1') or the Owner records it by hand
--              (no bank line, e.g. before the bank's statement layout exists).
-- A confirmation is allowed on a LOCKED day: banking facts arrive after the day is finalised, so there is no day-lock trigger.
-- One active confirmation per store and deposit date, and a bank line backs at most one active confirmation. A mistake is
-- withdrawn (with a reason), never deleted or edited; every change is copied to cash_deposit_confirmation_history.
-- The plan's cash_movements table waits for the till roll-forward (8b).
--
-- Owner only: Store Manager and Viewer hold no INSERT, UPDATE or DELETE here (the Owner is db_owner).
-- THROW numbers: lane block 51950-51959 (this file: 51955, 51956, 51957).
-- 0001-0063 are not edited. One transaction, no BEGIN/COMMIT, no GO (the runner wraps the script); triggers through EXEC.
-- Grants change: ExpectedRolePermissions.json gains the DENY rows below at the gate (regenerated there).
SET XACT_ABORT ON;

CREATE TABLE dbo.cash_deposit_confirmations(
 cash_deposit_confirmation_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_cash_deposit_confirmations PRIMARY KEY,
 store_code varchar(30) NOT NULL CONSTRAINT FK_cash_deposit_confirmations_store REFERENCES dbo.stores(store_code),
 deposit_business_date date NOT NULL,
 credited_date date NOT NULL,
 bank_account_id int NOT NULL CONSTRAINT FK_cash_deposit_confirmations_account REFERENCES dbo.bank_accounts(bank_account_id),
 bank_line_id bigint NULL CONSTRAINT FK_cash_deposit_confirmations_line REFERENCES dbo.bank_statement_lines(bank_line_id),
 amount decimal(19,4) NOT NULL,
 bank_reference nvarchar(100) NULL,
 proposal_rule varchar(10) NULL,
 confirmed_by nvarchar(100) NOT NULL CONSTRAINT DF_cash_deposit_confirmations_by DEFAULT(ORIGINAL_LOGIN()),
 confirmed_utc datetime2(3) NOT NULL CONSTRAINT DF_cash_deposit_confirmations_utc DEFAULT(SYSUTCDATETIME()),
 change_reason nvarchar(500) NOT NULL,
 withdrawn_by nvarchar(100) NULL,
 withdrawn_utc datetime2(3) NULL,
 withdrawn_reason nvarchar(500) NULL,
 CONSTRAINT CK_cash_deposit_confirmations_amount CHECK(amount>0),
 CONSTRAINT CK_cash_deposit_confirmations_dates CHECK(credited_date>=deposit_business_date),
 CONSTRAINT CK_cash_deposit_confirmations_rule CHECK(proposal_rule IS NULL OR (proposal_rule='R-DEP-1' AND bank_line_id IS NOT NULL)),
 CONSTRAINT CK_cash_deposit_confirmations_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0),
 CONSTRAINT CK_cash_deposit_confirmations_withdrawn CHECK(
   (withdrawn_utc IS NULL AND withdrawn_by IS NULL AND withdrawn_reason IS NULL)
   OR (withdrawn_utc IS NOT NULL AND withdrawn_by IS NOT NULL AND withdrawn_reason IS NOT NULL AND LEN(LTRIM(RTRIM(withdrawn_reason)))>0)));
CREATE UNIQUE INDEX UX_cash_deposit_confirmations_active ON dbo.cash_deposit_confirmations(store_code,deposit_business_date)
 WHERE withdrawn_utc IS NULL;
CREATE UNIQUE INDEX UX_cash_deposit_confirmations_line ON dbo.cash_deposit_confirmations(bank_line_id)
 WHERE withdrawn_utc IS NULL AND bank_line_id IS NOT NULL;

CREATE TABLE dbo.cash_deposit_confirmation_history(
 history_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_cash_deposit_confirmation_history PRIMARY KEY,
 cash_deposit_confirmation_id bigint NOT NULL,
 store_code varchar(30) NOT NULL,
 deposit_business_date date NOT NULL,
 action varchar(10) NOT NULL,
 amount decimal(19,4) NOT NULL,
 credited_date date NOT NULL,
 bank_line_id bigint NULL,
 changed_by nvarchar(100) NOT NULL CONSTRAINT DF_cash_deposit_confirmation_history_by DEFAULT(ORIGINAL_LOGIN()),
 changed_utc datetime2(3) NOT NULL CONSTRAINT DF_cash_deposit_confirmation_history_utc DEFAULT(SYSUTCDATETIME()),
 change_reason nvarchar(500) NOT NULL,
 CONSTRAINT CK_cash_deposit_confirmation_history_action CHECK(action IN('CONFIRMED','WITHDRAWN')));
CREATE INDEX IX_cash_deposit_confirmation_history_scope ON dbo.cash_deposit_confirmation_history(store_code,deposit_business_date,changed_utc DESC);

-- Only one change is allowed after the insert: the withdrawal (NULL -> set), once. Nothing is deleted.
EXEC(N'CREATE TRIGGER dbo.trg_cash_deposit_confirmations_history ON dbo.cash_deposit_confirmations AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted) AND NOT EXISTS(SELECT 1 FROM inserted)
  THROW 51955,''A bank credit confirmation cannot be deleted. Withdraw it with a reason instead.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.cash_deposit_confirmation_id=i.cash_deposit_confirmation_id
   WHERE d.withdrawn_utc IS NOT NULL OR i.withdrawn_utc IS NULL
      OR i.store_code<>d.store_code OR i.deposit_business_date<>d.deposit_business_date OR i.credited_date<>d.credited_date
      OR i.bank_account_id<>d.bank_account_id OR ISNULL(i.bank_line_id,-1)<>ISNULL(d.bank_line_id,-1) OR i.amount<>d.amount
      OR ISNULL(i.bank_reference,N'''')<>ISNULL(d.bank_reference,N'''') OR ISNULL(i.proposal_rule,'''')<>ISNULL(d.proposal_rule,'''')
      OR i.confirmed_by<>d.confirmed_by OR i.confirmed_utc<>d.confirmed_utc OR i.change_reason<>d.change_reason)
  THROW 51956,''A bank credit confirmation can only be withdrawn, once.'',1;
 INSERT dbo.cash_deposit_confirmation_history(cash_deposit_confirmation_id,store_code,deposit_business_date,action,amount,credited_date,bank_line_id,changed_by,change_reason)
 SELECT i.cash_deposit_confirmation_id,i.store_code,i.deposit_business_date,
   CASE WHEN d.cash_deposit_confirmation_id IS NULL THEN ''CONFIRMED'' ELSE ''WITHDRAWN'' END,
   i.amount,i.credited_date,i.bank_line_id,
   CASE WHEN d.cash_deposit_confirmation_id IS NULL THEN i.confirmed_by ELSE i.withdrawn_by END,
   CASE WHEN d.cash_deposit_confirmation_id IS NULL THEN i.change_reason ELSE i.withdrawn_reason END
 FROM inserted i LEFT JOIN deleted d ON d.cash_deposit_confirmation_id=i.cash_deposit_confirmation_id;
END');

EXEC(N'CREATE TRIGGER dbo.trg_cash_deposit_confirmation_history_immutable ON dbo.cash_deposit_confirmation_history AFTER UPDATE,DELETE AS
BEGIN
 THROW 51957,''Bank credit history cannot be changed.'',1;
END');

DENY INSERT,UPDATE,DELETE ON dbo.cash_deposit_confirmations TO etp_store_manager,etp_viewer;
-- Review m-4 (decision 53): the Viewer reads deposit states through the views only.
DENY SELECT ON dbo.cash_deposit_confirmations TO etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.cash_deposit_confirmation_history TO etp_store_manager,etp_viewer;
