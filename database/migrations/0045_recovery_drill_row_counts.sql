SET XACT_ABORT ON;

-- 1.9.3, Phase 4 A4.4 and A4.4a (decided by Sagar, 2 October 2026).
--
-- The backup receipt now records COUNT_BIG(*) of sales_invoices, sales_lines, import_files
-- and daily_reporting_days, and the recovery drill compares them with the same tables in
-- the restored copy. System status has to show that result - passed with the four pairs,
-- passed with the reason no counts were recorded, or failed naming the table - and it
-- cannot come from anywhere already there:
--   * <database>-latest-drill.json is in %ProgramData%\EtpReporting\Backups, which only
--     SYSTEM and Administrators can read (A4.2), so the unelevated application cannot read it;
--   * dbo.operational_audit details may not contain digits (0026), and generic audit
--     events are deliberately not a source of trusted recovery health (0023);
--   * dbo.verified_operation_receipts records successful drills only.
-- So the drill records its result here, through a procedure only the operations account
-- and Owners may run - the same rule as dbo.record_verified_operation - and the
-- application reads the latest row through dbo.load_latest_recovery_drill_result.
-- Append-only, like the receipts.
--
-- Written as 0043 on branch fix193/phase4-rowcounts; renumbered 0045 when it was merged into
-- feature/import-engine after 0043 (owner grant option) and 0044 (Phase 5 reason white space).
-- This file depends on neither.

CREATE TABLE dbo.recovery_drill_results
(
 recovery_drill_result_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_recovery_drill_results PRIMARY KEY,
 completed_utc datetime2(7) NOT NULL CONSTRAINT DF_recovery_drill_results_completed DEFAULT SYSUTCDATETIME(),
 outcome varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
 backup_sha256 varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 row_counts_status varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
 not_recorded_reason varchar(40) COLLATE Latin1_General_100_BIN2 NULL,
 sales_invoices_receipt bigint NULL,
 sales_invoices_restored bigint NULL,
 sales_lines_receipt bigint NULL,
 sales_lines_restored bigint NULL,
 import_files_receipt bigint NULL,
 import_files_restored bigint NULL,
 daily_reporting_days_receipt bigint NULL,
 daily_reporting_days_restored bigint NULL,
 recorded_by nvarchar(128) NOT NULL,
 CONSTRAINT CK_recovery_drill_results_outcome CHECK(outcome IN ('Succeeded','Failed')),
 CONSTRAINT CK_recovery_drill_results_hash CHECK(DATALENGTH(backup_sha256)=64 AND backup_sha256 NOT LIKE '%[^0-9A-F]%'),
 CONSTRAINT CK_recovery_drill_results_status CHECK(row_counts_status IN ('Matched','Mismatch','NotRecorded')),
 CONSTRAINT CK_recovery_drill_results_reason CHECK(not_recorded_reason IS NULL OR not_recorded_reason IN
   ('CHANGED_DURING_BACKUP','OPERATIONS_MODULE_OUTDATED','COUNT_FAILED','RECEIPT_WITHOUT_COUNTS','RESTORED_COPY_NOT_COUNTED','RECEIPT_COUNTS_UNREADABLE')),
 -- Matched and Mismatch carry all eight numbers and no reason; NotRecorded carries a reason
 -- and no numbers. Matched passes with every pair equal; Mismatch fails with one differing.
 CONSTRAINT CK_recovery_drill_results_shape CHECK(
   (row_counts_status IN ('Matched','Mismatch') AND not_recorded_reason IS NULL
     AND sales_invoices_receipt IS NOT NULL AND sales_invoices_restored IS NOT NULL
     AND sales_lines_receipt IS NOT NULL AND sales_lines_restored IS NOT NULL
     AND import_files_receipt IS NOT NULL AND import_files_restored IS NOT NULL
     AND daily_reporting_days_receipt IS NOT NULL AND daily_reporting_days_restored IS NOT NULL
     AND sales_invoices_receipt>=0 AND sales_invoices_restored>=0 AND sales_lines_receipt>=0 AND sales_lines_restored>=0
     AND import_files_receipt>=0 AND import_files_restored>=0 AND daily_reporting_days_receipt>=0 AND daily_reporting_days_restored>=0)
   OR (row_counts_status='NotRecorded' AND not_recorded_reason IS NOT NULL
     AND sales_invoices_receipt IS NULL AND sales_invoices_restored IS NULL AND sales_lines_receipt IS NULL AND sales_lines_restored IS NULL
     AND import_files_receipt IS NULL AND import_files_restored IS NULL AND daily_reporting_days_receipt IS NULL AND daily_reporting_days_restored IS NULL)),
 CONSTRAINT CK_recovery_drill_results_verdict CHECK(
   (row_counts_status='Matched' AND outcome='Succeeded'
     AND sales_invoices_receipt=sales_invoices_restored AND sales_lines_receipt=sales_lines_restored
     AND import_files_receipt=import_files_restored AND daily_reporting_days_receipt=daily_reporting_days_restored)
   OR (row_counts_status='Mismatch' AND outcome='Failed'
     AND (sales_invoices_receipt<>sales_invoices_restored OR sales_lines_receipt<>sales_lines_restored
       OR import_files_receipt<>import_files_restored OR daily_reporting_days_receipt<>daily_reporting_days_restored))
   OR (row_counts_status='NotRecorded' AND outcome='Succeeded'
     AND not_recorded_reason IN ('CHANGED_DURING_BACKUP','OPERATIONS_MODULE_OUTDATED','COUNT_FAILED','RECEIPT_WITHOUT_COUNTS'))
   OR (row_counts_status='NotRecorded' AND outcome='Failed'
     AND not_recorded_reason IN ('OPERATIONS_MODULE_OUTDATED','RESTORED_COPY_NOT_COUNTED','RECEIPT_COUNTS_UNREADABLE')))
);

EXEC(N'CREATE OR ALTER TRIGGER dbo.tr_recovery_drill_results_append_only
 ON dbo.recovery_drill_results INSTEAD OF UPDATE,DELETE AS
 BEGIN THROW 51343,''Recovery drill history is append-only.'',1; END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.record_recovery_drill_result
 @outcome varchar(20), @backup_sha256 varchar(128), @row_counts_status varchar(20), @not_recorded_reason varchar(40)=NULL,
 @sales_invoices_receipt bigint=NULL, @sales_invoices_restored bigint=NULL,
 @sales_lines_receipt bigint=NULL, @sales_lines_restored bigint=NULL,
 @import_files_receipt bigint=NULL, @import_files_restored bigint=NULL,
 @daily_reporting_days_receipt bigint=NULL, @daily_reporting_days_restored bigint=NULL
AS
BEGIN
 SET NOCOUNT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_automation''),0)<>1
    AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51341,''The dedicated operations account or an Owner is required.'',1;
 IF @backup_sha256 IS NULL OR DATALENGTH(@backup_sha256)<>64
    OR @backup_sha256 COLLATE Latin1_General_100_BIN2 LIKE ''%[^0-9A-Fa-f]%''
  THROW 51342,''A complete backup verification hash is required.'',1;
 -- The table''s constraints decide which combinations are valid; this turns a refusal into
 -- one message instead of a constraint name.
 BEGIN TRY
  INSERT dbo.recovery_drill_results(outcome,backup_sha256,row_counts_status,not_recorded_reason,
    sales_invoices_receipt,sales_invoices_restored,sales_lines_receipt,sales_lines_restored,
    import_files_receipt,import_files_restored,daily_reporting_days_receipt,daily_reporting_days_restored,recorded_by)
  VALUES(@outcome,UPPER(@backup_sha256),@row_counts_status,@not_recorded_reason,
    @sales_invoices_receipt,@sales_invoices_restored,@sales_lines_receipt,@sales_lines_restored,
    @import_files_receipt,@import_files_restored,@daily_reporting_days_receipt,@daily_reporting_days_restored,SUSER_SNAME());
 END TRY
 BEGIN CATCH
  IF ERROR_NUMBER()=547 THROW 51344,''The recovery drill result is not consistent.'',1;
  THROW;
 END CATCH;
END');

EXEC(N'CREATE OR ALTER PROCEDURE dbo.load_latest_recovery_drill_result
AS
BEGIN
 SET NOCOUNT ON;
 SELECT TOP(1) completed_utc,outcome,backup_sha256,row_counts_status,not_recorded_reason,
   sales_invoices_receipt,sales_invoices_restored,sales_lines_receipt,sales_lines_restored,
   import_files_receipt,import_files_restored,daily_reporting_days_receipt,daily_reporting_days_restored
 FROM dbo.recovery_drill_results
 ORDER BY recovery_drill_result_id DESC;
END');

DENY SELECT,INSERT,UPDATE,DELETE ON dbo.recovery_drill_results TO etp_store_manager,etp_viewer,etp_automation;
GRANT EXECUTE ON dbo.record_recovery_drill_result TO etp_owner,etp_automation;
GRANT EXECUTE ON dbo.load_latest_recovery_drill_result TO etp_owner,etp_store_manager,etp_viewer,etp_automation;
