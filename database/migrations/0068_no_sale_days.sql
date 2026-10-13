-- 1.12 wave 2, lane N1 "Mark as no-sale day" (13 Oct 2026). Decision 52; decision 55 (Q1 option a): a store-day marked as a
-- no-sale day no longer asks for the shop's daily exports or the daily entries (walk-ins, opening cash, expenses, deposit);
-- the month's targets are still asked for, and the day is finalised and locked like any other.
--
-- dbo.no_sale_days        one row per store and business date. A mark is withdrawn (with a reason), never deleted; a withdrawn
--                         row can be marked again (the row is reused). Every change is copied to no_sale_day_history.
-- dbo.no_sale_day_history append-only.
-- The lock protects the mark like every other fact: on a LOCKED day the row cannot be written (reopen first).
-- The procedures dbo.mark_no_sale_day / dbo.withdraw_no_sale_day and the view dbo.v_no_sale_days_current are object files
-- (database/objects), never created here.
--
-- CK_operational_audit_type is rebuilt WITH CHECK: every type 0062 allowed, plus NoSaleDayMarked and NoSaleDayWithdrawn.
--
-- Permissions: the Owner is db_owner. Store Manager and Viewer write nothing directly (the procedures write through the
-- ownership chain); the free-text reason is Owner / Store Manager only (column DENY for the Viewer, also on the view).
-- Grants change: ExpectedRolePermissions.json gains the DENY rows below and the object files' rows (regenerated at the gate).
-- THROW numbers: lane N1 block 51980-51989 (this file: 51983, 51989).
-- 0001-0067 are not edited. One transaction, no BEGIN/COMMIT, no GO (the runner wraps the script); triggers through EXEC.
SET XACT_ABORT ON;

-- >>> PRECHECK0068 begin
-- Nested IF, not AND: SQL Server does not short-circuit AND.
IF OBJECT_ID(N'dbo.operational_audit',N'U') IS NOT NULL
 IF EXISTS(SELECT 1 FROM dbo.operational_audit WHERE event_type NOT IN
 ('ApplicationStart','SessionStart','ConnectionTest','ImportBatch','ImportFailed','ReportRun','ExportExcel','ExportPdf','DatabaseSetup','SupportPackage',
  'ManualInput','DayFinalised','DayReopened','ReportPack','Backup','RestoreDrill','ConfigurationChange','MappingProfileChange','Restatement','StockCount','StaffTarget',
  'UserAdministration','MasterDataChange','AutomationRun','ReportArchive','DocumentIntake','DocumentExtraction','DocumentExtractionReview','SharingContactChange',
  'RegisterEntry','ShareInitiated','ReportPackage','Approval','Adjustment','AccountingBatch','AccountingExport','ImportConflict','IssueWorkflow','VisualRender',
  'CollectionsSetup','CollectionsImport','CashDepositConfirmed','SoftwareUpdate','ShopResultPublished','TallyReadback','ImportFixAction',
  'NoSaleDayMarked','NoSaleDayWithdrawn'))
 THROW 51989,'Migration 0068 refused: dbo.operational_audit holds an event type that is not in the 1.12 list. Nothing was changed.',1;
-- <<< PRECHECK0068 end

CREATE TABLE dbo.no_sale_days(
 store_code varchar(30) NOT NULL CONSTRAINT FK_no_sale_days_store REFERENCES dbo.stores(store_code),
 business_date date NOT NULL,
 reason_code varchar(30) NOT NULL,
 reason nvarchar(500) NOT NULL,
 marked_by nvarchar(100) NOT NULL CONSTRAINT DF_no_sale_days_marked_by DEFAULT(ORIGINAL_LOGIN()),
 marked_utc datetime2(3) NOT NULL CONSTRAINT DF_no_sale_days_marked_utc DEFAULT(SYSUTCDATETIME()),
 withdrawn_by nvarchar(100) NULL,
 withdrawn_utc datetime2(3) NULL,
 withdraw_reason nvarchar(500) NULL,
 row_version rowversion NOT NULL,
 CONSTRAINT PK_no_sale_days PRIMARY KEY(store_code,business_date),
 CONSTRAINT CK_no_sale_days_reason_code CHECK(reason_code IN('CLOSED_HOLIDAY','CLOSED_OTHER','OPEN_NO_SALE')),
 CONSTRAINT CK_no_sale_days_reason CHECK(LEN(LTRIM(RTRIM(reason)))>0),
 CONSTRAINT CK_no_sale_days_withdrawn CHECK(
   (withdrawn_utc IS NULL AND withdrawn_by IS NULL AND withdraw_reason IS NULL)
   OR (withdrawn_utc IS NOT NULL AND withdrawn_by IS NOT NULL AND withdraw_reason IS NOT NULL AND LEN(LTRIM(RTRIM(withdraw_reason)))>0)));
CREATE INDEX IX_no_sale_days_date ON dbo.no_sale_days(business_date,store_code) WHERE withdrawn_utc IS NULL;

CREATE TABLE dbo.no_sale_day_history(
 history_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_no_sale_day_history PRIMARY KEY,
 store_code varchar(30) NOT NULL,
 business_date date NOT NULL,
 action varchar(10) NOT NULL,
 reason_code varchar(30) NOT NULL,
 reason nvarchar(500) NOT NULL,
 changed_by nvarchar(100) NOT NULL CONSTRAINT DF_no_sale_day_history_by DEFAULT(ORIGINAL_LOGIN()),
 changed_utc datetime2(3) NOT NULL CONSTRAINT DF_no_sale_day_history_utc DEFAULT(SYSUTCDATETIME()),
 CONSTRAINT CK_no_sale_day_history_action CHECK(action IN('MARKED','WITHDRAWN')));
CREATE INDEX IX_no_sale_day_history_scope ON dbo.no_sale_day_history(store_code,business_date,changed_utc DESC);

-- Allowed changes: an insert (a mark), withdrawing an active mark (withdrawn columns NULL -> set, nothing else changes) and
-- marking a withdrawn row again (withdrawn columns set -> NULL, with a new reason, marker and time). Nothing is deleted.
-- No change at all on a LOCKED day. Each allowed change writes one history row (the reason of that change).
EXEC(N'CREATE TRIGGER dbo.trg_no_sale_days_history ON dbo.no_sale_days AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 -- A delete, or a change of the key (the old key has no inserted row), is refused.
 IF EXISTS(SELECT 1 FROM deleted d WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.store_code=d.store_code AND i.business_date=d.business_date))
  THROW 51989,''A no-sale day mark cannot be deleted or rewritten. Withdraw it with a reason instead.'',1;
 IF EXISTS(SELECT 1 FROM (SELECT store_code,business_date FROM inserted UNION SELECT store_code,business_date FROM deleted) changed
   JOIN dbo.daily_reporting_days d ON d.store_code=changed.store_code AND d.business_date=changed.business_date
   WHERE d.status=''LOCKED'')
  THROW 51983,''This business day is finalised. Reopen it before marking or withdrawing a no-sale day.'',1;
 IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.store_code=i.store_code AND d.business_date=i.business_date
   WHERE NOT (
     -- withdraw an active mark
     (d.withdrawn_utc IS NULL AND i.withdrawn_utc IS NOT NULL
      AND i.reason_code=d.reason_code AND i.reason=d.reason AND i.marked_by=d.marked_by AND i.marked_utc=d.marked_utc)
     -- mark a withdrawn row again
     OR (d.withdrawn_utc IS NOT NULL AND i.withdrawn_utc IS NULL)))
  THROW 51989,''A no-sale day mark cannot be deleted or rewritten. Withdraw it with a reason instead.'',1;
 INSERT dbo.no_sale_day_history(store_code,business_date,action,reason_code,reason,changed_by)
 SELECT i.store_code,i.business_date,
   CASE WHEN i.withdrawn_utc IS NULL THEN ''MARKED'' ELSE ''WITHDRAWN'' END,
   i.reason_code,
   CASE WHEN i.withdrawn_utc IS NULL THEN i.reason ELSE i.withdraw_reason END,
   CASE WHEN i.withdrawn_utc IS NULL THEN i.marked_by ELSE i.withdrawn_by END
 FROM inserted i;
END');

EXEC(N'CREATE TRIGGER dbo.trg_no_sale_day_history_immutable ON dbo.no_sale_day_history AFTER UPDATE,DELETE AS
BEGIN
 THROW 51989,''No-sale day history cannot be changed.'',1;
END');

ALTER TABLE dbo.operational_audit DROP CONSTRAINT CK_operational_audit_type;
ALTER TABLE dbo.operational_audit WITH CHECK ADD CONSTRAINT CK_operational_audit_type CHECK
 (event_type IN ('ApplicationStart','SessionStart','ConnectionTest','ImportBatch','ImportFailed','ReportRun','ExportExcel','ExportPdf','DatabaseSetup','SupportPackage',
                 'ManualInput','DayFinalised','DayReopened','ReportPack','Backup','RestoreDrill','ConfigurationChange','MappingProfileChange','Restatement','StockCount','StaffTarget',
                 'UserAdministration','MasterDataChange','AutomationRun','ReportArchive','DocumentIntake','DocumentExtraction','DocumentExtractionReview','SharingContactChange',
                 'RegisterEntry','ShareInitiated','ReportPackage','Approval','Adjustment','AccountingBatch','AccountingExport','ImportConflict','IssueWorkflow','VisualRender',
                 'CollectionsSetup','CollectionsImport','CashDepositConfirmed','SoftwareUpdate','ShopResultPublished','TallyReadback','ImportFixAction',
                 'NoSaleDayMarked','NoSaleDayWithdrawn'));

DENY INSERT,UPDATE,DELETE ON dbo.no_sale_days TO etp_store_manager,etp_viewer;
DENY SELECT ON dbo.no_sale_days(reason,withdraw_reason) TO etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.no_sale_day_history TO etp_store_manager,etp_viewer;
DENY SELECT ON dbo.no_sale_day_history(reason) TO etp_viewer;
