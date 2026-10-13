-- Views, procedures and functions as repeatable objects (1.12 wave 1, lane A3; 112-WAVE-PLAN A3, plan number 0061 renumbered
-- to 0062 because 1.11.4 took 0061).
-- From 1.12 the tables, columns, constraints, indexes, triggers, table types, roles, column DENYs and seed data stay in these
-- append-only, checksummed migrations. Views, procedures and functions live one file each in database/objects
-- (<layer>-<schema>.<name>.sql) and SchemaObjectRunner (Infrastructure.SqlServer/SchemaObjects.cs) applies a file again when
-- its text changes, right after the migrations. This migration adds the runner's journal:
--   dbo.schema_objects         one row per object the runner manages: the hash of its file (LF line endings) and the hash of
--                              OBJECT_DEFINITION as it was after the runner adopted or applied it (a hand edit shows as a
--                              different definition hash; health check DB-07).
--   dbo.schema_object_history  append-only: every adopt, apply and retire, with the definition hash before and after.
-- It also pre-allocates every 1.12 operational audit event type in CK_operational_audit_type (the plan's contract freeze), so
-- no other 1.12 lane re-creates that CHECK: CollectionsSetup, CollectionsImport, CashDepositConfirmed, SoftwareUpdate,
-- ShopResultPublished, TallyReadback, ImportFixAction.
-- No role gets a permission here: only setup (as dbo) writes the journal. ExpectedRolePermissions.json does not change.
-- THROW numbers: lane A3 block 51960-51969 (51960 pre-check, 51961 history trigger).
-- 0001-0061 are never edited. The migration runner applies this entire script in one transaction, so it has no BEGIN or
-- COMMIT and no GO; the trigger is created through EXEC because CREATE TRIGGER must start a batch.
SET XACT_ABORT ON;

-- >>> PRECHECK0062 begin
-- Nested IF, not AND: SQL Server does not short-circuit AND, so on a new database the EXISTS would read a missing table.
IF OBJECT_ID(N'dbo.operational_audit',N'U') IS NOT NULL
 IF EXISTS(SELECT 1 FROM dbo.operational_audit WHERE event_type NOT IN
 ('ApplicationStart','SessionStart','ConnectionTest','ImportBatch','ImportFailed','ReportRun','ExportExcel','ExportPdf','DatabaseSetup','SupportPackage',
  'ManualInput','DayFinalised','DayReopened','ReportPack','Backup','RestoreDrill','ConfigurationChange','MappingProfileChange','Restatement','StockCount','StaffTarget',
  'UserAdministration','MasterDataChange','AutomationRun','ReportArchive','DocumentIntake','DocumentExtraction','DocumentExtractionReview','SharingContactChange',
  'RegisterEntry','ShareInitiated','ReportPackage','Approval','Adjustment','AccountingBatch','AccountingExport','ImportConflict','IssueWorkflow','VisualRender',
  'CollectionsSetup','CollectionsImport','CashDepositConfirmed','SoftwareUpdate','ShopResultPublished','TallyReadback','ImportFixAction'))
 THROW 51960,'Migration 0062 refused: dbo.operational_audit holds an event type that is not in the 1.12 list. Nothing was changed.',1;
-- <<< PRECHECK0062 end

CREATE TABLE dbo.schema_objects(
 object_name nvarchar(260) COLLATE Latin1_General_100_CI_AS NOT NULL,
 object_type varchar(2) NOT NULL,
 file_name nvarchar(260) NOT NULL,
 file_sha256 char(64) NOT NULL,
 definition_sha256 char(64) NOT NULL,
 applied_utc datetime2(3) NOT NULL CONSTRAINT DF_schema_objects_applied DEFAULT SYSUTCDATETIME(),
 applied_by nvarchar(256) NOT NULL CONSTRAINT DF_schema_objects_by DEFAULT ORIGINAL_LOGIN(),
 release_version nvarchar(40) NULL,
 CONSTRAINT PK_schema_objects PRIMARY KEY(object_name),
 CONSTRAINT CK_schema_objects_name CHECK(object_name LIKE N'_%._%' AND object_name NOT LIKE N'%[^-A-Za-z0-9_.]%'),
 CONSTRAINT CK_schema_objects_type CHECK(object_type IN ('V','P','FN','IF','TF')),
 CONSTRAINT CK_schema_objects_hashes CHECK(file_sha256 NOT LIKE '%[^0-9a-f]%' AND definition_sha256 NOT LIKE '%[^0-9a-f]%')
);

CREATE TABLE dbo.schema_object_history(
 schema_object_history_id bigint IDENTITY(1,1) NOT NULL,
 object_name nvarchar(260) COLLATE Latin1_General_100_CI_AS NOT NULL,
 object_type varchar(2) NOT NULL,
 action varchar(10) NOT NULL,
 file_name nvarchar(260) NOT NULL,
 file_sha256 char(64) NOT NULL,
 previous_definition_sha256 char(64) NULL,
 definition_sha256 char(64) NULL,
 applied_utc datetime2(3) NOT NULL CONSTRAINT DF_schema_object_history_applied DEFAULT SYSUTCDATETIME(),
 applied_by nvarchar(256) NOT NULL CONSTRAINT DF_schema_object_history_by DEFAULT ORIGINAL_LOGIN(),
 release_version nvarchar(40) NULL,
 CONSTRAINT PK_schema_object_history PRIMARY KEY(schema_object_history_id),
 CONSTRAINT CK_schema_object_history_action CHECK(action IN ('ADOPT','APPLY','RETIRE')),
 CONSTRAINT CK_schema_object_history_type CHECK(object_type IN ('V','P','FN','IF','TF')),
 -- An adopt or apply records the definition the object has now; a retire records none.
 CONSTRAINT CK_schema_object_history_definition CHECK((action='RETIRE' AND definition_sha256 IS NULL) OR (action<>'RETIRE' AND definition_sha256 IS NOT NULL))
);

CREATE INDEX IX_schema_object_history_object ON dbo.schema_object_history(object_name,schema_object_history_id);

EXEC(N'CREATE TRIGGER dbo.trg_schema_object_history_append_only
 ON dbo.schema_object_history INSTEAD OF UPDATE,DELETE AS
 BEGIN THROW 51961,''The database object history is append-only.'',1; END');

ALTER TABLE dbo.operational_audit DROP CONSTRAINT CK_operational_audit_type;
ALTER TABLE dbo.operational_audit WITH CHECK ADD CONSTRAINT CK_operational_audit_type CHECK
 (event_type IN ('ApplicationStart','SessionStart','ConnectionTest','ImportBatch','ImportFailed','ReportRun','ExportExcel','ExportPdf','DatabaseSetup','SupportPackage',
                 'ManualInput','DayFinalised','DayReopened','ReportPack','Backup','RestoreDrill','ConfigurationChange','MappingProfileChange','Restatement','StockCount','StaffTarget',
                 'UserAdministration','MasterDataChange','AutomationRun','ReportArchive','DocumentIntake','DocumentExtraction','DocumentExtractionReview','SharingContactChange',
                 'RegisterEntry','ShareInitiated','ReportPackage','Approval','Adjustment','AccountingBatch','AccountingExport','ImportConflict','IssueWorkflow','VisualRender',
                 'CollectionsSetup','CollectionsImport','CashDepositConfirmed','SoftwareUpdate','ShopResultPublished','TallyReadback','ImportFixAction'));
