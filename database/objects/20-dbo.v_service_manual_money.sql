CREATE OR ALTER VIEW dbo.v_service_manual_money AS
SELECT m.business_date,m.store_code,s.store_name,m.field_code,m.numeric_value amount,
  CASE m.field_code WHEN 'SERVICE_CASH' THEN 'CASH' WHEN 'SERVICE_CARD' THEN 'CARD' WHEN 'SERVICE_UPI' THEN 'UPI' END tender,
  CONVERT(bit,COALESCE(s.is_service_money_shop,0)) is_service_money_shop
FROM dbo.manual_operational_inputs m LEFT JOIN dbo.stores s ON s.store_code=m.store_code
WHERE m.field_code LIKE 'SERVICE[_]%' AND m.numeric_value IS NOT NULL
-- >>> GRANTS
DENY DELETE ON dbo.v_service_manual_money TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_manual_money TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_manual_money TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_manual_money TO etp_owner,etp_store_manager,etp_viewer;
