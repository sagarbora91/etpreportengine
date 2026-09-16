SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- These migration-only metadata/value backfills must also work for finalised days.
-- DDL is transactional: any failed migration rolls the guard state back as well.
DISABLE TRIGGER dbo.trg_sales_enrichments_protect_locked ON dbo.sales_line_enrichments;
DISABLE TRIGGER dbo.trg_import_files_protect_locked ON dbo.import_files;
DISABLE TRIGGER dbo.trg_stock_movements_protect_locked ON dbo.stock_movements;
DISABLE TRIGGER dbo.trg_sales_tenders_protect_locked ON dbo.sales_tenders;
DISABLE TRIGGER dbo.trg_sales_lines_protect_locked ON dbo.sales_lines;
DISABLE TRIGGER dbo.trg_sales_invoices_protect_locked ON dbo.sales_invoices;

-- Source net retains ex-GST NETVALUE; gross is GST-inclusive NETAMOUNT.
ALTER TABLE dbo.sales_lines ADD source_tax_amount decimal(19,4) NULL;
ALTER TABLE dbo.sales_line_enrichments ADD source_gross_value decimal(19,4) NULL,
    staff_name nvarchar(200) NULL, content_key varchar(80) NULL, invoice_year int NULL;
EXEC(N'UPDATE dbo.sales_line_enrichments SET invoice_year=YEAR(transaction_date)+CASE WHEN MONTH(transaction_date)>=4 THEN 1 ELSE 0 END');
EXEC(N'CREATE UNIQUE INDEX UX_enrichment_content ON dbo.sales_line_enrichments(enrichment_type,store_code,content_key)
    WHERE content_key IS NOT NULL');
ALTER TABLE dbo.import_files ADD period_start date NULL, period_end date NULL;
ALTER TABLE dbo.import_files ADD data_truth_version smallint NOT NULL CONSTRAINT DF_import_files_data_truth DEFAULT(0);
DROP INDEX UX_import_files_source_sha256 ON dbo.import_files;
EXEC(N'CREATE UNIQUE INDEX UX_import_files_source_sha256 ON dbo.import_files(source_sha256,report_code,store_code,period_start,period_end,data_truth_version)');
ALTER TABLE dbo.import_restatements DROP CONSTRAINT UQ_import_restatements_replacement;
ALTER TABLE dbo.import_restatements ADD CONSTRAINT UQ_import_restatements_pair UNIQUE(previous_import_file_id,replacement_import_file_id);
ALTER TABLE dbo.source_documents ADD period_start date NULL, period_end date NULL;
EXEC(N'UPDATE f SET period_start=COALESCE(b.period_start,f.business_date),period_end=COALESCE(b.period_end,f.business_date)
FROM dbo.import_files f JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id');
EXEC(N'ALTER TABLE dbo.import_files ADD CONSTRAINT CK_import_files_period CHECK(period_start IS NULL OR period_end>=period_start)');
EXEC(N'CREATE INDEX IX_import_files_range ON dbo.import_files(store_code,report_code,period_start,period_end) INCLUDE(is_superseded,source_sha256)');

-- Re-key invoice identity without transient collisions with the old calendar-year key.
IF EXISTS(SELECT 1 FROM dbo.sales_invoices GROUP BY store_code,document_number,
    YEAR(transaction_date)+CASE WHEN MONTH(transaction_date)>=4 THEN 1 ELSE 0 END HAVING COUNT(*)>1)
    THROW 51240,'Existing invoices collide within a financial year. Review their imports before upgrading.',1;
UPDATE dbo.sales_invoices SET invoice_year=-invoice_year;
UPDATE dbo.sales_invoices SET invoice_year=YEAR(transaction_date)+CASE WHEN MONTH(transaction_date)>=4 THEN 1 ELSE 0 END;
UPDATE dbo.stock_movements SET invoice_year=YEAR(document_date)+CASE WHEN MONTH(document_date)>=4 THEN 1 ELSE 0 END;
UPDATE dbo.sales_tenders SET is_reporting_eligible=1,exclusion_reason=NULL WHERE tender_type='PAYMENTTYPE25';
EXEC(N'UPDATE dbo.sales_lines SET source_tax_amount=source_gross_amount-source_net_amount WHERE source_gross_amount IS NOT NULL AND source_net_amount IS NOT NULL');

EXEC(N'ALTER PROCEDURE dbo.persist_sales_line
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@line nvarchar(80),@product nvarchar(80),@type nvarchar(80)=NULL,@qty decimal(19,4),@gross decimal(19,4)=NULL,@net decimal(19,4)=NULL,@brandcode nvarchar(80)=NULL,@brandname nvarchar(200)=NULL,@segment nvarchar(100)=NULL,@currency char(3),@lineage bigint,@tax decimal(19,4)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @invoice bigint,@existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
 SET @identity=CONCAT(@store,N''/'',@year,N''/'',@doc,N''/'',@line);
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@date,N''|'',@product,N''|'',ISNULL(@type,N''''),N''|'',@qty,N''|'',ISNULL(@gross,0),N''|'',ISNULL(@net,0),N''|'',ISNULL(@brandcode,N''''),N''|'',ISNULL(@brandname,N''''),N''|'',ISNULL(@segment,N''''),N''|'',@currency)),2));
 SELECT @invoice=sales_invoice_id FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND invoice_year=@year AND document_number=@doc;
 IF @invoice IS NULL BEGIN INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,@doc,@year,@date); SET @invoice=SCOPE_IDENTITY(); END
 ELSE IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice AND transaction_date<>@date)
 BEGIN
   SET @current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(N''invoice-date|'',(SELECT transaction_date FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice))),2));
   INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''The invoice identity already exists with a different date.'');
   INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R025'',@identity,@current,@incoming,N''Invoice date differs. Review and request a controlled restatement.''); RETURN;
 END
 SELECT @existing=sales_line_id,
   @current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@date,N''|'',product_code,N''|'',ISNULL(source_transaction_type,N''''),N''|'',source_quantity,N''|'',ISNULL(source_gross_amount,0),N''|'',ISNULL(source_net_amount,0),N''|'',ISNULL(source_brand_code,N''''),N''|'',ISNULL(source_brand_name,N''''),N''|'',ISNULL(brand_segment,N''''),N''|'',currency_code)),2))
 FROM dbo.sales_lines WHERE sales_invoice_id=@invoice AND line_identifier=@line;
 IF @existing IS NOT NULL
 BEGIN
   IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''ALREADY_PRESENT'',@incoming,N''Identical sales transaction already exists.'');
   ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''Sales transaction identity exists with different content.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R025'',@identity,@current,@incoming,N''One or more protected transaction values differ. Review and request a controlled restatement.''); END
   RETURN;
 END
 INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_brand_code,source_brand_name,brand_segment,currency_code,source_lineage_id,source_tax_amount) VALUES(@invoice,@line,@product,@type,@qty,@gross,@net,@brandcode,@brandname,@segment,@currency,@lineage,@tax);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''NEW'',@incoming,N''New sales transaction imported.'');
END');

EXEC(N'ALTER PROCEDURE dbo.persist_sales_invoice_control
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@type nvarchar(80)=NULL,@qty decimal(19,4),@net decimal(19,4),@currency char(3),@lineage bigint
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @invoice bigint,@existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage; SET @identity=CONCAT(@store,N''/'',@year,N''/'',@doc,N''/CONTROL'');
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@date,N''|'',ISNULL(@type,N''''),N''|'',@qty,N''|'',@net,N''|'',@currency)),2));
 SELECT @invoice=sales_invoice_id FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND invoice_year=@year AND document_number=@doc;
 IF @invoice IS NULL BEGIN INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,@doc,@year,@date); SET @invoice=SCOPE_IDENTITY(); END
 ELSE IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice AND transaction_date<>@date)
 BEGIN SET @current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(N''invoice-date|'',(SELECT transaction_date FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice))),2)); INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''The invoice identity already exists with a different date.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R022'',@identity,@current,@incoming,N''Invoice date differs. Review and request a controlled restatement.''); RETURN; END
 SELECT TOP(1) @existing=sales_invoice_control_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@date,N''|'',ISNULL(source_transaction_type,N''''),N''|'',source_invoice_quantity,N''|'',source_net_value,N''|'',currency_code)),2)) FROM dbo.sales_invoice_controls WHERE sales_invoice_id=@invoice ORDER BY sales_invoice_control_id;
 IF @existing IS NOT NULL BEGIN IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''ALREADY_PRESENT'',@incoming,N''Identical invoice control already exists.''); ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''Invoice control identity exists with different content.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R022'',@identity,@current,@incoming,N''Revenue control values differ. Review and request a controlled restatement.''); END RETURN; END
 INSERT dbo.sales_invoice_controls(sales_invoice_id,source_transaction_type,source_invoice_quantity,source_net_value,currency_code,source_lineage_id) VALUES(@invoice,@type,@qty,@net,@currency,@lineage);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''NEW'',@incoming,N''New invoice control imported.'');
END');

EXEC(N'ALTER PROCEDURE dbo.persist_sales_tender
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@type nvarchar(80),@amount decimal(19,4),@currency char(3),@lineage bigint,@eligible bit=1,@reason nvarchar(200)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @invoice bigint,@existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage; SET @identity=CONCAT(@store,N''/'',@year,N''/'',@doc,N''/TENDER/'',UPPER(@type));
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@date,N''|'',UPPER(@type),N''|'',@amount,N''|'',@currency,N''|'',@eligible,N''|'',ISNULL(@reason,N''''))),2));
 SELECT @invoice=sales_invoice_id FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND invoice_year=@year AND document_number=@doc;
 IF @invoice IS NULL BEGIN INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,@doc,@year,@date); SET @invoice=SCOPE_IDENTITY(); END
 ELSE IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice AND transaction_date<>@date)
 BEGIN SET @current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(N''invoice-date|'',(SELECT transaction_date FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice))),2)); INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''The invoice identity already exists with a different date.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R022'',@identity,@current,@incoming,N''Invoice date differs. Review and request a controlled restatement.''); RETURN; END
 SELECT TOP(1) @existing=sales_tender_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES(''SHA2_256'',CONCAT(@date,N''|'',UPPER(tender_type),N''|'',source_amount,N''|'',currency_code,N''|'',is_reporting_eligible,N''|'',ISNULL(exclusion_reason,N''''))),2)) FROM dbo.sales_tenders WHERE sales_invoice_id=@invoice AND UPPER(tender_type)=UPPER(@type) ORDER BY sales_tender_id;
 IF @existing IS NOT NULL BEGIN IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''ALREADY_PRESENT'',@incoming,N''Identical tender transaction already exists.''); ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''CONFLICT'',@incoming,N''Tender identity exists with different content.''); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,''R022'',@identity,@current,@incoming,N''Tender values differ. Review and request a controlled restatement.''); END RETURN; END
 INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id,is_reporting_eligible,exclusion_reason) VALUES(@invoice,@type,@amount,@currency,@lineage,@eligible,@reason);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,''NEW'',@incoming,N''New tender transaction imported.'');
END');

EXEC(N'ALTER PROCEDURE dbo.prepare_import_restatement
 @previous_file_id bigint,@replacement_file_id bigint,@user nvarchar(100),@reason nvarchar(500)
AS
BEGIN
 SET NOCOUNT ON;
 IF LEN(LTRIM(RTRIM(@reason)))=0 THROW 51039,''A restatement reason is required.'',1;
 DECLARE @store varchar(30),@date date,@report varchar(30),@newStore varchar(30),@newDate date,@newReport varchar(30),@restatement bigint,@start date,@end date,@newStart date,@newEnd date;
 SELECT @store=store_code,@date=business_date,@report=report_code,@start=COALESCE(period_start,business_date),@end=COALESCE(period_end,business_date) FROM dbo.import_files WITH(UPDLOCK,HOLDLOCK)
  WHERE import_file_id=@previous_file_id AND is_superseded=0;
 SELECT @newStore=store_code,@newDate=business_date,@newReport=report_code,@newStart=COALESCE(period_start,business_date),@newEnd=COALESCE(period_end,business_date) FROM dbo.import_files WHERE import_file_id=@replacement_file_id;
 IF @report IS NULL THROW 51040,''The previous current import file was not found.'',1;
 IF @store<>@newStore OR (@newStart>@start OR @newEnd<@end) OR @report<>@newReport THROW 51041,''A restatement must replace the same store and report type and cover the previous date range.'',1;
 IF EXISTS(SELECT 1 FROM dbo.daily_reporting_days WHERE store_code=@store AND business_date BETWEEN @newStart AND @newEnd AND status=''LOCKED'')
   THROW 51042,''Reopen the finalised business date before applying a restatement.'',1;

 INSERT dbo.import_restatements(store_code,business_date,report_code,previous_import_file_id,replacement_import_file_id,requested_by,reason,impact_summary)
 VALUES(@store,@date,@report,@previous_file_id,@replacement_file_id,@user,@reason,N''Previous canonical facts archived; replacement facts become the only current reporting generation.'');
 SET @restatement=SCOPE_IDENTITY();

 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''SalesLine'',l.source_lineage_id,
   (SELECT l.sales_line_id,l.sales_invoice_id,l.line_identifier,l.product_code,l.source_transaction_type,l.source_quantity,l.source_gross_amount,l.source_net_amount,l.source_brand_code,l.source_brand_name,l.brand_segment,l.currency_code FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_lines l JOIN dbo.source_lineage s ON s.source_lineage_id=l.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''InvoiceControl'',c.source_lineage_id,
   (SELECT c.sales_invoice_control_id,c.sales_invoice_id,c.source_transaction_type,c.source_invoice_quantity,c.source_net_value,c.currency_code FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_invoice_controls c JOIN dbo.source_lineage s ON s.source_lineage_id=c.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''Tender'',t.source_lineage_id,
   (SELECT t.sales_tender_id,t.sales_invoice_id,t.tender_type,t.source_amount,t.currency_code,t.is_reporting_eligible,t.exclusion_reason FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_tenders t JOIN dbo.source_lineage s ON s.source_lineage_id=t.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''StockMovement'',m.source_lineage_id,
   (SELECT m.stock_movement_id,m.store_code,m.document_number,m.invoice_year,m.document_date,m.product_code,m.source_transaction_type,m.from_location,m.to_location,m.opening_quantity,m.transaction_quantity,m.closing_quantity FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.stock_movements m JOIN dbo.source_lineage s ON s.source_lineage_id=m.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''StockSnapshot'',p.source_lineage_id,
   (SELECT p.stock_snapshot_id,p.store_code,p.snapshot_date,p.product_code,p.ean,p.brand_code,p.brand_name,p.cluster,p.gender,p.batch_number,p.source_uid,p.quantity,p.unit_cost,p.total_cost FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.stock_snapshots p JOIN dbo.source_lineage s ON s.source_lineage_id=p.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''SalesEnrichment'',e.source_lineage_id,
   (SELECT e.sales_line_enrichment_id,e.enrichment_type,e.store_code,e.transaction_date,e.document_number,e.product_code,e.source_transaction_type,e.source_quantity,e.source_net_value,e.source_cro_number,e.scheme_discount,e.user_discount,e.pre_discount,e.other_charges,e.activation_details,e.user_discount_details,e.match_status FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_line_enrichments e JOIN dbo.source_lineage s ON s.source_lineage_id=e.source_lineage_id WHERE s.import_file_id=@previous_file_id;

 UPDATE e SET matched_sales_line_id=NULL,match_status=''Missing''
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
END');
EXEC(N'ALTER TRIGGER dbo.trg_import_files_protect_locked ON dbo.import_files AFTER INSERT,UPDATE,DELETE AS
BEGIN
 SET NOCOUNT ON;
 IF EXISTS(SELECT 1 FROM (SELECT store_code,COALESCE(period_start,business_date) period_start,COALESCE(period_end,business_date) period_end FROM inserted
 UNION SELECT store_code,COALESCE(period_start,business_date),COALESCE(period_end,business_date) FROM deleted) f
 JOIN dbo.daily_reporting_days d ON d.store_code=f.store_code AND d.business_date BETWEEN f.period_start AND f.period_end WHERE d.status=''LOCKED'')
 THROW 51021,''A day inside this export is finalised. Reopen it before changing its sources.'',1;
END');
ENABLE TRIGGER dbo.trg_sales_enrichments_protect_locked ON dbo.sales_line_enrichments;
ENABLE TRIGGER dbo.trg_import_files_protect_locked ON dbo.import_files;
ENABLE TRIGGER dbo.trg_stock_movements_protect_locked ON dbo.stock_movements;
ENABLE TRIGGER dbo.trg_sales_tenders_protect_locked ON dbo.sales_tenders;
ENABLE TRIGGER dbo.trg_sales_lines_protect_locked ON dbo.sales_lines;
ENABLE TRIGGER dbo.trg_sales_invoices_protect_locked ON dbo.sales_invoices;
COMMIT TRANSACTION;
