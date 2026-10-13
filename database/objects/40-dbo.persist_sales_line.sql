CREATE OR ALTER PROCEDURE dbo.persist_sales_line
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@line nvarchar(80),@product nvarchar(80),@type nvarchar(80)=NULL,@qty decimal(19,4),@gross decimal(19,4)=NULL,@net decimal(19,4)=NULL,@brandcode nvarchar(80)=NULL,@brandname nvarchar(200)=NULL,@segment nvarchar(100)=NULL,@currency char(3),@lineage bigint,@tax decimal(19,4)=NULL
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @invoice bigint,@existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
 SET @identity=CONCAT(@store,N'/',@year,N'/',@doc,N'/',@line);
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(@date,N'|',@product,N'|',ISNULL(@type,N''),N'|',@qty,N'|',ISNULL(@gross,0),N'|',ISNULL(@net,0),N'|',ISNULL(@brandcode,N''),N'|',ISNULL(@brandname,N''),N'|',ISNULL(@segment,N''),N'|',@currency)),2));
 SELECT @invoice=sales_invoice_id FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND invoice_year=@year AND document_number=@doc;
 IF @invoice IS NULL BEGIN INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,@doc,@year,@date); SET @invoice=SCOPE_IDENTITY(); END
 ELSE IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice AND transaction_date<>@date)
 BEGIN
   SET @current=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(N'invoice-date|',(SELECT transaction_date FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice))),2));
   INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'CONFLICT',@incoming,N'The invoice identity already exists with a different date.');
   INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,'R025',@identity,@current,@incoming,N'Invoice date differs. Review and request a controlled restatement.'); RETURN;
 END
 SELECT @existing=sales_line_id,
   @current=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(@date,N'|',product_code,N'|',ISNULL(source_transaction_type,N''),N'|',source_quantity,N'|',ISNULL(source_gross_amount,0),N'|',ISNULL(source_net_amount,0),N'|',ISNULL(source_brand_code,N''),N'|',ISNULL(source_brand_name,N''),N'|',ISNULL(brand_segment,N''),N'|',currency_code)),2))
 FROM dbo.sales_lines WHERE sales_invoice_id=@invoice AND line_identifier=@line;
 IF @existing IS NOT NULL
 BEGIN
   IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'ALREADY_PRESENT',@incoming,N'Identical sales transaction already exists.');
   ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'CONFLICT',@incoming,N'Sales transaction identity exists with different content.'); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,'R025',@identity,@current,@incoming,N'One or more protected transaction values differ. Review and request a controlled restatement.'); END
   RETURN;
 END
 INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_brand_code,source_brand_name,brand_segment,currency_code,source_lineage_id,source_tax_amount) VALUES(@invoice,@line,@product,@type,@qty,@gross,@net,@brandcode,@brandname,@segment,@currency,@lineage,@tax);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'NEW',@incoming,N'New sales transaction imported.');
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.persist_sales_line TO etp_store_manager;
