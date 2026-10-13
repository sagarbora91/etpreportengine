CREATE OR ALTER FUNCTION diag.caller_role()
RETURNS varchar(16)
AS
BEGIN
 -- The caller's ETP role, never the account: stored on occurrences instead of a Windows name.
 RETURN CASE
  WHEN COALESCE(IS_ROLEMEMBER('etp_owner'),0)=1 THEN 'Owner'
  WHEN COALESCE(IS_ROLEMEMBER('etp_automation'),0)=1 THEN 'Automation'
  WHEN COALESCE(IS_ROLEMEMBER('etp_store_manager'),0)=1 THEN 'StoreManager'
  WHEN COALESCE(IS_ROLEMEMBER('etp_viewer'),0)=1 THEN 'Viewer'
  ELSE 'Unknown' END;
END
-- >>> GRANTS
