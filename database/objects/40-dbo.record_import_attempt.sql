CREATE OR ALTER PROCEDURE dbo.record_import_attempt
 @name nvarchar(260),@hash char(64)=NULL,@report varchar(40)=NULL,@store varchar(30)=NULL,
 @start date=NULL,@end date=NULL,@outcome varchar(40),@rows int,@new int,@present int,@conflicts int,
 @diagnostics nvarchar(max),
 @failure_code varchar(80)=NULL,@failure_stage varchar(12)=NULL,@failure_message nvarchar(1000)=NULL,
 @sql_error int=NULL,@exception_type varchar(120)=NULL,@batch uniqueidentifier=NULL,
 @commit_state varchar(12)=NULL,@evidence varchar(16)=NULL,@summary nvarchar(max)=NULL,@issues nvarchar(max)=NULL
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_store_manager'),0)<>1 AND COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51420,'Owner or Store Manager permission is required.',1;
 IF @name LIKE N'%\%' OR @name LIKE N'%/%' OR @name LIKE N'%:%' OR ISJSON(@diagnostics)<>1
    OR (@summary IS NOT NULL AND ISJSON(@summary)<>1)
    OR (@issues IS NOT NULL AND (ISJSON(@issues)<>1 OR LEFT(LTRIM(@issues),1)<>N'['))
  THROW 51422,'Provide a file name and valid safe diagnostics.',1;
 IF @outcome NOT IN('Imported','empty export','Duplicate','Duplicate content','Already present','Failed',
    'Unknown layout','Not needed','Cancelled','Imported (changes pending)','Held','Restatement pending','Re-decided')
  THROW 51422,'Unsupported import outcome.',1;
 DECLARE @list TABLE(n int PRIMARY KEY,severity varchar(12),code varchar(80),block_no smallint,source_row_number int,
   column_name nvarchar(128),document_ref nvarchar(200),message nvarchar(500),occurrences int);
 IF @issues IS NOT NULL
  INSERT @list(n,severity,code,block_no,source_row_number,column_name,document_ref,message,occurrences)
  SELECT CONVERT(int,j.[key])+1,i.severity,i.code,i.block_no,i.source_row_number,i.column_name,i.document_ref,i.message,
    COALESCE(i.occurrences,1)
  FROM OPENJSON(@issues) j
  CROSS APPLY OPENJSON(j.value) WITH(severity varchar(12) '$.severity',code varchar(80) '$.code',block_no smallint '$.block',
    source_row_number int '$.row',column_name nvarchar(128) '$.column',document_ref nvarchar(200) '$.document',
    message nvarchar(500) '$.message',occurrences int '$.occurrences') i;
 IF EXISTS(SELECT 1 FROM @list WHERE code IS NULL OR message IS NULL OR occurrences<1
    OR severity IS NULL OR severity NOT IN('INFORMATION','WARNING','BLOCKER'))
  THROW 51422,'Provide a file name and valid safe diagnostics.',1;
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
   MAX(CASE r.severity WHEN 'BLOCKER' THEN 3 WHEN 'WARNING' THEN 2 ELSE 1 END),MIN(r.n),SUM(r.occurrences)
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
   CASE a.severity_rank WHEN 3 THEN 'BLOCKER' WHEN 2 THEN 'WARNING' ELSE 'INFORMATION' END,
   a.code,(SELECT TOP(1) x.message FROM @list x WHERE x.code=a.code AND x.n>@kept ORDER BY x.n),a.occurrences
 FROM @agg a WHERE a.rk<=@last;
 INSERT dbo.import_attempt_issues(import_attempt_id,seq,severity,code,message,occurrences)
 SELECT @attempt,200,
   CASE MAX(a.severity_rank) WHEN 3 THEN 'BLOCKER' WHEN 2 THEN 'WARNING' ELSE 'INFORMATION' END,
   'ISSUES_TRUNCATED',N'More issue codes than an attempt keeps; their occurrences are counted together.',SUM(a.occurrences)
 FROM @agg a WHERE a.rk>@last
 HAVING COUNT(*)>0;
 COMMIT TRANSACTION;
END
-- >>> GRANTS
DENY EXECUTE ON dbo.record_import_attempt TO etp_viewer;
GRANT EXECUTE ON dbo.record_import_attempt TO etp_owner,etp_store_manager;
