SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.sales_lines ADD
    source_brand_code nvarchar(80) NULL,
    source_brand_name nvarchar(200) NULL,
    brand_segment nvarchar(100) NULL;

EXEC(N'ALTER PROCEDURE dbo.persist_sales_line
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@line nvarchar(80),@product nvarchar(80),@type nvarchar(80)=NULL,
 @qty decimal(19,4),@gross decimal(19,4)=NULL,@net decimal(19,4)=NULL,@brandcode nvarchar(80)=NULL,@brandname nvarchar(200)=NULL,
 @segment nvarchar(100)=NULL,@currency char(3),@lineage bigint
AS
BEGIN
 SET NOCOUNT ON;
 DECLARE @invoice bigint;
 SELECT @invoice=sales_invoice_id FROM dbo.sales_invoices WITH(UPDLOCK,HOLDLOCK) WHERE store_code=@store AND invoice_year=@year AND document_number=@doc;
 IF @invoice IS NULL BEGIN INSERT dbo.sales_invoices(store_code,document_number,invoice_year,transaction_date) VALUES(@store,@doc,@year,@date); SET @invoice=SCOPE_IDENTITY(); END
 ELSE IF EXISTS(SELECT 1 FROM dbo.sales_invoices WHERE sales_invoice_id=@invoice AND transaction_date<>@date) THROW 51001,''Invoice date conflicts with the existing natural key.'',1;
 INSERT dbo.sales_lines(sales_invoice_id,line_identifier,product_code,source_transaction_type,source_quantity,source_gross_amount,source_net_amount,source_brand_code,source_brand_name,brand_segment,currency_code,source_lineage_id)
 VALUES(@invoice,@line,@product,@type,@qty,@gross,@net,@brandcode,@brandname,@segment,@currency,@lineage);
END');

CREATE INDEX IX_sales_lines_brand_segment ON dbo.sales_lines(source_brand_code,brand_segment) INCLUDE(source_quantity,source_net_amount);

COMMIT TRANSACTION;
