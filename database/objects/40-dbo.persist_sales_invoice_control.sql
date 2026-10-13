CREATE OR ALTER PROCEDURE dbo.persist_sales_invoice_control
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@type nvarchar(80)=NULL,@qty decimal(19,4),@net decimal(19,4),@currency char(3),@lineage bigint
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @invoice bigint,@existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage; SET @identity=CONCAT(@store,N'/',@year,N'/',@doc,N'/CONTROL');
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(@date,N'|',ISNULL(@type,N''),N'|',@qty,N'|',@net,N'|',@currency)),2));
 SELECT @invoice=sales_invoice_id FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND invoice_year=@year AND document_number=@doc;
 IF @invoice IS NULL BEGIN INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,@doc,@year,@date); SET @invoice=SCOPE_IDENTITY(); END
 ELSE IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice AND transaction_date<>@date)
 BEGIN SET @current=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(N'invoice-date|',(SELECT transaction_date FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice))),2)); INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'CONFLICT',@incoming,N'The invoice identity already exists with a different date.'); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,'R022',@identity,@current,@incoming,N'Invoice date differs. Review and request a controlled restatement.'); RETURN; END
 SELECT TOP(1) @existing=sales_invoice_control_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(@date,N'|',ISNULL(source_transaction_type,N''),N'|',source_invoice_quantity,N'|',source_net_value,N'|',currency_code)),2)) FROM dbo.sales_invoice_controls WHERE sales_invoice_id=@invoice ORDER BY sales_invoice_control_id;
 IF @existing IS NOT NULL BEGIN IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'ALREADY_PRESENT',@incoming,N'Identical invoice control already exists.'); ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'CONFLICT',@incoming,N'Invoice control identity exists with different content.'); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,'R022',@identity,@current,@incoming,N'Revenue control values differ. Review and request a controlled restatement.'); END RETURN; END
 INSERT dbo.sales_invoice_controls(sales_invoice_id,source_transaction_type,source_invoice_quantity,source_net_value,currency_code,source_lineage_id) VALUES(@invoice,@type,@qty,@net,@currency,@lineage);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'NEW',@incoming,N'New invoice control imported.');
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.persist_sales_invoice_control TO etp_store_manager;
