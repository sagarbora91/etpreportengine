-- Import engine fixes for release 1.9.3 (CONSOLIDATED-AND-RAW-IMPORT-SPEC.md section 5.1).
-- The migration runner applies this entire script in one transaction, so it has no BEGIN or COMMIT.
-- The pre-checks THROW before anything changes; every later statement is idempotent
-- (COL_LENGTH/OBJECT_ID guards, CREATE OR ALTER). A statement that uses a column added earlier in
-- this script, and every CREATE PROCEDURE, VIEW or TRIGGER, runs through EXEC(N'...').
-- New error numbers are 51700-51799.
-- The PRECHECK_* sections also run on their own before ANY pending migration applies (the runner's
-- pre-flight, MigrationRunner), so a refusal leaves a 0037 database at 0037 rather than at Tally's
-- 0040, where 1.9.2 could no longer open it. There they read the schema as it is before every pending
-- migration, which may predate the sales tables: each check is guarded by OBJECT_ID and only reads.
-- Each section changes only what lies between its own begin and end markers.
SET XACT_ABORT ON;

-- >>> PRECHECK_FY begin
-- Every invoice is keyed by the financial year of its own date (OD-1, IF-019); ETP's INVOICEYEAR is only a label.
-- The refusal names the first invoices and counts those whose financial year already has a header of its own (an
-- R022 header keyed by the label beside the R025 header keyed by the financial year): UQ_sales_invoices_natural
-- forbids re-keying those, so scripts/repair-invoice-financial-year.sql merges them instead (see its header).
-- FOR XML PATH, not STRING_AGG, so the list compiles at any database compatibility level.
IF OBJECT_ID(N'dbo.sales_invoices',N'U') IS NOT NULL
BEGIN
  DECLARE @fy_count int, @fy_twins int, @fy_list nvarchar(max), @fy_message nvarchar(2048);
  SELECT @fy_count=COUNT(*), @fy_twins=COUNT(t.sales_invoice_id)
  FROM dbo.sales_invoices i WITH(UPDLOCK,HOLDLOCK)
  LEFT JOIN dbo.sales_invoices t ON t.store_code=i.store_code AND t.document_number=i.document_number
    AND t.invoice_year=YEAR(i.transaction_date)+CASE WHEN MONTH(i.transaction_date)>=4 THEN 1 ELSE 0 END
  WHERE i.invoice_year<>YEAR(i.transaction_date)+CASE WHEN MONTH(i.transaction_date)>=4 THEN 1 ELSE 0 END;
  IF @fy_count>0
  BEGIN
    SET @fy_list=STUFF((SELECT TOP(5) N'; '+CONCAT(i.store_code,N' ',i.document_number,N' dated ',CONVERT(char(10),i.transaction_date,23),
        N' keyed ',i.invoice_year,N' not ',YEAR(i.transaction_date)+CASE WHEN MONTH(i.transaction_date)>=4 THEN 1 ELSE 0 END)
      FROM dbo.sales_invoices i
      WHERE i.invoice_year<>YEAR(i.transaction_date)+CASE WHEN MONTH(i.transaction_date)>=4 THEN 1 ELSE 0 END
      ORDER BY i.store_code,i.transaction_date,i.document_number
      FOR XML PATH(''),TYPE).value('.','nvarchar(max)'),1,2,N'');
    SET @fy_message=LEFT(CONCAT(N'Some invoices carry a year other than the financial year of their date. ',@fy_count,
      CASE WHEN @fy_count=1 THEN N' invoice' ELSE N' invoices' END,
      CASE WHEN @fy_twins>0 THEN CONCAT(N', ',@fy_twins,N' of them beside a header already keyed by that financial year') ELSE N'' END,
      CASE WHEN @fy_count>5 THEN N'; the first five: ' ELSE N': ' END,@fy_list,
      N'. Run scripts/check-import-upgrade.sql to list them all, then scripts/repair-invoice-financial-year.sql (read its header) before upgrading.'),2048);
    THROW 51700,@fy_message,1;
  END;
END;
-- <<< PRECHECK_FY end

-- >>> PRECHECK_CONTROLS_TENDERS begin
-- Section C2 makes one revenue control per invoice and one tender per invoice and type unique (IF-006).
IF OBJECT_ID(N'dbo.sales_invoice_controls',N'U') IS NOT NULL
 IF EXISTS(SELECT 1 FROM dbo.sales_invoice_controls WITH(UPDLOCK,HOLDLOCK)
  GROUP BY sales_invoice_id HAVING COUNT(*)>1)
  THROW 51701,'An invoice has more than one revenue control. Review before upgrading.',1;
IF OBJECT_ID(N'dbo.sales_tenders',N'U') IS NOT NULL
 IF EXISTS(SELECT 1 FROM dbo.sales_tenders WITH(UPDLOCK,HOLDLOCK)
  GROUP BY sales_invoice_id,UPPER(tender_type) HAVING COUNT(*)>1)
  THROW 51702,'An invoice has the same tender type twice. Review before upgrading.',1;
-- <<< PRECHECK_CONTROLS_TENDERS end

-- >>> A_DIAGNOSTICS begin
-- A. Failure diagnostics (IF-017). Every attempt keeps why it failed: code, stage, a message the code
-- wrote, the SQL error, the commit and evidence state and counts. Its issues go to their own table,
-- at most 200 rows per attempt. Every new column is NULL-able, so attempts recorded before stay valid.
IF COL_LENGTH(N'dbo.import_attempts',N'failure_code') IS NULL
 ALTER TABLE dbo.import_attempts ADD failure_code varchar(80) NULL;
IF COL_LENGTH(N'dbo.import_attempts',N'failure_stage') IS NULL
 ALTER TABLE dbo.import_attempts ADD failure_stage varchar(12) NULL;
IF COL_LENGTH(N'dbo.import_attempts',N'failure_message') IS NULL
 ALTER TABLE dbo.import_attempts ADD failure_message nvarchar(1000) NULL;
IF COL_LENGTH(N'dbo.import_attempts',N'sql_error_number') IS NULL
 ALTER TABLE dbo.import_attempts ADD sql_error_number int NULL;
IF COL_LENGTH(N'dbo.import_attempts',N'exception_type') IS NULL
 ALTER TABLE dbo.import_attempts ADD exception_type varchar(120) NULL;
IF COL_LENGTH(N'dbo.import_attempts',N'source_sha256') IS NULL
 ALTER TABLE dbo.import_attempts ADD source_sha256 char(64) NULL;
IF COL_LENGTH(N'dbo.import_attempts',N'import_batch_id') IS NULL
 ALTER TABLE dbo.import_attempts ADD import_batch_id uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.import_attempts',N'commit_state') IS NULL
 ALTER TABLE dbo.import_attempts ADD commit_state varchar(12) NULL;
IF COL_LENGTH(N'dbo.import_attempts',N'evidence_state') IS NULL
 ALTER TABLE dbo.import_attempts ADD evidence_state varchar(16) NULL;
IF COL_LENGTH(N'dbo.import_attempts',N'summary_json') IS NULL
 ALTER TABLE dbo.import_attempts ADD summary_json nvarchar(max) NULL;
IF OBJECT_ID(N'dbo.CK_import_attempts_failure_stage',N'C') IS NULL
 EXEC(N'ALTER TABLE dbo.import_attempts ADD CONSTRAINT CK_import_attempts_failure_stage
  CHECK(failure_stage IN(''READ'',''MATCH'',''SOURCE'',''SCOPE'',''PLAN'',''APPLY'',''COMMIT'',''EVIDENCE'',''RECORD''))');
IF OBJECT_ID(N'dbo.CK_import_attempts_commit_state',N'C') IS NULL
 EXEC(N'ALTER TABLE dbo.import_attempts ADD CONSTRAINT CK_import_attempts_commit_state
  CHECK(commit_state IN(''ROLLED_BACK'',''COMMITTED'',''UNKNOWN''))');
IF OBJECT_ID(N'dbo.CK_import_attempts_evidence_state',N'C') IS NULL
 EXEC(N'ALTER TABLE dbo.import_attempts ADD CONSTRAINT CK_import_attempts_evidence_state
  CHECK(evidence_state IN(''RETAINED'',''ALREADY_HELD'',''NOT_RETAINED'',''NOT_ATTEMPTED''))');
IF OBJECT_ID(N'dbo.CK_import_attempts_summary_json',N'C') IS NULL
 EXEC(N'ALTER TABLE dbo.import_attempts ADD CONSTRAINT CK_import_attempts_summary_json CHECK(ISJSON(summary_json)=1)');

-- An issue keeps its location, code and a message the code wrote; document_ref holds only a
-- document number, date and product code. Never a cell value.
IF OBJECT_ID(N'dbo.import_attempt_issues',N'U') IS NULL
 CREATE TABLE dbo.import_attempt_issues (
  import_attempt_id bigint NOT NULL CONSTRAINT FK_import_attempt_issues_attempt REFERENCES dbo.import_attempts(import_attempt_id),
  seq int NOT NULL,
  severity varchar(12) NOT NULL,
  code varchar(80) NOT NULL,
  block_no smallint NULL,
  source_row_number int NULL,
  column_name nvarchar(128) NULL,
  document_ref nvarchar(200) NULL,
  message nvarchar(500) NOT NULL,
  occurrences int NOT NULL CONSTRAINT DF_import_attempt_issues_occurrences DEFAULT(1),
  CONSTRAINT PK_import_attempt_issues PRIMARY KEY(import_attempt_id,seq),
  CONSTRAINT CK_import_attempt_issues_seq CHECK(seq BETWEEN 1 AND 200),
  CONSTRAINT CK_import_attempt_issues_severity CHECK(severity IN('INFORMATION','WARNING','BLOCKER')),
  CONSTRAINT CK_import_attempt_issues_occurrences CHECK(occurrences>=1)
 );

-- Today's parameters (0028) plus the diagnostics, all optional, so older callers keep working.
-- @hash is now stored as well as used to find the file. The attempt and its issues are written together.
EXEC(N'CREATE OR ALTER PROCEDURE dbo.record_import_attempt
 @name nvarchar(260),@hash char(64)=NULL,@report varchar(40)=NULL,@store varchar(30)=NULL,
 @start date=NULL,@end date=NULL,@outcome varchar(40),@rows int,@new int,@present int,@conflicts int,
 @diagnostics nvarchar(max),
 @failure_code varchar(80)=NULL,@failure_stage varchar(12)=NULL,@failure_message nvarchar(1000)=NULL,
 @sql_error int=NULL,@exception_type varchar(120)=NULL,@batch uniqueidentifier=NULL,
 @commit_state varchar(12)=NULL,@evidence varchar(16)=NULL,@summary nvarchar(max)=NULL,@issues nvarchar(max)=NULL
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @name LIKE N''%\%'' OR @name LIKE N''%/%'' OR @name LIKE N''%:%'' OR ISJSON(@diagnostics)<>1
    OR (@summary IS NOT NULL AND ISJSON(@summary)<>1)
    OR (@issues IS NOT NULL AND (ISJSON(@issues)<>1 OR LEFT(LTRIM(@issues),1)<>N''[''))
  THROW 51422,''Provide a file name and valid safe diagnostics.'',1;
 IF @outcome NOT IN(''Imported'',''empty export'',''Duplicate'',''Duplicate content'',''Already present'',''Failed'',
    ''Unknown layout'',''Not needed'',''Cancelled'',''Imported (changes pending)'',''Held'',''Restatement pending'',''Re-decided'')
  THROW 51422,''Unsupported import outcome.'',1;
 DECLARE @list TABLE(n int PRIMARY KEY,severity varchar(12),code varchar(80),block_no smallint,source_row_number int,
   column_name nvarchar(128),document_ref nvarchar(200),message nvarchar(500),occurrences int);
 IF @issues IS NOT NULL
  INSERT @list(n,severity,code,block_no,source_row_number,column_name,document_ref,message,occurrences)
  SELECT CONVERT(int,j.[key])+1,i.severity,i.code,i.block_no,i.source_row_number,i.column_name,i.document_ref,i.message,
    COALESCE(i.occurrences,1)
  FROM OPENJSON(@issues) j
  CROSS APPLY OPENJSON(j.value) WITH(severity varchar(12) ''$.severity'',code varchar(80) ''$.code'',block_no smallint ''$.block'',
    source_row_number int ''$.row'',column_name nvarchar(128) ''$.column'',document_ref nvarchar(200) ''$.document'',
    message nvarchar(500) ''$.message'',occurrences int ''$.occurrences'') i;
 IF EXISTS(SELECT 1 FROM @list WHERE code IS NULL OR message IS NULL OR occurrences<1
    OR severity IS NULL OR severity NOT IN(''INFORMATION'',''WARNING'',''BLOCKER''))
  THROW 51422,''Provide a file name and valid safe diagnostics.'',1;
 DECLARE @file bigint,@attempt bigint;
 SELECT TOP(1) @file=f.import_file_id FROM dbo.import_files f
 WHERE f.source_sha256=@hash AND f.report_code=@report AND f.store_code=@store
   AND f.period_start=@start AND f.period_end=@end AND f.data_truth_version=1
 ORDER BY f.import_file_id DESC;
 -- Above 200 issues the first ones are kept as they are and the rest become one row per code with
 -- its occurrences. A row is reserved for every code, so the total stays within 200. Above 200 codes
 -- the codes past the 199th share the last row, ISSUES_TRUNCATED, which keeps their occurrences.
 DECLARE @total int=(SELECT COUNT(*) FROM @list),@codes int=(SELECT COUNT(DISTINCT code) FROM @list);
 DECLARE @kept int=CASE WHEN @total<=200 THEN @total WHEN @codes>=200 THEN 0 ELSE 200-@codes END;
 DECLARE @agg TABLE(rk int PRIMARY KEY,code varchar(80),severity_rank int,first_n int,occurrences int);
 INSERT @agg(rk,code,severity_rank,first_n,occurrences)
 SELECT ROW_NUMBER() OVER(ORDER BY MIN(r.n)),r.code,
   MAX(CASE r.severity WHEN ''BLOCKER'' THEN 3 WHEN ''WARNING'' THEN 2 ELSE 1 END),MIN(r.n),SUM(r.occurrences)
 FROM @list r WHERE r.n>@kept GROUP BY r.code;
 DECLARE @last int=CASE WHEN (SELECT COUNT(*) FROM @agg)>200-@kept THEN 199-@kept ELSE 200-@kept END;
 BEGIN TRANSACTION;
 INSERT dbo.import_attempts(import_file_id,file_name,report_code,store_code,period_start,period_end,outcome,
   rows_processed,new_rows,already_present_rows,conflict_rows,diagnostics_json,
   failure_code,failure_stage,failure_message,sql_error_number,exception_type,source_sha256,import_batch_id,
   commit_state,evidence_state,summary_json)
 VALUES(@file,@name,@report,@store,@start,@end,@outcome,@rows,@new,@present,@conflicts,@diagnostics,
   @failure_code,@failure_stage,@failure_message,@sql_error,@exception_type,@hash,@batch,@commit_state,@evidence,@summary);
 SET @attempt=SCOPE_IDENTITY();
 INSERT dbo.import_attempt_issues(import_attempt_id,seq,severity,code,block_no,source_row_number,column_name,document_ref,message,occurrences)
 SELECT @attempt,n,severity,code,block_no,source_row_number,column_name,document_ref,message,occurrences
 FROM @list WHERE n<=@kept;
 INSERT dbo.import_attempt_issues(import_attempt_id,seq,severity,code,message,occurrences)
 SELECT @attempt,@kept+a.rk,
   CASE a.severity_rank WHEN 3 THEN ''BLOCKER'' WHEN 2 THEN ''WARNING'' ELSE ''INFORMATION'' END,
   a.code,(SELECT TOP(1) x.message FROM @list x WHERE x.code=a.code AND x.n>@kept ORDER BY x.n),a.occurrences
 FROM @agg a WHERE a.rk<=@last;
 INSERT dbo.import_attempt_issues(import_attempt_id,seq,severity,code,message,occurrences)
 SELECT @attempt,200,
   CASE MAX(a.severity_rank) WHEN 3 THEN ''BLOCKER'' WHEN 2 THEN ''WARNING'' ELSE ''INFORMATION'' END,
   ''ISSUES_TRUNCATED'',N''More issue codes than an attempt keeps; their occurrences are counted together.'',SUM(a.occurrences)
 FROM @agg a WHERE a.rk>@last
 HAVING COUNT(*)>0;
 COMMIT TRANSACTION;
END');
GRANT SELECT ON dbo.import_attempt_issues TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.import_attempt_issues TO etp_viewer,etp_store_manager,etp_owner,etp_automation;
GRANT EXECUTE ON dbo.record_import_attempt TO etp_store_manager,etp_owner;
DENY EXECUTE ON dbo.record_import_attempt TO etp_viewer;
-- <<< A_DIAGNOSTICS end

-- >>> B_EVIDENCE begin
-- B. Evidence inside the database (IF-023, Owner decision OD-2). The import transaction stores the
-- bytes the reader parsed, once per SHA-256. A file's evidence is present when this table holds its own
-- import_files.source_sha256, so no link table is needed. Viewers and store managers cannot read the
-- bytes; v_import_source_evidence shows every role what is held, without them.
IF OBJECT_ID(N'dbo.import_source_content',N'U') IS NULL
CREATE TABLE dbo.import_source_content(
 source_sha256 char(64) NOT NULL CONSTRAINT PK_import_source_content PRIMARY KEY,
 size_bytes bigint NOT NULL,
 content varbinary(max) NOT NULL,
 first_import_file_id bigint NULL CONSTRAINT FK_import_source_content_file REFERENCES dbo.import_files(import_file_id),
 retained_utc datetime2(3) NOT NULL CONSTRAINT DF_import_source_content_utc DEFAULT SYSUTCDATETIME(),
 retained_by nvarchar(200) NOT NULL CONSTRAINT DF_import_source_content_by DEFAULT SUSER_SNAME(),
 CONSTRAINT CK_import_source_content_hash CHECK(source_sha256 COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
 CONSTRAINT CK_import_source_content_size CHECK(size_bytes>=0 AND DATALENGTH(content)=size_bytes)
);
DENY SELECT ON dbo.import_source_content TO etp_viewer,etp_store_manager;
DENY INSERT,UPDATE,DELETE ON dbo.import_source_content TO etp_viewer,etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER VIEW dbo.v_import_source_evidence AS
SELECT source_sha256,size_bytes,first_import_file_id,retained_utc FROM dbo.import_source_content');
GRANT SELECT ON dbo.v_import_source_evidence TO etp_viewer,etp_store_manager,etp_owner;

-- @state is RETAINED when this call stored the bytes and ALREADY_HELD when they were held already.
-- The bytes must hash to @hash and belong to an imported file, so nothing else can be stored here.
EXEC(N'CREATE OR ALTER PROCEDURE dbo.retain_import_source
 @hash char(64),@size bigint,@content varbinary(max),@file bigint,@state varchar(16)=NULL OUTPUT
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF @hash IS NULL OR LEN(@hash)<>64 OR @hash COLLATE Latin1_General_100_BIN2 LIKE ''%[^0-9a-f]%''
  THROW 51750,''The source file hash must be 64 lowercase hexadecimal characters.'',1;
 IF @content IS NULL OR @size IS NULL OR DATALENGTH(@content)<>@size
    OR HASHBYTES(''SHA2_256'',@content)<>CONVERT(binary(32),@hash,2)
  THROW 51751,''The source file bytes do not match their hash and size.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files WHERE import_file_id=@file AND source_sha256=@hash)
  THROW 51752,''Source files are kept only for an import of the same file.'',1;
 -- No HOLDLOCK: a key-range probe for a new hash would lock the gap until the import commits and
  -- serialise imports for other stores. The primary key still stops a second copy of the same bytes.
 INSERT dbo.import_source_content(source_sha256,size_bytes,content,first_import_file_id)
 SELECT @hash,@size,@content,@file
 WHERE NOT EXISTS(SELECT 1 FROM dbo.import_source_content WHERE source_sha256=@hash);
 SET @state=CASE WHEN @@ROWCOUNT=1 THEN ''RETAINED'' ELSE ''ALREADY_HELD'' END;
END');
GRANT EXECUTE ON dbo.retain_import_source TO etp_store_manager,etp_owner;
DENY EXECUTE ON dbo.retain_import_source TO etp_viewer;
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
-- D. Snapshot source and line (IF-020). Every snapshot fact names the report it came from, so an R010
-- and an R011 reading of the same store-day are separate identities, and repeated identical rows each
-- keep their own line_seq instead of collapsing into ALREADY_PRESENT. Readers use the view below.
IF COL_LENGTH('dbo.stock_snapshots','source_report_code') IS NULL
 ALTER TABLE dbo.stock_snapshots ADD source_report_code varchar(30) NULL;
IF COL_LENGTH('dbo.stock_snapshots','line_seq') IS NULL
 ALTER TABLE dbo.stock_snapshots ADD line_seq int NOT NULL CONSTRAINT DF_stock_snapshots_line_seq DEFAULT(1);
IF COL_LENGTH('dbo.stock_snapshots','item_discriminator') IS NULL
 ALTER TABLE dbo.stock_snapshots ADD item_discriminator AS COALESCE(source_uid,batch_number,ean,N'') PERSISTED;

-- An R010 row standing in for an R011 row that the backfill below could not rebuild (review 1.9.3). The view
-- shows it on a store-day read from R011 while its R011 file is current and no R011 row of the item is stored.
-- Only this migration writes it; a restatement that deletes the R010 row deletes its entry.
IF OBJECT_ID(N'dbo.stock_snapshot_r011_fallbacks',N'U') IS NULL
 CREATE TABLE dbo.stock_snapshot_r011_fallbacks(
  stock_snapshot_id bigint NOT NULL CONSTRAINT FK_stock_snapshot_r011_fallbacks_snapshot
   REFERENCES dbo.stock_snapshots(stock_snapshot_id) ON DELETE CASCADE,
  import_file_id bigint NOT NULL,
  CONSTRAINT PK_stock_snapshot_r011_fallbacks PRIMARY KEY(stock_snapshot_id,import_file_id));

-- The backfill must also work for finalised days. 0017 disabled every other fact guard but not this one
-- (0009); DDL is transactional, so a failed migration rolls the trigger state back as well.
-- The backfill runs once: until source_report_code is NOT NULL.
DISABLE TRIGGER dbo.trg_stock_snapshots_protect_locked ON dbo.stock_snapshots;
EXEC(N'IF COLUMNPROPERTY(OBJECT_ID(N''dbo.stock_snapshots''),''source_report_code'',''AllowsNull'')=1
BEGIN
 UPDATE s SET source_report_code=CASE WHEN l.source_record_type=''R010_SNAPSHOT'' THEN ''R010'' ELSE ''CLOSING_STOCK'' END
 FROM dbo.stock_snapshots s JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id
 WHERE s.source_report_code IS NULL;
 -- Before 0041 the identity had no source or line, so an R011 row could be logged against another row and not
 -- stored: ALREADY_PRESENT or CONFLICT against the R010 row of a store-day where R010 was imported first, or
 -- ALREADY_PRESENT against an earlier row of its own file (a repeated item). The view reads R011 only for a
 -- store-day that has R011 rows, so those rows are stored now, as Closing Stock under their own lineage:
 --  * from the earliest current R011 file that logged the item, and only while no row of the item was stored by
 --    another file, so a later re-import of the same rows is not counted twice;
 --  * with the values the R011 row had: its own landing row (dbo.etp_r011) or a stored row of the item, each
 --    used only when it hashes exactly as persist_stock_snapshot hashed the R011 row (review 1.9.3);
 --  * a row that cannot be rebuilt (a conflict committed before 15 Sep 2026 kept only its hash) is not lost: its
 --    item keeps the R010 reading it had before the upgrade, through dbo.stock_snapshot_r011_fallbacks, until
 --    an R011 row of the item is stored or its R011 file is superseded. The PRINT below counts these rows and
 --    scripts\check-import-upgrade.sql (SNAPSHOT_R011_BACKFILL) lists them; restating that R011 file stores them.
 SELECT o.source_lineage_id,o.import_file_id,o.business_identity,o.content_sha256,l.sheet_name,l.source_row_number,
        CONVERT(varchar(30),LEFT(o.business_identity,CHARINDEX(N''/'',o.business_identity)-1)) store_code,
        TRY_CONVERT(date,SUBSTRING(o.business_identity,CHARINDEX(N''/'',o.business_identity)+1,10),23) snapshot_date,
        DENSE_RANK() OVER(PARTITION BY o.business_identity ORDER BY o.import_file_id) file_rank,
        ROW_NUMBER() OVER(PARTITION BY o.source_lineage_id ORDER BY o.import_row_outcome_id) lineage_rank,
        CONVERT(bigint,NULL) landing_row,CONVERT(bigint,NULL) matched_row
 INTO #r011
 FROM dbo.import_row_outcomes o
 JOIN dbo.source_lineage l ON l.source_lineage_id=o.source_lineage_id AND l.source_record_type=''CLOSING_STOCK''
 JOIN dbo.import_files f ON f.import_file_id=o.import_file_id AND f.is_superseded=0
 WHERE o.outcome IN(''ALREADY_PRESENT'',''CONFLICT'') AND CHARINDEX(N''/'',o.business_identity)>1
   AND NOT EXISTS(SELECT 1 FROM dbo.stock_snapshots c WHERE c.source_lineage_id=o.source_lineage_id);
 -- A Closing Stock row of the item that this file did not insert as NEW belongs to another file (a promotion
 -- relinks an earlier file''s rows to the later file without a NEW outcome there).
 DELETE r FROM #r011 r WHERE r.file_rank>1 OR r.lineage_rank>1 OR r.snapshot_date IS NULL
   OR EXISTS(SELECT 1 FROM dbo.stock_snapshots x
     WHERE x.store_code=r.store_code AND x.snapshot_date=r.snapshot_date AND x.source_report_code=''CLOSING_STOCK''
       AND CONCAT(x.store_code,N''/'',x.snapshot_date,N''/'',x.product_code,N''/'',x.item_discriminator)=r.business_identity
       AND NOT EXISTS(SELECT 1 FROM dbo.import_row_outcomes n WHERE n.import_file_id=r.import_file_id AND n.outcome=''NEW''
                      AND n.source_lineage_id=x.source_lineage_id));
 UPDATE r SET landing_row=(SELECT MIN(e.etp_row_id) FROM dbo.etp_r011 e
   JOIN dbo.source_lineage el ON el.source_lineage_id=e.source_lineage_id
   WHERE e.import_file_id=r.import_file_id AND el.sheet_name=r.sheet_name AND el.source_row_number=r.source_row_number
     AND CONCAT(e.store_code,N''/'',e.snapshot_date,N''/'',e.product_code,N''/'',COALESCE(e.source_uid,e.batch_number,e.ean,N''''))
         COLLATE Latin1_General_100_BIN2=r.business_identity COLLATE Latin1_General_100_BIN2
     AND LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(ISNULL(e.ean,N''''),N''|'',ISNULL(e.brand_code,N''''),N''||'',
         ISNULL(e.cluster,N''''),N''|'',ISNULL(e.gender,N''''),N''|'',ISNULL(e.batch_number,N''''),N''|'',ISNULL(e.source_uid,N''''),N''|'',
         e.quantity,N''|'',ISNULL(e.unit_cost,0),N''|'',ISNULL(e.total_cost,0))),2))=r.content_sha256)
 FROM #r011 r;
 UPDATE r SET matched_row=(SELECT MIN(x.stock_snapshot_id) FROM dbo.stock_snapshots x
   WHERE x.store_code=r.store_code AND x.snapshot_date=r.snapshot_date
     AND CONCAT(x.store_code,N''/'',x.snapshot_date,N''/'',x.product_code,N''/'',x.item_discriminator)=r.business_identity
     AND LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(ISNULL(x.ean,N''''),N''|'',ISNULL(x.brand_code,N''''),N''|'',
         ISNULL(x.brand_name,N''''),N''|'',ISNULL(x.cluster,N''''),N''|'',ISNULL(x.gender,N''''),N''|'',ISNULL(x.batch_number,N''''),N''|'',
         ISNULL(x.source_uid,N''''),N''|'',x.quantity,N''|'',ISNULL(x.unit_cost,0),N''|'',ISNULL(x.total_cost,0))),2))=r.content_sha256)
 FROM #r011 r WHERE r.landing_row IS NULL;
 -- A row that cannot be rebuilt keeps the R010 reading of its item visible (dbo.stock_snapshot_r011_fallbacks).
 DECLARE @unrebuilt int=(SELECT COUNT(*) FROM #r011 WHERE landing_row IS NULL AND matched_row IS NULL),@rebuilt int;
 INSERT dbo.stock_snapshot_r011_fallbacks(stock_snapshot_id,import_file_id)
 SELECT DISTINCT x.stock_snapshot_id,r.import_file_id FROM #r011 r JOIN dbo.stock_snapshots x
   ON x.store_code=r.store_code AND x.snapshot_date=r.snapshot_date AND x.source_report_code=''R010''
  AND CONCAT(x.store_code,N''/'',x.snapshot_date,N''/'',x.product_code,N''/'',x.item_discriminator)=r.business_identity
 WHERE r.landing_row IS NULL AND r.matched_row IS NULL
   AND NOT EXISTS(SELECT 1 FROM dbo.stock_snapshot_r011_fallbacks k WHERE k.stock_snapshot_id=x.stock_snapshot_id AND k.import_file_id=r.import_file_id);
 INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,ean,brand_code,brand_name,cluster,gender,batch_number,source_uid,quantity,unit_cost,total_cost,source_lineage_id,source_report_code)
 SELECT r.store_code,r.snapshot_date,e.product_code,e.ean,e.brand_code,NULL,e.cluster,e.gender,e.batch_number,e.source_uid,e.quantity,e.unit_cost,e.total_cost,r.source_lineage_id,''CLOSING_STOCK''
 FROM #r011 r JOIN dbo.etp_r011 e ON e.etp_row_id=r.landing_row
 UNION ALL
 SELECT x.store_code,x.snapshot_date,x.product_code,x.ean,x.brand_code,x.brand_name,x.cluster,x.gender,x.batch_number,x.source_uid,x.quantity,x.unit_cost,x.total_cost,r.source_lineage_id,''CLOSING_STOCK''
 FROM #r011 r JOIN dbo.stock_snapshots x ON x.stock_snapshot_id=r.matched_row WHERE r.landing_row IS NULL;
 SET @rebuilt=@@ROWCOUNT;
 IF @rebuilt+@unrebuilt>0
  PRINT CONCAT(N''0041: '',@rebuilt,N'' Closing Stock rows rebuilt from logged import outcomes; '',@unrebuilt,
   N'' could not be rebuilt and keep their BinWise reading until the R011 file is restated. scripts\check-import-upgrade.sql (SNAPSHOT_R011_BACKFILL) lists them.'');
 WITH s AS (SELECT line_seq,ROW_NUMBER() OVER(PARTITION BY store_code,snapshot_date,source_report_code,product_code,item_discriminator
   ORDER BY quantity,unit_cost,total_cost,stock_snapshot_id) n FROM dbo.stock_snapshots)
 UPDATE s SET line_seq=n WHERE n>1;
END');
ENABLE TRIGGER dbo.trg_stock_snapshots_protect_locked ON dbo.stock_snapshots;
IF COLUMNPROPERTY(OBJECT_ID(N'dbo.stock_snapshots'),'source_report_code','AllowsNull')=1
 EXEC(N'ALTER TABLE dbo.stock_snapshots ALTER COLUMN source_report_code varchar(30) NOT NULL');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.stock_snapshots') AND name=N'UX_stock_snapshots_identity')
 EXEC(N'CREATE UNIQUE INDEX UX_stock_snapshots_identity ON dbo.stock_snapshots(store_code,snapshot_date,source_report_code,
  product_code,item_discriminator,line_seq)');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.stock_snapshots') AND name=N'IX_stock_snapshots_source')
 EXEC(N'CREATE INDEX IX_stock_snapshots_source ON dbo.stock_snapshots(store_code,snapshot_date,source_report_code)
  INCLUDE(product_code,quantity,total_cost)');

-- @source NULL (an older caller) is derived from the lineage record type, as the backfill above derives it.
-- A given @source must agree with that record type (51760), so no caller can store a NULL or a wrong source. Conflicts are logged under the source, not R001.
EXEC(N'CREATE OR ALTER PROCEDURE dbo.persist_stock_snapshot
 @store varchar(30),@date date,@product nvarchar(80),@ean nvarchar(80)=NULL,@brand nvarchar(80)=NULL,@brandname nvarchar(200)=NULL,@cluster nvarchar(100)=NULL,@gender nvarchar(50)=NULL,@batch nvarchar(80)=NULL,@uid nvarchar(100)=NULL,@qty decimal(19,4),@unit decimal(19,4)=NULL,@total decimal(19,4)=NULL,@lineage bigint,@source varchar(30)=NULL,@line_seq int=1
AS
BEGIN
 SET NOCOUNT ON; DECLARE @existing bigint,@file bigint,@record varchar(40),@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id,@record=source_record_type FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
 DECLARE @derived varchar(30)=CASE WHEN @record=''R010_SNAPSHOT'' THEN ''R010'' ELSE ''CLOSING_STOCK'' END;
 SET @source=NULLIF(UPPER(LTRIM(RTRIM(@source))),'''');
 IF @source IS NOT NULL AND @source<>@derived THROW 51760,''The stock snapshot source does not match the record type of its lineage.'',1;
 SET @source=@derived;
 SET @identity=CONCAT(@store,N''/'',@date,N''/'',@source,N''/'',@product,N''/'',COALESCE(@uid,@batch,@ean,N''''),N''/#'',@line_seq);
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(ISNULL(@ean,N''''),N''|'',ISNULL(@brand,N''''),N''|'',ISNULL(@brandname,N''''),N''|'',ISNULL(@cluster,N''''),N''|'',ISNULL(@gender,N''''),N''|'',ISNULL(@batch,N''''),N''|'',ISNULL(@uid,N''''),N''|'',@qty,N''|'',ISNULL(@unit,0),N''|'',ISNULL(@total,0))),2));
 SELECT TOP(1) @existing=stock_snapshot_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(ISNULL(ean,N''''),N''|'',ISNULL(brand_code,N''''),N''|'',ISNULL(brand_name,N''''),N''|'',ISNULL(cluster,N''''),N''|'',ISNULL(gender,N''''),N''|'',ISNULL(batch_number,N''''),N''|'',ISNULL(source_uid,N''''),N''|'',quantity,N''|'',ISNULL(unit_cost,0),N''|'',ISNULL(total_cost,0))),2)) FROM dbo.stock_snapshots WHERE store_code=@store AND snapshot_date=@date AND source_report_code=@source AND product_code=@product AND item_discriminator=COALESCE(@uid,@batch,@ean,N'''') AND line_seq=@line_seq ORDER BY stock_snapshot_id;
 IF @existing IS NOT NULL BEGIN IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''ALREADY_PRESENT'',@incoming,N''Identical stock snapshot row already exists.''); ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''Stock snapshot identity exists with different content.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,@source,@identity,@current,@incoming,N''Closing-stock values differ. Review and request a controlled restatement.''); END RETURN; END
 INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,ean,brand_code,brand_name,cluster,gender,batch_number,source_uid,quantity,unit_cost,total_cost,source_lineage_id,source_report_code,line_seq) VALUES(@store,@date,@product,@ean,@brand,@brandname,@cluster,@gender,@batch,@uid,@qty,@unit,@total,@lineage,@source,@line_seq);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''NEW'',@incoming,N''New stock snapshot row imported.'');
END');

-- The rows of the preferred source present for each (store, snapshot date): Closing Stock (R011) first,
-- then BinWise (R010), then any other source. Same result as ranking the sources per store-day and
-- keeping the best rank, written as seeks on IX_stock_snapshots_source so a per-row caller stays cheap.
-- On a Closing Stock day an R010 row also shows when it stands in for an R011 row that 0041 could not rebuild.
EXEC(N'CREATE OR ALTER VIEW dbo.v_stock_snapshots_effective AS
SELECT s.stock_snapshot_id,s.store_code,s.snapshot_date,s.product_code,s.ean,s.brand_code,s.brand_name,s.cluster,s.gender,
       s.batch_number,s.source_uid,s.quantity,s.unit_cost,s.total_cost,s.source_lineage_id,s.source_report_code,s.line_seq,
       s.item_discriminator
FROM dbo.stock_snapshots s
WHERE s.source_report_code=''CLOSING_STOCK''
   OR (NOT EXISTS(SELECT 1 FROM dbo.stock_snapshots c WHERE c.store_code=s.store_code AND c.snapshot_date=s.snapshot_date
                  AND c.source_report_code=''CLOSING_STOCK'')
       AND (s.source_report_code=''R010''
            OR NOT EXISTS(SELECT 1 FROM dbo.stock_snapshots b WHERE b.store_code=s.store_code AND b.snapshot_date=s.snapshot_date
                          AND b.source_report_code=''R010'')))
   OR (s.source_report_code=''R010''
       AND EXISTS(SELECT 1 FROM dbo.stock_snapshot_r011_fallbacks k JOIN dbo.import_files f ON f.import_file_id=k.import_file_id
                  WHERE k.stock_snapshot_id=s.stock_snapshot_id AND f.is_superseded=0)
       AND NOT EXISTS(SELECT 1 FROM dbo.stock_snapshots c WHERE c.store_code=s.store_code AND c.snapshot_date=s.snapshot_date
                      AND c.source_report_code=''CLOSING_STOCK'' AND c.product_code=s.product_code
                      AND c.item_discriminator=s.item_discriminator))');
GRANT SELECT ON dbo.v_stock_snapshots_effective TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_stock_snapshots_effective TO etp_store_manager,etp_viewer;
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

