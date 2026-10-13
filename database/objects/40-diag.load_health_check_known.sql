CREATE OR ALTER PROCEDURE diag.load_health_check_known
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 SELECT check_id,finding_code,until_date,reason,set_by,set_utc
 FROM diag.health_check_known
 WHERE until_date>=CONVERT(date,SYSDATETIME())
 ORDER BY check_id,finding_code;
END
-- >>> GRANTS
GRANT EXECUTE ON diag.load_health_check_known TO etp_automation,etp_owner;
