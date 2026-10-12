-- 1.11.2, lane L5 "Report logic and SQL speed" (12 Oct 2026). Audits of 12 Oct: AUDIT-2 S-07, AUDIT-1 D7, AUDIT-4 spots 2-4.
--
-- A. S-07 / decision 37: the retail invoice summary (Customer-wise, the daily pack's Invoice Summary) gives the customer name
--    by role. dbo.v_sales_invoice_customer_name returns the current R024 name of each invoice, wrapped in 0056's rule (Owner,
--    Store Manager and db_owner see it, a Viewer reads NULL). The report reads the name only through this view, so it keeps
--    working for every role when 0059 (lane L1) denies the staging tables to a Viewer: dbo owns the view and the tables,
--    so ownership chaining applies.
-- B. S-07: product_settings.share_pack_customer_names, the Owner-only setting "include customer names in shared packs".
--    Default 0 (off): the daily pack is generated with the Customer column blank. product_settings is already Owner-only
--    for writes (no role holds INSERT/UPDATE on it; 0022 and later revoke them), so the column needs no grant.
-- C. D7 / decision 41: a bill cancelled the same day does not count in the DSR INVOICE count. dbo.v_sales_cancelled_same_day
--    lists each invoice that a BC (bill cancellation) row of a current R025 file cancels on the invoice's own date (the BC's
--    INVREFNO is the invoice number). Store, document and date only: no name or contact column.
-- D. AUDIT-4: indexes only.
--    - spot 2: a covering index on sales_line_enrichments in the order of EnrichmentOccurrencePairing's window (partition keys,
--      then source_lineage_id), so the staff pairing reads it in order instead of sorting the whole table;
--    - spot 4: etp_r024 by invoice date for the customer-name lookup (store_code and inv_number are nvarchar(max) and cannot
--      be index keys, so the date is the key and they are included);
--    - for C: etp_r025 by transaction date, so the DSR's cancelled-bill lookup seeks the period instead of scanning staging.
--    Spot 3 (the Sales query's per-line stock lookup) is a query change in C# (SqlReportingQueries.Sales); the base table
--    already has IX_stock_snapshots_product (store_code, product_code, snapshot_date).
--
-- 0001-0059 are not edited. The migration runner applies this entire script in one transaction, so it has no BEGIN or
-- COMMIT and no GO; statements that must start a batch, or that read the new column, run through EXEC.
-- Grants: the two new views get the same GRANT/DENY lines the Service views carry (0048/0050/0056), so
-- ExpectedRolePermissions.json gains four entries per view at the gate (regenerated there; lane L1 owns the file).
SET XACT_ABORT ON;

-- A. The customer name of a retail invoice, by role.
EXEC(N'CREATE OR ALTER VIEW dbo.v_sales_invoice_customer_name AS
SELECT x.store_code,x.document_number,x.transaction_date,
       CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1
       THEN x.customer_name END customer_name
FROM
(
  SELECT k.store_code,k.document_number,k.transaction_date,k.customer_name,
         ROW_NUMBER() OVER(PARTITION BY k.store_code,k.document_number,k.transaction_date ORDER BY k.etp_row_id DESC) newest
  FROM
  (
    SELECT CONVERT(varchar(30),d.store_code) store_code,CONVERT(nvarchar(80),d.inv_number) document_number,d.inv_date transaction_date,
           CONVERT(nvarchar(200),d.customer_name) customer_name,d.etp_row_id
    FROM dbo.etp_r024 d JOIN dbo.import_files f ON f.import_file_id=d.import_file_id
    WHERE f.is_superseded=0 AND d.inv_date IS NOT NULL
  ) k
) x
WHERE x.newest=1
');
GRANT SELECT ON dbo.v_sales_invoice_customer_name TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_sales_invoice_customer_name TO etp_store_manager,etp_viewer;

-- B. Owner-only pack setting, default off.
ALTER TABLE dbo.product_settings ADD share_pack_customer_names bit NOT NULL
    CONSTRAINT DF_product_settings_share_pack_customer_names DEFAULT(0);

-- C. Invoices cancelled the same day by a BC row of a current R025 file (decision 41).
EXEC(N'CREATE OR ALTER VIEW dbo.v_sales_cancelled_same_day AS
SELECT DISTINCT i.store_code,i.document_number,i.transaction_date
FROM dbo.etp_r025 r
JOIN dbo.import_files f ON f.import_file_id=r.import_file_id AND f.report_code=''R025'' AND f.is_superseded=0 AND f.data_truth_version=1
JOIN dbo.sales_invoices i ON i.store_code=CONVERT(varchar(30),LTRIM(RTRIM(r.store_code)))
  AND i.document_number=CONVERT(nvarchar(80),LTRIM(RTRIM(r.reference_invoice_number)))
  AND i.transaction_date=r.transaction_date
WHERE UPPER(LTRIM(RTRIM(r.source_transaction_type)))=N''BC'' AND NULLIF(LTRIM(RTRIM(r.reference_invoice_number)),N'''') IS NOT NULL
  AND (r.reference_invoice_date IS NULL OR r.reference_invoice_date=r.transaction_date)
');
GRANT SELECT ON dbo.v_sales_cancelled_same_day TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_sales_cancelled_same_day TO etp_store_manager,etp_viewer;

-- D. Indexes.
CREATE INDEX IX_sales_line_enrichments_occurrence ON dbo.sales_line_enrichments
    (enrichment_type,store_code,transaction_date,document_number,product_code,source_lineage_id)
    INCLUDE(source_transaction_type,source_cro_number,staff_name,source_quantity,source_gross_value,
            scheme_discount,user_discount,pre_discount,invoice_year,matched_sales_line_id,match_status);
CREATE INDEX IX_etp_r024_inv_date ON dbo.etp_r024(inv_date) INCLUDE(import_file_id,store_code,inv_number);
CREATE INDEX IX_etp_r025_transaction_date ON dbo.etp_r025(transaction_date)
    INCLUDE(import_file_id,store_code,source_transaction_type,reference_invoice_number,reference_invoice_date);
