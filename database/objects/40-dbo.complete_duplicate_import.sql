CREATE OR ALTER PROCEDURE dbo.complete_duplicate_import @file bigint,@previous bigint
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_store_manager'),0)<>1 AND COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  THROW 51420,'Owner or Store Manager permission is required.',1;
 IF @@TRANCOUNT=0 THROW 51422,'Import writes require an enclosing transaction.',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status='Processing')
  THROW 51422,'The source import is not open for writing.',1;
 IF @file=@previous OR NOT EXISTS(SELECT 1 FROM dbo.import_files f JOIN dbo.import_files old
   ON old.import_file_id=@previous AND old.is_superseded=0 AND old.data_truth_version=1
     AND old.store_code=f.store_code AND old.report_code=f.report_code
     AND COALESCE(old.period_start,old.business_date)<=COALESCE(f.period_end,f.business_date)
     AND COALESCE(old.period_end,old.business_date)>=COALESCE(f.period_start,f.business_date)
   WHERE f.import_file_id=@file)
  THROW 51425,'A matching current source is required for duplicate classification.',1;
 IF EXISTS(SELECT 1 FROM dbo.etp_import_content incoming WHERE incoming.import_file_id=@file AND NOT EXISTS(
   SELECT 1 FROM dbo.etp_import_content existing JOIN dbo.import_files old ON old.import_file_id=existing.import_file_id
   JOIN dbo.import_files fresh ON fresh.import_file_id=@file
   WHERE old.import_file_id<>@file AND old.is_superseded=0 AND old.data_truth_version=1
     AND old.store_code=fresh.store_code AND old.report_code=fresh.report_code
     AND COALESCE(old.period_start,old.business_date)<=COALESCE(fresh.period_end,fresh.business_date)
     AND COALESCE(old.period_end,old.business_date)>=COALESCE(fresh.period_start,fresh.business_date)
     AND existing.content_key=incoming.content_key))
  THROW 51425,'The duplicate contains source content not already present.',1;
 IF EXISTS(SELECT 1 FROM dbo.source_lineage s WHERE s.import_file_id=@file AND (
   EXISTS(SELECT 1 FROM dbo.sales_lines x WHERE x.source_lineage_id=s.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.sales_invoice_controls x WHERE x.source_lineage_id=s.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.sales_tenders x WHERE x.source_lineage_id=s.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.sales_line_enrichments x WHERE x.source_lineage_id=s.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.stock_movements x WHERE x.source_lineage_id=s.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.stock_snapshots x WHERE x.source_lineage_id=s.source_lineage_id)))
  THROW 51425,'Duplicate classification cannot discard canonical facts.',1;
 UPDATE dbo.import_files SET is_superseded=1,superseded_by_import_file_id=@previous,superseded_utc=SYSUTCDATETIME(),
   superseded_by=SUSER_SNAME(),restatement_reason=N'Duplicate content; all rows already present.' WHERE import_file_id=@file;
 UPDATE b SET status='Completed',source_row_count=(SELECT COUNT(*) FROM dbo.etp_import_content WHERE import_file_id=@file),completed_utc=SYSUTCDATETIME()
 FROM dbo.import_batches b JOIN dbo.import_files f ON f.import_batch_id=b.import_batch_id WHERE f.import_file_id=@file;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.complete_duplicate_import TO etp_owner,etp_store_manager;
