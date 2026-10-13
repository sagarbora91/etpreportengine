CREATE OR ALTER PROCEDURE diag.load_health_check_runs @top int=30,@trigger varchar(8)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @count int=CASE WHEN @top IS NULL OR @top<1 THEN 30 WHEN @top>500 THEN 500 ELSE @top END;
 SELECT TOP(@count) health_check_run_id,trigger_kind,business_date,started_utc,completed_utc,duration_ms,app_version,machine_name,
   run_by_role,overall,pass_count,warning_count,fail_count,known_count,skipped_count,export_file_name
 FROM diag.health_check_runs
 WHERE @trigger IS NULL OR trigger_kind=@trigger
 ORDER BY started_utc DESC,health_check_run_id DESC;
END
-- >>> GRANTS
GRANT EXECUTE ON diag.load_health_check_runs TO etp_automation,etp_owner;
