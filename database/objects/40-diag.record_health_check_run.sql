CREATE OR ALTER PROCEDURE diag.record_health_check_run @run nvarchar(max)
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF @run IS NULL OR ISJSON(@run)<>1 OR COALESCE(LEFT(JSON_QUERY(@run,'$.results'),1),N'')<>N'['
    OR COALESCE(LEFT(JSON_QUERY(@run,'$.counts'),1),N'')<>N'{'
  THROW 51820,'The health check run was refused: it is not a valid run.',1;
 IF (SELECT COUNT(*) FROM OPENJSON(@run,'$.results'))>80
  THROW 51821,'The health check run was refused: it has more than 80 results.',1;

 DECLARE @trigger nvarchar(max),@business nvarchar(max),@started nvarchar(max),@completed nvarchar(max),@duration nvarchar(max),
   @version nvarchar(max),@machine nvarchar(max),@role nvarchar(max),@overall nvarchar(max),@export nvarchar(max),
   @pass nvarchar(max),@warning nvarchar(max),@fail nvarchar(max),@known nvarchar(max),@skipped nvarchar(max);
 SELECT @trigger=h.trigger_kind,@business=h.business_date,@started=h.started_utc,@completed=h.completed_utc,@duration=h.duration_ms,
   @version=h.app_version,@machine=h.machine_name,@role=h.run_by_role,@overall=h.overall,@export=h.export_file_name,
   @pass=h.pass_count,@warning=h.warning_count,@fail=h.fail_count,@known=h.known_count,@skipped=h.skipped_count
 FROM OPENJSON(@run) WITH(
   trigger_kind nvarchar(max) '$.trigger',business_date nvarchar(max) '$.businessDate',started_utc nvarchar(max) '$.startedUtc',
   completed_utc nvarchar(max) '$.completedUtc',duration_ms nvarchar(max) '$.durationMs',app_version nvarchar(max) '$.appVersion',
   machine_name nvarchar(max) '$.machineName',run_by_role nvarchar(max) '$.runByRole',overall nvarchar(max) '$.overall',
   export_file_name nvarchar(max) '$.exportFileName',pass_count nvarchar(max) '$.counts.pass',warning_count nvarchar(max) '$.counts.warning',
   fail_count nvarchar(max) '$.counts.fail',known_count nvarchar(max) '$.counts.known',skipped_count nvarchar(max) '$.counts.skipped') h;

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
   CASE WHEN v.is_valid=1 THEN NULLIF(r.fix_task_id,N'') END,
   CASE WHEN v.is_valid=1 THEN TRY_CONVERT(int,r.duration_ms) END,
   CASE WHEN v.is_valid=1 THEN v.metric END,
   CASE WHEN v.is_valid=1 THEN
     CASE WHEN r.detail IS NULL THEN NULL
          WHEN LEN(r.detail)<=2000 THEN r.detail
          ELSE (SELECT N'['+STRING_AGG(CONVERT(nvarchar(max),x.value),N',') WITHIN GROUP(ORDER BY x.position)+N']'
                FROM (SELECT CONVERT(int,e.[key]) position,e.value,
                        SUM(LEN(e.value)+1) OVER(ORDER BY CONVERT(int,e.[key]) ROWS UNBOUNDED PRECEDING) running
                      FROM OPENJSON(r.detail) e) x
                WHERE x.running<=1998) END END,
   v.is_valid
 FROM OPENJSON(@run,'$.results') a
 OUTER APPLY OPENJSON(CASE WHEN a.[type]=5 THEN a.value ELSE N'{}' END) WITH(
   check_id nvarchar(max) '$.checkId',area nvarchar(max) '$.area',outcome nvarchar(max) '$.outcome',
   finding_code nvarchar(max) '$.findingCode',explanation nvarchar(max) '$.explanation',fix_task_id nvarchar(max) '$.fixTaskId',
   duration_ms nvarchar(max) '$.durationMs',metric_value nvarchar(max) '$.metricValue',detail nvarchar(max) '$.detail' AS JSON) r
 CROSS APPLY(SELECT COALESCE(TRY_CONVERT(decimal(19,4),r.metric_value),TRY_CONVERT(decimal(19,4),TRY_CONVERT(float,r.metric_value))) metric) m
 CROSS APPLY(SELECT m.metric,CONVERT(bit,CASE WHEN a.[type]=5
   AND diag.is_check_id(r.check_id)=1
   AND r.area COLLATE Latin1_General_100_BIN2 IN(N'Database',N'Security',N'Reports',N'Imports',N'Data',N'Operations',N'Errors')
   AND r.outcome COLLATE Latin1_General_100_BIN2 IN(N'Pass',N'Warning',N'Fail',N'Known',N'Skipped')
   AND diag.is_code(r.finding_code,60)=1
   AND diag.is_safe_text(r.explanation,400)=1
   AND (NULLIF(r.fix_task_id,N'') IS NULL OR diag.is_token(r.fix_task_id,40)=1)
   AND r.duration_ms COLLATE Latin1_General_100_BIN2 LIKE N'%[0-9]%' AND TRY_CONVERT(int,r.duration_ms)>=0
   AND (r.metric_value IS NULL OR m.metric IS NOT NULL)
   AND diag.is_safe_detail(r.detail)=1
  THEN 1 ELSE 0 END) is_valid) v;

 IF EXISTS(SELECT 1 FROM @results WHERE is_valid=0)
    OR EXISTS(SELECT check_id FROM @results GROUP BY check_id HAVING COUNT(*)>1)
    OR @trigger IS NULL OR @trigger COLLATE Latin1_General_100_BIN2 NOT IN(N'Manual',N'Nightly')
    OR diag.parse_date(@business) IS NULL
    OR diag.parse_utc(@started) IS NULL OR diag.parse_utc(@completed) IS NULL
    OR diag.parse_utc(@completed)<diag.parse_utc(@started)
    OR @duration COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[0-9]%' OR TRY_CONVERT(int,@duration) IS NULL OR TRY_CONVERT(int,@duration)<0
    OR diag.version_key(CASE WHEN LEN(@version)<=20 THEN @version END) IS NULL
    OR diag.is_token(@machine,64)=0
    OR @role IS NULL OR @role COLLATE Latin1_General_100_BIN2 NOT IN(N'Owner',N'StoreManager',N'Viewer',N'Automation',N'Unknown')
    OR @overall IS NULL OR @overall COLLATE Latin1_General_100_BIN2 NOT IN(N'Pass',N'Warning',N'Fail')
    OR (NULLIF(@export,N'') IS NOT NULL AND diag.is_token(@export,120)=0)
    OR COALESCE(TRY_CONVERT(int,@pass),-1)<>(SELECT COUNT(*) FROM @results WHERE outcome='Pass')
    OR COALESCE(TRY_CONVERT(int,@warning),-1)<>(SELECT COUNT(*) FROM @results WHERE outcome='Warning')
    OR COALESCE(TRY_CONVERT(int,@fail),-1)<>(SELECT COUNT(*) FROM @results WHERE outcome='Fail')
    OR COALESCE(TRY_CONVERT(int,@known),-1)<>(SELECT COUNT(*) FROM @results WHERE outcome='Known')
    OR COALESCE(TRY_CONVERT(int,@skipped),-1)<>(SELECT COUNT(*) FROM @results WHERE outcome='Skipped')
  THROW 51820,'The health check run was refused: it is not a valid run.',1;

 DECLARE @now datetime2(3)=SYSUTCDATETIME(),@id bigint;
 BEGIN TRY
  BEGIN TRANSACTION;
  INSERT diag.health_check_runs(trigger_kind,business_date,started_utc,completed_utc,duration_ms,app_version,machine_name,run_by_role,
    overall,pass_count,warning_count,fail_count,known_count,skipped_count,export_file_name)
  VALUES(@trigger,diag.parse_date(@business),diag.parse_utc(@started),diag.parse_utc(@completed),CONVERT(int,@duration),@version,@machine,@role,
    @overall,CONVERT(smallint,@pass),CONVERT(smallint,@warning),CONVERT(smallint,@fail),CONVERT(smallint,@known),CONVERT(smallint,@skipped),
    NULLIF(@export,N''));
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
END
-- >>> GRANTS
GRANT EXECUTE ON diag.record_health_check_run TO etp_automation,etp_owner;
