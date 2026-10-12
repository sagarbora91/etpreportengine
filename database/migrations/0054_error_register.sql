-- 1.11.0, lane L1 (sql). Self-diagnostics, part 1: the error register (design SELF-DIAGNOSTICS-DESIGN-1.11.0.md
-- sections 4 and 6.1; procedure contract docs\design\SELF-DIAGNOSTICS-PROCEDURES.md; decisions 33 and 36).
--
-- New schema diag, owned by dbo, with no schema grant to any role: etp_store_manager and etp_viewer hold
-- SELECT ON SCHEMA::dbo (0022), and the automation account is a member of etp_store_manager, so a table-level
-- DENY in dbo would also block automation. Every access is through a dbo-owned diag procedure (ownership chain).
--
-- What is stored. One row per distinct bug (diag.error_register, keyed by the fingerprint the app computes:
-- SHA-256 of event base, sub code, source, site, exception type and SQL number; never recomputed here), the
-- occurrence count per app version (diag.error_register_versions), and the occurrences themselves
-- (diag.error_occurrences) with context only: Ref, time, version, severity, report, store, dates, operation and
-- the reporter's ETP role (Owner, StoreManager, Viewer, Automation, Unknown), never a Windows account name.
-- Every text that arrives in an ingest batch is checked again here against the SafeToken allow-list
-- (A-Z a-z 0-9 . _ : < > + -; event and finding codes A-Z 0-9 _ only); an item that fails is skipped and counted
-- as rejected, so nothing but tokens, numbers, dates and versions reaches these tables from a log.
--
-- Retention (decision 36, Q14): register and version rows are kept for ever (one row per bug); occurrences
-- older than 180 days are deleted by the next ingest, except the newest 20 of each entry. More than 200
-- occurrences of one entry in one UTC day keep only the counters (spam guard).
--
-- Errors 51800-51819 (each has a curated message in DesktopFriendlyError.ProductErrors, lane L8):
--   51800 malformed ingest batch     51801 Owner only          51802 a reason is required
--   51803 text is not safe           51804 malformed fix manifest
--   51805 entry not found / status change not allowed / merge into itself or a merged entry
--   51806 the error register is busy (lock timeout; added by L1, not in the contract table)
--
-- Version keys: major*10^9 + minor*10^6 + build*10^3 + revision, each part 0-999, missing parts 0
-- (diag.version_key). The migration runner applies this script in one transaction: no BEGIN, COMMIT or GO.
-- Everything diag is created through EXEC so the batch compiles before the schema exists.
SET XACT_ABORT ON;

IF SCHEMA_ID(N'diag') IS NULL EXEC(N'CREATE SCHEMA diag AUTHORIZATION dbo');

-- Helpers. Schema-bound scalar functions; no grant (reached through the dbo-owned procedures).
EXEC(N'CREATE OR ALTER FUNCTION diag.version_key(@version varchar(20))
RETURNS bigint WITH SCHEMABINDING
AS
BEGIN
 -- 1 to 4 dot-separated parts of 1-3 digits each; NULL for anything else.
 IF @version IS NULL OR LEN(@version)=0 OR DATALENGTH(@version)<>LEN(@version)
    OR @version COLLATE Latin1_General_100_BIN2 LIKE ''%[^0-9.]%''
    OR @version LIKE ''.%'' OR @version LIKE ''%.'' OR @version LIKE ''%..%''
  RETURN NULL;
 DECLARE @dots int=LEN(@version)-LEN(REPLACE(@version,''.'',''''));
 IF @dots>3 RETURN NULL;
 DECLARE @padded varchar(40)=@version+REPLICATE(''.0'',3-@dots);
 DECLARE @a varchar(40)=PARSENAME(@padded,4),@b varchar(40)=PARSENAME(@padded,3),
         @c varchar(40)=PARSENAME(@padded,2),@d varchar(40)=PARSENAME(@padded,1);
 IF LEN(@a)>3 OR LEN(@b)>3 OR LEN(@c)>3 OR LEN(@d)>3 RETURN NULL;
 RETURN CONVERT(bigint,@a)*1000000000+CONVERT(bigint,@b)*1000000+CONVERT(bigint,@c)*1000+CONVERT(bigint,@d);
END');

EXEC(N'CREATE OR ALTER FUNCTION diag.is_token(@value nvarchar(max),@max_length int)
RETURNS bit WITH SCHEMABINDING
AS
BEGIN
 -- The SafeToken allow-list: 1 to @max_length characters of A-Z a-z 0-9 . _ : < > + -
 IF @value IS NULL OR LEN(@value)=0 OR DATALENGTH(@value)<>2*LEN(@value) OR LEN(@value)>@max_length RETURN 0;
 IF @value COLLATE Latin1_General_100_BIN2 LIKE N''%[^A-Za-z0-9._:<>+-]%'' RETURN 0;
 RETURN 1;
END');

EXEC(N'CREATE OR ALTER FUNCTION diag.is_code(@value nvarchar(max),@max_length int)
RETURNS bit WITH SCHEMABINDING
AS
BEGIN
 -- Event and finding codes: 1 to @max_length characters of A-Z 0-9 _
 IF @value IS NULL OR LEN(@value)=0 OR DATALENGTH(@value)<>2*LEN(@value) OR LEN(@value)>@max_length RETURN 0;
 IF @value COLLATE Latin1_General_100_BIN2 LIKE N''%[^A-Z0-9_]%'' RETURN 0;
 RETURN 1;
END');

EXEC(N'CREATE OR ALTER FUNCTION diag.is_safe_text(@value nvarchar(max),@max_length int)
RETURNS bit WITH SCHEMABINDING
AS
BEGIN
 -- Free text typed by the Owner (a reason) or built from a template (an explanation): not blank in the sense of
 -- dbo.is_blank_text (0044), at most @max_length characters, and no control characters (U+0000-U+001F,
 -- U+007F-U+009F, U+2028, U+2029), so a line break or an escape can never reach an export or a screen.
 IF @value IS NULL OR dbo.is_blank_text(@value)=1 OR DATALENGTH(@value)>2*@max_length RETURN 0;
 DECLARE @i int=1,@n int=DATALENGTH(@value)/2,@code int;
 WHILE @i<=@n
 BEGIN
  SET @code=UNICODE(SUBSTRING(@value,@i,1));
  IF @code<32 OR @code BETWEEN 127 AND 159 OR @code IN(8232,8233) RETURN 0;
  SET @i+=1;
 END;
 RETURN 1;
END');

EXEC(N'CREATE OR ALTER FUNCTION diag.parse_utc(@value nvarchar(max))
RETURNS datetime2(3) WITH SCHEMABINDING
AS
BEGIN
 -- ISO 8601 with a Z or an offset, converted to UTC; NULL for anything else (an empty string is never 1900-01-01).
 IF @value IS NULL OR LEN(@value) NOT BETWEEN 19 AND 40
    OR @value COLLATE Latin1_General_100_BIN2 NOT LIKE N''[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]%''
  RETURN NULL;
 RETURN CONVERT(datetime2(3),SWITCHOFFSET(TRY_CONVERT(datetimeoffset(3),@value,127),''+00:00''));
END');

EXEC(N'CREATE OR ALTER FUNCTION diag.parse_date(@value nvarchar(max))
RETURNS date WITH SCHEMABINDING
AS
BEGIN
 -- yyyy-MM-dd only.
 IF @value IS NULL OR LEN(@value)<>10
    OR @value COLLATE Latin1_General_100_BIN2 NOT LIKE N''[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]''
  RETURN NULL;
 RETURN TRY_CONVERT(date,@value,23);
END');

EXEC(N'CREATE OR ALTER FUNCTION diag.caller_role()
RETURNS varchar(16)
AS
BEGIN
 -- The caller''s ETP role, never the account: stored on occurrences instead of a Windows name.
 RETURN CASE
  WHEN COALESCE(IS_ROLEMEMBER(''etp_owner''),0)=1 THEN ''Owner''
  WHEN COALESCE(IS_ROLEMEMBER(''etp_automation''),0)=1 THEN ''Automation''
  WHEN COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)=1 THEN ''StoreManager''
  WHEN COALESCE(IS_ROLEMEMBER(''etp_viewer''),0)=1 THEN ''Viewer''
  ELSE ''Unknown'' END;
END');

-- Tables.
EXEC(N'CREATE TABLE diag.error_register
(
 error_register_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_error_register PRIMARY KEY,
 fingerprint char(64) COLLATE Latin1_General_100_BIN2 NOT NULL CONSTRAINT UQ_error_register_fingerprint UNIQUE,
 fingerprint_version tinyint NOT NULL,
 event_code varchar(80) COLLATE Latin1_General_100_BIN2 NOT NULL,
 sub_code varchar(60) COLLATE Latin1_General_100_BIN2 NULL,
 source varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
 site varchar(200) COLLATE Latin1_General_100_BIN2 NULL,
 exception_type varchar(200) COLLATE Latin1_General_100_BIN2 NOT NULL,
 sql_number int NOT NULL,
 max_severity varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
 status varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
 first_seen_utc datetime2(3) NOT NULL,
 last_seen_utc datetime2(3) NOT NULL,
 occurrence_count bigint NOT NULL,
 first_version varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
 last_version varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
 fixed_in_version varchar(20) COLLATE Latin1_General_100_BIN2 NULL,
 fixed_in_version_key bigint NULL,
 fixed_ticket varchar(12) COLLATE Latin1_General_100_BIN2 NULL,
 fixed_utc datetime2(3) NULL,
 recurred_utc datetime2(3) NULL,
 recurred_version varchar(20) COLLATE Latin1_General_100_BIN2 NULL,
 latest_stack nvarchar(3400) NULL,
 status_reason nvarchar(200) NULL,
 status_changed_utc datetime2(3) NOT NULL,
 status_changed_by nvarchar(128) NOT NULL,
 merged_into_id int NULL CONSTRAINT FK_error_register_merged REFERENCES diag.error_register(error_register_id),
 CONSTRAINT CK_error_register_fingerprint CHECK(LEN(fingerprint)=64 AND fingerprint NOT LIKE ''%[^0-9a-f]%''),
 CONSTRAINT CK_error_register_severity CHECK(max_severity IN(''Error'',''Critical'')),
 CONSTRAINT CK_error_register_status CHECK(status IN(''New'',''Seen'',''Expected'',''Fixed'',''Recurred'')),
 CONSTRAINT CK_error_register_tokens CHECK(LEN(event_code)>0 AND event_code NOT LIKE ''%[^A-Z0-9_]%''
   AND LEN(source)>0 AND source NOT LIKE ''%[^A-Za-z0-9._:<>+-]%''
   AND LEN(exception_type)>0 AND exception_type NOT LIKE ''%[^A-Za-z0-9._:<>+-]%''
   AND (sub_code IS NULL OR (LEN(sub_code)>0 AND sub_code NOT LIKE ''%[^A-Za-z0-9._:<>+-]%''))
   AND (site IS NULL OR (LEN(site)>0 AND site NOT LIKE ''%[^A-Za-z0-9._:<>+-]%''))),
 CONSTRAINT CK_error_register_counts CHECK(occurrence_count>=0 AND first_seen_utc<=last_seen_utc),
 CONSTRAINT CK_error_register_fixed CHECK(status<>''Fixed'' OR (fixed_in_version IS NOT NULL AND fixed_in_version_key IS NOT NULL AND fixed_utc IS NOT NULL)),
 CONSTRAINT CK_error_register_recurred CHECK(status<>''Recurred'' OR (recurred_utc IS NOT NULL AND recurred_version IS NOT NULL AND fixed_in_version_key IS NOT NULL)),
 CONSTRAINT CK_error_register_stack CHECK(latest_stack IS NULL OR ISJSON(latest_stack)=1),
 CONSTRAINT CK_error_register_merge CHECK(merged_into_id IS NULL OR merged_into_id<>error_register_id)
);');

EXEC(N'CREATE TABLE diag.error_register_versions
(
 error_register_id int NOT NULL CONSTRAINT FK_error_register_versions_entry REFERENCES diag.error_register(error_register_id),
 app_version varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
 app_version_key bigint NOT NULL,
 occurrence_count bigint NOT NULL,
 first_seen_utc datetime2(3) NOT NULL,
 last_seen_utc datetime2(3) NOT NULL,
 CONSTRAINT PK_error_register_versions PRIMARY KEY(error_register_id,app_version),
 CONSTRAINT CK_error_register_versions_counts CHECK(occurrence_count>=0 AND first_seen_utc<=last_seen_utc)
);');

EXEC(N'CREATE TABLE diag.error_occurrences
(
 error_occurrence_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_error_occurrences PRIMARY KEY,
 error_register_id int NOT NULL CONSTRAINT FK_error_occurrences_entry REFERENCES diag.error_register(error_register_id),
 occurred_utc datetime2(3) NOT NULL,
 reference char(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
 app_version varchar(20) COLLATE Latin1_General_100_BIN2 NOT NULL,
 severity varchar(10) COLLATE Latin1_General_100_BIN2 NOT NULL,
 report_code varchar(30) COLLATE Latin1_General_100_BIN2 NULL,
 store_code varchar(30) COLLATE Latin1_General_100_BIN2 NULL,
 business_date date NULL,
 date_from date NULL,
 operation nvarchar(120) COLLATE Latin1_General_100_BIN2 NULL,
 reporter_role varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
 ingested_utc datetime2(3) NOT NULL CONSTRAINT DF_error_occurrences_ingested DEFAULT SYSUTCDATETIME(),
 CONSTRAINT UQ_error_occurrences_ref UNIQUE(reference,occurred_utc),
 CONSTRAINT CK_error_occurrences_severity CHECK(severity IN(''Error'',''Critical'')),
 CONSTRAINT CK_error_occurrences_role CHECK(reporter_role IN(''Owner'',''StoreManager'',''Viewer'',''Automation'',''Unknown'')),
 CONSTRAINT CK_error_occurrences_tokens CHECK(reference NOT LIKE ''%[^A-Za-z0-9._:<>+ -]%''
   AND (report_code IS NULL OR report_code NOT LIKE ''%[^A-Za-z0-9._:<>+-]%'')
   AND (store_code IS NULL OR store_code NOT LIKE ''%[^A-Za-z0-9._:<>+-]%'')
   AND (operation IS NULL OR operation NOT LIKE N''%[^A-Za-z0-9._:<>+-]%''))
);');
EXEC(N'CREATE INDEX IX_error_occurrences_entry ON diag.error_occurrences(error_register_id,occurred_utc DESC)');
EXEC(N'CREATE INDEX IX_error_occurrences_ref ON diag.error_occurrences(reference)');
EXEC(N'CREATE INDEX IX_error_occurrences_time ON diag.error_occurrences(occurred_utc) INCLUDE(error_register_id)');

-- diag.ingest_error_occurrences: every ETP process sends its own account's Error and Critical log entries,
-- at most 500 per call, oldest first. Returns accepted, duplicates, rejected, new_entries, recurred_entries.
EXEC(N'CREATE OR ALTER PROCEDURE diag.ingest_error_occurrences @items nvarchar(max),@app_version varchar(20)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF diag.version_key(@app_version) IS NULL
  THROW 51800,''The error batch was refused: the application version is not valid.'',1;
 DECLARE @first int=PATINDEX(N''%[^ ''+NCHAR(9)+NCHAR(10)+NCHAR(13)+N'']%'',COALESCE(@items,N''''));
 IF @items IS NULL OR ISJSON(@items)<>1 OR @first=0 OR SUBSTRING(@items,@first,1)<>N''[''
  THROW 51800,''The error batch was refused: it is not a JSON array.'',1;
 IF (SELECT COUNT(*) FROM OPENJSON(@items))>500
  THROW 51800,''The error batch was refused: it holds more than 500 items.'',1;

 DECLARE @now datetime2(3)=SYSUTCDATETIME(),@role varchar(16)=diag.caller_role();
 DECLARE @rows TABLE(
  ordinal int NOT NULL PRIMARY KEY,
  is_valid bit NOT NULL,
  is_duplicate bit NOT NULL DEFAULT(0),
  fingerprint char(64) COLLATE Latin1_General_100_BIN2 NULL, fingerprint_version tinyint NULL,
  event_code varchar(80) NULL, sub_code varchar(60) NULL, source varchar(128) NULL, site varchar(200) NULL,
  exception_type varchar(200) NULL, sql_number int NULL,
  occurred_utc datetime2(3) NULL, severity varchar(10) NULL, reference char(12) COLLATE Latin1_General_100_BIN2 NULL,
  app_version varchar(20) NULL, app_version_key bigint NULL,
  report_code varchar(30) NULL, store_code varchar(30) NULL, business_date date NULL, date_from date NULL,
  operation nvarchar(120) NULL, stack nvarchar(3400) NULL);

 INSERT @rows(ordinal,is_valid,fingerprint,fingerprint_version,event_code,sub_code,source,site,exception_type,sql_number,
   occurred_utc,severity,reference,app_version,app_version_key,report_code,store_code,business_date,date_from,operation,stack)
 SELECT CONVERT(int,a.[key]),v.is_valid,
   CASE WHEN v.is_valid=1 THEN j.fingerprint END,
   CASE WHEN v.is_valid=1 THEN TRY_CONVERT(tinyint,j.fingerprint_version) END,
   CASE WHEN v.is_valid=1 THEN j.event_base END,
   CASE WHEN v.is_valid=1 THEN NULLIF(j.sub_code,N'''') END,
   CASE WHEN v.is_valid=1 THEN j.source END,
   CASE WHEN v.is_valid=1 THEN NULLIF(j.site,N'''') END,
   CASE WHEN v.is_valid=1 THEN j.exception_type END,
   CASE WHEN v.is_valid=1 THEN TRY_CONVERT(int,j.sql_number) END,
   CASE WHEN v.is_valid=1 THEN diag.parse_utc(j.timestamp_utc) END,
   CASE WHEN v.is_valid=1 THEN j.severity END,
   CASE WHEN v.is_valid=1 THEN j.reference END,
   CASE WHEN v.is_valid=1 THEN j.application_version END,
   CASE WHEN v.is_valid=1 THEN diag.version_key(j.application_version) END,
   CASE WHEN v.is_valid=1 THEN NULLIF(j.report_code,N'''') END,
   CASE WHEN v.is_valid=1 THEN NULLIF(j.store_code,N'''') END,
   CASE WHEN v.is_valid=1 THEN diag.parse_date(j.business_date) END,
   CASE WHEN v.is_valid=1 THEN diag.parse_date(j.date_from) END,
   CASE WHEN v.is_valid=1 THEN NULLIF(j.operation,N'''') END,
   -- latest stack: the frames that are tokens, in order, as many as fit 3400 characters of JSON
   CASE WHEN v.is_valid=1 THEN (
     SELECT N''[''+STRING_AGG(CONVERT(nvarchar(max),N''"''+f.value+N''"''),N'','') WITHIN GROUP(ORDER BY f.position)+N'']''
     FROM (SELECT TRY_CONVERT(int,s.[key]) position,s.value,
             SUM(LEN(s.value)+3) OVER(ORDER BY TRY_CONVERT(int,s.[key]) ROWS UNBOUNDED PRECEDING) running
           FROM OPENJSON(CASE WHEN LEFT(j.stack,1)=N''['' THEN j.stack END) s
           WHERE s.[type]=1 AND diag.is_token(s.value,200)=1) f
     WHERE f.running<=3390) END
 FROM OPENJSON(@items) a
 OUTER APPLY OPENJSON(CASE WHEN a.[type]=5 THEN a.value ELSE N''{}'' END) WITH(
   fingerprint nvarchar(max) ''$.fingerprint'', fingerprint_version nvarchar(max) ''$.fingerprintVersion'',
   event_base nvarchar(max) ''$.eventBase'', sub_code nvarchar(max) ''$.subCode'', source nvarchar(max) ''$.source'',
   site nvarchar(max) ''$.site'', exception_type nvarchar(max) ''$.exceptionType'', sql_number nvarchar(max) ''$.sqlNumber'',
   timestamp_utc nvarchar(max) ''$.timestampUtc'', severity nvarchar(max) ''$.severity'', reference nvarchar(max) ''$.reference'',
   application_version nvarchar(max) ''$.applicationVersion'', report_code nvarchar(max) ''$.reportCode'',
   store_code nvarchar(max) ''$.storeCode'', business_date nvarchar(max) ''$.businessDate'', date_from nvarchar(max) ''$.dateFrom'',
   operation nvarchar(max) ''$.operation'', stack nvarchar(max) ''$.stack'' AS JSON) j
 CROSS APPLY(SELECT CONVERT(bit,CASE WHEN a.[type]=5
   AND LEN(j.fingerprint)=64 AND DATALENGTH(j.fingerprint)=128 AND j.fingerprint COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%''
   AND TRY_CONVERT(tinyint,j.fingerprint_version)>=1
   AND diag.is_code(j.event_base,80)=1
   AND (NULLIF(j.sub_code,N'''') IS NULL OR diag.is_token(j.sub_code,60)=1)
   AND diag.is_token(j.source,128)=1
   AND (NULLIF(j.site,N'''') IS NULL OR diag.is_token(j.site,200)=1)
   AND diag.is_token(j.exception_type,200)=1
   AND j.sql_number COLLATE Latin1_General_100_BIN2 LIKE N''%[0-9]%'' AND TRY_CONVERT(int,j.sql_number) IS NOT NULL
   AND diag.parse_utc(j.timestamp_utc) IS NOT NULL AND diag.parse_utc(j.timestamp_utc)<=DATEADD(day,1,@now)
   AND j.severity COLLATE Latin1_General_100_BIN2 IN(N''Error'',N''Critical'')
   AND diag.is_token(j.reference,12)=1
   AND diag.version_key(CASE WHEN LEN(j.application_version)<=20 THEN j.application_version END) IS NOT NULL
   AND (NULLIF(j.report_code,N'''') IS NULL OR diag.is_token(j.report_code,30)=1)
   AND (NULLIF(j.store_code,N'''') IS NULL OR diag.is_token(j.store_code,30)=1)
   AND (j.business_date IS NULL OR diag.parse_date(j.business_date) IS NOT NULL)
   AND (j.date_from IS NULL OR diag.parse_date(j.date_from) IS NOT NULL)
   AND (NULLIF(j.operation,N'''') IS NULL OR diag.is_token(j.operation,120)=1)
  THEN 1 ELSE 0 END) is_valid) v;

 DECLARE @accepted int=0,@duplicates int=0,@rejected int=(SELECT COUNT(*) FROM @rows WHERE is_valid=0),
         @new_entries int=0,@recurred_entries int=0;

 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @lock int;
  EXEC @lock=sys.sp_getapplock @Resource=N''ETP.ErrorRegister'',@LockMode=N''Exclusive'',@LockOwner=N''Transaction'',@LockTimeout=15000;
  IF @lock<0 THROW 51806,''The error register is busy. Try again.'',1;

  -- A Ref and time already stored, or seen earlier in this batch, is a duplicate (a lost cursor re-sends lines).
  UPDATE r SET is_duplicate=1 FROM @rows r
  WHERE r.is_valid=1 AND (
   EXISTS(SELECT 1 FROM diag.error_occurrences o WHERE o.reference=r.reference AND o.occurred_utc=r.occurred_utc)
   OR EXISTS(SELECT 1 FROM @rows e WHERE e.is_valid=1 AND e.reference=r.reference AND e.occurred_utc=r.occurred_utc AND e.ordinal<r.ordinal));
  SET @duplicates=(SELECT COUNT(*) FROM @rows WHERE is_duplicate=1);

  DECLARE @fp char(64),@fpv tinyint,@event varchar(80),@sub varchar(60),@source varchar(128),@site varchar(200),
          @exception varchar(200),@sql int,@ts datetime2(3),@severity varchar(10),@ref char(12),@ver varchar(20),@key bigint,
          @report varchar(30),@store varchar(30),@business date,@from date,@operation nvarchar(120),@stack nvarchar(3400),
          @target int,@status varchar(10),@fixed_key bigint,@day datetime2(3),@hops int;
  DECLARE items CURSOR LOCAL FAST_FORWARD FOR
   SELECT fingerprint,fingerprint_version,event_code,sub_code,source,site,exception_type,sql_number,occurred_utc,severity,reference,
          app_version,app_version_key,report_code,store_code,business_date,date_from,operation,stack
   FROM @rows WHERE is_valid=1 AND is_duplicate=0 ORDER BY occurred_utc,ordinal;
  OPEN items;
  FETCH NEXT FROM items INTO @fp,@fpv,@event,@sub,@source,@site,@exception,@sql,@ts,@severity,@ref,@ver,@key,
    @report,@store,@business,@from,@operation,@stack;
  WHILE @@FETCH_STATUS=0
  BEGIN
   SET @target=NULL;
   SELECT @target=error_register_id FROM diag.error_register WHERE fingerprint=@fp;
   IF @target IS NULL
   BEGIN
    INSERT diag.error_register(fingerprint,fingerprint_version,event_code,sub_code,source,site,exception_type,sql_number,
      max_severity,status,first_seen_utc,last_seen_utc,occurrence_count,first_version,last_version,latest_stack,
      status_changed_utc,status_changed_by)
    VALUES(@fp,@fpv,@event,@sub,@source,@site,@exception,@sql,@severity,''New'',@ts,@ts,0,@ver,@ver,@stack,@now,N''Ingest'');
    SET @target=SCOPE_IDENTITY();
    SET @new_entries+=1;
   END;
   -- A merged entry''s fingerprint counts on the entry it was merged into (merges keep chains one step long).
   SET @hops=0;
   WHILE @hops<10 AND EXISTS(SELECT 1 FROM diag.error_register WHERE error_register_id=@target AND merged_into_id IS NOT NULL)
   BEGIN
    SELECT @target=merged_into_id FROM diag.error_register WHERE error_register_id=@target;
    SET @hops+=1;
   END;
   SELECT @status=status,@fixed_key=fixed_in_version_key FROM diag.error_register WHERE error_register_id=@target;
   IF @status=''Fixed'' AND @key>=@fixed_key SET @recurred_entries+=1;

   UPDATE diag.error_register SET
    max_severity=CASE WHEN @severity=''Critical'' THEN ''Critical'' ELSE max_severity END,
    first_version=CASE WHEN @ts<first_seen_utc THEN @ver ELSE first_version END,
    first_seen_utc=CASE WHEN @ts<first_seen_utc THEN @ts ELSE first_seen_utc END,
    last_version=CASE WHEN @ts>=last_seen_utc THEN @ver ELSE last_version END,
    latest_stack=CASE WHEN @ts>=last_seen_utc AND @stack IS NOT NULL THEN @stack ELSE latest_stack END,
    last_seen_utc=CASE WHEN @ts>=last_seen_utc THEN @ts ELSE last_seen_utc END,
    occurrence_count=occurrence_count+1,
    status=CASE WHEN status=''Fixed'' AND @key>=fixed_in_version_key THEN ''Recurred'' ELSE status END,
    recurred_utc=CASE WHEN status=''Fixed'' AND @key>=fixed_in_version_key THEN @ts ELSE recurred_utc END,
    recurred_version=CASE WHEN status=''Fixed'' AND @key>=fixed_in_version_key THEN @ver ELSE recurred_version END,
    status_changed_utc=CASE WHEN status=''Fixed'' AND @key>=fixed_in_version_key THEN @now ELSE status_changed_utc END,
    status_changed_by=CASE WHEN status=''Fixed'' AND @key>=fixed_in_version_key THEN N''Ingest'' ELSE status_changed_by END
   WHERE error_register_id=@target;

   UPDATE diag.error_register_versions SET occurrence_count=occurrence_count+1,
    first_seen_utc=CASE WHEN @ts<first_seen_utc THEN @ts ELSE first_seen_utc END,
    last_seen_utc=CASE WHEN @ts>last_seen_utc THEN @ts ELSE last_seen_utc END
   WHERE error_register_id=@target AND app_version=@ver;
   IF @@ROWCOUNT=0
    INSERT diag.error_register_versions(error_register_id,app_version,app_version_key,occurrence_count,first_seen_utc,last_seen_utc)
    VALUES(@target,@ver,@key,1,@ts,@ts);

   -- Spam guard: at most 200 stored occurrences of one entry per UTC day; the counters above still grow.
   SET @day=CONVERT(datetime2(3),CONVERT(date,@ts));
   IF (SELECT COUNT(*) FROM diag.error_occurrences WHERE error_register_id=@target AND occurred_utc>=@day AND occurred_utc<DATEADD(day,1,@day))<200
    INSERT diag.error_occurrences(error_register_id,occurred_utc,reference,app_version,severity,report_code,store_code,
      business_date,date_from,operation,reporter_role,ingested_utc)
    VALUES(@target,@ts,@ref,@ver,@severity,@report,@store,@business,@from,@operation,@role,@now);

   SET @accepted+=1;
   FETCH NEXT FROM items INTO @fp,@fpv,@event,@sub,@source,@site,@exception,@sql,@ts,@severity,@ref,@ver,@key,
     @report,@store,@business,@from,@operation,@stack;
  END;
  CLOSE items; DEALLOCATE items;

  -- Retention (decision 36, Q14): occurrences older than 180 days go, but every entry keeps its newest 20. Only
  -- entries that hold an occurrence older than 180 days are ranked (IX_error_occurrences_time finds them), so an
  -- ingest does not sort the whole table under the register lock (1.11.0 review finding 9).
  WITH ranked AS(
   SELECT o.occurred_utc,ROW_NUMBER() OVER(PARTITION BY o.error_register_id ORDER BY o.occurred_utc DESC,o.error_occurrence_id DESC) newest
   FROM diag.error_occurrences o
   WHERE o.error_register_id IN(SELECT x.error_register_id FROM diag.error_occurrences x WHERE x.occurred_utc<DATEADD(day,-180,@now)))
  DELETE FROM ranked WHERE newest>20 AND occurred_utc<DATEADD(day,-180,@now);

  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;

 SELECT @accepted accepted,@duplicates duplicates,@rejected rejected,@new_entries new_entries,@recurred_entries recurred_entries;
END');

-- diag.load_error_register: the register screen and ERR-02..04. Merged entries are left out.
EXEC(N'CREATE OR ALTER PROCEDURE diag.load_error_register @status varchar(10)=NULL,@reference char(12)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 SELECT e.error_register_id,e.fingerprint,e.fingerprint_version,e.event_code,e.sub_code,e.source,e.site,e.exception_type,e.sql_number,
   e.max_severity,e.status,e.first_seen_utc,e.last_seen_utc,e.occurrence_count,e.first_version,e.last_version,e.fixed_in_version,
   e.fixed_ticket,e.fixed_utc,e.recurred_utc,e.recurred_version,e.latest_stack,e.status_reason,e.status_changed_utc,
   e.status_changed_by,e.merged_into_id
 FROM diag.error_register e
 WHERE e.merged_into_id IS NULL
   AND (@status IS NULL OR e.status=@status)
   AND (@reference IS NULL OR EXISTS(SELECT 1 FROM diag.error_occurrences o
        WHERE o.error_register_id=e.error_register_id
          AND o.reference COLLATE Latin1_General_100_CI_AS=RTRIM(@reference) COLLATE Latin1_General_100_CI_AS))
 ORDER BY e.last_seen_utc DESC,e.error_register_id DESC;
END');

-- diag.load_error_entry: one entry (zero rows when not found), its versions, its latest 20 occurrences. Owner only.
EXEC(N'CREATE OR ALTER PROCEDURE diag.load_error_entry @error_register_id int
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51801,''Only an Owner can open an error register entry.'',1;
 SELECT e.error_register_id,e.fingerprint,e.fingerprint_version,e.event_code,e.sub_code,e.source,e.site,e.exception_type,e.sql_number,
   e.max_severity,e.status,e.first_seen_utc,e.last_seen_utc,e.occurrence_count,e.first_version,e.last_version,e.fixed_in_version,
   e.fixed_ticket,e.fixed_utc,e.recurred_utc,e.recurred_version,e.latest_stack,e.status_reason,e.status_changed_utc,
   e.status_changed_by,e.merged_into_id
 FROM diag.error_register e WHERE e.error_register_id=@error_register_id;
 SELECT v.app_version,v.occurrence_count,v.first_seen_utc,v.last_seen_utc
 FROM diag.error_register_versions v WHERE v.error_register_id=@error_register_id
 ORDER BY v.app_version_key DESC,v.app_version DESC;
 SELECT TOP(20) o.error_occurrence_id,o.occurred_utc,o.reference,o.app_version,o.severity,o.report_code,o.store_code,
   o.business_date,o.date_from,o.operation,o.reporter_role,o.ingested_utc
 FROM diag.error_occurrences o WHERE o.error_register_id=@error_register_id
 ORDER BY o.occurred_utc DESC,o.error_occurrence_id DESC;
END');

-- diag.set_error_status: the Owner's actions. Allowed: New|Seen|Recurred -> Seen; New|Seen -> Expected (reason);
-- Expected -> New (undo); New|Seen|Recurred -> Fixed (manual fallback: version and reason). A ticket id, when given,
-- is kept with a Seen or Fixed entry. A manual Fixed for a version the entry was already seen on (or later) is at
-- once Recurred, as the fix manifest does.
EXEC(N'CREATE OR ALTER PROCEDURE diag.set_error_status @error_register_id int,@status varchar(10),@fixed_in_version varchar(20)=NULL,
 @ticket varchar(12)=NULL,@reason nvarchar(200)=NULL
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51801,''Only an Owner can change the status of an error register entry.'',1;
 IF @status IS NULL OR @status COLLATE Latin1_General_100_BIN2 NOT IN(''Seen'',''Expected'',''New'',''Fixed'')
  THROW 51805,''This status change is not allowed.'',1;
 IF @status IN(''Expected'',''Fixed'') AND dbo.is_blank_text(@reason)=1
  THROW 51802,''Enter a reason for this status.'',1;
 IF dbo.is_blank_text(@reason)=0 AND diag.is_safe_text(@reason,200)=0
  THROW 51803,''The reason must be plain text of at most 200 characters, without line breaks.'',1;
 IF NULLIF(@ticket,'''') IS NOT NULL AND diag.is_token(@ticket,12)=0
  THROW 51803,''The ticket id is not valid.'',1;
 DECLARE @key bigint=diag.version_key(@fixed_in_version);
 IF @status=''Fixed'' AND @key IS NULL
  THROW 51803,''The fixed-in version is not valid.'',1;

 DECLARE @now datetime2(3)=SYSUTCDATETIME(),@current varchar(10),@merged int,@seen_version varchar(20),@seen_utc datetime2(3);
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @lock int;
  EXEC @lock=sys.sp_getapplock @Resource=N''ETP.ErrorRegister'',@LockMode=N''Exclusive'',@LockOwner=N''Transaction'',@LockTimeout=15000;
  IF @lock<0 THROW 51806,''The error register is busy. Try again.'',1;
  SELECT @current=status,@merged=merged_into_id FROM diag.error_register WHERE error_register_id=@error_register_id;
  IF @current IS NULL OR @merged IS NOT NULL
   THROW 51805,''This error register entry was not found, or it was merged into another entry.'',1;
  IF NOT((@status=''Seen'' AND @current IN(''New'',''Seen'',''Recurred''))
      OR (@status=''Expected'' AND @current IN(''New'',''Seen''))
      OR (@status=''New'' AND @current=''Expected'')
      OR (@status=''Fixed'' AND @current IN(''New'',''Seen'',''Recurred'')))
   THROW 51805,''This status change is not allowed.'',1;

  UPDATE diag.error_register SET
   status=@status,
   status_reason=CASE WHEN dbo.is_blank_text(@reason)=1 THEN NULL ELSE LTRIM(RTRIM(@reason)) END,
   fixed_ticket=CASE WHEN @status IN(''Seen'',''Fixed'') AND NULLIF(@ticket,'''') IS NOT NULL THEN @ticket ELSE fixed_ticket END,
   fixed_in_version=CASE WHEN @status=''Fixed'' THEN @fixed_in_version ELSE fixed_in_version END,
   fixed_in_version_key=CASE WHEN @status=''Fixed'' THEN @key ELSE fixed_in_version_key END,
   fixed_utc=CASE WHEN @status=''Fixed'' THEN @now ELSE fixed_utc END,
   status_changed_utc=@now,
   status_changed_by=ORIGINAL_LOGIN()
  WHERE error_register_id=@error_register_id;

  IF @status=''Fixed''
  BEGIN
   SELECT TOP(1) @seen_version=app_version,@seen_utc=last_seen_utc FROM diag.error_register_versions
   WHERE error_register_id=@error_register_id AND app_version_key>=@key ORDER BY app_version_key DESC,last_seen_utc DESC;
   IF @seen_version IS NOT NULL
    UPDATE diag.error_register SET status=''Recurred'',recurred_version=@seen_version,recurred_utc=@seen_utc
    WHERE error_register_id=@error_register_id;
  END;
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END');

-- diag.merge_error_entries: the Owner's Merge (a renamed method split one bug in two). Occurrences and version counts
-- move to @into_id; its counts, dates and severity widen; @from_id keeps its row (and fingerprint) with merged_into_id
-- set, so later occurrences of that fingerprint count on @into_id. Entries merged into @from_id are re-pointed.
EXEC(N'CREATE OR ALTER PROCEDURE diag.merge_error_entries @from_id int,@into_id int,@reason nvarchar(200)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51801,''Only an Owner can merge error register entries.'',1;
 IF dbo.is_blank_text(@reason)=1 THROW 51802,''Enter a reason for the merge.'',1;
 IF diag.is_safe_text(@reason,200)=0
  THROW 51803,''The reason must be plain text of at most 200 characters, without line breaks.'',1;
 IF @from_id IS NULL OR @into_id IS NULL OR @from_id=@into_id
  THROW 51805,''An entry cannot be merged into itself.'',1;

 DECLARE @now datetime2(3)=SYSUTCDATETIME();
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @lock int;
  EXEC @lock=sys.sp_getapplock @Resource=N''ETP.ErrorRegister'',@LockMode=N''Exclusive'',@LockOwner=N''Transaction'',@LockTimeout=15000;
  IF @lock<0 THROW 51806,''The error register is busy. Try again.'',1;
  IF NOT EXISTS(SELECT 1 FROM diag.error_register WHERE error_register_id=@from_id AND merged_into_id IS NULL)
     OR NOT EXISTS(SELECT 1 FROM diag.error_register WHERE error_register_id=@into_id AND merged_into_id IS NULL)
   THROW 51805,''Both entries must exist and neither may already be merged.'',1;

  UPDATE diag.error_occurrences SET error_register_id=@into_id WHERE error_register_id=@from_id;

  UPDATE t SET occurrence_count=t.occurrence_count+f.occurrence_count,
   first_seen_utc=CASE WHEN f.first_seen_utc<t.first_seen_utc THEN f.first_seen_utc ELSE t.first_seen_utc END,
   last_seen_utc=CASE WHEN f.last_seen_utc>t.last_seen_utc THEN f.last_seen_utc ELSE t.last_seen_utc END
  FROM diag.error_register_versions t
  JOIN diag.error_register_versions f ON f.error_register_id=@from_id AND f.app_version=t.app_version
  WHERE t.error_register_id=@into_id;
  INSERT diag.error_register_versions(error_register_id,app_version,app_version_key,occurrence_count,first_seen_utc,last_seen_utc)
  SELECT @into_id,f.app_version,f.app_version_key,f.occurrence_count,f.first_seen_utc,f.last_seen_utc
  FROM diag.error_register_versions f
  WHERE f.error_register_id=@from_id
    AND NOT EXISTS(SELECT 1 FROM diag.error_register_versions t WHERE t.error_register_id=@into_id AND t.app_version=f.app_version);
  DELETE diag.error_register_versions WHERE error_register_id=@from_id;

  UPDATE t SET
   occurrence_count=t.occurrence_count+f.occurrence_count,
   max_severity=CASE WHEN f.max_severity=''Critical'' THEN ''Critical'' ELSE t.max_severity END,
   first_version=CASE WHEN f.first_seen_utc<t.first_seen_utc THEN f.first_version ELSE t.first_version END,
   first_seen_utc=CASE WHEN f.first_seen_utc<t.first_seen_utc THEN f.first_seen_utc ELSE t.first_seen_utc END,
   last_version=CASE WHEN f.last_seen_utc>t.last_seen_utc THEN f.last_version ELSE t.last_version END,
   latest_stack=CASE WHEN f.last_seen_utc>t.last_seen_utc AND f.latest_stack IS NOT NULL THEN f.latest_stack ELSE t.latest_stack END,
   last_seen_utc=CASE WHEN f.last_seen_utc>t.last_seen_utc THEN f.last_seen_utc ELSE t.last_seen_utc END
  FROM diag.error_register t CROSS JOIN diag.error_register f
  WHERE t.error_register_id=@into_id AND f.error_register_id=@from_id;

  UPDATE diag.error_register SET merged_into_id=@into_id WHERE merged_into_id=@from_id;
  UPDATE diag.error_register SET merged_into_id=@into_id,status_reason=LTRIM(RTRIM(@reason)),
   status_changed_utc=@now,status_changed_by=ORIGINAL_LOGIN()
  WHERE error_register_id=@from_id;
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END');

-- diag.apply_error_fix_manifest: ErrorFixManifest.json, applied by the version that ships it. A line whose fixedIn is
-- newer than the running version is skipped (an old PC never marks a fix it does not have). New, Seen and Recurred
-- (with an older fix) become Fixed; if the entry was already seen on the fixed version or later it is Recurred at once.
-- Already Fixed, or Recurred after this fix: already_fixed. Expected entries are left alone and counted as skipped.
-- Idempotent.
EXEC(N'CREATE OR ALTER PROCEDURE diag.apply_error_fix_manifest @manifest nvarchar(max),@running_version varchar(20)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 DECLARE @running bigint=diag.version_key(@running_version);
 IF @running IS NULL THROW 51804,''The fix manifest was refused: the running version is not valid.'',1;
 IF @manifest IS NULL OR ISJSON(@manifest)<>1
    OR TRY_CONVERT(int,JSON_VALUE(@manifest,''$.manifest''))IS NULL OR TRY_CONVERT(int,JSON_VALUE(@manifest,''$.manifest''))<>1
    OR COALESCE(LEFT(JSON_QUERY(@manifest,''$.fixes''),1),N'''')<>N''[''
  THROW 51804,''The fix manifest is not valid.'',1;

 DECLARE @lines TABLE(ordinal int NOT NULL PRIMARY KEY,fingerprint char(64) NULL,fixed_in varchar(20) NULL,fixed_key bigint NULL,
   ticket varchar(12) NULL,is_valid bit NOT NULL);
 INSERT @lines(ordinal,fingerprint,fixed_in,fixed_key,ticket,is_valid)
 SELECT CONVERT(int,a.[key]),
   CASE WHEN LEN(j.fingerprint)=64 THEN j.fingerprint END,
   CASE WHEN LEN(j.fixed_in)<=20 THEN j.fixed_in END,
   diag.version_key(CASE WHEN LEN(j.fixed_in)<=20 THEN j.fixed_in END),
   CASE WHEN LEN(j.ticket)<=12 THEN NULLIF(j.ticket,N'''') END,
   CASE WHEN a.[type]=5
     AND LEN(j.fingerprint)=64 AND DATALENGTH(j.fingerprint)=128 AND j.fingerprint COLLATE Latin1_General_100_BIN2 NOT LIKE N''%[^0-9a-f]%''
     AND diag.version_key(CASE WHEN LEN(j.fixed_in)<=20 THEN j.fixed_in END) IS NOT NULL
     AND (NULLIF(j.ticket,N'''') IS NULL OR diag.is_token(j.ticket,12)=1)
   THEN 1 ELSE 0 END
 FROM OPENJSON(@manifest,''$.fixes'') a
 OUTER APPLY OPENJSON(CASE WHEN a.[type]=5 THEN a.value ELSE N''{}'' END) WITH(
   fingerprint nvarchar(max) ''$.fingerprint'',fixed_in nvarchar(max) ''$.fixedIn'',ticket nvarchar(max) ''$.ticket'') j;
 IF (SELECT COUNT(*) FROM @lines)>5000 OR EXISTS(SELECT 1 FROM @lines WHERE is_valid=0)
  THROW 51804,''The fix manifest is not valid: a line has a bad fingerprint, version or ticket.'',1;

 DECLARE @now datetime2(3)=SYSUTCDATETIME(),@lines_count int=(SELECT COUNT(*) FROM @lines),
         @marked int=0,@already int=0,@not_found int=0,@skipped int=0;
 DECLARE @fp char(64),@fixed_in varchar(20),@fixed_key bigint,@ticket varchar(12),@id int,@status varchar(10),@current_key bigint,
         @seen_version varchar(20),@seen_utc datetime2(3);
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @lock int;
  EXEC @lock=sys.sp_getapplock @Resource=N''ETP.ErrorRegister'',@LockMode=N''Exclusive'',@LockOwner=N''Transaction'',@LockTimeout=15000;
  IF @lock<0 THROW 51806,''The error register is busy. Try again.'',1;
  DECLARE fixes CURSOR LOCAL FAST_FORWARD FOR SELECT fingerprint,fixed_in,fixed_key,ticket FROM @lines ORDER BY ordinal;
  OPEN fixes;
  FETCH NEXT FROM fixes INTO @fp,@fixed_in,@fixed_key,@ticket;
  WHILE @@FETCH_STATUS=0
  BEGIN
   SET @id=NULL; SET @status=NULL; SET @current_key=NULL; SET @seen_version=NULL; SET @seen_utc=NULL;
   IF @fixed_key>@running SET @skipped+=1;
   ELSE
   BEGIN
    SELECT @id=COALESCE(merged_into_id,error_register_id) FROM diag.error_register WHERE fingerprint=@fp;
    IF @id IS NULL SET @not_found+=1;
    ELSE
    BEGIN
     SELECT @status=status,@current_key=fixed_in_version_key FROM diag.error_register WHERE error_register_id=@id;
     IF @status=''Fixed'' OR (@status=''Recurred'' AND @current_key>=@fixed_key) SET @already+=1;
     ELSE IF @status=''Expected'' SET @skipped+=1;
     ELSE
     BEGIN
      UPDATE diag.error_register SET status=''Fixed'',fixed_in_version=@fixed_in,fixed_in_version_key=@fixed_key,
       fixed_ticket=COALESCE(@ticket,fixed_ticket),fixed_utc=@now,status_changed_utc=@now,status_changed_by=N''FixManifest''
      WHERE error_register_id=@id;
      SELECT TOP(1) @seen_version=app_version,@seen_utc=last_seen_utc FROM diag.error_register_versions
      WHERE error_register_id=@id AND app_version_key>=@fixed_key ORDER BY app_version_key DESC,last_seen_utc DESC;
      IF @seen_version IS NOT NULL
       UPDATE diag.error_register SET status=''Recurred'',recurred_version=@seen_version,recurred_utc=@seen_utc
       WHERE error_register_id=@id;
      SET @marked+=1;
     END;
    END;
   END;
   FETCH NEXT FROM fixes INTO @fp,@fixed_in,@fixed_key,@ticket;
  END;
  CLOSE fixes; DEALLOCATE fixes;
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;

 SELECT @lines_count lines,@marked entries_marked_fixed,@already already_fixed,@not_found not_found,@skipped skipped;
END');

-- Grants (design 6.4). No role has any permission on the diag tables or helper functions.
GRANT EXECUTE ON diag.ingest_error_occurrences TO etp_owner,etp_store_manager,etp_viewer,etp_automation;
GRANT EXECUTE ON diag.load_error_register TO etp_owner,etp_automation;
GRANT EXECUTE ON diag.load_error_entry TO etp_owner;
GRANT EXECUTE ON diag.set_error_status TO etp_owner;
GRANT EXECUTE ON diag.merge_error_entries TO etp_owner;
GRANT EXECUTE ON diag.apply_error_fix_manifest TO etp_owner,etp_automation;
