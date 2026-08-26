SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.import_files ADD
    report_code varchar(30) NULL,
    store_code varchar(30) NULL,
    business_date date NULL,
    source_report_date date NULL,
    imported_by nvarchar(100) NULL;
CREATE INDEX IX_import_files_business_scope ON dbo.import_files(business_date,store_code,report_code);

CREATE TABLE dbo.manual_input_definitions
(
    field_code varchar(50) NOT NULL CONSTRAINT PK_manual_input_definitions PRIMARY KEY,
    display_name nvarchar(120) NOT NULL,
    value_kind varchar(20) NOT NULL,
    is_required_for_finalisation bit NOT NULL,
    applies_to varchar(20) NOT NULL,
    is_active bit NOT NULL CONSTRAINT DF_manual_input_definitions_active DEFAULT(1),
    CONSTRAINT CK_manual_input_definitions_kind CHECK(value_kind IN ('Quantity','Money','Text')),
    CONSTRAINT CK_manual_input_definitions_scope CHECK(applies_to IN ('Store','Service','Stock','Cash'))
);

INSERT dbo.manual_input_definitions(field_code,display_name,value_kind,is_required_for_finalisation,applies_to)
VALUES
 ('WALK_INS',N'Walk-ins','Quantity',1,'Store'),
 ('OPENING_CASH',N'Opening cash','Money',1,'Cash'),
 ('CASH_DEPOSIT',N'Cash deposit','Money',1,'Cash'),
 ('EXPENSES',N'Expenses','Money',1,'Cash'),
 ('DISPLAY_STOCK',N'Display stock','Quantity',0,'Stock'),
 ('BACKSTOCK',N'Backstock','Quantity',0,'Stock'),
 ('DEFECTIVE_STOCK',N'Defective stock','Quantity',0,'Stock'),
 ('Y_LOCATION_STOCK',N'Y-location stock','Quantity',0,'Stock'),
 ('PHYSICAL_STOCK',N'Physical stock','Quantity',0,'Stock'),
 ('SALES_TARGET',N'Sales target','Money',0,'Store'),
 ('OPERATIONAL_REMARK',N'Operational remark','Text',0,'Store');

CREATE TABLE dbo.daily_reporting_days
(
    store_code varchar(30) NOT NULL,
    business_date date NOT NULL,
    status varchar(30) NOT NULL CONSTRAINT DF_daily_reporting_days_status DEFAULT('OPEN'),
    finalised_by nvarchar(100) NULL,
    finalised_utc datetime2(3) NULL,
    reopened_by nvarchar(100) NULL,
    reopened_utc datetime2(3) NULL,
    reopen_reason nvarchar(500) NULL,
    row_version rowversion NOT NULL,
    CONSTRAINT PK_daily_reporting_days PRIMARY KEY(store_code,business_date),
    CONSTRAINT CK_daily_reporting_days_status CHECK(status IN ('OPEN','PARTIAL','READY_WITH_WARNINGS','RECONCILED','LOCKED')),
    CONSTRAINT CK_daily_reporting_days_finalised CHECK
      ((status='LOCKED' AND finalised_by IS NOT NULL AND finalised_utc IS NOT NULL) OR status<>'LOCKED')
);

CREATE TABLE dbo.manual_operational_inputs
(
    manual_input_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_manual_operational_inputs PRIMARY KEY,
    store_code varchar(30) NOT NULL,
    business_date date NOT NULL,
    field_code varchar(50) NOT NULL,
    numeric_value decimal(19,4) NULL,
    text_value nvarchar(1000) NULL,
    entered_by nvarchar(100) NOT NULL,
    entered_utc datetime2(3) NOT NULL CONSTRAINT DF_manual_operational_inputs_entered DEFAULT SYSUTCDATETIME(),
    modified_by nvarchar(100) NOT NULL,
    modified_utc datetime2(3) NOT NULL CONSTRAINT DF_manual_operational_inputs_modified DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL,
    row_version rowversion NOT NULL,
    CONSTRAINT FK_manual_operational_inputs_definition FOREIGN KEY(field_code) REFERENCES dbo.manual_input_definitions(field_code),
    CONSTRAINT UQ_manual_operational_inputs UNIQUE(store_code,business_date,field_code),
    CONSTRAINT CK_manual_operational_inputs_value CHECK
      ((numeric_value IS NOT NULL AND text_value IS NULL) OR (numeric_value IS NULL AND text_value IS NOT NULL)),
    CONSTRAINT CK_manual_operational_inputs_reason CHECK(LEN(LTRIM(RTRIM(change_reason)))>0)
);

CREATE TABLE dbo.manual_operational_input_history
(
    manual_input_history_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_manual_operational_input_history PRIMARY KEY,
    manual_input_id bigint NOT NULL,
    store_code varchar(30) NOT NULL,
    business_date date NOT NULL,
    field_code varchar(50) NOT NULL,
    old_numeric_value decimal(19,4) NULL,
    new_numeric_value decimal(19,4) NULL,
    old_text_value nvarchar(1000) NULL,
    new_text_value nvarchar(1000) NULL,
    changed_by nvarchar(100) NOT NULL,
    changed_utc datetime2(3) NOT NULL CONSTRAINT DF_manual_input_history_changed DEFAULT SYSUTCDATETIME(),
    change_reason nvarchar(500) NOT NULL
);
CREATE INDEX IX_manual_input_history_scope ON dbo.manual_operational_input_history(store_code,business_date,changed_utc DESC);

CREATE TABLE dbo.daily_reporting_events
(
    daily_reporting_event_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_daily_reporting_events PRIMARY KEY,
    store_code varchar(30) NOT NULL,
    business_date date NOT NULL,
    event_type varchar(30) NOT NULL,
    performed_by nvarchar(100) NOT NULL,
    event_utc datetime2(3) NOT NULL CONSTRAINT DF_daily_reporting_events_utc DEFAULT SYSUTCDATETIME(),
    reason nvarchar(500) NULL,
    CONSTRAINT CK_daily_reporting_events_type CHECK(event_type IN ('ManualInputCreated','ManualInputChanged','DayFinalised','DayReopened','ReportPackGenerated'))
);
CREATE INDEX IX_daily_reporting_events_scope ON dbo.daily_reporting_events(store_code,business_date,event_utc DESC);

EXEC(N'CREATE TRIGGER dbo.trg_manual_operational_inputs_protect_locked
ON dbo.manual_operational_inputs
AFTER INSERT,UPDATE,DELETE
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS
 (
   SELECT 1 FROM
   (
     SELECT store_code,business_date FROM inserted
     UNION
     SELECT store_code,business_date FROM deleted
   ) changed
   JOIN dbo.daily_reporting_days d ON d.store_code=changed.store_code AND d.business_date=changed.business_date
   WHERE d.status=''LOCKED''
 ) THROW 51020,''The business date is finalised. Reopen it before changing manual inputs.'',1;

 INSERT dbo.manual_operational_input_history
   (manual_input_id,store_code,business_date,field_code,old_numeric_value,new_numeric_value,old_text_value,new_text_value,changed_by,change_reason)
 SELECT COALESCE(i.manual_input_id,d.manual_input_id),COALESCE(i.store_code,d.store_code),COALESCE(i.business_date,d.business_date),
        COALESCE(i.field_code,d.field_code),d.numeric_value,i.numeric_value,d.text_value,i.text_value,
        COALESCE(i.modified_by,d.modified_by),COALESCE(i.change_reason,d.change_reason)
 FROM inserted i FULL OUTER JOIN deleted d ON d.manual_input_id=i.manual_input_id;
END');

EXEC(N'CREATE TRIGGER dbo.trg_import_files_protect_locked
ON dbo.import_files
AFTER INSERT,UPDATE
AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS
 (
   SELECT 1 FROM inserted i
   JOIN dbo.daily_reporting_days d ON d.store_code=i.store_code AND d.business_date=i.business_date
   WHERE d.status=''LOCKED''
 ) THROW 51021,''The business date is finalised. Reopen it before importing a restatement.'',1;
END');

ALTER TABLE dbo.operational_audit DROP CONSTRAINT CK_operational_audit_type;
ALTER TABLE dbo.operational_audit ADD CONSTRAINT CK_operational_audit_type CHECK
 (event_type IN ('ApplicationStart','ConnectionTest','ImportBatch','ReportRun','ExportExcel','ExportPdf','DatabaseSetup','SupportPackage',
                 'ManualInput','DayFinalised','DayReopened','ReportPack'));

COMMIT TRANSACTION;
