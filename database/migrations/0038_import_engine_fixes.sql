-- Import engine fixes for release 1.9.3 (CONSOLIDATED-AND-RAW-IMPORT-SPEC.md section 5.1).
-- The migration runner applies this entire script in one transaction, so it has no BEGIN or COMMIT.
-- The pre-checks THROW before anything changes; every later statement is idempotent
-- (COL_LENGTH/OBJECT_ID guards, CREATE OR ALTER). A statement that uses a column added earlier in
-- this script, and every CREATE PROCEDURE, VIEW or TRIGGER, runs through EXEC(N'...').
-- New error numbers are 51700-51799.
-- Each section changes only what lies between its own begin and end markers.
SET XACT_ABORT ON;

-- >>> PRECHECK_FY begin
-- <<< PRECHECK_FY end

-- >>> PRECHECK_CONTROLS_TENDERS begin
-- <<< PRECHECK_CONTROLS_TENDERS end

-- >>> A_DIAGNOSTICS begin
-- <<< A_DIAGNOSTICS end

-- >>> B_EVIDENCE begin
-- <<< B_EVIDENCE end

-- >>> C_STOCK_MOVEMENT begin
-- <<< C_STOCK_MOVEMENT end

-- >>> C2_CONTROL_TENDER begin
-- <<< C2_CONTROL_TENDER end

-- >>> D_SNAPSHOT begin
-- D. Snapshot source and line (IF-020). Every snapshot fact names the report it came from, so an R010
-- and an R011 reading of the same store-day are separate identities, and repeated identical rows each
-- keep their own line_seq instead of collapsing into ALREADY_PRESENT. Readers use the view below.
IF COL_LENGTH('dbo.stock_snapshots','source_report_code') IS NULL
 ALTER TABLE dbo.stock_snapshots ADD source_report_code varchar(30) NULL;
IF COL_LENGTH('dbo.stock_snapshots','line_seq') IS NULL
 ALTER TABLE dbo.stock_snapshots ADD line_seq int NOT NULL CONSTRAINT DF_stock_snapshots_line_seq DEFAULT(1);
IF COL_LENGTH('dbo.stock_snapshots','item_discriminator') IS NULL
 ALTER TABLE dbo.stock_snapshots ADD item_discriminator AS COALESCE(source_uid,batch_number,ean,N'') PERSISTED;

-- The backfill must also work for finalised days. 0017 disabled every other fact guard but not this one
-- (0009); DDL is transactional, so a failed migration rolls the trigger state back as well.
-- The backfill runs once: until source_report_code is NOT NULL.
DISABLE TRIGGER dbo.trg_stock_snapshots_protect_locked ON dbo.stock_snapshots;
EXEC(N'IF COLUMNPROPERTY(OBJECT_ID(N''dbo.stock_snapshots''),''source_report_code'',''AllowsNull'')=1
BEGIN
 UPDATE s SET source_report_code=CASE WHEN l.source_record_type=''R010_SNAPSHOT'' THEN ''R010'' ELSE ''CLOSING_STOCK'' END
 FROM dbo.stock_snapshots s JOIN dbo.source_lineage l ON l.source_lineage_id=s.source_lineage_id
 WHERE s.source_report_code IS NULL;
 WITH s AS (SELECT line_seq,ROW_NUMBER() OVER(PARTITION BY store_code,snapshot_date,source_report_code,product_code,item_discriminator
   ORDER BY quantity,unit_cost,total_cost,stock_snapshot_id) n FROM dbo.stock_snapshots)
 UPDATE s SET line_seq=n WHERE n>1;
END');
ENABLE TRIGGER dbo.trg_stock_snapshots_protect_locked ON dbo.stock_snapshots;
IF COLUMNPROPERTY(OBJECT_ID(N'dbo.stock_snapshots'),'source_report_code','AllowsNull')=1
 EXEC(N'ALTER TABLE dbo.stock_snapshots ALTER COLUMN source_report_code varchar(30) NOT NULL');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.stock_snapshots') AND name=N'UX_stock_snapshots_identity')
 EXEC(N'CREATE UNIQUE INDEX UX_stock_snapshots_identity ON dbo.stock_snapshots(store_code,snapshot_date,source_report_code,
  product_code,item_discriminator,line_seq)');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.stock_snapshots') AND name=N'IX_stock_snapshots_source')
 EXEC(N'CREATE INDEX IX_stock_snapshots_source ON dbo.stock_snapshots(store_code,snapshot_date,source_report_code)
  INCLUDE(product_code,quantity,total_cost)');

-- @source NULL (an older caller) is derived from the lineage record type, as the backfill above derives it,
-- so no caller can store a NULL or a wrong source. Conflicts are logged under the source, not R001.
EXEC(N'CREATE OR ALTER PROCEDURE dbo.persist_stock_snapshot
 @store varchar(30),@date date,@product nvarchar(80),@ean nvarchar(80)=NULL,@brand nvarchar(80)=NULL,@brandname nvarchar(200)=NULL,@cluster nvarchar(100)=NULL,@gender nvarchar(50)=NULL,@batch nvarchar(80)=NULL,@uid nvarchar(100)=NULL,@qty decimal(19,4),@unit decimal(19,4)=NULL,@total decimal(19,4)=NULL,@lineage bigint,@source varchar(30)=NULL,@line_seq int=1
AS
BEGIN
 SET NOCOUNT ON; DECLARE @existing bigint,@file bigint,@record varchar(40),@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id,@record=source_record_type FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
 SET @source=COALESCE(NULLIF(UPPER(LTRIM(RTRIM(@source))),''''),CASE WHEN @record=''R010_SNAPSHOT'' THEN ''R010'' ELSE ''CLOSING_STOCK'' END);
 SET @identity=CONCAT(@store,N''/'',@date,N''/'',@source,N''/'',@product,N''/'',COALESCE(@uid,@batch,@ean,N''''),N''/#'',@line_seq);
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(ISNULL(@ean,N''''),N''|'',ISNULL(@brand,N''''),N''|'',ISNULL(@brandname,N''''),N''|'',ISNULL(@cluster,N''''),N''|'',ISNULL(@gender,N''''),N''|'',ISNULL(@batch,N''''),N''|'',ISNULL(@uid,N''''),N''|'',@qty,N''|'',ISNULL(@unit,0),N''|'',ISNULL(@total,0))),2));
 SELECT TOP(1) @existing=stock_snapshot_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(ISNULL(ean,N''''),N''|'',ISNULL(brand_code,N''''),N''|'',ISNULL(brand_name,N''''),N''|'',ISNULL(cluster,N''''),N''|'',ISNULL(gender,N''''),N''|'',ISNULL(batch_number,N''''),N''|'',ISNULL(source_uid,N''''),N''|'',quantity,N''|'',ISNULL(unit_cost,0),N''|'',ISNULL(total_cost,0))),2)) FROM dbo.stock_snapshots WHERE store_code=@store AND snapshot_date=@date AND source_report_code=@source AND product_code=@product AND item_discriminator=COALESCE(@uid,@batch,@ean,N'''') AND line_seq=@line_seq ORDER BY stock_snapshot_id;
 IF @existing IS NOT NULL BEGIN IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''ALREADY_PRESENT'',@incoming,N''Identical stock snapshot row already exists.''); ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''Stock snapshot identity exists with different content.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,@source,@identity,@current,@incoming,N''Closing-stock values differ. Review and request a controlled restatement.''); END RETURN; END
 INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,ean,brand_code,brand_name,cluster,gender,batch_number,source_uid,quantity,unit_cost,total_cost,source_lineage_id,source_report_code,line_seq) VALUES(@store,@date,@product,@ean,@brand,@brandname,@cluster,@gender,@batch,@uid,@qty,@unit,@total,@lineage,@source,@line_seq);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''NEW'',@incoming,N''New stock snapshot row imported.'');
END');

-- The rows of the preferred source present for each (store, snapshot date): Closing Stock (R011) first,
-- then BinWise (R010), then any other source. Same result as ranking the sources per store-day and
-- keeping the best rank, written as seeks on IX_stock_snapshots_source so a per-row caller stays cheap.
EXEC(N'CREATE OR ALTER VIEW dbo.v_stock_snapshots_effective AS
SELECT s.stock_snapshot_id,s.store_code,s.snapshot_date,s.product_code,s.ean,s.brand_code,s.brand_name,s.cluster,s.gender,
       s.batch_number,s.source_uid,s.quantity,s.unit_cost,s.total_cost,s.source_lineage_id,s.source_report_code,s.line_seq,
       s.item_discriminator
FROM dbo.stock_snapshots s
WHERE s.source_report_code=''CLOSING_STOCK''
   OR (NOT EXISTS(SELECT 1 FROM dbo.stock_snapshots c WHERE c.store_code=s.store_code AND c.snapshot_date=s.snapshot_date
                  AND c.source_report_code=''CLOSING_STOCK'')
       AND (s.source_report_code=''R010''
            OR NOT EXISTS(SELECT 1 FROM dbo.stock_snapshots b WHERE b.store_code=s.store_code AND b.snapshot_date=s.snapshot_date
                          AND b.source_report_code=''R010'')))');
GRANT SELECT ON dbo.v_stock_snapshots_effective TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_stock_snapshots_effective TO etp_viewer,etp_store_manager,etp_owner;
-- <<< D_SNAPSHOT end

-- >>> E_ENRICHMENT begin
-- <<< E_ENRICHMENT end

-- >>> G_RESTATEMENT begin
-- <<< G_RESTATEMENT end

