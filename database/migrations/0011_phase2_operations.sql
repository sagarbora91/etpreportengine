SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.daily_report_generations ADD
    report_document_json nvarchar(max) NULL,
    document_sha256 char(64) NULL;
EXEC(N'ALTER TABLE dbo.daily_report_generations ADD CONSTRAINT CK_daily_report_generations_document
    CHECK((report_document_json IS NULL AND document_sha256 IS NULL) OR
          (report_document_json IS NOT NULL AND ISJSON(report_document_json)=1 AND document_sha256 IS NOT NULL AND document_sha256 NOT LIKE ''%[^0-9a-f]%''))');

CREATE TABLE dbo.application_users
(
    application_user_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_application_users PRIMARY KEY,
    windows_identity nvarchar(200) NOT NULL,
    display_name nvarchar(200) NOT NULL,
    role_code varchar(30) NOT NULL,
    is_active bit NOT NULL CONSTRAINT DF_application_users_active DEFAULT(1),
    modified_by nvarchar(200) NOT NULL,
    modified_utc datetime2(3) NOT NULL CONSTRAINT DF_application_users_modified DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    CONSTRAINT UQ_application_users_identity UNIQUE(windows_identity),
    CONSTRAINT CK_application_users_role CHECK(role_code IN('OWNER','STORE_MANAGER','VIEWER')),
    CONSTRAINT CK_application_users_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0)
);

INSERT dbo.application_users(windows_identity,display_name,role_code,modified_by,change_reason)
SELECT SUSER_SNAME(),SUSER_SNAME(),'OWNER',SUSER_SNAME(),N'Initial owner created during secured Phase 2 migration'
WHERE NOT EXISTS(SELECT 1 FROM dbo.application_users WHERE role_code='OWNER' AND is_active=1);

CREATE TABLE dbo.application_user_history
(
    application_user_history_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_application_user_history PRIMARY KEY,
    application_user_id int NOT NULL,
    windows_identity nvarchar(200) NOT NULL,
    old_values_json nvarchar(max) NULL,
    new_values_json nvarchar(max) NULL,
    changed_by nvarchar(200) NOT NULL,
    changed_utc datetime2(3) NOT NULL CONSTRAINT DF_application_user_history_utc DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    CONSTRAINT CK_application_user_history_old CHECK(old_values_json IS NULL OR ISJSON(old_values_json)=1),
    CONSTRAINT CK_application_user_history_new CHECK(new_values_json IS NULL OR ISJSON(new_values_json)=1)
);
CREATE INDEX IX_application_user_history_identity ON dbo.application_user_history(windows_identity,changed_utc DESC);

ALTER TABLE dbo.stores ADD
    modified_by nvarchar(200) NULL,
    modified_utc datetime2(3) NULL,
    change_reason nvarchar(500) NULL;
EXEC(N'IF NOT EXISTS(SELECT 1 FROM dbo.stores WHERE store_code=''WLMHW'')
    INSERT dbo.stores(store_code,store_name,is_active,modified_by,modified_utc,change_reason)
    VALUES(''WLMHW'',N''Titan World'',1,SUSER_SNAME(),SYSUTCDATETIME(),N''Known ETP store seeded from verified source reports'');
IF NOT EXISTS(SELECT 1 FROM dbo.stores WHERE store_code=''HEMW'')
    INSERT dbo.stores(store_code,store_name,is_active,modified_by,modified_utc,change_reason)
    VALUES(''HEMW'',N''Helios'',1,SUSER_SNAME(),SYSUTCDATETIME(),N''Known ETP store seeded from verified source reports'');');

CREATE TABLE dbo.controlled_master_values
(
    controlled_master_value_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_controlled_master_values PRIMARY KEY,
    master_type varchar(30) NOT NULL,
    master_code nvarchar(100) NOT NULL,
    display_name nvarchar(200) NOT NULL,
    approval_status varchar(20) NOT NULL CONSTRAINT DF_controlled_master_values_approval DEFAULT('OBSERVED'),
    is_active bit NOT NULL CONSTRAINT DF_controlled_master_values_active DEFAULT(1),
    modified_by nvarchar(200) NOT NULL,
    modified_utc datetime2(3) NOT NULL CONSTRAINT DF_controlled_master_values_modified DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    CONSTRAINT UQ_controlled_master_values UNIQUE(master_type,master_code),
    CONSTRAINT CK_controlled_master_values_type CHECK(master_type IN('BRAND_SEGMENT','INVENTORY_GROUP','TENDER')),
    CONSTRAINT CK_controlled_master_values_approval CHECK(approval_status IN('OBSERVED','APPROVED','QUARANTINED')),
    CONSTRAINT CK_controlled_master_values_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0)
);
CREATE INDEX IX_controlled_master_values_type ON dbo.controlled_master_values(master_type,is_active,master_code);

INSERT dbo.controlled_master_values(master_type,master_code,display_name,approval_status,modified_by,change_reason)
VALUES('BRAND_SEGMENT',N'GAUTO',N'Titan Automatic','APPROVED',SUSER_SNAME(),N'Confirmed by Owner from ETP CLUSTER evidence');

CREATE TABLE dbo.controlled_master_history
(
    controlled_master_history_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_controlled_master_history PRIMARY KEY,
    controlled_master_value_id int NOT NULL,
    master_type varchar(30) NOT NULL,
    master_code nvarchar(100) NOT NULL,
    old_values_json nvarchar(max) NULL,
    new_values_json nvarchar(max) NULL,
    changed_by nvarchar(200) NOT NULL,
    changed_utc datetime2(3) NOT NULL CONSTRAINT DF_controlled_master_history_utc DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    CONSTRAINT CK_controlled_master_history_old CHECK(old_values_json IS NULL OR ISJSON(old_values_json)=1),
    CONSTRAINT CK_controlled_master_history_new CHECK(new_values_json IS NULL OR ISJSON(new_values_json)=1)
);
CREATE INDEX IX_controlled_master_history_scope ON dbo.controlled_master_history(master_type,master_code,changed_utc DESC);

CREATE TABLE dbo.watch_folder_settings
(
    watch_folder_setting_id tinyint NOT NULL CONSTRAINT PK_watch_folder_settings PRIMARY KEY,
    inbound_path nvarchar(500) NOT NULL,
    processed_path nvarchar(500) NOT NULL,
    failed_path nvarchar(500) NOT NULL,
    report_output_path nvarchar(500) NOT NULL,
    poll_minutes int NOT NULL CONSTRAINT DF_watch_folder_settings_poll DEFAULT(5),
    is_enabled bit NOT NULL CONSTRAINT DF_watch_folder_settings_enabled DEFAULT(1),
    modified_by nvarchar(200) NOT NULL,
    modified_utc datetime2(3) NOT NULL CONSTRAINT DF_watch_folder_settings_modified DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    CONSTRAINT CK_watch_folder_settings_singleton CHECK(watch_folder_setting_id=1),
    CONSTRAINT CK_watch_folder_settings_poll CHECK(poll_minutes BETWEEN 1 AND 60),
    CONSTRAINT CK_watch_folder_settings_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0)
);
INSERT dbo.watch_folder_settings(watch_folder_setting_id,inbound_path,processed_path,failed_path,report_output_path,poll_minutes,is_enabled,modified_by,change_reason)
VALUES(1,N'C:\ProgramData\EtpReporting\Inbound',N'C:\ProgramData\EtpReporting\Processed',N'C:\ProgramData\EtpReporting\Failed',N'C:\ProgramData\EtpReporting\ReportPacks',5,1,SUSER_SNAME(),N'Safe local defaults created during Phase 2 migration');

CREATE TABLE dbo.report_pack_schedules
(
    report_pack_schedule_id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_report_pack_schedules PRIMARY KEY,
    schedule_name nvarchar(100) NOT NULL,
    local_run_time time(0) NOT NULL,
    is_enabled bit NOT NULL CONSTRAINT DF_report_pack_schedules_enabled DEFAULT(1),
    export_excel bit NOT NULL CONSTRAINT DF_report_pack_schedules_excel DEFAULT(1),
    export_pdf bit NOT NULL CONSTRAINT DF_report_pack_schedules_pdf DEFAULT(1),
    last_business_date date NULL,
    last_run_utc datetime2(3) NULL,
    last_status varchar(20) NULL,
    last_message nvarchar(500) NULL,
    modified_by nvarchar(200) NOT NULL,
    modified_utc datetime2(3) NOT NULL CONSTRAINT DF_report_pack_schedules_modified DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    CONSTRAINT UQ_report_pack_schedules_name UNIQUE(schedule_name),
    CONSTRAINT CK_report_pack_schedules_status CHECK(last_status IS NULL OR last_status IN('Succeeded','Failed','Blocked')),
    CONSTRAINT CK_report_pack_schedules_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0)
);
INSERT dbo.report_pack_schedules(schedule_name,local_run_time,modified_by,change_reason)
VALUES(N'Morning management pack','08:00',SUSER_SNAME(),N'Default morning schedule'),
      (N'Evening management pack','21:30',SUSER_SNAME(),N'Default evening schedule');

CREATE TABLE dbo.automation_runs
(
    automation_run_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_automation_runs PRIMARY KEY,
    run_type varchar(30) NOT NULL,
    source_file_name nvarchar(260) NULL,
    store_code varchar(30) NULL,
    business_date date NULL,
    outcome varchar(20) NOT NULL,
    safe_message nvarchar(500) NOT NULL,
    started_utc datetime2(3) NOT NULL,
    completed_utc datetime2(3) NOT NULL,
    run_by nvarchar(200) NOT NULL,
    CONSTRAINT CK_automation_runs_type CHECK(run_type IN('WATCH_IMPORT','AUTO_REPORT_PACK','SCHEDULED_REPORT_PACK')),
    CONSTRAINT CK_automation_runs_outcome CHECK(outcome IN('Succeeded','Failed','Blocked','Skipped'))
);
CREATE INDEX IX_automation_runs_recent ON dbo.automation_runs(completed_utc DESC,run_type);

ALTER TABLE dbo.operational_audit DROP CONSTRAINT CK_operational_audit_type;
ALTER TABLE dbo.operational_audit ADD CONSTRAINT CK_operational_audit_type CHECK
 (event_type IN ('ApplicationStart','SessionStart','ConnectionTest','ImportBatch','ImportFailed','ReportRun','ExportExcel','ExportPdf','DatabaseSetup','SupportPackage',
                 'ManualInput','DayFinalised','DayReopened','ReportPack','Backup','RestoreDrill','ConfigurationChange','MappingProfileChange','Restatement','StockCount','StaffTarget',
                 'UserAdministration','MasterDataChange','AutomationRun','ReportArchive'));

EXEC(N'CREATE TRIGGER dbo.trg_application_users_history
ON dbo.application_users AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM deleted d WHERE d.role_code=''OWNER'' AND d.is_active=1)
    AND NOT EXISTS(SELECT 1 FROM dbo.application_users WHERE role_code=''OWNER'' AND is_active=1)
   THROW 51100,''At least one active Owner is required.'',1;
 INSERT dbo.application_user_history(application_user_id,windows_identity,old_values_json,new_values_json,changed_by,change_reason)
 SELECT COALESCE(i.application_user_id,d.application_user_id),COALESCE(i.windows_identity,d.windows_identity),
   CASE WHEN d.application_user_id IS NULL THEN NULL ELSE (SELECT d.display_name displayName,d.role_code roleCode,d.is_active isActive FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) END,
   CASE WHEN i.application_user_id IS NULL THEN NULL ELSE (SELECT i.display_name displayName,i.role_code roleCode,i.is_active isActive FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) END,
   COALESCE(i.modified_by,d.modified_by),COALESCE(i.change_reason,d.change_reason)
 FROM inserted i FULL OUTER JOIN deleted d ON d.application_user_id=i.application_user_id;
 INSERT dbo.operational_audit(event_type,outcome,safe_detail,application_version,actor_name)
 VALUES(''UserAdministration'',''Succeeded'',N''Application access changed'',N''database'',ORIGINAL_LOGIN());
END');

EXEC(N'CREATE TRIGGER dbo.trg_controlled_master_history
ON dbo.controlled_master_values AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 INSERT dbo.controlled_master_history(controlled_master_value_id,master_type,master_code,old_values_json,new_values_json,changed_by,change_reason)
 SELECT COALESCE(i.controlled_master_value_id,d.controlled_master_value_id),COALESCE(i.master_type,d.master_type),COALESCE(i.master_code,d.master_code),
   CASE WHEN d.controlled_master_value_id IS NULL THEN NULL ELSE (SELECT d.display_name displayName,d.approval_status approvalStatus,d.is_active isActive FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) END,
   CASE WHEN i.controlled_master_value_id IS NULL THEN NULL ELSE (SELECT i.display_name displayName,i.approval_status approvalStatus,i.is_active isActive FOR JSON PATH,WITHOUT_ARRAY_WRAPPER) END,
   COALESCE(i.modified_by,d.modified_by),COALESCE(i.change_reason,d.change_reason)
 FROM inserted i FULL OUTER JOIN deleted d ON d.controlled_master_value_id=i.controlled_master_value_id;
 INSERT dbo.operational_audit(event_type,outcome,safe_detail,application_version,actor_name)
 VALUES(''MasterDataChange'',''Succeeded'',N''Controlled master value changed'',N''database'',ORIGINAL_LOGIN());
END');

DROP TRIGGER dbo.trg_daily_report_generations_immutable;
EXEC(N'CREATE TRIGGER dbo.trg_daily_report_generations_immutable
ON dbo.daily_report_generations AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF NOT EXISTS(SELECT 1 FROM inserted) THROW 51044,''Report generations cannot be deleted.'',1;
 IF EXISTS
 (
   SELECT 1 FROM inserted i JOIN deleted d ON d.daily_report_generation_id=i.daily_report_generation_id
   WHERE d.is_final<>0 OR i.is_final<>1 OR
         i.store_code<>d.store_code OR i.business_date<>d.business_date OR i.generation_number<>d.generation_number OR
         i.content_sha256<>d.content_sha256 OR i.control_json<>d.control_json OR i.generated_by<>d.generated_by OR
         i.generated_utc<>d.generated_utc OR ISNULL(i.supersedes_generation_id,-1)<>ISNULL(d.supersedes_generation_id,-1) OR
         ISNULL(i.report_document_json,N'''')<>ISNULL(d.report_document_json,N'''') OR ISNULL(i.document_sha256,'''')<>ISNULL(d.document_sha256,'''')
 ) THROW 51045,''A report generation is immutable except for its one-way finalisation flag.'',1;
END');

COMMIT TRANSACTION;
