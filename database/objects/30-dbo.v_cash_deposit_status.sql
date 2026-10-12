-- 1.12 wave 1, lane D2 (112-WAVE-PLAN D-b; migration 0064). One row per store and deposit date: what the daily sheet says
-- was sent (v_cash_deposits_sent) next to the active bank credit confirmation, if any.
-- deposit_state: SENT (in transit), CREDITED (same amount), CREDITED_DIFFERENT (another amount),
-- CREDITED_NOT_ON_SHEET (a credit was confirmed for a day whose sheet has no deposit).
CREATE OR ALTER VIEW dbo.v_cash_deposit_status AS
SELECT COALESCE(s.store_code,c.store_code) AS store_code,
       COALESCE(s.business_date,c.deposit_business_date) AS deposit_business_date,
       s.sent_amount,
       c.cash_deposit_confirmation_id,
       c.credited_date,
       c.amount AS credited_amount,
       c.bank_account_id,
       c.bank_line_id,
       c.bank_reference,
       c.proposal_rule,
       CASE WHEN c.cash_deposit_confirmation_id IS NULL THEN 'SENT'
            WHEN s.sent_amount IS NULL THEN 'CREDITED_NOT_ON_SHEET'
            WHEN c.amount=s.sent_amount THEN 'CREDITED'
            ELSE 'CREDITED_DIFFERENT' END AS deposit_state
FROM dbo.v_cash_deposits_sent s
FULL OUTER JOIN (SELECT x.cash_deposit_confirmation_id,x.store_code,x.deposit_business_date,x.credited_date,x.amount,
                        x.bank_account_id,x.bank_line_id,x.bank_reference,x.proposal_rule
                 FROM dbo.cash_deposit_confirmations x WHERE x.withdrawn_utc IS NULL) c
  ON c.store_code=s.store_code AND c.deposit_business_date=s.business_date;
-- >>> GRANTS
GRANT SELECT ON dbo.v_cash_deposit_status TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_cash_deposit_status TO etp_store_manager,etp_viewer;
