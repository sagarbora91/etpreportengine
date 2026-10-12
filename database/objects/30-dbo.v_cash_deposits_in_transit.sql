-- 1.12 wave 1, lane D2 (112-WAVE-PLAN D-b; migration 0064). Every deposit the daily sheet says was sent and no bank credit
-- has been confirmed for yet, with its age in days (server date). The report lists them oldest first.
-- Owner and Store Manager see the list; a Viewer sees the totals on the Collections statement only.
CREATE OR ALTER VIEW dbo.v_cash_deposits_in_transit AS
SELECT s.store_code,s.business_date AS deposit_business_date,s.sent_amount,
       DATEDIFF(day,s.business_date,CONVERT(date,SYSDATETIME())) AS age_days
FROM dbo.v_cash_deposits_sent s
WHERE NOT EXISTS(SELECT 1 FROM dbo.cash_deposit_confirmations c
                 WHERE c.store_code=s.store_code AND c.deposit_business_date=s.business_date AND c.withdrawn_utc IS NULL);
-- >>> GRANTS
GRANT SELECT ON dbo.v_cash_deposits_in_transit TO etp_store_manager,etp_owner;
DENY SELECT ON dbo.v_cash_deposits_in_transit TO etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.v_cash_deposits_in_transit TO etp_store_manager,etp_viewer;
