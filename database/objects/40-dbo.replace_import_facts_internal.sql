CREATE OR ALTER PROCEDURE dbo.replace_import_facts_internal
 @previous_file_id bigint,@replacement_file_id bigint,@user nvarchar(100),@reason nvarchar(500)
AS
BEGIN
 SET NOCOUNT ON;
 IF COALESCE(IS_ROLEMEMBER('etp_owner'),0)<>1 AND COALESCE(IS_SRVROLEMEMBER('sysadmin'),0)<>1
  AND NOT EXISTS(SELECT 1 FROM dbo.import_restatement_approvals r JOIN dbo.approval_requests a ON a.approval_request_id=r.approval_request_id WHERE r.previous_import_file_id=@previous_file_id AND r.applied_import_file_id=@replacement_file_id AND a.status='APPROVED')
  THROW 51421,'Owner permission is required for corrective restatement.',1;
 IF @@TRANCOUNT=0 THROW 51422,'Import writes require an enclosing transaction.',1;
 SET @user=SUSER_SNAME();
 IF dbo.is_blank_text(@reason)=1 THROW 51039,'A restatement reason is required.',1;
 DECLARE @store varchar(30),@date date,@report varchar(30),@newStore varchar(30),@newDate date,@newReport varchar(30),@restatement bigint,@start date,@end date,@newStart date,@newEnd date;
 SELECT @store=store_code,@date=business_date,@report=report_code,@start=COALESCE(period_start,business_date),@end=COALESCE(period_end,business_date) FROM dbo.import_files WITH(UPDLOCK,HOLDLOCK)
  WHERE import_file_id=@previous_file_id AND is_superseded=0;
 SELECT @newStore=store_code,@newDate=business_date,@newReport=report_code,@newStart=COALESCE(period_start,business_date),@newEnd=COALESCE(period_end,business_date) FROM dbo.import_files WHERE import_file_id=@replacement_file_id;
 IF @report IS NULL THROW 51040,'The previous current import file was not found.',1;
 IF @store<>@newStore OR (@newStart>@start OR @newEnd<@end) OR @report<>@newReport THROW 51041,'A restatement must replace the same store and report type and cover the previous date range.',1;
 IF EXISTS(SELECT 1 FROM dbo.daily_reporting_days WHERE store_code=@store AND business_date BETWEEN @newStart AND @newEnd AND status='LOCKED')
   THROW 51042,'Reopen the finalised business date before applying a restatement.',1;

 INSERT dbo.import_restatements(store_code,business_date,report_code,previous_import_file_id,replacement_import_file_id,requested_by,reason,impact_summary)
 VALUES(@store,@date,@report,@previous_file_id,@replacement_file_id,@user,@reason,N'Previous canonical facts archived; replacement facts become the only current reporting generation.');
 SET @restatement=SCOPE_IDENTITY();

 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,'SalesLine',l.source_lineage_id,
   (SELECT l.sales_line_id,l.sales_invoice_id,l.line_identifier,l.product_code,l.source_transaction_type,l.source_quantity,l.source_gross_amount,l.source_net_amount,l.source_tax_amount,l.source_brand_code,l.source_brand_name,l.brand_segment,l.currency_code FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_lines l JOIN dbo.source_lineage s ON s.source_lineage_id=l.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,'InvoiceControl',c.source_lineage_id,
   (SELECT c.sales_invoice_control_id,c.sales_invoice_id,c.source_transaction_type,c.source_invoice_quantity,c.source_net_value,c.currency_code FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_invoice_controls c JOIN dbo.source_lineage s ON s.source_lineage_id=c.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,'Tender',t.source_lineage_id,
   (SELECT t.sales_tender_id,t.sales_invoice_id,t.tender_type,t.source_amount,t.currency_code,t.is_reporting_eligible,t.exclusion_reason FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_tenders t JOIN dbo.source_lineage s ON s.source_lineage_id=t.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,'StockMovement',m.source_lineage_id,
   (SELECT m.stock_movement_id,m.store_code,m.document_number,m.invoice_year,m.document_date,m.product_code,m.source_transaction_type,m.from_location,m.to_location,m.opening_quantity,m.transaction_quantity,m.closing_quantity FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.stock_movements m JOIN dbo.source_lineage s ON s.source_lineage_id=m.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,'StockSnapshot',p.source_lineage_id,
   (SELECT p.stock_snapshot_id,p.store_code,p.snapshot_date,p.product_code,p.ean,p.brand_code,p.brand_name,p.cluster,p.gender,p.batch_number,p.source_uid,p.quantity,p.unit_cost,p.total_cost FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.stock_snapshots p JOIN dbo.source_lineage s ON s.source_lineage_id=p.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,'SalesEnrichment',e.source_lineage_id,
   (SELECT e.sales_line_enrichment_id,e.enrichment_type,e.store_code,e.transaction_date,e.document_number,e.product_code,e.source_transaction_type,e.source_quantity,e.source_net_value,e.source_gross_value,e.content_key,e.invoice_year,e.staff_name,e.source_cro_number,e.scheme_discount,e.user_discount,e.pre_discount,e.other_charges,e.activation_details,e.user_discount_details,e.match_status FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_line_enrichments e JOIN dbo.source_lineage s ON s.source_lineage_id=e.source_lineage_id WHERE s.import_file_id=@previous_file_id;

 UPDATE e SET matched_sales_line_id=NULL,match_status='Missing'
 FROM dbo.sales_line_enrichments e JOIN dbo.sales_lines l ON l.sales_line_id=e.matched_sales_line_id
 JOIN dbo.source_lineage s ON s.source_lineage_id=l.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE e FROM dbo.sales_line_enrichments e JOIN dbo.source_lineage s ON s.source_lineage_id=e.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE l FROM dbo.sales_lines l JOIN dbo.source_lineage s ON s.source_lineage_id=l.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE c FROM dbo.sales_invoice_controls c JOIN dbo.source_lineage s ON s.source_lineage_id=c.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE t FROM dbo.sales_tenders t JOIN dbo.source_lineage s ON s.source_lineage_id=t.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE m FROM dbo.stock_movements m JOIN dbo.source_lineage s ON s.source_lineage_id=m.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE p FROM dbo.stock_snapshots p JOIN dbo.source_lineage s ON s.source_lineage_id=p.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 UPDATE dbo.import_files SET is_superseded=1,superseded_by_import_file_id=@replacement_file_id,superseded_utc=SYSUTCDATETIME(),superseded_by=@user,restatement_reason=@reason
 WHERE import_file_id=@previous_file_id;
END
-- >>> GRANTS
DENY EXECUTE ON dbo.replace_import_facts_internal TO etp_owner,etp_store_manager,etp_viewer;
