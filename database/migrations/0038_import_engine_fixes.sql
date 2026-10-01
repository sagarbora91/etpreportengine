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
 -- its occurrences. A row is reserved for every code, so the total stays within 200.
 DECLARE @total int=(SELECT COUNT(*) FROM @list),@codes int=(SELECT COUNT(DISTINCT code) FROM @list);
 DECLARE @kept int=CASE WHEN @total<=200 THEN @total WHEN @codes>=200 THEN 0 ELSE 200-@codes END;
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
 SELECT TOP(200-@kept) @attempt,@kept+ROW_NUMBER() OVER(ORDER BY MIN(r.n)),
   CASE MAX(CASE r.severity WHEN ''BLOCKER'' THEN 3 WHEN ''WARNING'' THEN 2 ELSE 1 END)
     WHEN 3 THEN ''BLOCKER'' WHEN 2 THEN ''WARNING'' ELSE ''INFORMATION'' END,
   r.code,(SELECT TOP(1) x.message FROM @list x WHERE x.code=r.code AND x.n>@kept ORDER BY x.n),SUM(r.occurrences)
 FROM @list r WHERE r.n>@kept
 GROUP BY r.code
 ORDER BY MIN(r.n);
 COMMIT TRANSACTION;
END');
GRANT SELECT ON dbo.import_attempt_issues TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.import_attempt_issues TO etp_viewer,etp_store_manager,etp_owner,etp_automation;
GRANT EXECUTE ON dbo.record_import_attempt TO etp_store_manager,etp_owner;
DENY EXECUTE ON dbo.record_import_attempt TO etp_viewer;
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
-- <<< E_ENRICHMENT end

-- >>> G_RESTATEMENT begin
-- <<< G_RESTATEMENT end

