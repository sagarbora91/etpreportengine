CREATE OR ALTER PROCEDURE diag.clear_health_check_known @check_id varchar(10),@finding_code varchar(60)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51822,'Only an Owner can clear a known health check warning.',1;
 DELETE diag.health_check_known WHERE check_id=@check_id AND finding_code=@finding_code;
END
-- >>> GRANTS
GRANT EXECUTE ON diag.clear_health_check_known TO etp_owner;
