CREATE OR ALTER VIEW dbo.v_service_gprc_claims AS
WITH claims AS (
  SELECT r.import_file_id,w.snapshot_date,r.transaction_date business_date,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.document_number))),N'') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.job_order_number))),N'') job_order_number,
    CONVERT(nvarchar(100),r.item_id) item_id,r.quantity,CONVERT(nvarchar(100),r.account_number) account_number,
    r.price,r.net_amount,r.tax_amount,r.gross_amount,r.taxable,r.total_tax,r.igst_value,r.net_amount_inc_tax,r.value,r.ucp_value
  FROM dbo.etp_landing_s041 r JOIN dbo.v_service_datelog_readings w ON w.report_code='S041' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transaction_date
), history AS (
  SELECT r.import_file_id,w.snapshot_date,r.transdate business_date,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.documentnum))),N'') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.jonumber))),N'') job_order_number,
    CONVERT(nvarchar(100),r.itemid) item_id,r.quantity,CONVERT(nvarchar(100),r.accountnum) account_number,
    r.price,r.netamount net_amount,r.taxamount tax_amount,r.grossamount gross_amount,r.taxable,r.totaltax total_tax,
    r.igstvalue igst_value,r.netamountinctax net_amount_inc_tax,r.value,r.ucpvalue ucp_value
  FROM dbo.etp_landing_s023 r JOIN dbo.v_service_datelog_readings w ON w.report_code='S023' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transdate
)
SELECT 'S041' report_code,business_date,document_number,job_order_number,item_id,quantity,account_number,price,net_amount,
  tax_amount,gross_amount,taxable,total_tax,igst_value,net_amount_inc_tax,value,ucp_value,snapshot_date,import_file_id
FROM claims
UNION ALL
SELECT 'S023',h.business_date,h.document_number,h.job_order_number,h.item_id,h.quantity,h.account_number,h.price,h.net_amount,
  h.tax_amount,h.gross_amount,h.taxable,h.total_tax,h.igst_value,h.net_amount_inc_tax,h.value,h.ucp_value,h.snapshot_date,h.import_file_id
FROM history h
WHERE NOT EXISTS(SELECT 1 FROM claims c WHERE c.document_number=h.document_number)
  AND (h.document_number IS NOT NULL OR NOT EXISTS(SELECT 1 FROM claims c WHERE c.business_date=h.business_date))
-- >>> GRANTS
DENY DELETE ON dbo.v_service_gprc_claims TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_gprc_claims TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_gprc_claims TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_gprc_claims TO etp_owner,etp_store_manager,etp_viewer;
