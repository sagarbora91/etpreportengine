CREATE OR ALTER VIEW dbo.v_service_s004_daily AS
WITH winning AS (
  SELECT r.billingdate business_date,w.snapshot_date,w.import_file_id,r.cashamount,r.cardamount,r.upi,r.bharatpe,
    TRY_CONVERT(decimal(19,4),REPLACE(CONVERT(nvarchar(60),r.phonepe),N',',N'')) phonepe,
    r.chequeamount,r.rtgsamount,r.advanceamount
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s004 r
    ON w.import_file_id=r.import_file_id AND w.business_date=r.billingdate
  WHERE w.report_code='S004' AND w.reading_rank=1
)
SELECT x.business_date,t.tender,SUM(t.amount) amount,CONVERT(int,COUNT_BIG(*)) row_count,MAX(x.snapshot_date) snapshot_date,MAX(x.import_file_id) import_file_id
FROM winning x CROSS APPLY (VALUES
  ('CASH',x.cashamount),('CARD',x.cardamount),
  ('UPI',CASE WHEN x.upi IS NULL AND x.bharatpe IS NULL AND x.phonepe IS NULL THEN NULL ELSE COALESCE(x.upi,0)+COALESCE(x.bharatpe,0)+COALESCE(x.phonepe,0) END),
  ('CHEQUE',x.chequeamount),('RTGS',x.rtgsamount),('ADVANCE',x.advanceamount)
) t(tender,amount)
GROUP BY x.business_date,t.tender
-- >>> GRANTS
DENY DELETE ON dbo.v_service_s004_daily TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_s004_daily TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_s004_daily TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_s004_daily TO etp_owner,etp_store_manager,etp_viewer;
