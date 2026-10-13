CREATE OR ALTER VIEW dbo.v_service_parts_transit AS
SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.stm_number))),N'') stm_number,s.stm_date business_date,CONVERT(nvarchar(100),s.item_id) item_id,s.qty_shipped quantity_shipped,
  CONVERT(nvarchar(100),s.invent_location_id_from) from_location,CONVERT(nvarchar(100),s.invent_location_id_to) to_location,s.ucp,w.snapshot_date,s.import_file_id
FROM dbo.etp_landing_s013 s JOIN dbo.v_service_datelog_readings w ON w.report_code='S013' AND w.reading_rank=1
  AND w.import_file_id=s.import_file_id AND w.business_date=s.stm_date
-- >>> GRANTS
DENY DELETE ON dbo.v_service_parts_transit TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_parts_transit TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_parts_transit TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_parts_transit TO etp_owner,etp_store_manager,etp_viewer;
