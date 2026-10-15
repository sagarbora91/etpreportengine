-- 1.12.1 P8: closing stock per Service Centre. The S006 rows carry a store_code; a row is placed at the SERVICE-unit store whose
-- code it names (trimmed, database collation, as the manual entries and import files do); a row that names no Service Centre
-- has service_store_code NULL ("Not matched to any centre"), except that with exactly one Service Centre it is that centre's
-- (the same fallback as v_service_job). One row per reading, centre; v_service_stock_summary keeps the all-centres totals.
CREATE OR ALTER VIEW dbo.v_service_stock_summary_centre AS
WITH centres AS (
  SELECT s.store_code
  FROM dbo.stores s JOIN dbo.business_units u ON u.business_unit_id=s.business_unit_id
  WHERE u.unit_kind='SERVICE'
), only_centre AS (
  SELECT CASE WHEN COUNT(*)=1 THEN MIN(store_code) END store_code FROM centres
)
SELECT r.snapshot_date,COALESCE(m.store_code,oc.store_code) service_store_code,CONVERT(int,COUNT_BIG(*)) items,
  SUM(s.quantity) quantity,SUM(s.quantity*s.price) value,r.import_file_id
FROM dbo.etp_landing_s006 s JOIN dbo.v_service_readings r ON r.import_file_id=s.import_file_id AND r.report_code='S006' AND r.is_latest=1
CROSS JOIN only_centre oc
OUTER APPLY (SELECT TOP (1) c.store_code FROM centres c
  WHERE c.store_code=LEFT(LTRIM(RTRIM(CONVERT(nvarchar(60),s.store_code))),30) ORDER BY c.store_code) m
GROUP BY r.snapshot_date,COALESCE(m.store_code,oc.store_code),r.import_file_id
-- >>> GRANTS
DENY DELETE ON dbo.v_service_stock_summary_centre TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_stock_summary_centre TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_stock_summary_centre TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_stock_summary_centre TO etp_owner,etp_store_manager,etp_viewer;
