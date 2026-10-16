-- 1.12.1 (VM acceptance 16 Oct 2026, finding 1): what health check DB-07 "Database objects match this release" needs, for
-- the automation account that runs the nightly check. DB-07 read dbo.schema_objects, OBJECT_DEFINITION and
-- sys.sql_expression_dependencies itself; only db_owner (the Owner) may read that catalog view, and etp_automation has no VIEW
-- DEFINITION and no right of its own on the journal (0062 grants nothing), so the nightly run was always Skipped NOT_PERMITTED.
-- Like diag.load_role_permissions this runs as diag_catalog_reader (0055: a user WITHOUT LOGIN holding only VIEW
-- DEFINITION; EXECUTE AS OWNER fails on a restored database whose dbo maps to a missing login). The journal is read
-- through the ownership chain (diag and dbo are both owned by dbo), so the reader gets no new right and no role gets
-- SELECT on the journal or VIEW DEFINITION. No parameters, read only, a fixed shape. The definitions it returns are the
-- dbo and diag views, procedures and functions, the same text as the object files shipped in the installation folder.
-- Two result sets:
--   1. dbo.schema_objects: object_name, object_type, file_sha256, definition_sha256 (SchemaObjectSql.JournalSql)
--   2. every view, procedure and function of dbo and diag: object_name, type_code, definition. Unlike
--      SchemaObjectSql.LiveObjectsSql there is no pinned_by: sys.sql_expression_dependencies needs SELECT that only
--      db_owner has, and a file for a pinned object is refused by setup and the guard tests long before DB-07 runs.
CREATE OR ALTER PROCEDURE diag.load_schema_object_state
WITH EXECUTE AS N'diag_catalog_reader'
AS
BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;

 SELECT object_name,object_type,file_sha256,definition_sha256 FROM dbo.schema_objects;

 SELECT CONCAT(s.name,N'.',o.name) AS object_name,RTRIM(o.type) AS type_code,OBJECT_DEFINITION(o.object_id) AS definition
 FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
 WHERE o.type IN ('V','P','FN','IF','TF') AND s.name IN (N'dbo',N'diag') AND o.is_ms_shipped=0;
END
-- >>> GRANTS
GRANT EXECUTE ON diag.load_schema_object_state TO etp_automation,etp_owner;
