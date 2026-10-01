-- Pre-upgrade check for the import engine (CONSOLIDATED-AND-RAW-IMPORT-SPEC.md section 13.1).
--
-- Run it SELECT-only against the database to be upgraded: the live database, a shop-PC backup
-- or an older backup restored under another name. It reads and reports; it writes nothing,
-- not even a temporary table, and is safe for a db_datareader account. One batch, no GO:
--   sqlcmd -S .\SQLEXPRESS -d EtpReporting -E -i scripts\check-import-upgrade.sql -W -s "|"
--
-- Result sets, in order:
--   1. environment: database, collation (and whether it is case-sensitive), SQL Server version,
--      edition, latest applied migration;
--   2. summary: one row per check with its finding count and whether it blocks the upgrade
--      (every blocking item is repeated by a migration pre-check that THROWs);
--   3. fact counts per table (the baseline that the P3 upgrade verifies against);
--   4. onwards: the first 200 rows behind each check that found something, labelled by check.
--
-- The checks read columns that later migrations add (line_seq, source_report_code) only when
-- they exist, so the same script runs before and after 0038. It needs migrations up to 0018.
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.etp_import_content', N'U') IS NULL OR COL_LENGTH(N'dbo.import_files', N'data_truth_version') IS NULL
BEGIN
 SELECT N'This database predates migration 0018. Upgrade it to release 1.8 or later before checking.' AS refused;
 RETURN;
END;

DECLARE @collation sysname = CONVERT(sysname, DATABASEPROPERTYEX(DB_NAME(), 'Collation'));
DECLARE @caseSensitive bit = CASE WHEN @collation LIKE N'%[_]CS[_]%' OR @collation LIKE N'%[_]CS' OR @collation LIKE N'%[_]BIN%' THEN 1 ELSE 0 END;
DECLARE @movementLine bit = CASE WHEN COL_LENGTH(N'dbo.stock_movements', N'line_seq') IS NULL THEN 0 ELSE 1 END;
DECLARE @snapshotSource bit = CASE WHEN COL_LENGTH(N'dbo.stock_snapshots', N'source_report_code') IS NULL THEN 0 ELSE 1 END;
DECLARE @snapshotLine bit = CASE WHEN COL_LENGTH(N'dbo.stock_snapshots', N'line_seq') IS NULL THEN 0 ELSE 1 END;

-- 1. Environment
SELECT DB_NAME() AS database_name,
       @collation AS database_collation,
       @caseSensitive AS collation_case_sensitive,
       CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion')) AS sql_server_version,
       CONVERT(nvarchar(128), SERVERPROPERTY('Edition')) AS sql_server_edition,
       (SELECT MAX(migration_id) FROM dbo.schema_migrations) AS latest_migration,
       (SELECT COUNT(*) FROM dbo.schema_migrations) AS applied_migrations,
       SYSUTCDATETIME() AS checked_utc;

-- Identity texts for the stock checks. Before 0038 the snapshot source is derived from the lineage
-- record type exactly as 0038's backfill does, and every row's line is 1.
DECLARE @movementKey nvarchar(max) = N'm.store_code,m.invoice_year,m.document_number,m.document_date,m.product_code,
  m.source_transaction_type,ISNULL(m.from_location,N''''),ISNULL(m.to_location,N'''')'
  + CASE WHEN @movementLine = 1 THEN N',m.line_seq' ELSE N'' END;
DECLARE @snapshotSourceText nvarchar(max) = CASE WHEN @snapshotSource = 1
  THEN N'COALESCE(s.source_report_code,CASE WHEN l.source_record_type=''R010_SNAPSHOT'' THEN ''R010'' ELSE ''CLOSING_STOCK'' END)'
  ELSE N'CASE WHEN l.source_record_type=''R010_SNAPSHOT'' THEN ''R010'' ELSE ''CLOSING_STOCK'' END' END;
DECLARE @snapshotKey nvarchar(max) = N's.store_code,s.snapshot_date,' + @snapshotSourceText
  + N',s.product_code,COALESCE(s.source_uid,s.batch_number,s.ean,N'''')'
  + CASE WHEN @snapshotLine = 1 THEN N',s.line_seq' ELSE N'' END;
DECLARE @movementSql nvarchar(max) = N'SELECT @groups=COUNT(*),@rows=COALESCE(SUM(n),0) FROM (SELECT COUNT(*) n FROM dbo.stock_movements m
  GROUP BY ' + @movementKey + N' HAVING COUNT(*)>1) d;';
DECLARE @snapshotSql nvarchar(max) = N'SELECT @groups=COUNT(*),@rows=COALESCE(SUM(n),0) FROM (SELECT COUNT(*) n FROM dbo.stock_snapshots s
  JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id GROUP BY ' + @snapshotKey + N' HAVING COUNT(*)>1) d;';
DECLARE @movementGroups bigint, @movementRows bigint, @snapshotGroups bigint, @snapshotRows bigint;
EXEC sys.sp_executesql @movementSql, N'@groups bigint OUTPUT,@rows bigint OUTPUT', @movementGroups OUTPUT, @movementRows OUTPUT;
EXEC sys.sp_executesql @snapshotSql, N'@groups bigint OUTPUT,@rows bigint OUTPUT', @snapshotGroups OUTPUT, @snapshotRows OUTPUT;

-- Facts of every family table (etp_r* and etp_landing_*) whose file is superseded, read through
-- one UNION over the tables that exist in this database.
DECLARE @familyUnion nvarchar(max) = N'';
SELECT @familyUnion = @familyUnion + CASE WHEN @familyUnion = N'' THEN N'' ELSE N' UNION ALL ' END
  + N'SELECT N''' + t.name + N''' table_name,import_file_id FROM dbo.' + QUOTENAME(t.name)
FROM sys.tables t
WHERE SCHEMA_NAME(t.schema_id) = N'dbo' AND t.name LIKE N'etp[_]%' AND t.name <> N'etp_import_content'
  AND COL_LENGTH(N'dbo.' + QUOTENAME(t.name), N'import_file_id') IS NOT NULL
ORDER BY t.name;
DECLARE @familyRows bigint = 0, @familySuperseded bigint = 0;
IF @familyUnion <> N''
BEGIN
 DECLARE @familySql nvarchar(max) = N'SELECT @rows=COUNT(*),@superseded=COALESCE(SUM(CASE WHEN f.is_superseded=1 THEN 1 ELSE 0 END),0)
   FROM (' + @familyUnion + N') x JOIN dbo.import_files f ON f.import_file_id=x.import_file_id;';
 EXEC sys.sp_executesql @familySql, N'@rows bigint OUTPUT,@superseded bigint OUTPUT', @familyRows OUTPUT, @familySuperseded OUTPUT;
END;

DECLARE @lineageSuperseded bigint =
   (SELECT COUNT(*) FROM dbo.sales_lines x JOIN dbo.source_lineage l ON l.source_lineage_id=x.source_lineage_id JOIN dbo.import_files f ON f.import_file_id=l.import_file_id WHERE f.is_superseded=1)
 + (SELECT COUNT(*) FROM dbo.sales_invoice_controls x JOIN dbo.source_lineage l ON l.source_lineage_id=x.source_lineage_id JOIN dbo.import_files f ON f.import_file_id=l.import_file_id WHERE f.is_superseded=1)
 + (SELECT COUNT(*) FROM dbo.sales_tenders x JOIN dbo.source_lineage l ON l.source_lineage_id=x.source_lineage_id JOIN dbo.import_files f ON f.import_file_id=l.import_file_id WHERE f.is_superseded=1)
 + (SELECT COUNT(*) FROM dbo.stock_movements x JOIN dbo.source_lineage l ON l.source_lineage_id=x.source_lineage_id JOIN dbo.import_files f ON f.import_file_id=l.import_file_id WHERE f.is_superseded=1)
 + (SELECT COUNT(*) FROM dbo.stock_snapshots x JOIN dbo.source_lineage l ON l.source_lineage_id=x.source_lineage_id JOIN dbo.import_files f ON f.import_file_id=l.import_file_id WHERE f.is_superseded=1)
 + (SELECT COUNT(*) FROM dbo.sales_line_enrichments x JOIN dbo.source_lineage l ON l.source_lineage_id=x.source_lineage_id JOIN dbo.import_files f ON f.import_file_id=l.import_file_id WHERE f.is_superseded=1);

-- 2. Summary. blocks_upgrade = 1 marks what a migration pre-check refuses (0038: 51700-51702;
-- the stock identity indexes cannot be built over a repeated identity).
SELECT check_code, findings, blocks_upgrade, detail FROM (VALUES
 (1, 'CURRENT_V0_FILES', (SELECT COUNT_BIG(*) FROM dbo.import_files WHERE is_superseded=0 AND data_truth_version=0), CONVERT(bit,0),
  N'Current files imported before data truth version 1; the upgrade keeps their facts as canonical-only versions.'),
 (2, 'LOCKED_DAYS', (SELECT COUNT_BIG(*) FROM dbo.daily_reporting_days WHERE status='LOCKED'), CONVERT(bit,0),
  N'Finalised store-days; planner 1 refuses any file whose period contains one.'),
 (3, 'INVOICE_YEAR_NOT_FINANCIAL_YEAR', (SELECT COUNT_BIG(*) FROM dbo.sales_invoices
    WHERE invoice_year<>YEAR(transaction_date)+CASE WHEN MONTH(transaction_date)>=4 THEN 1 ELSE 0 END), CONVERT(bit,1),
  N'Invoices whose year is not the financial year of their date (0038 THROWs 51700).'),
 (4, 'MOVEMENT_YEAR_NOT_FINANCIAL_YEAR', (SELECT COUNT_BIG(*) FROM dbo.stock_movements
    WHERE invoice_year<>YEAR(document_date)+CASE WHEN MONTH(document_date)>=4 THEN 1 ELSE 0 END), CONVERT(bit,0),
  N'Stock movements whose year is not the financial year of their document date (information).'),
 (5, 'DUPLICATE_CONTROLS', (SELECT COUNT_BIG(*) FROM (SELECT sales_invoice_id FROM dbo.sales_invoice_controls GROUP BY sales_invoice_id HAVING COUNT(*)>1) d), CONVERT(bit,1),
  N'Invoices with more than one revenue control (0038 THROWs 51701).'),
 (6, 'DUPLICATE_TENDERS', (SELECT COUNT_BIG(*) FROM (SELECT sales_invoice_id FROM dbo.sales_tenders GROUP BY sales_invoice_id,UPPER(tender_type) HAVING COUNT(*)>1) d), CONVERT(bit,1),
  N'Invoices with the same tender type twice, case ignored (0038 THROWs 51702).'),
 (7, 'REPEATED_MOVEMENT_IDENTITY', @movementGroups, CONVERT(bit,@movementLine),
  CASE WHEN @movementLine=1 THEN N'Movements sharing store, year, document, date, product, type, locations and line.'
       ELSE CONCAT(N'Movement identities held by more than one row (', @movementRows, N' rows); 0038 numbers them with line_seq.') END),
 (8, 'REPEATED_SNAPSHOT_IDENTITY', @snapshotGroups, CONVERT(bit,@snapshotLine),
  CASE WHEN @snapshotLine=1 THEN N'Snapshot rows sharing store, date, source, product, item and line.'
       ELSE CONCAT(N'Snapshot identities held by more than one row (', @snapshotRows, N' rows); 0038 numbers them with line_seq.') END),
 (9, 'DUPLICATE_CONTENT_ROWS', (SELECT COUNT_BIG(*) FROM (SELECT import_file_id FROM dbo.etp_import_content GROUP BY import_file_id,source_row_number HAVING COUNT(*)>1) d), CONVERT(bit,0),
  N'Source rows of one file holding more than one content row (etp_import_content keeps no sheet name).'),
 (10, 'FILES_WITH_ROWS_ON_SEVERAL_SHEETS', (SELECT COUNT_BIG(*) FROM (SELECT import_file_id FROM dbo.source_lineage GROUP BY import_file_id HAVING COUNT(DISTINCT sheet_name)>1) d), CONVERT(bit,0),
  N'Planner-1 files whose lineage spans more than one sheet, so a row number alone does not identify a source row.'),
 (11, 'FACTS_OF_SUPERSEDED_FILES', @lineageSuperseded + @familySuperseded, CONVERT(bit,0),
  N'Facts and family rows whose lineage file is superseded; a restatement should have archived them.'),
 (12, 'SHA_SHARED_ACROSS_SCOPES', (SELECT COUNT_BIG(*) FROM (SELECT source_sha256 FROM dbo.import_files WHERE is_superseded=0 GROUP BY source_sha256
    HAVING COUNT(DISTINCT CONCAT(report_code,'|',store_code,'|',COALESCE(period_start,business_date),'|',COALESCE(period_end,business_date)))>1) d), CONVERT(bit,0),
  N'Source hashes held by current files with different report, store or period.'),
 (13, 'ORPHAN_INVOICE_HEADERS', (SELECT COUNT_BIG(*) FROM dbo.sales_invoices i
    WHERE NOT EXISTS(SELECT 1 FROM dbo.sales_lines x WHERE x.sales_invoice_id=i.sales_invoice_id)
      AND NOT EXISTS(SELECT 1 FROM dbo.sales_invoice_controls x WHERE x.sales_invoice_id=i.sales_invoice_id)
      AND NOT EXISTS(SELECT 1 FROM dbo.sales_tenders x WHERE x.sales_invoice_id=i.sales_invoice_id)), CONVERT(bit,0),
  N'Invoice headers with no line, control or tender.'),
 (14, 'TENDER_TYPE_CASE_VARIANTS', (SELECT COUNT_BIG(*) FROM (SELECT UPPER(tender_type) t FROM dbo.sales_tenders GROUP BY UPPER(tender_type)
    HAVING COUNT(DISTINCT tender_type COLLATE Latin1_General_100_BIN2)>1) d), CONVERT(bit,0),
  CASE WHEN @caseSensitive=1 THEN N'Tender types spelled in more than one case. The collation is case-sensitive: 0038 indexes UPPER(tender_type).'
       ELSE N'Tender types spelled in more than one case (information; the collation ignores case).' END)
) c(n, check_code, findings, blocks_upgrade, detail)
ORDER BY n;

-- 3. Fact counts (the baseline for the P3 verification).
SELECT table_name, row_count FROM (VALUES
 (1, 'import_files (current)', (SELECT COUNT_BIG(*) FROM dbo.import_files WHERE is_superseded=0)),
 (2, 'import_files (superseded)', (SELECT COUNT_BIG(*) FROM dbo.import_files WHERE is_superseded=1)),
 (3, 'sales_invoices', (SELECT COUNT_BIG(*) FROM dbo.sales_invoices)),
 (4, 'sales_lines', (SELECT COUNT_BIG(*) FROM dbo.sales_lines)),
 (5, 'sales_invoice_controls', (SELECT COUNT_BIG(*) FROM dbo.sales_invoice_controls)),
 (6, 'sales_tenders', (SELECT COUNT_BIG(*) FROM dbo.sales_tenders)),
 (7, 'stock_movements', (SELECT COUNT_BIG(*) FROM dbo.stock_movements)),
 (8, 'stock_snapshots', (SELECT COUNT_BIG(*) FROM dbo.stock_snapshots)),
 (9, 'stock_snapshots (R010)', (SELECT COUNT_BIG(*) FROM dbo.stock_snapshots s JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id WHERE l.source_record_type='R010_SNAPSHOT')),
 (10, 'sales_line_enrichments', (SELECT COUNT_BIG(*) FROM dbo.sales_line_enrichments)),
 (11, 'etp_import_content', (SELECT COUNT_BIG(*) FROM dbo.etp_import_content)),
 (12, 'family tables (etp_r*, etp_landing_*)', @familyRows)
) c(n, table_name, row_count)
ORDER BY n;

-- 4. Detail behind each check that found something (at most 200 rows each).
IF EXISTS(SELECT 1 FROM dbo.import_files WHERE is_superseded=0 AND data_truth_version=0)
 SELECT TOP (200) 'CURRENT_V0_FILES' AS check_code, import_file_id, report_code, store_code,
        COALESCE(period_start,business_date) AS period_start, COALESCE(period_end,business_date) AS period_end, original_file_name
 FROM dbo.import_files WHERE is_superseded=0 AND data_truth_version=0 ORDER BY report_code, store_code, import_file_id;

IF EXISTS(SELECT 1 FROM dbo.daily_reporting_days WHERE status='LOCKED')
 SELECT TOP (200) 'LOCKED_DAYS' AS check_code, store_code, business_date, finalised_utc
 FROM dbo.daily_reporting_days WHERE status='LOCKED' ORDER BY store_code, business_date;

IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE invoice_year<>YEAR(transaction_date)+CASE WHEN MONTH(transaction_date)>=4 THEN 1 ELSE 0 END)
 SELECT TOP (200) 'INVOICE_YEAR_NOT_FINANCIAL_YEAR' AS check_code, sales_invoice_id, store_code, document_number, invoice_year, transaction_date,
        YEAR(transaction_date)+CASE WHEN MONTH(transaction_date)>=4 THEN 1 ELSE 0 END AS financial_year
 FROM dbo.sales_invoices WHERE invoice_year<>YEAR(transaction_date)+CASE WHEN MONTH(transaction_date)>=4 THEN 1 ELSE 0 END
 ORDER BY store_code, transaction_date, document_number;

IF EXISTS(SELECT 1 FROM dbo.sales_invoice_controls GROUP BY sales_invoice_id HAVING COUNT(*)>1)
 SELECT TOP (200) 'DUPLICATE_CONTROLS' AS check_code, i.sales_invoice_id, i.store_code, i.invoice_year, i.document_number, COUNT(*) AS controls
 FROM dbo.sales_invoice_controls c JOIN dbo.sales_invoices i ON i.sales_invoice_id=c.sales_invoice_id
 GROUP BY i.sales_invoice_id, i.store_code, i.invoice_year, i.document_number HAVING COUNT(*)>1 ORDER BY i.sales_invoice_id;

IF EXISTS(SELECT 1 FROM dbo.sales_tenders GROUP BY sales_invoice_id,UPPER(tender_type) HAVING COUNT(*)>1)
 SELECT TOP (200) 'DUPLICATE_TENDERS' AS check_code, i.sales_invoice_id, i.store_code, i.invoice_year, i.document_number, UPPER(t.tender_type) AS tender_type, COUNT(*) AS tenders
 FROM dbo.sales_tenders t JOIN dbo.sales_invoices i ON i.sales_invoice_id=t.sales_invoice_id
 GROUP BY i.sales_invoice_id, i.store_code, i.invoice_year, i.document_number, UPPER(t.tender_type) HAVING COUNT(*)>1 ORDER BY i.sales_invoice_id;

IF @movementGroups > 0
BEGIN
 DECLARE @movementDetail nvarchar(max) = N'SELECT TOP (200) ''REPEATED_MOVEMENT_IDENTITY'' AS check_code,m.store_code,m.invoice_year,m.document_number,
   m.document_date,m.product_code,m.source_transaction_type,COUNT(*) AS movement_rows FROM dbo.stock_movements m
   GROUP BY ' + @movementKey + N' HAVING COUNT(*)>1 ORDER BY m.store_code,m.document_date,m.document_number;';
 EXEC sys.sp_executesql @movementDetail;
END;

IF @snapshotGroups > 0
BEGIN
 DECLARE @snapshotDetail nvarchar(max) = N'SELECT TOP (200) ''REPEATED_SNAPSHOT_IDENTITY'' AS check_code,s.store_code,s.snapshot_date,'
   + @snapshotSourceText + N' AS source_report_code,s.product_code,COUNT(*) AS snapshot_rows FROM dbo.stock_snapshots s
   JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id
   GROUP BY ' + @snapshotKey + N' HAVING COUNT(*)>1 ORDER BY s.store_code,s.snapshot_date,s.product_code;';
 EXEC sys.sp_executesql @snapshotDetail;
END;

IF EXISTS(SELECT 1 FROM dbo.etp_import_content GROUP BY import_file_id,source_row_number HAVING COUNT(*)>1)
 SELECT TOP (200) 'DUPLICATE_CONTENT_ROWS' AS check_code, c.import_file_id, f.report_code, f.store_code, c.source_row_number, COUNT(*) AS content_rows
 FROM dbo.etp_import_content c JOIN dbo.import_files f ON f.import_file_id=c.import_file_id
 GROUP BY c.import_file_id, f.report_code, f.store_code, c.source_row_number HAVING COUNT(*)>1 ORDER BY c.import_file_id, c.source_row_number;

IF EXISTS(SELECT 1 FROM dbo.source_lineage GROUP BY import_file_id HAVING COUNT(DISTINCT sheet_name)>1)
 SELECT TOP (200) 'FILES_WITH_ROWS_ON_SEVERAL_SHEETS' AS check_code, l.import_file_id, f.report_code, f.store_code, f.is_superseded,
        COUNT(DISTINCT l.sheet_name) AS sheets, COUNT(*) AS lineage_rows
 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
 GROUP BY l.import_file_id, f.report_code, f.store_code, f.is_superseded HAVING COUNT(DISTINCT l.sheet_name)>1 ORDER BY l.import_file_id;

IF @lineageSuperseded + @familySuperseded > 0
 SELECT TOP (200) 'FACTS_OF_SUPERSEDED_FILES' AS check_code, f.import_file_id, f.report_code, f.store_code, f.superseded_by_import_file_id,
        COUNT(*) AS lineage_rows
 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
 WHERE f.is_superseded=1 AND (EXISTS(SELECT 1 FROM dbo.sales_lines x WHERE x.source_lineage_id=l.source_lineage_id)
   OR EXISTS(SELECT 1 FROM dbo.sales_invoice_controls x WHERE x.source_lineage_id=l.source_lineage_id)
   OR EXISTS(SELECT 1 FROM dbo.sales_tenders x WHERE x.source_lineage_id=l.source_lineage_id)
   OR EXISTS(SELECT 1 FROM dbo.stock_movements x WHERE x.source_lineage_id=l.source_lineage_id)
   OR EXISTS(SELECT 1 FROM dbo.stock_snapshots x WHERE x.source_lineage_id=l.source_lineage_id)
   OR EXISTS(SELECT 1 FROM dbo.sales_line_enrichments x WHERE x.source_lineage_id=l.source_lineage_id))
 GROUP BY f.import_file_id, f.report_code, f.store_code, f.superseded_by_import_file_id ORDER BY f.import_file_id;

IF EXISTS(SELECT 1 FROM dbo.import_files WHERE is_superseded=0 GROUP BY source_sha256
   HAVING COUNT(DISTINCT CONCAT(report_code,'|',store_code,'|',COALESCE(period_start,business_date),'|',COALESCE(period_end,business_date)))>1)
 SELECT TOP (200) 'SHA_SHARED_ACROSS_SCOPES' AS check_code, f.source_sha256, f.import_file_id, f.report_code, f.store_code,
        COALESCE(f.period_start,f.business_date) AS period_start, COALESCE(f.period_end,f.business_date) AS period_end
 FROM dbo.import_files f
 WHERE f.is_superseded=0 AND f.source_sha256 IN (SELECT source_sha256 FROM dbo.import_files WHERE is_superseded=0 GROUP BY source_sha256
   HAVING COUNT(DISTINCT CONCAT(report_code,'|',store_code,'|',COALESCE(period_start,business_date),'|',COALESCE(period_end,business_date)))>1)
 ORDER BY f.source_sha256, f.import_file_id;

IF EXISTS(SELECT 1 FROM dbo.sales_invoices i
   WHERE NOT EXISTS(SELECT 1 FROM dbo.sales_lines x WHERE x.sales_invoice_id=i.sales_invoice_id)
     AND NOT EXISTS(SELECT 1 FROM dbo.sales_invoice_controls x WHERE x.sales_invoice_id=i.sales_invoice_id)
     AND NOT EXISTS(SELECT 1 FROM dbo.sales_tenders x WHERE x.sales_invoice_id=i.sales_invoice_id))
 SELECT TOP (200) 'ORPHAN_INVOICE_HEADERS' AS check_code, i.sales_invoice_id, i.store_code, i.invoice_year, i.document_number, i.transaction_date
 FROM dbo.sales_invoices i
 WHERE NOT EXISTS(SELECT 1 FROM dbo.sales_lines x WHERE x.sales_invoice_id=i.sales_invoice_id)
   AND NOT EXISTS(SELECT 1 FROM dbo.sales_invoice_controls x WHERE x.sales_invoice_id=i.sales_invoice_id)
   AND NOT EXISTS(SELECT 1 FROM dbo.sales_tenders x WHERE x.sales_invoice_id=i.sales_invoice_id)
 ORDER BY i.store_code, i.transaction_date, i.document_number;
