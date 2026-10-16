CREATE OR ALTER PROCEDURE dbo.retire_redated_import @previous_file_id bigint,@replacement_file_id bigint
AS BEGIN
 -- IF-028 (decision 66): the same stock file (same SHA-256, store and report) imported again under a corrected date
 -- retires the earlier, wrongly dated import by itself, with the reason "re-dated". Nothing is lost: the earlier import's
 -- record and landed source rows stay (superseded, with the reason), its snapshot facts are copied to
 -- dbo.restatement_fact_archive before they leave dbo.stock_snapshots, and dbo.import_restatements records who and why.
 -- 1.12.1 review M-3: a dbo.stock_snapshot_r011_fallbacks row of those snapshots (an R010 row standing in for an R011
 -- row 0041 could not rebuild) goes with its snapshot (ON DELETE CASCADE), so it is archived too, as 'StockSnapshotFallback'.
 -- The importer reads both back to tell the user which day lost its stock reading (IMPORT_REDATED_RETIRED).
 -- Called inside the import transaction by SqlServerTransactionalImportStore (PhaseOneImportPersistence.cs).
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_store_manager'),0)<>1 AND COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51420,'Owner or Store Manager permission is required.',1;
 IF @@TRANCOUNT=0 THROW 51422,'Import writes require an enclosing transaction.',1;
 DECLARE @store varchar(30),@date date,@report varchar(30),@start date,@end date,@newStart date,@newEnd date,
   @actor nvarchar(100)=SUSER_SNAME(),@restatement bigint,@reason nvarchar(500);
 SELECT @store=old.store_code,@date=old.business_date,@report=old.report_code,
   @start=COALESCE(old.period_start,old.business_date),@end=COALESCE(old.period_end,old.business_date),
   @newStart=COALESCE(fresh.period_start,fresh.business_date),@newEnd=COALESCE(fresh.period_end,fresh.business_date)
 FROM dbo.import_files old WITH(UPDLOCK,HOLDLOCK)
 JOIN dbo.import_files fresh WITH(UPDLOCK,HOLDLOCK) ON fresh.import_file_id=@replacement_file_id
 WHERE old.import_file_id=@previous_file_id AND old.import_file_id<>fresh.import_file_id
   AND old.is_superseded=0 AND fresh.is_superseded=0 AND old.data_truth_version=1 AND fresh.data_truth_version=1
   AND old.source_sha256=fresh.source_sha256 AND old.store_code=fresh.store_code AND old.report_code=fresh.report_code
   AND old.report_code IN('R010','CLOSING_STOCK');
 IF @report IS NULL
  THROW 51423,'Only a current import of the same stock file under another date can retire an earlier import.',1;
 IF @start=@newStart AND @end=@newEnd
  THROW 51423,'Only a current import of the same stock file under another date can retire an earlier import.',1;
 -- A stock snapshot import holds snapshot facts only; anything else is not a re-dating and needs an Owner restatement.
 IF EXISTS(SELECT 1 FROM dbo.source_lineage l WHERE l.import_file_id=@previous_file_id AND (
   EXISTS(SELECT 1 FROM dbo.sales_lines x WHERE x.source_lineage_id=l.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.sales_invoice_controls x WHERE x.source_lineage_id=l.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.sales_tenders x WHERE x.source_lineage_id=l.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.sales_line_enrichments x WHERE x.source_lineage_id=l.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.stock_movements x WHERE x.source_lineage_id=l.source_lineage_id)))
  THROW 51424,'Only stock snapshot imports are retired automatically when re-dated.',1;
 IF EXISTS(SELECT 1 FROM dbo.daily_reporting_days WHERE store_code=@store AND business_date BETWEEN @start AND @end AND status='LOCKED')
  THROW 51042,'Reopen the finalised business date of the earlier, wrongly dated import of this file, then import it again.',1;

 SET @reason=CONCAT(N're-dated: the same file was imported again as import ',@replacement_file_id,N' for ',
   CONVERT(char(10),@newStart,23),CASE WHEN @newEnd<>@newStart THEN CONCAT(N' to ',CONVERT(char(10),@newEnd,23)) END,
   N'; this import had stored it for ',CONVERT(char(10),@start,23),
   CASE WHEN @end<>@start THEN CONCAT(N' to ',CONVERT(char(10),@end,23)) END,N'.');
 INSERT dbo.import_restatements(store_code,business_date,report_code,previous_import_file_id,replacement_import_file_id,requested_by,reason,impact_summary)
 VALUES(@store,@date,@report,@previous_file_id,@replacement_file_id,@actor,@reason,
   N'Snapshot rows (and any Closing Stock fallback rows) of the wrongly dated import archived; the re-import under the corrected date is the current reading. The import record and its source rows are kept.');
 SET @restatement=SCOPE_IDENTITY();
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,'StockSnapshot',p.source_lineage_id,
   (SELECT p.stock_snapshot_id,p.store_code,p.snapshot_date,p.product_code,p.ean,p.brand_code,p.brand_name,p.cluster,p.gender,p.batch_number,p.source_uid,p.quantity,p.unit_cost,p.total_cost,p.source_report_code,p.line_seq FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.stock_snapshots p JOIN dbo.source_lineage s ON s.source_lineage_id=p.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,'StockSnapshotFallback',p.source_lineage_id,
   (SELECT k.stock_snapshot_id,k.import_file_id,p.store_code,p.snapshot_date,p.product_code FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.stock_snapshot_r011_fallbacks k JOIN dbo.stock_snapshots p ON p.stock_snapshot_id=k.stock_snapshot_id
 JOIN dbo.source_lineage s ON s.source_lineage_id=p.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE p FROM dbo.stock_snapshots p JOIN dbo.source_lineage s ON s.source_lineage_id=p.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 UPDATE dbo.import_files SET is_superseded=1,superseded_by_import_file_id=@replacement_file_id,superseded_utc=SYSUTCDATETIME(),
   superseded_by=@actor,restatement_reason=@reason WHERE import_file_id=@previous_file_id;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.retire_redated_import TO etp_owner,etp_store_manager;
