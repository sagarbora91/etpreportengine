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
-- <<< D_SNAPSHOT end

-- >>> E_ENRICHMENT begin
-- <<< E_ENRICHMENT end

-- >>> G_RESTATEMENT begin
-- <<< G_RESTATEMENT end

