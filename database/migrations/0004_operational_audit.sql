SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.operational_audit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.operational_audit
    (
        operational_audit_id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_operational_audit PRIMARY KEY,
        event_utc datetime2(3) NOT NULL CONSTRAINT DF_operational_audit_event_utc DEFAULT SYSUTCDATETIME(),
        event_type varchar(40) NOT NULL,
        outcome varchar(20) NOT NULL,
        safe_detail nvarchar(200) NULL,
        application_version varchar(30) NOT NULL,
        CONSTRAINT CK_operational_audit_type CHECK (event_type IN ('ApplicationStart','ConnectionTest','ImportBatch','ReportRun','ExportExcel','ExportPdf','DatabaseSetup','SupportPackage')),
        CONSTRAINT CK_operational_audit_outcome CHECK (outcome IN ('Succeeded','Failed','Blocked','Cancelled'))
    );
    CREATE INDEX IX_operational_audit_event_utc ON dbo.operational_audit(event_utc DESC);
END;

COMMIT TRANSACTION;
