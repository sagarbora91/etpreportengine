CREATE OR ALTER PROCEDURE diag.load_health_check_summary
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 SELECT TOP(1) business_date,completed_utc,overall,pass_count,warning_count,fail_count,known_count,skipped_count
 FROM diag.health_check_runs
 WHERE trigger_kind='Nightly'
 ORDER BY started_utc DESC,health_check_run_id DESC;
END
-- >>> GRANTS
GRANT EXECUTE ON diag.load_health_check_summary TO etp_automation,etp_owner,etp_store_manager,etp_viewer;
