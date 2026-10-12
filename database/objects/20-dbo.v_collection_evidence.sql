-- 1.12 wave 1, lane D2 (112-WAVE-PLAN D-b; migration 0063). Outside evidence for UPI and card, as totals only: per store,
-- transaction business date (IST) and provider, the settlement file's SALE minus REFUND amounts. Only current lines that did
-- not fail, whose store is known from provider_merchant_ids and whose transaction date is known. "Totals only, not matched
-- yet": line-by-line matching (8b) waits for the D19 samples. gross_total is NULL when the layout maps no gross column; then
-- net_total is the only figure. No payer, VPA or raw column is read here.
CREATE OR ALTER VIEW dbo.v_collection_evidence AS
SELECT l.store_code,l.txn_business_date AS business_date,l.provider_key,
       SUM(CASE l.line_kind WHEN 'SALE' THEN l.gross_amount WHEN 'REFUND' THEN -l.gross_amount END) AS gross_total,
       SUM(CASE l.line_kind WHEN 'SALE' THEN l.net_amount WHEN 'REFUND' THEN -l.net_amount END) AS net_total,
       COUNT_BIG(*) AS line_count,
       SUM(CASE WHEN l.line_kind='UNKNOWN' THEN 1 ELSE 0 END) AS unknown_lines
FROM dbo.provider_settlement_lines l
WHERE l.is_current=1 AND l.is_failed=0 AND l.store_code IS NOT NULL AND l.txn_business_date IS NOT NULL
GROUP BY l.store_code,l.txn_business_date,l.provider_key;
-- >>> GRANTS
GRANT SELECT ON dbo.v_collection_evidence TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_collection_evidence TO etp_store_manager,etp_viewer;
