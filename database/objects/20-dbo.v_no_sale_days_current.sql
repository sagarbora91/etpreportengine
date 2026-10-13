-- 1.12 lane N1 (decision 52): the store-days marked as no-sale days now (withdrawn marks left out). Read by the daily
-- readiness query, DATA-01, the DSR and the no-sale day screens. The reason text is Owner / Store Manager only.
-- has_day_data (review M-1): 1 when, since the mark, ETP holds a sales invoice for the store-day or a daily entry other than
-- zero (the same tests dbo.mark_no_sale_day refuses on). Such a mark no longer applies: readiness, Close day, Morning,
-- DATA-01 and the DSR treat the day as an ordinary day and the screens ask for the mark to be withdrawn.
CREATE OR ALTER VIEW dbo.v_no_sale_days_current AS
SELECT n.store_code,n.business_date,n.reason_code,n.reason,n.marked_by,n.marked_utc,
 CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.sales_invoices s WHERE s.store_code=n.store_code AND s.transaction_date=n.business_date)
   OR EXISTS(SELECT 1 FROM dbo.manual_operational_inputs i WHERE i.store_code=n.store_code AND i.business_date=n.business_date
     AND (COALESCE(i.numeric_value,0)<>0 OR NULLIF(LTRIM(RTRIM(i.text_value)),N'') IS NOT NULL))
  THEN 1 ELSE 0 END AS bit) AS has_day_data
FROM dbo.no_sale_days n
WHERE n.withdrawn_utc IS NULL;
-- >>> GRANTS
GRANT SELECT ON dbo.v_no_sale_days_current TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_no_sale_days_current TO etp_store_manager,etp_viewer;
DENY SELECT ON dbo.v_no_sale_days_current(reason) TO etp_viewer;
