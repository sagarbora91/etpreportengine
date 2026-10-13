-- 1.12 lane N1 (decision 52): the store-days marked as no-sale days now (withdrawn marks left out). Read by the daily
-- readiness query, DATA-01, the DSR and the no-sale day screens. The reason text is Owner / Store Manager only.
CREATE OR ALTER VIEW dbo.v_no_sale_days_current AS
SELECT n.store_code,n.business_date,n.reason_code,n.reason,n.marked_by,n.marked_utc
FROM dbo.no_sale_days n
WHERE n.withdrawn_utc IS NULL;
-- >>> GRANTS
GRANT SELECT ON dbo.v_no_sale_days_current TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_no_sale_days_current TO etp_store_manager,etp_viewer;
DENY SELECT ON dbo.v_no_sale_days_current(reason) TO etp_viewer;
