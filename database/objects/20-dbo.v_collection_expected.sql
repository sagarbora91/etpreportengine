-- 1.12 wave 1, lane D2 (112-WAVE-PLAN D-b; migration 0063). What money each store and business date should produce, by
-- payment mode and provider: the reporting-eligible tenders (R022, through reporting_sales_tenders) mapped by tender_modes,
-- and the Service inputs (SERVICE_CASH / SERVICE_CARD / SERVICE_UPI) of the daily sheet. Service modes come from the Service
-- inputs only, never from sales tenders. Signed sums: a refund reduces its day once. A missing input is no row (not 0).
-- R020's TC fill (TC_R020) is not here: TC expects no outside evidence.
-- evidence_kind: CASH_DEPOSIT (the deposit is the evidence), NONE (CN, TC, Gift Card, Service Cash, or a provider that settles
-- nowhere), PROVIDER_SETTLEMENT, PROVIDER_NOT_SET (D19: no or an inactive provider), UNMAPPED (a tender code tender_modes
-- does not know; unmapped_tender_code names it).
CREATE OR ALTER VIEW dbo.v_collection_expected AS
SELECT r.store_code,r.business_date,r.mode,r.provider_key,r.unmapped_tender_code,r.source_kind,r.expected_amount,r.source_rows,
       CONVERT(varchar(20),
       CASE WHEN r.mode='Unmapped' THEN 'UNMAPPED'
            WHEN r.mode='Cash' THEN 'CASH_DEPOSIT'
            WHEN r.mode IN('CN','TC','Gift Card','Service Cash') THEN 'NONE'
            WHEN p.provider_key IS NOT NULL AND p.settles_via='NONE' THEN 'NONE'
            WHEN p.provider_key IS NULL OR p.active=0 THEN 'PROVIDER_NOT_SET'
            ELSE 'PROVIDER_SETTLEMENT' END) AS evidence_kind
FROM
(
  SELECT i.store_code,i.transaction_date AS business_date,
         CONVERT(varchar(30),COALESCE(m.mode,'Unmapped')) AS mode,m.provider_key,
         CASE WHEN m.mode IS NULL THEN t.tender_type END AS unmapped_tender_code,
         CONVERT(varchar(15),'SALES_TENDERS') AS source_kind,
         SUM(t.source_amount) AS expected_amount,COUNT_BIG(*) AS source_rows
  FROM dbo.reporting_sales_tenders t
  JOIN dbo.sales_invoices i ON i.sales_invoice_id=t.sales_invoice_id
  LEFT JOIN dbo.tender_modes m ON m.source_tender_code=t.tender_type AND m.active=1
  WHERE m.mode IS NULL OR m.mode NOT LIKE 'Service %'
  GROUP BY i.store_code,i.transaction_date,COALESCE(m.mode,'Unmapped'),m.provider_key,CASE WHEN m.mode IS NULL THEN t.tender_type END
  UNION ALL
  SELECT x.store_code,x.business_date,
         CONVERT(varchar(30),COALESCE(m.mode,CASE x.field_code WHEN 'SERVICE_CASH' THEN 'Service Cash' WHEN 'SERVICE_CARD' THEN 'Service Card' ELSE 'Service UPI' END)),
         m.provider_key,CONVERT(nvarchar(80),NULL),CONVERT(varchar(15),'SERVICE_INPUT'),
         SUM(x.numeric_value),COUNT_BIG(*)
  FROM dbo.manual_operational_inputs x
  LEFT JOIN dbo.tender_modes m ON m.source_tender_code=CONVERT(nvarchar(80),x.field_code) AND m.active=1
  WHERE x.field_code IN('SERVICE_CASH','SERVICE_CARD','SERVICE_UPI') AND x.numeric_value IS NOT NULL
  GROUP BY x.store_code,x.business_date,
           COALESCE(m.mode,CASE x.field_code WHEN 'SERVICE_CASH' THEN 'Service Cash' WHEN 'SERVICE_CARD' THEN 'Service Card' ELSE 'Service UPI' END),
           m.provider_key
) r
LEFT JOIN dbo.collection_providers p ON p.provider_key=r.provider_key;
-- >>> GRANTS
GRANT SELECT ON dbo.v_collection_expected TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_collection_expected TO etp_store_manager,etp_viewer;
