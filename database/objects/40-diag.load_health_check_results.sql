CREATE OR ALTER PROCEDURE diag.load_health_check_results @health_check_run_id bigint
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF NOT EXISTS(SELECT 1 FROM diag.health_check_runs WHERE health_check_run_id=@health_check_run_id)
  THROW 51824,'That health check run was not found. It may be older than 400 days.',1;
 SELECT check_id,area,outcome,finding_code,explanation,fix_task_id,duration_ms,metric_value,detail_json
 FROM diag.health_check_results
 WHERE health_check_run_id=@health_check_run_id
 ORDER BY CASE LEFT(check_id,CHARINDEX('-',check_id)-1)
   WHEN 'DB' THEN 1 WHEN 'SEC' THEN 2 WHEN 'RPT' THEN 3 WHEN 'IMP' THEN 4 WHEN 'DATA' THEN 5 WHEN 'OPS' THEN 6 WHEN 'ERR' THEN 7 ELSE 8 END,
   check_id;
END
-- >>> GRANTS
GRANT EXECUTE ON diag.load_health_check_results TO etp_automation,etp_owner;
