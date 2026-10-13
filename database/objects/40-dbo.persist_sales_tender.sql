CREATE OR ALTER PROCEDURE dbo.persist_sales_tender
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@type nvarchar(80),@amount decimal(19,4),@currency char(3),@lineage bigint,@eligible bit=1,@reason nvarchar(200)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @invoice bigint,@existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage; SET @identity=CONCAT(@store,N'/',@year,N'/',@doc,N'/TENDER/',UPPER(@type));
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(@date,N'|',UPPER(@type),N'|',@amount,N'|',@currency,N'|',@eligible,N'|',ISNULL(@reason,N''))),2));
 SELECT @invoice=sales_invoice_id FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND invoice_year=@year AND document_number=@doc;
 IF @invoice IS NULL BEGIN INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,@doc,@year,@date); SET @invoice=SCOPE_IDENTITY(); END
 ELSE IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice AND transaction_date<>@date)
 BEGIN SET @current=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(N'invoice-date|',(SELECT transaction_date FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice))),2)); INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'CONFLICT',@incoming,N'The invoice identity already exists with a different date.'); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,'R022',@identity,@current,@incoming,N'Invoice date differs. Review and request a controlled restatement.'); RETURN; END
 SELECT TOP(1) @existing=sales_tender_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(@date,N'|',UPPER(tender_type),N'|',source_amount,N'|',currency_code,N'|',is_reporting_eligible,N'|',ISNULL(exclusion_reason,N''))),2)) FROM dbo.sales_tenders WHERE sales_invoice_id=@invoice AND UPPER(tender_type)=UPPER(@type) ORDER BY sales_tender_id;
 IF @existing IS NOT NULL BEGIN IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'ALREADY_PRESENT',@incoming,N'Identical tender transaction already exists.'); ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'CONFLICT',@incoming,N'Tender identity exists with different content.'); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,'R022',@identity,@current,@incoming,N'Tender values differ. Review and request a controlled restatement.'); END RETURN; END
 INSERT dbo.sales_tenders(sales_invoice_id,tender_type,source_amount,currency_code,source_lineage_id,is_reporting_eligible,exclusion_reason) VALUES(@invoice,@type,@amount,@currency,@lineage,@eligible,@reason);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'NEW',@incoming,N'New tender transaction imported.');
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.persist_sales_tender TO etp_store_manager;
