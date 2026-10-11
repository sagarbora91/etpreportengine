-- 1.11.0, lane L1 (sql). Self-diagnostics, part 2: the health check (design SELF-DIAGNOSTICS-DESIGN-1.11.0.md
-- sections 5 and 6.2; procedure contract docs\design\SELF-DIAGNOSTICS-PROCEDURES.md; decisions 33 and 36).
-- Requires 0054 (schema diag and its helper functions).
--
-- A run is one row of diag.health_check_runs and one row per check line in diag.health_check_results; the
-- Owner's accepted Warnings are diag.health_check_known (at most 90 days). Texts are checked again here: check ids,
-- finding codes, tokens and numbers only in the detail; the explanation is plain text without control characters.
--
-- Retention (decision 36, Q14): runs and their results 400 days; Known rows until their date. Both are pruned by
-- diag.record_health_check_run, in its transaction.
--
-- diag.load_role_permissions (SEC-01..03) runs as diag_catalog_reader, a user WITHOUT LOGIN that holds only
-- VIEW DEFINITION, instead of EXECUTE AS OWNER as the contract first said: OWNER is dbo, and on a database restored
-- from another PC dbo can map to a login that does not exist there, which makes every EXECUTE AS OWNER module fail
-- (15517). The reader cannot sign in, nobody but db_owner can impersonate it, and the procedure has no parameters
-- and a fixed result shape limited to the four etp roles. Member names are returned only to an Owner; the Owner
-- grant-option check (server scope) is made in the caller's own context and is -1 when the caller cannot see it.
--
-- Errors 51820-51839 (curated messages in DesktopFriendlyError.ProductErrors, lane L8):
--   51820 malformed run JSON   51821 more than 80 results   51822 Owner only
--   51823 Known: bad check id / finding code, until date in the past or more than 90 days ahead, reason missing or not safe
--   51824 run not found
-- The migration runner applies this script in one transaction: no BEGIN, COMMIT or GO.
SET XACT_ABORT ON;

EXEC(N'CREATE TABLE diag.health_check_runs
(
 health_check_run_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_health_check_runs PRIMARY KEY,
 trigger_kind varchar(8) COLLATE Latin1_General_100_BIN2 NOT NULL,
 business_date date NOT NULL,
 started_utc datetime2(3) NOT NULL,
 completed_utc datetime2(3) NOT NULL,
 duration_ms int NOT NULL,
 app_version varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
 machine_name varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
 run_by_role varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
 overall varchar(8) COLLATE Latin1_General_100_BIN2 NOT NULL,
 pass_count smallint NOT NULL,
 warning_count smallint NOT NULL,
 fail_count smallint NOT NULL,
 known_count smallint NOT NULL,
 skipped_count smallint NOT NULL,
 export_file_name nvarchar(120) COLLATE Latin1_General_100_BIN2 NULL,
 CONSTRAINT CK_health_check_runs_trigger CHECK(trigger_kind IN(''Manual'',''Nightly'')),
 CONSTRAINT CK_health_check_runs_overall CHECK(overall IN(''Pass'',''Warning'',''Fail'')),
 CONSTRAINT CK_health_check_runs_role CHECK(run_by_role IN(''Owner'',''StoreManager'',''Viewer'',''Automation'',''Unknown'')),
 CONSTRAINT CK_health_check_runs_times CHECK(started_utc<=completed_utc AND duration_ms>=0),
 CONSTRAINT CK_health_check_runs_counts CHECK(pass_count>=0 AND warning_count>=0 AND fail_count>=0 AND known_count>=0 AND skipped_count>=0),
 CONSTRAINT CK_health_check_runs_tokens CHECK(machine_name NOT LIKE ''%[^A-Za-z0-9._:<>+-]%''
   AND (export_file_name IS NULL OR export_file_name NOT LIKE N''%[^A-Za-z0-9._:<>+-]%''))
);');
EXEC(N'CREATE INDEX IX_health_check_runs_recent ON diag.health_check_runs(trigger_kind,started_utc DESC)');

EXEC(N'CREATE TABLE diag.health_check_results
(
 health_check_run_id bigint NOT NULL CONSTRAINT FK_health_check_results_run REFERENCES diag.health_check_runs(health_check_run_id),
 check_id varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
 area varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
 outcome varchar(8) COLLATE Latin1_General_100_BIN2 NOT NULL,
 finding_code varchar(60) COLLATE Latin1_General_100_BIN2 NOT NULL,
 explanation nvarchar(400) NOT NULL,
 fix_task_id varchar(40) COLLATE Latin1_General_100_BIN2 NULL,
 duration_ms int NOT NULL,
 metric_value decimal(19,4) NULL,
 detail_json nvarchar(2000) NULL,
 CONSTRAINT PK_health_check_results PRIMARY KEY(health_check_run_id,check_id),
 CONSTRAINT CK_health_check_results_check CHECK(check_id LIKE ''[A-Z][A-Z]%-[0-9][0-9]'' AND check_id NOT LIKE ''%[^A-Z0-9-]%''),
 CONSTRAINT CK_health_check_results_area CHECK(area IN(''Database'',''Security'',''Reports'',''Imports'',''Data'',''Operations'',''Errors'')),
 CONSTRAINT CK_health_check_results_outcome CHECK(outcome IN(''Pass'',''Warning'',''Fail'',''Known'',''Skipped'')),
 CONSTRAINT CK_health_check_results_finding CHECK(LEN(finding_code)>0 AND finding_code NOT LIKE ''%[^A-Z0-9_]%''),
 CONSTRAINT CK_health_check_results_task CHECK(fix_task_id IS NULL OR fix_task_id NOT LIKE ''%[^A-Za-z0-9._:<>+-]%''),
 CONSTRAINT CK_health_check_results_duration CHECK(duration_ms>=0),
 CONSTRAINT CK_health_check_results_detail CHECK(detail_json IS NULL OR ISJSON(detail_json)=1)
);');

EXEC(N'CREATE TABLE diag.health_check_known
(
 check_id varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
 finding_code varchar(60) COLLATE Latin1_General_100_BIN2 NOT NULL,
 until_date date NOT NULL,
 reason nvarchar(200) NOT NULL,
 set_by nvarchar(128) NOT NULL,
 set_utc datetime2(3) NOT NULL,
 CONSTRAINT PK_health_check_known PRIMARY KEY(check_id,finding_code),
 CONSTRAINT CK_health_check_known_check CHECK(check_id LIKE ''[A-Z][A-Z]%-[0-9][0-9]'' AND check_id NOT LIKE ''%[^A-Z0-9-]%''),
 CONSTRAINT CK_health_check_known_finding CHECK(LEN(finding_code)>0 AND finding_code NOT LIKE ''%[^A-Z0-9_]%''),
 -- 90 days from the server''s local date, which is at most one day ahead of the UTC date of set_utc.
 CONSTRAINT CK_health_check_known_until CHECK(until_date<=DATEADD(day,91,CONVERT(date,set_utc)))
);');

EXEC(N'CREATE OR ALTER FUNCTION diag.is_check_id(@value nvarchar(max))
RETURNS bit WITH SCHEMABINDING
AS
BEGIN
 -- DB-01, SEC-03, RPT-22, DATA-05 ...: 2-4 capital letters, a dash, two digits.
 IF @value IS NULL OR LEN(@value) NOT BETWEEN 5 AND 7 OR DATALENGTH(@value)<>2*LEN(@value) RETURN 0;
 IF @value COLLATE Latin1_General_100_BIN2 NOT LIKE N''[A-Z][A-Z]%-[0-9][0-9]''
    OR @value COLLATE Latin1_General_100_BIN2 LIKE N''%[^A-Z0-9-]%''
    OR @value COLLATE Latin1_General_100_BIN2 LIKE N''%[0-9]%-%''
    OR LEN(@value)-LEN(REPLACE(@value,N''-'',N''''))<>1 RETURN 0;
 RETURN 1;
END');

EXEC(N'CREATE OR ALTER FUNCTION diag.is_safe_detail(@detail nvarchar(max))
RETURNS bit
AS
BEGIN
 -- A result''s sub-results: an array of objects {key, outcome, findingCode?, metricValue?, fields?}; key and field
 -- values are tokens, field values may also be numbers, null or empty; nothing else is allowed.
 IF @detail IS NULL RETURN 1;
 IF ISJSON(@detail)<>1 OR LEFT(LTRIM(@detail),1)<>N''['' RETURN 0;
 IF EXISTS(SELECT 1 FROM OPENJSON(@detail) e WHERE e.[type]<>5) RETURN 0;
 IF EXISTS(SELECT 1 FROM OPENJSON(@detail) e
   WHERE JSON_VALUE(e.value,''$.key'') IS NULL OR JSON_VALUE(e.value,''$.outcome'') IS NULL) RETURN 0;
 IF EXISTS(SELECT 1 FROM OPENJSON(@detail) e CROSS APPLY OPENJSON(e.value) p
   WHERE NOT(
        (p.[key]=N''key'' AND p.[type]=1 AND diag.is_token(p.value,100)=1)
     OR (p.[key]=N''outcome'' AND p.[type]=1 AND p.value COLLATE Latin1_General_100_BIN2 IN(N''Pass'',N''Warning'',N''Fail'',N''Known'',N''Skipped''))
     OR (p.[key]=N''findingCode'' AND (p.[type]=0 OR (p.[type]=1 AND diag.is_code(p.value,60)=1)))
     OR (p.[key]=N''metricValue'' AND p.[type] IN(0,2))
     OR (p.[key]=N''fields'' AND p.[type] IN(0,5)))) RETURN 0;
 IF EXISTS(SELECT 1 FROM OPENJSON(@detail) e CROSS APPLY OPENJSON(e.value) p
   CROSS APPLY OPENJSON(CASE WHEN p.[type]=5 THEN p.value END) f
   WHERE p.[key]=N''fields''
     AND NOT(diag.is_token(f.[key],60)=1
         AND (f.[type] IN(0,2) OR (f.[type]=1 AND (f.value=N'''' OR diag.is_token(f.value,100)=1))))) RETURN 0;
 RETURN 1;
END');

-- diag.record_health_check_run: a finished run in one transaction; prunes runs older than 400 days and expired
-- Known rows. The counts must equal the outcomes of the results. A detail longer than 2000 characters keeps the
-- whole sub-results that fit.
EXEC(N'CREATE OR ALTER PROCEDURE diag.record_health_check_run @run nvarchar(max)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @run IS NULL OR ISJSON(@run)<>1 OR COALESCE(LEFT(JSON_QUERY(@run,''$.results''),1),N'''')<>N''[''
    OR COALESCE(LEFT(JSON_QUERY(@run,''$.counts''),1),N'''')<>N''{''
  THROW 51820,''The health check run was refused: it is not a valid run.'',1;
 IF (SELECT COUNT(*) FROM OPENJSON(@run,''$.results''))>80
  THROW 51821,''The health check run was refused: it has more than 80 results.'',1;

 DECLARE @trigger nvarchar(max),@business nvarchar(max),@started nvarchar(max),@completed nvarchar(max),@duration nvarchar(max),
   @version nvarchar(max),@machine nvarchar(max),@role nvarchar(max),@overall nvarchar(max),@export nvarchar(max),
   @pass nvarchar(max),@warning nvarchar(max),@fail nvarchar(max),@known nvarchar(max),@skipped nvarchar(max);
 SELECT @trigger=h.trigger_kind,@business=h.business_date,@started=h.started_utc,@completed=h.completed_utc,@duration=h.duration_ms,
   @version=h.app_version,@machine=h.machine_name,@role=h.run_by_role,@overall=h.overall,@export=h.export_file_name,
   @pass=h.pass_count,@warning=h.warning_count,@fail=h.fail_count,@known=h.known_count,@skipped=h.skipped_count
 FROM OPENJSON(@run) WITH(
   trigger_kind nvarchar(max) ''$.trigger'',business_date nvarchar(max) ''$.businessDate'',started_utc nvarchar(max) ''$.startedUtc'',
   completed_utc nvarchar(max) ''$.completedUtc'',duration_ms nvarchar(max) ''$.durationMs'',app_version nvarchar(max) ''$.appVersion'',
   machine_name nvarchar(max) ''$.machineName'',run_by_role nvarchar(max) ''$.runByRole'',overall nvarchar(max) ''$.overall'',
   export_file_name nvarchar(max) ''$.exportFileName'',pass_count nvarchar(max) ''$.counts.pass'',warning_count nvarchar(max) ''$.counts.warning'',
   fail_count nvarchar(max) ''$.counts.fail'',known_count nvarchar(max) ''$.counts.known'',skipped_count nvarchar(max) ''$.counts.skipped'') h;

 DECLARE @results TABLE(
  ordinal int NOT NULL PRIMARY KEY,
  check_id varchar(10) COLLATE Latin1_General_100_BIN2 NULL,
  area varchar(10) COLLATE Latin1_General_100_BIN2 NULL,
  outcome varchar(8) COLLATE Latin1_General_100_BIN2 NULL,
  finding_code varchar(60) COLLATE Latin1_General_100_BIN2 NULL,
  explanation nvarchar(400) NULL,
  fix_task_id varchar(40) COLLATE Latin1_General_100_BIN2 NULL,
  duration_ms int NULL,
  metric_value decimal(19,4) NULL,
  detail_json nvarchar(2000) NULL,
  is_valid bit NOT NULL);
 INSERT @results(ordinal,check_id,area,outcome,finding_code,explanation,fix_task_id,duration_ms,metric_value,detail_json,is_valid)
 SELECT CONVERT(int,a.[key]),
   CASE WHEN v.is_valid=1 THEN r.check_id END,
   CASE WHEN v.is_valid=1 THEN r.area END,
   CASE WHEN v.is_valid=1 THEN r.outcome END,
   CASE WHEN v.is_valid=1 THEN r.finding_code END,
   CASE WHEN v.is_valid=1 THEN r.explanation END,
   CASE WHEN v.is_valid=1 THEN NULLIF(r.fix_task_id,N'''') END,
   CASE WHEN v.is_valid=1 THEN TRY_CONVERT(int,r.duration_ms) END,
   CASE WHEN v.is_valid=1 THEN v.metric END,
   CASE WHEN v.is_valid=1 THEN
     CASE WHEN r.detail IS NULL THEN NULL
          WHEN LEN(r.detail)<=2000 THEN r.detail
          ELSE (SELECT N''[''+STRING_AGG(CONVERT(nvarchar(max),x.value),N'','') WITHIN GROUP(ORDER BY x.position)+N'']''
                FROM (SELECT CONVERT(int,e.[key]) position,e.value,
                        SUM(LEN(e.value)+1) OVER(ORDER BY CONVERT(int,e.[key]) ROWS UNBOUNDED PRECEDING) running
                      FROM OPENJSON(r.detail) e) x
                WHERE x.running<=1998) END END,
   v.is_valid
 FROM OPENJSON(@run,''$.results'') a
 OUTER APPLY OPENJSON(CASE WHEN a.[type]=5 THEN a.value ELSE N''{}'' END) WITH(
   check_id nvarchar(max) ''$.checkId'',area nvarchar(max) ''$.area'',outcome nvarchar(max) ''$.outcome'',
   finding_code nvarchar(max) ''$.findingCode'',explanation nvarchar(max) ''$.explanation'',fix_task_id nvarchar(max) ''$.fixTaskId'',
   duration_ms nvarchar(max) ''$.durationMs'',metric_value nvarchar(max) ''$.metricValue'',detail nvarchar(max) ''$.detail'' AS JSON) r
 CROSS APPLY(SELECT COALESCE(TRY_CONVERT(decimal(19,4),r.metric_value),TRY_CONVERT(decimal(19,4),TRY_CONVERT(float,r.metric_value))) metric) m
 CROSS APPLY(SELECT m.metric,CONVERT(bit,CASE WHEN a.[type]=5
   AND diag.is_check_id(r.check_id)=1
   AND r.area COLLATE Latin1_General_100_BIN2 IN(N''Database'',N''Security'',N''Reports'',N''Imports'',N''Data'',N''Operations'',N''Errors'')
   AND r.outcome COLLATE Latin1_General_100_BIN2 IN(N''Pass'',N''Warning'',N''Fail'',N''Known'',N''Skipped'')
   AND diag.is_code(r.finding_code,60)=1
   AND diag.is_safe_text(r.explanation,400)=1
   AND (NULLIF(r.fix_task_id,N'''') IS NULL OR diag.is_token(r.fix_task_id,40)=1)
   AND r.duration_ms COLLATE Latin1_General_100_BIN2 LIKE N''%[0-9]%'' AND TRY_CONVERT(int,r.duration_ms)>=0
   AND (r.metric_value IS NULL OR m.metric IS NOT NULL)
   AND diag.is_safe_detail(r.detail)=1
  THEN 1 ELSE 0 END) is_valid) v;

 IF EXISTS(SELECT 1 FROM @results WHERE is_valid=0)
    OR EXISTS(SELECT check_id FROM @results GROUP BY check_id HAVING COUNT(*)>1)
    OR @trigger IS NULL OR @trigger COLLATE Latin1_General_100_BIN2 NOT IN(N''Manual'',N''Nightly'')
    OR diag.parse_date(@business) IS NULL
    OR diag.parse_utc(@started) IS NULL OR diag.parse_utc(@completed) IS NULL
    OR diag.parse_utc(@completed)<diag.parse_utc(@started)
    OR @duration COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[0-9]%'' OR TRY_CONVERT(int,@duration) IS NULL OR TRY_CONVERT(int,@duration)<0
    OR diag.version_key(CASE WHEN LEN(@version)<=20 THEN @version END) IS NULL
    OR diag.is_token(@machine,64)=0
    OR @role IS NULL OR @role COLLATE Latin1_General_100_BIN2 NOT IN(N''Owner'',N''StoreManager'',N''Viewer'',N''Automation'',N''Unknown'')
    OR @overall IS NULL OR @overall COLLATE Latin1_General_100_BIN2 NOT IN(N''Pass'',N''Warning'',N''Fail'')
    OR (NULLIF(@export,N'''') IS NOT NULL AND diag.is_token(@export,120)=0)
    OR COALESCE(TRY_CONVERT(int,@pass),-1)<>(SELECT COUNT(*) FROM @results WHERE outcome=''Pass'')
    OR COALESCE(TRY_CONVERT(int,@warning),-1)<>(SELECT COUNT(*) FROM @results WHERE outcome=''Warning'')
    OR COALESCE(TRY_CONVERT(int,@fail),-1)<>(SELECT COUNT(*) FROM @results WHERE outcome=''Fail'')
    OR COALESCE(TRY_CONVERT(int,@known),-1)<>(SELECT COUNT(*) FROM @results WHERE outcome=''Known'')
    OR COALESCE(TRY_CONVERT(int,@skipped),-1)<>(SELECT COUNT(*) FROM @results WHERE outcome=''Skipped'')
  THROW 51820,''The health check run was refused: it is not a valid run.'',1;

 DECLARE @now datetime2(3)=SYSUTCDATETIME(),@id bigint;
 BEGIN TRY
  BEGIN TRANSACTION;
  INSERT diag.health_check_runs(trigger_kind,business_date,started_utc,completed_utc,duration_ms,app_version,machine_name,run_by_role,
    overall,pass_count,warning_count,fail_count,known_count,skipped_count,export_file_name)
  VALUES(@trigger,diag.parse_date(@business),diag.parse_utc(@started),diag.parse_utc(@completed),CONVERT(int,@duration),@version,@machine,@role,
    @overall,CONVERT(smallint,@pass),CONVERT(smallint,@warning),CONVERT(smallint,@fail),CONVERT(smallint,@known),CONVERT(smallint,@skipped),
    NULLIF(@export,N''''));
  SET @id=SCOPE_IDENTITY();
  INSERT diag.health_check_results(health_check_run_id,check_id,area,outcome,finding_code,explanation,fix_task_id,duration_ms,metric_value,detail_json)
  SELECT @id,check_id,area,outcome,finding_code,LTRIM(RTRIM(explanation)),fix_task_id,duration_ms,metric_value,detail_json FROM @results;

  -- Retention (decision 36, Q14): 400 days of runs; Known rows until their date (server local date).
  DELETE r FROM diag.health_check_results r
  JOIN diag.health_check_runs h ON h.health_check_run_id=r.health_check_run_id
  WHERE h.started_utc<DATEADD(day,-400,@now);
  DELETE diag.health_check_runs WHERE started_utc<DATEADD(day,-400,@now);
  DELETE diag.health_check_known WHERE until_date<CONVERT(date,SYSDATETIME());
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
 SELECT @id health_check_run_id;
END');

EXEC(N'CREATE OR ALTER PROCEDURE diag.load_health_check_runs @top int=30,@trigger varchar(8)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @count int=CASE WHEN @top IS NULL OR @top<1 THEN 30 WHEN @top>500 THEN 500 ELSE @top END;
 SELECT TOP(@count) health_check_run_id,trigger_kind,business_date,started_utc,completed_utc,duration_ms,app_version,machine_name,
   run_by_role,overall,pass_count,warning_count,fail_count,known_count,skipped_count,export_file_name
 FROM diag.health_check_runs
 WHERE @trigger IS NULL OR trigger_kind=@trigger
 ORDER BY started_utc DESC,health_check_run_id DESC;
END');

EXEC(N'CREATE OR ALTER PROCEDURE diag.load_health_check_results @health_check_run_id bigint
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF NOT EXISTS(SELECT 1 FROM diag.health_check_runs WHERE health_check_run_id=@health_check_run_id)
  THROW 51824,''That health check run was not found. It may be older than 400 days.'',1;
 SELECT check_id,area,outcome,finding_code,explanation,fix_task_id,duration_ms,metric_value,detail_json
 FROM diag.health_check_results
 WHERE health_check_run_id=@health_check_run_id
 ORDER BY CASE LEFT(check_id,CHARINDEX(''-'',check_id)-1)
   WHEN ''DB'' THEN 1 WHEN ''SEC'' THEN 2 WHEN ''RPT'' THEN 3 WHEN ''IMP'' THEN 4 WHEN ''DATA'' THEN 5 WHEN ''OPS'' THEN 6 WHEN ''ERR'' THEN 7 ELSE 8 END,
   check_id;
END');

EXEC(N'CREATE OR ALTER PROCEDURE diag.load_health_check_summary
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 SELECT TOP(1) business_date,completed_utc,overall,pass_count,warning_count,fail_count,known_count,skipped_count
 FROM diag.health_check_runs
 WHERE trigger_kind=''Nightly''
 ORDER BY started_utc DESC,health_check_run_id DESC;
END');

EXEC(N'CREATE OR ALTER PROCEDURE diag.load_health_check_known
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 SELECT check_id,finding_code,until_date,reason,set_by,set_utc
 FROM diag.health_check_known
 WHERE until_date>=CONVERT(date,SYSDATETIME())
 ORDER BY check_id,finding_code;
END');

EXEC(N'CREATE OR ALTER PROCEDURE diag.set_health_check_known @check_id varchar(10),@finding_code varchar(60),@until date,@reason nvarchar(200)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51822,''Only an Owner can mark a health check warning as known.'',1;
 DECLARE @today date=CONVERT(date,SYSDATETIME());
 IF diag.is_check_id(@check_id)=0 OR diag.is_code(@finding_code,60)=0
    OR @until IS NULL OR @until<@today OR @until>DATEADD(day,90,@today)
    OR diag.is_safe_text(@reason,200)=0
  THROW 51823,''Enter a reason (plain text, at most 200 characters) and a date from today up to 90 days ahead.'',1;
 DECLARE @now datetime2(3)=SYSUTCDATETIME();
 BEGIN TRY
  BEGIN TRANSACTION;
  UPDATE diag.health_check_known WITH(UPDLOCK,HOLDLOCK)
  SET until_date=@until,reason=LTRIM(RTRIM(@reason)),set_by=ORIGINAL_LOGIN(),set_utc=@now
  WHERE check_id=@check_id AND finding_code=@finding_code;
  IF @@ROWCOUNT=0
   INSERT diag.health_check_known(check_id,finding_code,until_date,reason,set_by,set_utc)
   VALUES(@check_id,@finding_code,@until,LTRIM(RTRIM(@reason)),ORIGINAL_LOGIN(),@now);
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END');

EXEC(N'CREATE OR ALTER PROCEDURE diag.clear_health_check_known @check_id varchar(10),@finding_code varchar(60)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51822,''Only an Owner can clear a known health check warning.'',1;
 DELETE diag.health_check_known WHERE check_id=@check_id AND finding_code=@finding_code;
END');

-- The catalog reader for diag.load_role_permissions: cannot sign in; can read metadata of this database only.
IF DATABASE_PRINCIPAL_ID(N'diag_catalog_reader') IS NULL
 EXEC(N'CREATE USER diag_catalog_reader WITHOUT LOGIN WITH DEFAULT_SCHEMA=diag');
EXEC(N'GRANT VIEW DEFINITION TO diag_catalog_reader');

-- diag.load_role_permissions (SEC-01..03). Three result sets: the four etp roles' permissions; their members (names
-- only for an Owner caller, otherwise grouped counts); the active Owners whose login lacks ALTER ANY LOGIN WITH
-- GRANT OPTION (names only for an Owner caller with a non-zero count, otherwise one row with a NULL name). The
-- count is -1 when the caller cannot see server permissions (for example the automation account): SEC-03 Skipped.
EXEC(N'CREATE OR ALTER PROCEDURE diag.load_role_permissions
WITH EXECUTE AS N''diag_catalog_reader''
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @is_owner bit=0,@owners_without int=-1;
 DECLARE @app_users TABLE(windows_identity nvarchar(200) NOT NULL,role_code varchar(30) NOT NULL,is_active bit NOT NULL,
   application_user_id int NOT NULL,login_sid varbinary(85) NULL);
 DECLARE @owners TABLE(member_name sysname NOT NULL);
 INSERT @app_users(windows_identity,role_code,is_active,application_user_id)
 SELECT windows_identity,role_code,is_active,application_user_id FROM dbo.application_users;

 -- The caller''s own context: its role, and the server-scope check it is allowed to see.
 EXECUTE AS CALLER;
 SET @is_owner=CASE WHEN COALESCE(IS_ROLEMEMBER(''etp_owner''),0)=1 OR COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)=1 THEN 1 ELSE 0 END;
 UPDATE @app_users SET login_sid=SUSER_SID(windows_identity);
 IF COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)=1
    OR COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,''CONTROL SERVER''),0)=1
    OR COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,''VIEW ANY DEFINITION''),0)=1
    OR COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,''ALTER ANY LOGIN''),0)=1
 BEGIN
  -- New-EtpOwnersWithoutGrantOptionSql (scripts\etp-operations-common.ps1), with the names kept.
  INSERT @owners(member_name)
  SELECT sp.name FROM sys.server_principals sp
  WHERE sp.type=''U''
    AND sp.sid NOT IN(SID_BINARY(N''S-1-5-18''),SID_BINARY(N''S-1-5-19''),SID_BINARY(N''S-1-5-20''))
    AND SUBSTRING(sp.sid,3,10)<>SUBSTRING(SID_BINARY(N''S-1-5-80-0''),3,10)
    AND sp.principal_id IN(SELECT SUSER_ID(u.windows_identity) FROM @app_users u WHERE u.role_code=''OWNER'' AND u.is_active=1)
    AND NOT EXISTS(SELECT 1 FROM sys.server_permissions p WHERE p.class=100 AND p.grantee_principal_id=sp.principal_id
                   AND p.permission_name=N''ALTER ANY LOGIN'' AND p.state=''W'');
  SET @owners_without=(SELECT COUNT(*) FROM @owners);
 END;
 REVERT;

 -- 1. Permissions of the four etp roles (grants to public and to single users are not listed).
 SELECT r.name role_name,p.class_desc,
   CASE p.class WHEN 1 THEN OBJECT_SCHEMA_NAME(p.major_id) WHEN 3 THEN SCHEMA_NAME(p.major_id)
     WHEN 6 THEN (SELECT SCHEMA_NAME(t.schema_id) FROM sys.types t WHERE t.user_type_id=p.major_id) END schema_name,
   CASE p.class
     WHEN 1 THEN OBJECT_NAME(p.major_id)+CASE WHEN p.minor_id<>0 THEN N''(''+COL_NAME(p.major_id,p.minor_id)+N'')'' ELSE N'''' END
     WHEN 6 THEN TYPE_NAME(p.major_id)
     WHEN 24 THEN (SELECT k.name FROM sys.symmetric_keys k WHERE k.symmetric_key_id=p.major_id)
     WHEN 25 THEN (SELECT c.name FROM sys.certificates c WHERE c.certificate_id=p.major_id)
     WHEN 26 THEN (SELECT k.name FROM sys.asymmetric_keys k WHERE k.asymmetric_key_id=p.major_id) END object_name,
   p.permission_name,p.state_desc
 FROM sys.database_permissions p
 JOIN sys.database_principals r ON r.principal_id=p.grantee_principal_id
 WHERE r.type=''R'' AND r.name IN(N''etp_owner'',N''etp_store_manager'',N''etp_viewer'',N''etp_automation'')
 ORDER BY role_name,p.class_desc,schema_name,object_name,p.permission_name,p.state_desc;

 -- 2. Members of the four etp roles, with the application user each one maps to (by name, else by SID).
 DECLARE @members TABLE(role_name sysname NOT NULL,member_name sysname NOT NULL,is_active_application_user bit NOT NULL,
   application_role_code varchar(30) NULL);
 INSERT @members(role_name,member_name,is_active_application_user,application_role_code)
 SELECT r.name,m.name,CONVERT(bit,COALESCE(u.is_active,0)),u.role_code
 FROM sys.database_role_members rm
 JOIN sys.database_principals r ON r.principal_id=rm.role_principal_id
 JOIN sys.database_principals m ON m.principal_id=rm.member_principal_id
 OUTER APPLY(SELECT TOP(1) a.is_active,a.role_code FROM @app_users a
   WHERE a.windows_identity=m.name OR (m.sid IS NOT NULL AND a.login_sid=m.sid)
   ORDER BY CASE WHEN a.windows_identity=m.name THEN 0 ELSE 1 END,a.is_active DESC,a.application_user_id) u
 WHERE r.type=''R'' AND r.name IN(N''etp_owner'',N''etp_store_manager'',N''etp_viewer'',N''etp_automation'');
 IF @is_owner=1
  SELECT role_name,member_name,is_active_application_user,application_role_code,1 member_count
  FROM @members ORDER BY role_name,member_name;
 ELSE
  SELECT role_name,CONVERT(sysname,NULL) member_name,is_active_application_user,application_role_code,COUNT(*) member_count
  FROM @members GROUP BY role_name,is_active_application_user,application_role_code
  ORDER BY role_name,is_active_application_user,application_role_code;

 -- 3. Active Owners without the grant option.
 IF @is_owner=1 AND @owners_without>0
  SELECT member_name,@owners_without owners_without_grant_option FROM @owners ORDER BY member_name;
 ELSE
  SELECT CONVERT(sysname,NULL) member_name,@owners_without owners_without_grant_option;
END');

-- Grants (design 6.4). No role has any permission on the diag tables or helper functions.
GRANT EXECUTE ON diag.record_health_check_run TO etp_owner,etp_automation;
GRANT EXECUTE ON diag.load_health_check_runs TO etp_owner,etp_automation;
GRANT EXECUTE ON diag.load_health_check_results TO etp_owner,etp_automation;
GRANT EXECUTE ON diag.load_health_check_summary TO etp_owner,etp_store_manager,etp_viewer,etp_automation;
GRANT EXECUTE ON diag.load_health_check_known TO etp_owner,etp_automation;
GRANT EXECUTE ON diag.set_health_check_known TO etp_owner;
GRANT EXECUTE ON diag.clear_health_check_known TO etp_owner;
GRANT EXECUTE ON diag.load_role_permissions TO etp_owner,etp_automation;
