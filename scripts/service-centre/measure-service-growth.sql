-- Service Centre interim (S-2, decision 15, 3 Oct 2026): storage growth of the Service import.
--
-- SELECT-only. It reads and reports; it writes nothing, not even a temporary table. One batch, no GO.
-- Run it on a scratch copy (for example EtpAccept_SVC) before and after a Service refresh and keep
-- both outputs; the difference is what one refresh costs. Never run it against a database you have
-- not been asked to measure.
--   sqlcmd -S .\SQLEXPRESS -d EtpAccept_SVC -E -i scripts\service-centre\measure-service-growth.sql -W -s "|"
--
-- Result sets, in order:
--   1. environment: database, latest migration, how many Service landing tables exist, data file
--      size and space used, and the share of the SQL Server Express 10 GB data limit in use;
--   2. per Service landing table (dbo.etp_landing_snnn): rows and sizes in KB (reserved, data,
--      index, unused), from sys.dm_db_partition_stats, as sp_spaceused reports them;
--   3. per reading (one Service import file): report code, file name, snapshot date (period_end),
--      superseded flag, start of the import, and the rows it holds in its landing table, in
--      dbo.etp_import_content and in dbo.source_lineage;
--   4. totals: readings, landing rows and KB, plus the Service share of etp_import_content and
--      source_lineage (rows exact, KB estimated in proportion to the table's rows).
--
-- Sizes need VIEW DATABASE STATE (db_owner has it). Without it, result set 2 gives rows only and
-- the size columns are NULL. Before migration 0048 the script says so and stops.
-- It prints no cell value of any landing table: only names, codes, dates and counts.
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.import_files', N'U') IS NULL OR OBJECT_ID(N'dbo.etp_import_content', N'U') IS NULL
   OR COL_LENGTH(N'dbo.import_files', N'period_end') IS NULL
BEGIN
 SELECT N'This database predates the ETP import tables (migration 0018). Nothing to measure.' AS refused;
 RETURN;
END;

DECLARE @serviceTables int =
 (SELECT COUNT(*) FROM sys.tables AS t
  WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.name LIKE N'etp[_]landing[_]s[0-9][0-9][0-9]');
DECLARE @canSize bit = CASE WHEN HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'VIEW DATABASE STATE') = 1 THEN 1 ELSE 0 END;

-- 1. Environment.
SELECT DB_NAME() AS database_name,
       (SELECT MAX(migration_id) FROM dbo.schema_migrations) AS latest_migration,
       @serviceTables AS service_landing_tables,
       CASE WHEN OBJECT_ID(N'dbo.etp_landing_s002', N'U') IS NULL THEN 0 ELSE 1 END AS migration_0048_applied,
       (SELECT CAST(SUM(CAST(f.size AS bigint)) * 8 / 1024.0 AS decimal(19,1)) FROM sys.database_files AS f WHERE f.type = 0) AS data_file_mb,
       (SELECT CAST(SUM(CAST(FILEPROPERTY(f.name, 'SpaceUsed') AS bigint)) * 8 / 1024.0 AS decimal(19,1)) FROM sys.database_files AS f WHERE f.type = 0) AS data_used_mb,
       10240 AS express_data_limit_mb,
       (SELECT CAST(SUM(CAST(FILEPROPERTY(f.name, 'SpaceUsed') AS bigint)) * 8 / 1024.0 * 100 / 10240 AS decimal(9,2)) FROM sys.database_files AS f WHERE f.type = 0) AS percent_of_express_limit,
       @canSize AS sizes_available,
       SYSUTCDATETIME() AS measured_utc;

IF @serviceTables = 0
BEGIN
 SELECT N'No Service landing table (dbo.etp_landing_snnn) exists: migration 0048 is not applied. Nothing more to measure.' AS note;
 RETURN;
END;

-- 2. Per Service landing table.
IF @canSize = 1
 SELECT t.name AS table_name,
        UPPER(SUBSTRING(t.name, 13, 4)) AS report_code,
        SUM(CASE WHEN p.index_id IN (0, 1) THEN p.row_count ELSE 0 END) AS row_count,
        SUM(p.reserved_page_count) * 8 AS reserved_kb,
        SUM(CASE WHEN p.index_id IN (0, 1) THEN p.in_row_data_page_count + p.lob_used_page_count + p.row_overflow_used_page_count ELSE 0 END) * 8 AS data_kb,
        (SUM(p.used_page_count) - SUM(CASE WHEN p.index_id IN (0, 1) THEN p.in_row_data_page_count + p.lob_used_page_count + p.row_overflow_used_page_count ELSE 0 END)) * 8 AS index_kb,
        (SUM(p.reserved_page_count) - SUM(p.used_page_count)) * 8 AS unused_kb
 FROM sys.tables AS t
 JOIN sys.dm_db_partition_stats AS p ON p.object_id = t.object_id
 WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.name LIKE N'etp[_]landing[_]s[0-9][0-9][0-9]'
 GROUP BY t.name
 ORDER BY t.name;
ELSE
 SELECT t.name AS table_name,
        UPPER(SUBSTRING(t.name, 13, 4)) AS report_code,
        SUM(CASE WHEN p.index_id IN (0, 1) THEN p.rows ELSE 0 END) AS row_count,
        CAST(NULL AS bigint) AS reserved_kb, CAST(NULL AS bigint) AS data_kb,
        CAST(NULL AS bigint) AS index_kb, CAST(NULL AS bigint) AS unused_kb
 FROM sys.tables AS t
 JOIN sys.partitions AS p ON p.object_id = t.object_id
 WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.name LIKE N'etp[_]landing[_]s[0-9][0-9][0-9]'
 GROUP BY t.name
 ORDER BY t.name;

-- 3. Per reading. The landing rows per file come from one UNION ALL over the tables that exist,
-- built from sys.tables (so a table missing on this database is simply not in the list). The
-- report code literal is made with QUOTENAME(..., '''') so no quote has to be doubled by hand.
DECLARE @landing nvarchar(max) =
 (SELECT STRING_AGG(CAST(N'SELECT ' + QUOTENAME(UPPER(SUBSTRING(t.name, 13, 4)), '''') + N' AS report_code, import_file_id, COUNT_BIG(*) AS landing_rows FROM dbo.'
         + QUOTENAME(t.name) + N' GROUP BY import_file_id' AS nvarchar(max)), N' UNION ALL ')
         WITHIN GROUP (ORDER BY t.name)
  FROM sys.tables AS t
  WHERE t.schema_id = SCHEMA_ID(N'dbo') AND t.name LIKE N'etp[_]landing[_]s[0-9][0-9][0-9]');

DECLARE @perReading nvarchar(max) = N'
WITH landing AS (' + @landing + N'),
service_files AS (
 SELECT f.import_file_id, f.report_code, f.original_file_name, f.period_end, f.is_superseded, b.started_utc
 FROM dbo.import_files AS f
 JOIN dbo.import_batches AS b ON b.import_batch_id = f.import_batch_id
 WHERE f.report_code LIKE ''S[0-9][0-9][0-9]'')
SELECT sf.report_code, sf.import_file_id, sf.original_file_name, sf.period_end AS snapshot_date,
       sf.is_superseded, sf.started_utc AS import_started_utc,
       ISNULL(l.landing_rows, 0) AS landing_rows,
       (SELECT COUNT_BIG(*) FROM dbo.etp_import_content AS c WHERE c.import_file_id = sf.import_file_id) AS content_rows,
       (SELECT COUNT_BIG(*) FROM dbo.source_lineage AS s WHERE s.import_file_id = sf.import_file_id) AS lineage_rows
FROM service_files AS sf
LEFT JOIN landing AS l ON l.import_file_id = sf.import_file_id
ORDER BY sf.report_code, sf.period_end, sf.import_file_id;';
EXEC sys.sp_executesql @perReading;

-- 4. Totals. etp_import_content and source_lineage are shared with Retail: their Service rows are
-- exact, their Service KB is the table's reserved KB in proportion to those rows (an estimate).
DECLARE @landingKb nvarchar(max) = CASE WHEN @canSize = 1 THEN N'(SELECT SUM(p.reserved_page_count) * 8 FROM sys.tables AS t JOIN sys.dm_db_partition_stats AS p ON p.object_id = t.object_id
  WHERE t.schema_id = SCHEMA_ID(N''dbo'') AND t.name LIKE N''etp[_]landing[_]s[0-9][0-9][0-9]'')' ELSE N'CAST(NULL AS bigint)' END;
DECLARE @contentKb nvarchar(max) = CASE WHEN @canSize = 1 THEN N'CASE WHEN c.content_rows_all > 0 THEN (SELECT SUM(p.reserved_page_count) * 8 FROM sys.dm_db_partition_stats AS p
  WHERE p.object_id = OBJECT_ID(N''dbo.etp_import_content'')) * c.content_rows / c.content_rows_all END' ELSE N'CAST(NULL AS bigint)' END;
DECLARE @lineageKb nvarchar(max) = CASE WHEN @canSize = 1 THEN N'CASE WHEN c.lineage_rows_all > 0 THEN (SELECT SUM(p.reserved_page_count) * 8 FROM sys.dm_db_partition_stats AS p
  WHERE p.object_id = OBJECT_ID(N''dbo.source_lineage'')) * c.lineage_rows / c.lineage_rows_all END' ELSE N'CAST(NULL AS bigint)' END;
DECLARE @totals nvarchar(max) = N'
WITH landing AS (' + @landing + N'),
service_files AS (SELECT f.import_file_id FROM dbo.import_files AS f WHERE f.report_code LIKE ''S[0-9][0-9][0-9]''),
counts AS (
 SELECT (SELECT COUNT(*) FROM service_files) AS readings,
        (SELECT ISNULL(SUM(landing_rows), 0) FROM landing) AS landing_rows,
        (SELECT COUNT_BIG(*) FROM dbo.etp_import_content AS c WHERE c.import_file_id IN (SELECT import_file_id FROM service_files)) AS content_rows,
        (SELECT COUNT_BIG(*) FROM dbo.etp_import_content) AS content_rows_all,
        (SELECT COUNT_BIG(*) FROM dbo.source_lineage AS s WHERE s.import_file_id IN (SELECT import_file_id FROM service_files)) AS lineage_rows,
        (SELECT COUNT_BIG(*) FROM dbo.source_lineage) AS lineage_rows_all)
SELECT c.readings, c.landing_rows, ' + @landingKb + N' AS landing_reserved_kb,
       c.content_rows, c.content_rows_all, ' + @contentKb + N' AS content_reserved_kb_estimate,
       c.lineage_rows, c.lineage_rows_all, ' + @lineageKb + N' AS lineage_reserved_kb_estimate
FROM counts AS c;';
EXEC sys.sp_executesql @totals;
