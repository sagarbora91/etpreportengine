CREATE OR ALTER PROCEDURE dbo.persist_sales_enrichment
 @report varchar(30),@store varchar(30),@date date,@document nvarchar(100),@product nvarchar(100),@type varchar(30),
 @quantity decimal(19,4),@net decimal(19,4),@cro nvarchar(100),@scheme decimal(19,4),@userDiscount decimal(19,4),
 @pre decimal(19,4),@other decimal(19,4),@activation nvarchar(1000),@discountDetails nvarchar(1000),@lineage bigint
AS BEGIN SET NOCOUNT ON;
DECLARE @matchCount int,@salesLineId bigint;
SELECT @matchCount=COUNT(*),@salesLineId=CASE WHEN COUNT(*)=1 THEN MAX(l.sales_line_id) END
FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
WHERE i.store_code=@store AND i.transaction_date=@date AND i.document_number=@document AND l.product_code=@product;
DECLARE @status varchar(20)=CASE @matchCount WHEN 0 THEN 'Missing' WHEN 1 THEN 'Matched' ELSE 'Ambiguous' END;
INSERT dbo.sales_line_enrichments
  (enrichment_type,store_code,transaction_date,document_number,product_code,source_transaction_type,source_quantity,source_net_value,
   source_cro_number,scheme_discount,user_discount,pre_discount,other_charges,activation_details,user_discount_details,
   matched_sales_line_id,match_status,source_lineage_id)
VALUES(@report,@store,@date,@document,@product,@type,@quantity,@net,@cro,@scheme,@userDiscount,@pre,@other,@activation,@discountDetails,
       @salesLineId,@status,@lineage);
SELECT @status;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.persist_sales_enrichment TO etp_store_manager;
