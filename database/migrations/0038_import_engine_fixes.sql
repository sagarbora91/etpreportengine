-- Import engine fixes for release 1.9.3 (CONSOLIDATED-AND-RAW-IMPORT-SPEC.md section 5.1).
-- The migration runner applies this entire script in one transaction, so it has no BEGIN or COMMIT.
-- The pre-checks THROW before anything changes; every later statement is idempotent
-- (COL_LENGTH/OBJECT_ID guards, CREATE OR ALTER). A statement that uses a column added earlier in
-- this script, and every CREATE PROCEDURE, VIEW or TRIGGER, runs through EXEC(N'...').
-- New error numbers are 51700-51799.
-- Each section changes only what lies between its own begin and end markers.
SET XACT_ABORT ON;

-- >>> PRECHECK_FY begin
-- <<< PRECHECK_FY end

-- >>> PRECHECK_CONTROLS_TENDERS begin
-- Section C2 makes one revenue control per invoice and one tender per invoice and type unique (IF-006).
IF EXISTS(SELECT 1 FROM dbo.sales_invoice_controls WITH(UPDLOCK,HOLDLOCK)
 GROUP BY sales_invoice_id HAVING COUNT(*)>1)
 THROW 51701,'An invoice has more than one revenue control. Review before upgrading.',1;
IF EXISTS(SELECT 1 FROM dbo.sales_tenders WITH(UPDLOCK,HOLDLOCK)
 GROUP BY sales_invoice_id,UPPER(tender_type) HAVING COUNT(*)>1)
 THROW 51702,'An invoice has the same tender type twice. Review before upgrading.',1;
-- <<< PRECHECK_CONTROLS_TENDERS end

-- >>> A_DIAGNOSTICS begin
-- <<< A_DIAGNOSTICS end

-- >>> B_EVIDENCE begin
-- <<< B_EVIDENCE end

-- >>> C_STOCK_MOVEMENT begin
-- Stock movement identity (IF-018). Per-unit ledger rows share every identity column, so line_seq numbers the
-- rows of one identity in running-balance order (StockUnitSequencer). from_key and to_key make a missing
-- location equal a blank one, as persist_stock_movement has always compared them.
IF COL_LENGTH(N'dbo.stock_movements',N'line_seq') IS NULL
 ALTER TABLE dbo.stock_movements ADD line_seq int NOT NULL CONSTRAINT DF_stock_movements_line_seq DEFAULT(1);
IF COL_LENGTH(N'dbo.stock_movements',N'from_key') IS NULL
 ALTER TABLE dbo.stock_movements ADD from_key AS ISNULL(from_location,N'') PERSISTED,
     to_key AS ISNULL(to_location,N'') PERSISTED;
IF INDEXPROPERTY(OBJECT_ID(N'dbo.stock_movements'),N'UX_stock_movements_identity',N'IndexID') IS NULL
BEGIN
 -- Number existing repeats as the sequencer would; the source row is not stored, so the id stands in for file
 -- order. The backfill must also work for finalised days, so the guard is off for this one statement only.
 DISABLE TRIGGER dbo.trg_stock_movements_protect_locked ON dbo.stock_movements;
 EXEC(N'WITH s AS (SELECT line_seq,ROW_NUMBER() OVER(PARTITION BY store_code,invoice_year,document_number,document_date,
       product_code,source_transaction_type,from_key,to_key
       ORDER BY CASE WHEN transaction_quantity<0 THEN -opening_quantity ELSE opening_quantity END,
                CASE WHEN transaction_quantity<0 THEN -closing_quantity ELSE closing_quantity END,
                opening_quantity,transaction_quantity,closing_quantity,stock_movement_id) n
     FROM dbo.stock_movements) UPDATE s SET line_seq=n WHERE n>1');
 ENABLE TRIGGER dbo.trg_stock_movements_protect_locked ON dbo.stock_movements;
 EXEC(N'CREATE UNIQUE INDEX UX_stock_movements_identity ON dbo.stock_movements(store_code,invoice_year,document_number,
       document_date,product_code,source_transaction_type,from_key,to_key,line_seq)');
END

-- The identity lookup adds line_seq, the identity text ends /#<line_seq>, and conflicts carry the ledger's report code.
EXEC(N'CREATE OR ALTER PROCEDURE dbo.persist_stock_movement
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@product nvarchar(80),@type nvarchar(80),@from nvarchar(80)=NULL,@to nvarchar(80)=NULL,@opening decimal(19,4),@transaction decimal(19,4),@closing decimal(19,4),@lineage bigint,@line_seq int=1
AS
BEGIN
 SET NOCOUNT ON; DECLARE @existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage; SET @identity=CONCAT(@store,N''/'',@year,N''/'',@doc,N''/'',@date,N''/'',@product,N''/'',UPPER(@type),N''/'',ISNULL(@from,N''''),N''/'',ISNULL(@to,N''''),N''/#'',@line_seq);
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@opening,N''|'',@transaction,N''|'',@closing)),2));
 SELECT TOP(1) @existing=stock_movement_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(opening_quantity,N''|'',transaction_quantity,N''|'',closing_quantity)),2)) FROM dbo.stock_movements WHERE store_code=@store AND invoice_year=@year AND document_number=@doc AND document_date=@date AND product_code=@product AND source_transaction_type=@type AND from_key=ISNULL(@from,N'''') AND to_key=ISNULL(@to,N'''') AND line_seq=@line_seq ORDER BY stock_movement_id;
 IF @existing IS NOT NULL BEGIN IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''ALREADY_PRESENT'',@incoming,N''Identical stock movement already exists.''); ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''Stock movement identity exists with different content.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''STOCK_LEDGER'',@identity,@current,@incoming,N''Stock movement values differ. Review and request a controlled restatement.''); END RETURN; END
 INSERT dbo.stock_movements(store_code,document_number,invoice_year,document_date,product_code,source_transaction_type,from_location,to_location,opening_quantity,transaction_quantity,closing_quantity,source_lineage_id,line_seq) VALUES(@store,@doc,@year,@date,@product,@type,@from,@to,@opening,@transaction,@closing,@lineage,@line_seq);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''NEW'',@incoming,N''New stock movement imported.'');
END');
-- <<< C_STOCK_MOVEMENT end

-- >>> C2_CONTROL_TENDER begin
-- Control and tender identity (IF-006). persist_sales_invoice_control and persist_sales_tender find an existing
-- row with TOP(1) and stay unchanged; these indexes make a second control or tender impossible.
IF INDEXPROPERTY(OBJECT_ID(N'dbo.sales_invoice_controls'),N'UX_sales_invoice_controls_invoice',N'IndexID') IS NULL
 EXEC(N'CREATE UNIQUE INDEX UX_sales_invoice_controls_invoice ON dbo.sales_invoice_controls(sales_invoice_id)');
IF INDEXPROPERTY(OBJECT_ID(N'dbo.sales_tenders'),N'UX_sales_tenders_invoice_type',N'IndexID') IS NULL
BEGIN
 -- persist_sales_tender compares UPPER(tender_type). A case-insensitive column (Latin1_General_CI_AS) is indexed
 -- as it is; on a case-sensitive install the index uses a persisted upper-case key, so both compare alike.
 DECLARE @tender_collation sysname=(SELECT collation_name FROM sys.columns
   WHERE object_id=OBJECT_ID(N'dbo.sales_tenders') AND name=N'tender_type'),@tender_ignores_case bit;
 DECLARE @tender_probe nvarchar(400)=N'SELECT @ignores=CASE WHEN N''a'' COLLATE '+@tender_collation+N'=N''A'' THEN 1 ELSE 0 END';
 EXEC sys.sp_executesql @tender_probe,N'@ignores bit OUTPUT',@ignores=@tender_ignores_case OUTPUT;
 IF @tender_ignores_case=1
  EXEC(N'CREATE UNIQUE INDEX UX_sales_tenders_invoice_type ON dbo.sales_tenders(sales_invoice_id,tender_type)');
 ELSE
 BEGIN
  IF COL_LENGTH(N'dbo.sales_tenders',N'tender_type_key') IS NULL
   ALTER TABLE dbo.sales_tenders ADD tender_type_key AS UPPER(tender_type) PERSISTED;
  EXEC(N'CREATE UNIQUE INDEX UX_sales_tenders_invoice_type ON dbo.sales_tenders(sales_invoice_id,tender_type_key)');
 END
END
-- <<< C2_CONTROL_TENDER end

-- >>> D_SNAPSHOT begin
-- <<< D_SNAPSHOT end

-- >>> E_ENRICHMENT begin
-- <<< E_ENRICHMENT end

-- >>> G_RESTATEMENT begin
-- <<< G_RESTATEMENT end

