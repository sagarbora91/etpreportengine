-- 1.12 lane U2 (wave plan C-c, wave-2 plan U2): where each imported or refused source file went.
--
-- dbo.import_source_locations: one row per file a run routed. The unattended run writes a row for every file of a watched
--    source when it moves that source to Processed, Processed\Duplicate or Failed (routed_path = the moved source, a zip
--    for a shop's daily files); a hand import writes route 'Hand' with the path the Owner or Store Manager picked. The
--    Problems and History screens read it to offer "Reopen day and re-apply", "Retry" and "Open folder" for a row: the
--    attempt row alone does not know where the file went (L7).
--    file_name is the file inside the source (as import_attempts.file_name records it); source_sha256 is that file's own
--    hash when it was read, so a refused file is found again by its bytes. attempt_ref is the 12-character diagnostics Ref
--    when the failure wrote one. import_file_id is set only for a file whose import committed.
--    Rows are append-only: a re-apply writes a new row (route 'Hand'); nothing updates or deletes one (THROW 51990).
--
-- Grants change: ExpectedRolePermissions.json gains the rows below at the gate (regenerated there). The Owner and the
-- Store Manager (the automation account is a Store Manager) may SELECT and INSERT; the Viewer is denied the table
-- outright (it holds local folder paths), so the schema-wide SELECT of 0022 does not reach it. Nobody may UPDATE or
-- DELETE. THROW numbers: lane U2 block 51990-51999 (51990 append-only trigger).
-- The migration runner applies this script in one transaction (no BEGIN, COMMIT or GO); the trigger is created through
-- EXEC because CREATE TRIGGER must start a batch.
SET XACT_ABORT ON;

CREATE TABLE dbo.import_source_locations (
 import_source_location_id bigint IDENTITY CONSTRAINT PK_import_source_locations PRIMARY KEY,
 import_file_id bigint NULL CONSTRAINT FK_import_source_locations_file REFERENCES dbo.import_files(import_file_id),
 attempt_ref varchar(12) NULL,
 file_name nvarchar(260) NOT NULL,
 source_sha256 char(64) NULL,
 routed_path nvarchar(400) NOT NULL,
 route varchar(10) NOT NULL,
 routed_utc datetime2 NOT NULL CONSTRAINT DF_import_source_locations_utc DEFAULT SYSUTCDATETIME(),
 routed_by nvarchar(128) NOT NULL CONSTRAINT DF_import_source_locations_by DEFAULT ORIGINAL_LOGIN(),
 CONSTRAINT CK_import_source_locations_route CHECK(route IN ('Processed','Duplicate','Failed','Hand')),
 CONSTRAINT CK_import_source_locations_file_name CHECK(LEN(LTRIM(RTRIM(file_name)))>0),
 CONSTRAINT CK_import_source_locations_path CHECK(LEN(LTRIM(RTRIM(routed_path)))>0),
 CONSTRAINT CK_import_source_locations_ref CHECK(attempt_ref IS NULL OR (LEN(attempt_ref)=12 AND attempt_ref NOT LIKE '%[^0-9A-Fa-f]%')),
 CONSTRAINT CK_import_source_locations_sha CHECK(source_sha256 IS NULL OR (LEN(source_sha256)=64 AND source_sha256 NOT LIKE '%[^0-9A-Fa-f]%'))
);

CREATE INDEX IX_import_source_locations_sha ON dbo.import_source_locations(source_sha256, routed_utc) WHERE source_sha256 IS NOT NULL;
CREATE INDEX IX_import_source_locations_name ON dbo.import_source_locations(file_name, routed_utc);

EXEC(N'CREATE TRIGGER dbo.trg_import_source_locations_append_only ON dbo.import_source_locations AFTER UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 THROW 51990,''An import source location cannot be changed or deleted. A re-apply records a new location.'',1;
END');

GRANT SELECT,INSERT ON dbo.import_source_locations TO etp_owner,etp_store_manager;
DENY UPDATE,DELETE ON dbo.import_source_locations TO etp_owner,etp_store_manager;
DENY SELECT,INSERT,UPDATE,DELETE ON dbo.import_source_locations TO etp_viewer;
