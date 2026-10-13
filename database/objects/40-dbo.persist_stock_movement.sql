CREATE OR ALTER PROCEDURE dbo.persist_stock_movement
 @store varchar(30),@doc nvarchar(80),@year int,@date date,@product nvarchar(80),@type nvarchar(80),@from nvarchar(80)=NULL,@to nvarchar(80)=NULL,@opening decimal(19,4),@transaction decimal(19,4),@closing decimal(19,4),@lineage bigint,@line_seq int=1,@location nvarchar(80)=NULL
AS
BEGIN
 SET NOCOUNT ON; DECLARE @existing bigint,@file bigint,@identity nvarchar(400),@incoming char(64),@current char(64);
 SET @location=NULLIF(LTRIM(RTRIM(@location)),N'');
 SELECT @file=import_file_id FROM dbo.source_lineage WHERE source_lineage_id=@lineage; SET @identity=CONCAT(@store,N'/',@year,N'/',@doc,N'/',@date,N'/',@product,N'/',UPPER(@type),N'/',ISNULL(@from,N''),N'/',ISNULL(@to,N''),N'/#',@line_seq);
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(@opening,N'|',@transaction,N'|',@closing)),2));
 SELECT TOP(1) @existing=stock_movement_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(opening_quantity,N'|',transaction_quantity,N'|',closing_quantity)),2)) FROM dbo.stock_movements WHERE store_code=@store AND invoice_year=@year AND document_number=@doc AND document_date=@date AND product_code=@product AND source_transaction_type=@type AND from_key=ISNULL(@from,N'') AND to_key=ISNULL(@to,N'') AND line_seq=@line_seq ORDER BY stock_movement_id;
 IF @existing IS NOT NULL
 BEGIN
  IF @current=@incoming
  BEGIN
   IF @location IS NOT NULL
    UPDATE dbo.stock_movements SET location=@location WHERE stock_movement_id=@existing AND location IS NULL
     AND NOT EXISTS(SELECT 1 FROM dbo.daily_reporting_days d WHERE d.store_code=@store AND d.business_date=@date AND d.status='LOCKED');
   INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'ALREADY_PRESENT',@incoming,N'Identical stock movement already exists.');
  END
  ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'CONFLICT',@incoming,N'Stock movement identity exists with different content.'); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,'STOCK_LEDGER',@identity,@current,@incoming,N'Stock movement values differ. Review and request a controlled restatement.'); END
  RETURN;
 END
 INSERT dbo.stock_movements(store_code,document_number,invoice_year,document_date,product_code,source_transaction_type,from_location,to_location,opening_quantity,transaction_quantity,closing_quantity,source_lineage_id,line_seq,location) VALUES(@store,@doc,@year,@date,@product,@type,@from,@to,@opening,@transaction,@closing,@lineage,@line_seq,@location);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'NEW',@incoming,N'New stock movement imported.');
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.persist_stock_movement TO etp_store_manager;
