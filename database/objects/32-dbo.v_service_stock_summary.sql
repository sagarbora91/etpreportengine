CREATE OR ALTER VIEW dbo.v_service_stock_summary AS
SELECT r.snapshot_date,CONVERT(int,COUNT_BIG(*)) items,SUM(s.quantity) quantity,SUM(s.quantity*s.price) value,r.import_file_id
FROM dbo.etp_landing_s006 s JOIN dbo.v_service_readings r ON r.import_file_id=s.import_file_id AND r.report_code='S006' AND r.is_latest=1
GROUP BY r.snapshot_date,r.import_file_id
-- >>> GRANTS
DENY DELETE ON dbo.v_service_stock_summary TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_stock_summary TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_stock_summary TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_stock_summary TO etp_owner,etp_store_manager,etp_viewer;
