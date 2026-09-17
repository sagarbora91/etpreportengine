-- Exact duplicates and pre-persistence failures do not create canonical import_files.
-- Record those attempts separately without altering fact identity or day locks.
SET XACT_ABORT ON;
CREATE TABLE dbo.import_attempts (
 import_attempt_id bigint IDENTITY PRIMARY KEY,
 import_file_id bigint NULL REFERENCES dbo.import_files(import_file_id),
 recorded_utc datetime2 NOT NULL CONSTRAINT DF_import_attempts_utc DEFAULT SYSUTCDATETIME(),
 file_name nvarchar(260) NOT NULL,
 report_code varchar(40) NULL,
 store_code varchar(30) NULL,
 period_start date NULL,
 period_end date NULL,
 outcome varchar(40) NOT NULL,
 rows_processed int NOT NULL,
 new_rows int NOT NULL,
 already_present_rows int NOT NULL,
 conflict_rows int NOT NULL,
 diagnostics_json nvarchar(max) NOT NULL,
 CONSTRAINT CK_import_attempts_counts CHECK(rows_processed>=0 AND new_rows>=0 AND already_present_rows>=0 AND conflict_rows>=0),
 CONSTRAINT CK_import_attempts_period CHECK(period_start IS NULL OR period_end>=period_start),
 CONSTRAINT CK_import_attempts_json CHECK(ISJSON(diagnostics_json)=1)
);
CREATE INDEX IX_import_attempts_scope ON dbo.import_attempts(store_code,period_start,period_end,recorded_utc);
GRANT SELECT ON dbo.import_attempts TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.import_attempts TO etp_viewer,etp_store_manager,etp_owner;

EXEC(N'CREATE PROCEDURE dbo.record_import_attempt
 @name nvarchar(260),@hash char(64)=NULL,@report varchar(40)=NULL,@store varchar(30)=NULL,
 @start date=NULL,@end date=NULL,@outcome varchar(40),@rows int,@new int,@present int,@conflicts int,
 @diagnostics nvarchar(max)
AS BEGIN
 SET NOCOUNT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @name LIKE N''%\%'' OR @name LIKE N''%/%'' OR @name LIKE N''%:%'' OR ISJSON(@diagnostics)<>1
  THROW 51422,''Provide a file name and valid safe diagnostics.'',1;
 IF @outcome NOT IN(''Imported'',''empty export'',''Duplicate'',''Duplicate content'',''Already present'',''Failed'',''Unknown layout'',''Not needed'',''Cancelled'')
  THROW 51422,''Unsupported import outcome.'',1;
 DECLARE @file bigint;
 SELECT TOP(1) @file=f.import_file_id FROM dbo.import_files f
 WHERE f.source_sha256=@hash AND f.report_code=@report AND f.store_code=@store
   AND f.period_start=@start AND f.period_end=@end AND f.data_truth_version=1
 ORDER BY f.import_file_id DESC;
 INSERT dbo.import_attempts(import_file_id,file_name,report_code,store_code,period_start,period_end,outcome,
   rows_processed,new_rows,already_present_rows,conflict_rows,diagnostics_json)
 VALUES(@file,@name,@report,@store,@start,@end,@outcome,@rows,@new,@present,@conflicts,@diagnostics);
END');
GRANT EXECUTE ON dbo.record_import_attempt TO etp_store_manager,etp_owner;
DENY EXECUTE ON dbo.record_import_attempt TO etp_viewer;
