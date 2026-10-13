CREATE OR ALTER PROCEDURE dbo.load_latest_recovery_drill_result
AS
BEGIN
 SET NOCOUNT ON;
 SELECT TOP(1) completed_utc,outcome,backup_sha256,row_counts_status,not_recorded_reason,
   sales_invoices_receipt,sales_invoices_restored,sales_lines_receipt,sales_lines_restored,
   import_files_receipt,import_files_restored,daily_reporting_days_receipt,daily_reporting_days_restored
 FROM dbo.recovery_drill_results
 ORDER BY recovery_drill_result_id DESC;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.load_latest_recovery_drill_result TO etp_automation,etp_owner,etp_store_manager,etp_viewer;
