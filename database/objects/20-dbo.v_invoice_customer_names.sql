CREATE OR ALTER VIEW dbo.v_invoice_customer_names AS
SELECT d.etp_row_id,d.import_file_id,d.store_code,d.inv_number,d.inv_date,
  CASE WHEN IS_MEMBER(N'etp_owner')=1 OR IS_MEMBER(N'etp_store_manager')=1 OR IS_MEMBER(N'db_owner')=1 THEN d.customer_name END customer_name
FROM dbo.etp_r024 d
-- >>> GRANTS
DENY DELETE ON dbo.v_invoice_customer_names TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_invoice_customer_names TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_invoice_customer_names TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_invoice_customer_names TO etp_owner,etp_store_manager,etp_viewer;
