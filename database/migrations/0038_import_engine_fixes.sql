-- Import engine fixes for release 1.9.3 (CONSOLIDATED-AND-RAW-IMPORT-SPEC.md section 5.1).
-- The migration runner applies this entire script in one transaction, so it has no BEGIN or COMMIT.
-- The pre-checks THROW before anything changes; every later statement is idempotent
-- (COL_LENGTH/OBJECT_ID guards, CREATE OR ALTER). A statement that uses a column added earlier in
-- this script, and every CREATE PROCEDURE, VIEW or TRIGGER, runs through EXEC(N'...').
-- New error numbers are 51700-51799.
-- Each section changes only what lies between its own begin and end markers.
SET XACT_ABORT ON;

-- >>> PRECHECK_FY begin
-- Every invoice is keyed by the financial year of its own date (OD-1, IF-019); ETP's INVOICEYEAR is only a label.
IF EXISTS(SELECT 1 FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK)
  WHERE invoice_year<>YEAR(transaction_date)+CASE WHEN MONTH(transaction_date)>=4 THEN 1 ELSE 0 END)
  THROW 51700,'Some invoices carry a year other than the financial year of their date. Run scripts/check-import-upgrade.sql and review before upgrading.',1;
-- <<< PRECHECK_FY end

-- >>> PRECHECK_CONTROLS_TENDERS begin
-- <<< PRECHECK_CONTROLS_TENDERS end

-- >>> A_DIAGNOSTICS begin
-- <<< A_DIAGNOSTICS end

-- >>> B_EVIDENCE begin
-- <<< B_EVIDENCE end

-- >>> C_STOCK_MOVEMENT begin
-- <<< C_STOCK_MOVEMENT end

-- >>> C2_CONTROL_TENDER begin
-- <<< C2_CONTROL_TENDER end

-- >>> D_SNAPSHOT begin
-- <<< D_SNAPSHOT end

-- >>> E_ENRICHMENT begin
-- E. Enrichment outcome. The procedure skipped a content key it already held while the importer recorded NEW.
-- It now reports NEW or ALREADY_PRESENT through @outcome, and planner 1 records that outcome. The key is derived
-- from the keyed content (EtpInvoiceIdentity.LineKeys), so no CONFLICT is reported; a later export that changes
-- only unkeyed values (gross value, other charges, staff name, activation or discount details) is reported
-- ALREADY_PRESENT and keeps the stored values (known limitation). A caller that does not pass @outcome behaves as before.
EXEC(N'CREATE OR ALTER PROCEDURE dbo.persist_phase_one_enrichment
 @file bigint,@report varchar(10),@store varchar(30),@doc nvarchar(80),@date date,@product nvarchar(80),@type nvarchar(80),
 @qty decimal(19,4),@net decimal(19,4),@gross decimal(19,4),@cro nvarchar(80),@name nvarchar(200),
 @scheme decimal(19,4),@userDiscount decimal(19,4),@pre decimal(19,4),@other decimal(19,4),
 @activation nvarchar(500),@details nvarchar(500),@lineage bigint,@key varchar(80),
 @outcome varchar(16)=NULL OUTPUT
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=@report AND f.store_code=@store
    AND @date BETWEEN COALESCE(f.period_start,f.business_date) AND COALESCE(f.period_end,f.business_date))
  THROW 51422,''The enrichment does not belong to this source import.'',1;
 DECLARE @matches int,@line bigint;
 SELECT @matches=COUNT(*),@line=CASE WHEN COUNT(*)=1 THEN MAX(l.sales_line_id) END
 FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
 WHERE i.store_code=@store AND i.document_number=@doc AND i.transaction_date=@date AND l.product_code=@product;
 IF NOT EXISTS(SELECT 1 FROM dbo.sales_line_enrichments WITH(UPDLOCK,HOLDLOCK) WHERE enrichment_type=@report AND store_code=@store AND content_key=@key)
 BEGIN
  INSERT dbo.sales_line_enrichments(enrichment_type,store_code,transaction_date,document_number,product_code,
    source_transaction_type,source_quantity,source_net_value,source_gross_value,source_cro_number,staff_name,
    scheme_discount,user_discount,pre_discount,other_charges,activation_details,user_discount_details,matched_sales_line_id,match_status,source_lineage_id,content_key,invoice_year)
  VALUES(@report,@store,@date,@doc,@product,@type,@qty,@net,@gross,@cro,@name,@scheme,@userDiscount,@pre,@other,@activation,@details,@line,
    CASE @matches WHEN 0 THEN ''Missing'' WHEN 1 THEN ''Matched'' ELSE ''Ambiguous'' END,@lineage,@key,YEAR(@date)+CASE WHEN MONTH(@date)>=4 THEN 1 ELSE 0 END);
  SET @outcome=''NEW'';
 END
 ELSE SET @outcome=''ALREADY_PRESENT'';
 IF NULLIF(LTRIM(RTRIM(@cro)),N'''') IS NOT NULL
  MERGE dbo.staff WITH(HOLDLOCK) AS target USING(SELECT @store store_code,@cro staff_code) source
  ON target.store_code=source.store_code AND target.staff_code=source.staff_code
  WHEN MATCHED AND target.staff_name=target.staff_code AND NULLIF(LTRIM(RTRIM(@name)),N'''') IS NOT NULL
   THEN UPDATE SET staff_name=@name,modified_utc=SYSUTCDATETIME(),modified_by=SUSER_SNAME()
  WHEN NOT MATCHED THEN INSERT(store_code,staff_code,staff_name,active) VALUES(@store,@cro,COALESCE(NULLIF(LTRIM(RTRIM(@name)),N''''),@cro),1);
END');

GRANT EXECUTE ON dbo.persist_phase_one_enrichment TO etp_store_manager,etp_owner;
-- <<< E_ENRICHMENT end

-- >>> G_RESTATEMENT begin
-- <<< G_RESTATEMENT end

