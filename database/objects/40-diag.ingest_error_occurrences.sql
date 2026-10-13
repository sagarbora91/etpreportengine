CREATE OR ALTER PROCEDURE diag.ingest_error_occurrences @items nvarchar(max),@app_version varchar(20)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF diag.version_key(@app_version) IS NULL
  THROW 51800,'The error batch was refused: the application version is not valid.',1;
 DECLARE @first int=PATINDEX(N'%[^ '+NCHAR(9)+NCHAR(10)+NCHAR(13)+N']%',COALESCE(@items,N''));
 IF @items IS NULL OR ISJSON(@items)<>1 OR @first=0 OR SUBSTRING(@items,@first,1)<>N'['
  THROW 51800,'The error batch was refused: it is not a JSON array.',1;
 IF (SELECT COUNT(*) FROM OPENJSON(@items))>500
  THROW 51800,'The error batch was refused: it holds more than 500 items.',1;

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
   CASE WHEN v.is_valid=1 THEN NULLIF(j.sub_code,N'') END,
   CASE WHEN v.is_valid=1 THEN j.source END,
   CASE WHEN v.is_valid=1 THEN NULLIF(j.site,N'') END,
   CASE WHEN v.is_valid=1 THEN j.exception_type END,
   CASE WHEN v.is_valid=1 THEN TRY_CONVERT(int,j.sql_number) END,
   CASE WHEN v.is_valid=1 THEN diag.parse_utc(j.timestamp_utc) END,
   CASE WHEN v.is_valid=1 THEN j.severity END,
   CASE WHEN v.is_valid=1 THEN j.reference END,
   CASE WHEN v.is_valid=1 THEN j.application_version END,
   CASE WHEN v.is_valid=1 THEN diag.version_key(j.application_version) END,
   CASE WHEN v.is_valid=1 THEN NULLIF(j.report_code,N'') END,
   CASE WHEN v.is_valid=1 THEN NULLIF(j.store_code,N'') END,
   CASE WHEN v.is_valid=1 THEN diag.parse_date(j.business_date) END,
   CASE WHEN v.is_valid=1 THEN diag.parse_date(j.date_from) END,
   CASE WHEN v.is_valid=1 THEN NULLIF(j.operation,N'') END,
   -- latest stack: the frames that are tokens, in order, as many as fit 3400 characters of JSON
   CASE WHEN v.is_valid=1 THEN (
     SELECT N'['+STRING_AGG(CONVERT(nvarchar(max),N'"'+f.value+N'"'),N',') WITHIN GROUP(ORDER BY f.position)+N']'
     FROM (SELECT TRY_CONVERT(int,s.[key]) position,s.value,
             SUM(LEN(s.value)+3) OVER(ORDER BY TRY_CONVERT(int,s.[key]) ROWS UNBOUNDED PRECEDING) running
           FROM OPENJSON(CASE WHEN LEFT(j.stack,1)=N'[' THEN j.stack END) s
           WHERE s.[type]=1 AND diag.is_token(s.value,200)=1) f
     WHERE f.running<=3390) END
 FROM OPENJSON(@items) a
 OUTER APPLY OPENJSON(CASE WHEN a.[type]=5 THEN a.value ELSE N'{}' END) WITH(
   fingerprint nvarchar(max) '$.fingerprint', fingerprint_version nvarchar(max) '$.fingerprintVersion',
   event_base nvarchar(max) '$.eventBase', sub_code nvarchar(max) '$.subCode', source nvarchar(max) '$.source',
   site nvarchar(max) '$.site', exception_type nvarchar(max) '$.exceptionType', sql_number nvarchar(max) '$.sqlNumber',
   timestamp_utc nvarchar(max) '$.timestampUtc', severity nvarchar(max) '$.severity', reference nvarchar(max) '$.reference',
   application_version nvarchar(max) '$.applicationVersion', report_code nvarchar(max) '$.reportCode',
   store_code nvarchar(max) '$.storeCode', business_date nvarchar(max) '$.businessDate', date_from nvarchar(max) '$.dateFrom',
   operation nvarchar(max) '$.operation', stack nvarchar(max) '$.stack' AS JSON) j
 CROSS APPLY(SELECT CONVERT(bit,CASE WHEN a.[type]=5
   AND LEN(j.fingerprint)=64 AND DATALENGTH(j.fingerprint)=128 AND j.fingerprint COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9a-f]%'
   AND TRY_CONVERT(tinyint,j.fingerprint_version)>=1
   AND diag.is_code(j.event_base,80)=1
   AND (NULLIF(j.sub_code,N'') IS NULL OR diag.is_token(j.sub_code,60)=1)
   AND diag.is_token(j.source,128)=1
   AND (NULLIF(j.site,N'') IS NULL OR diag.is_token(j.site,200)=1)
   AND diag.is_token(j.exception_type,200)=1
   AND j.sql_number COLLATE Latin1_General_100_BIN2 LIKE N'%[0-9]%' AND TRY_CONVERT(int,j.sql_number) IS NOT NULL
   AND diag.parse_utc(j.timestamp_utc) IS NOT NULL AND diag.parse_utc(j.timestamp_utc)<=DATEADD(day,1,@now)
   AND j.severity COLLATE Latin1_General_100_BIN2 IN(N'Error',N'Critical')
   AND diag.is_token(j.reference,12)=1
   AND diag.version_key(CASE WHEN LEN(j.application_version)<=20 THEN j.application_version END) IS NOT NULL
   AND (NULLIF(j.report_code,N'') IS NULL OR diag.is_token(j.report_code,30)=1)
   AND (NULLIF(j.store_code,N'') IS NULL OR diag.is_token(j.store_code,30)=1)
   AND (j.business_date IS NULL OR diag.parse_date(j.business_date) IS NOT NULL)
   AND (j.date_from IS NULL OR diag.parse_date(j.date_from) IS NOT NULL)
   AND (NULLIF(j.operation,N'') IS NULL OR diag.is_token(j.operation,120)=1)
  THEN 1 ELSE 0 END) is_valid) v;

 DECLARE @accepted int=0,@duplicates int=0,@rejected int=(SELECT COUNT(*) FROM @rows WHERE is_valid=0),
         @new_entries int=0,@recurred_entries int=0;

 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @lock int;
  EXEC @lock=sys.sp_getapplock @Resource=N'ETP.ErrorRegister',@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=15000;
  IF @lock<0 THROW 51806,'The error register is busy. Try again.',1;

  -- A Ref and time already stored, or seen earlier in this batch, is a duplicate (a lost cursor re-sends lines).
  UPDATE r SET is_duplicate=1 FROM @rows r
  WHERE r.is_valid=1 AND (
   EXISTS(SELECT 1 FROM diag.error_occurrences o WHERE o.reference=r.reference AND o.occurred_utc=r.occurred_utc)
   OR EXISTS(SELECT 1 FROM @rows e WHERE e.is_valid=1 AND e.reference=r.reference AND e.occurred_utc=r.occurred_utc AND e.ordinal<r.ordinal));
  SET @duplicates=(SELECT COUNT(*) FROM @rows WHERE is_duplicate=1);

  DECLARE @fp char(64),@fpv tinyint,@event varchar(80),@sub varchar(60),@source varchar(128),@site varchar(200),
          @exception varchar(200),@sql int,@ts datetime2(3),@severity varchar(10),@ref char(12),@ver varchar(20),@key bigint,
          @report varchar(30),@store varchar(30),@business date,@from date,@operation nvarchar(120),@stack nvarchar(3400),
          @target int,@status varchar(10),@fixed_key bigint,@day datetime2(3),@hops int,@skip bit;
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
    VALUES(@fp,@fpv,@event,@sub,@source,@site,@exception,@sql,@severity,'New',@ts,@ts,0,@ver,@ver,@stack,@now,N'Ingest');
    SET @target=SCOPE_IDENTITY();
    SET @new_entries+=1;
   END;
   -- A merged entry's fingerprint counts on the entry it was merged into (merges keep chains one step long).
   SET @hops=0;
   WHILE @hops<10 AND EXISTS(SELECT 1 FROM diag.error_register WHERE error_register_id=@target AND merged_into_id IS NOT NULL)
   BEGIN
    SELECT @target=merged_into_id FROM diag.error_register WHERE error_register_id=@target;
    SET @hops+=1;
   END;
   -- Overflow dedupe (1.11.0 review minor 7): the stored occurrences are the only way to spot a re-sent line, and a busy day
   -- stores at most 200. Once the day already holds 200 and this line is not newer than the entry's last_seen_utc, it was
   -- counted before (a lost cursor re-sends old lines; items arrive oldest first, so new overflow lines are always newer).
   -- It is a duplicate: it does not grow any counter. A genuinely new line older than last_seen on a spam day is dropped
   -- too; that is the accepted cost.
   SET @day=CONVERT(datetime2(3),CONVERT(date,@ts));
   SET @skip=CASE WHEN (SELECT COUNT(*) FROM diag.error_occurrences WHERE error_register_id=@target AND occurred_utc>=@day AND occurred_utc<DATEADD(day,1,@day))>=200
                   AND @ts<=(SELECT last_seen_utc FROM diag.error_register WHERE error_register_id=@target) THEN 1 ELSE 0 END;
   IF @skip=1 SET @duplicates+=1;
   IF @skip=0
   BEGIN
   SELECT @status=status,@fixed_key=fixed_in_version_key FROM diag.error_register WHERE error_register_id=@target;
   IF @status='Fixed' AND @key>=@fixed_key SET @recurred_entries+=1;

   UPDATE diag.error_register SET
    max_severity=CASE WHEN @severity='Critical' THEN 'Critical' ELSE max_severity END,
    first_version=CASE WHEN @ts<first_seen_utc THEN @ver ELSE first_version END,
    first_seen_utc=CASE WHEN @ts<first_seen_utc THEN @ts ELSE first_seen_utc END,
    last_version=CASE WHEN @ts>=last_seen_utc THEN @ver ELSE last_version END,
    latest_stack=CASE WHEN @ts>=last_seen_utc AND @stack IS NOT NULL THEN @stack ELSE latest_stack END,
    last_seen_utc=CASE WHEN @ts>=last_seen_utc THEN @ts ELSE last_seen_utc END,
    occurrence_count=occurrence_count+1,
    status=CASE WHEN status='Fixed' AND @key>=fixed_in_version_key THEN 'Recurred' ELSE status END,
    recurred_utc=CASE WHEN status='Fixed' AND @key>=fixed_in_version_key THEN @ts ELSE recurred_utc END,
    recurred_version=CASE WHEN status='Fixed' AND @key>=fixed_in_version_key THEN @ver ELSE recurred_version END,
    status_changed_utc=CASE WHEN status='Fixed' AND @key>=fixed_in_version_key THEN @now ELSE status_changed_utc END,
    status_changed_by=CASE WHEN status='Fixed' AND @key>=fixed_in_version_key THEN N'Ingest' ELSE status_changed_by END
   WHERE error_register_id=@target;

   UPDATE diag.error_register_versions SET occurrence_count=occurrence_count+1,
    first_seen_utc=CASE WHEN @ts<first_seen_utc THEN @ts ELSE first_seen_utc END,
    last_seen_utc=CASE WHEN @ts>last_seen_utc THEN @ts ELSE last_seen_utc END
   WHERE error_register_id=@target AND app_version=@ver;
   IF @@ROWCOUNT=0
    INSERT diag.error_register_versions(error_register_id,app_version,app_version_key,occurrence_count,first_seen_utc,last_seen_utc)
    VALUES(@target,@ver,@key,1,@ts,@ts);

   -- Spam guard: at most 200 stored occurrences of one entry per UTC day; the counters above still grow for new lines.
   IF (SELECT COUNT(*) FROM diag.error_occurrences WHERE error_register_id=@target AND occurred_utc>=@day AND occurred_utc<DATEADD(day,1,@day))<200
    INSERT diag.error_occurrences(error_register_id,occurred_utc,reference,app_version,severity,report_code,store_code,
      business_date,date_from,operation,reporter_role,ingested_utc)
    VALUES(@target,@ts,@ref,@ver,@severity,@report,@store,@business,@from,@operation,@role,@now);

   SET @accepted+=1;
   END;
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
END
-- >>> GRANTS
GRANT EXECUTE ON diag.ingest_error_occurrences TO etp_automation,etp_owner,etp_store_manager,etp_viewer;
