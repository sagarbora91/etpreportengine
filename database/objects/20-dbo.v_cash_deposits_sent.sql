-- 1.12 wave 1, lane D2 (112-WAVE-PLAN D-b; migration 0064). Cash sent to the bank, as the daily sheet says:
-- the CASH_DEPOSIT manual input of a store and day. There is no second entry for "sent"; a bank credit is a row in
-- dbo.cash_deposit_confirmations. A zero or missing value means no deposit that day and is not listed.
CREATE OR ALTER VIEW dbo.v_cash_deposits_sent AS
SELECT i.store_code,i.business_date,i.numeric_value AS sent_amount,i.modified_utc AS recorded_utc
FROM dbo.manual_operational_inputs i
WHERE i.field_code='CASH_DEPOSIT' AND i.numeric_value>0;
-- >>> GRANTS
GRANT SELECT ON dbo.v_cash_deposits_sent TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_cash_deposits_sent TO etp_store_manager,etp_viewer;
