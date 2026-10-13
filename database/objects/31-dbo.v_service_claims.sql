CREATE OR ALTER VIEW dbo.v_service_claims AS
WITH mb AS (
  SELECT r.import_file_id,w.snapshot_date,r.transdate business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.documentnum))),N'') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.jonumber))),N'') job_order_number,CONVERT(nvarchar(100),r.itemid) item_id,r.quantity,
    CONVERT(nvarchar(100),r.accountnum) account_number,r.netamountinctax net_amount_inc_tax,r.ucpamount ucp_value
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s024 r ON w.report_code='S024' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transdate
), wdc_new AS (
  SELECT r.import_file_id,w.snapshot_date,r.transaction_date business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.document_number))),N'') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.job_order_number))),N'') job_order_number,CONVERT(nvarchar(100),r.item_id) item_id,r.quantity,
    CONVERT(nvarchar(100),r.account_number) account_number,r.net_amount_inc_tax,r.ucp_amount ucp_value
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s039 r ON w.report_code='S039' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transaction_date
), wdc_old AS (
  SELECT r.import_file_id,w.snapshot_date,r.transdate business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.documentnum))),N'') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.jonumber))),N'') job_order_number,CONVERT(nvarchar(100),r.itemid) item_id,r.quantity,
    CONVERT(nvarchar(100),r.accountnum) account_number,r.netamountinctax net_amount_inc_tax,r.ucpamount ucp_value
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s025 r ON w.report_code='S025' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transdate
), wra_new AS (
  SELECT r.import_file_id,w.snapshot_date,r.transaction_date business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.document_number))),N'') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.job_order_number))),N'') job_order_number,CONVERT(nvarchar(100),r.item_id) item_id,r.quantity,
    CONVERT(nvarchar(100),r.account_number) account_number,r.net_amount_inc_tax,r.ucp_amount ucp_value
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s040 r ON w.report_code='S040' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transaction_date
), wra_old AS (
  SELECT r.import_file_id,w.snapshot_date,r.transdate business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.documentnum))),N'') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.jonumber))),N'') job_order_number,CONVERT(nvarchar(100),r.itemid) item_id,r.quantity,
    CONVERT(nvarchar(100),r.accountnum) account_number,r.netamountinctax net_amount_inc_tax,r.ucpamount ucp_value
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s026 r ON w.report_code='S026' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transdate
)
SELECT 'GPRC' claim_type,g.report_code,g.business_date,g.document_number,g.job_order_number,g.item_id,g.quantity,g.account_number,
  g.net_amount_inc_tax,g.ucp_value,g.snapshot_date,g.import_file_id
FROM dbo.v_service_gprc_claims g
UNION ALL
SELECT 'MB','S024',business_date,document_number,job_order_number,item_id,quantity,account_number,net_amount_inc_tax,ucp_value,snapshot_date,import_file_id FROM mb
UNION ALL
SELECT 'WDC','S039',business_date,document_number,job_order_number,item_id,quantity,account_number,net_amount_inc_tax,ucp_value,snapshot_date,import_file_id FROM wdc_new
UNION ALL
SELECT 'WDC','S025',h.business_date,h.document_number,h.job_order_number,h.item_id,h.quantity,h.account_number,h.net_amount_inc_tax,h.ucp_value,h.snapshot_date,h.import_file_id
FROM wdc_old h
WHERE NOT EXISTS(SELECT 1 FROM wdc_new c WHERE c.document_number=h.document_number)
  AND (h.document_number IS NOT NULL OR NOT EXISTS(SELECT 1 FROM wdc_new c WHERE c.business_date=h.business_date))
UNION ALL
SELECT 'WRA','S040',business_date,document_number,job_order_number,item_id,quantity,account_number,net_amount_inc_tax,ucp_value,snapshot_date,import_file_id FROM wra_new
UNION ALL
SELECT 'WRA','S026',h.business_date,h.document_number,h.job_order_number,h.item_id,h.quantity,h.account_number,h.net_amount_inc_tax,h.ucp_value,h.snapshot_date,h.import_file_id
FROM wra_old h
WHERE NOT EXISTS(SELECT 1 FROM wra_new c WHERE c.document_number=h.document_number)
  AND (h.document_number IS NOT NULL OR NOT EXISTS(SELECT 1 FROM wra_new c WHERE c.business_date=h.business_date))
  AND NOT EXISTS(SELECT 1 FROM wdc_old c WHERE c.document_number=h.document_number)
  AND NOT EXISTS(SELECT 1 FROM wdc_new c WHERE c.document_number=h.document_number)
-- >>> GRANTS
DENY DELETE ON dbo.v_service_claims TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_claims TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_claims TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_claims TO etp_owner,etp_store_manager,etp_viewer;
