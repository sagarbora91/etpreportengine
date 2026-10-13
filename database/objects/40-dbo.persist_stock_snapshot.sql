CREATE OR ALTER PROCEDURE dbo.persist_stock_snapshot
 @store varchar(30),@date date,@product nvarchar(80),@ean nvarchar(80)=NULL,@brand nvarchar(80)=NULL,@brandname nvarchar(200)=NULL,@cluster nvarchar(100)=NULL,@gender nvarchar(50)=NULL,@batch nvarchar(80)=NULL,@uid nvarchar(100)=NULL,@qty decimal(19,4),@unit decimal(19,4)=NULL,@total decimal(19,4)=NULL,@lineage bigint,@source varchar(30)=NULL,@line_seq int=1
AS
BEGIN
 SET NOCOUNT ON; DECLARE @existing bigint,@file bigint,@record varchar(40),@identity nvarchar(400),@incoming char(64),@current char(64);
 SELECT @file=import_file_id,@record=source_record_type FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
 DECLARE @derived varchar(30)=CASE WHEN @record='R010_SNAPSHOT' THEN 'R010' ELSE 'CLOSING_STOCK' END;
 SET @source=NULLIF(UPPER(LTRIM(RTRIM(@source))),'');
 IF @source IS NOT NULL AND @source<>@derived THROW 51760,'The stock snapshot source does not match the record type of its lineage.',1;
 SET @source=@derived;
 SET @identity=CONCAT(@store,N'/',@date,N'/',@source,N'/',@product,N'/',COALESCE(@uid,@batch,@ean,N''),N'/#',@line_seq);
 SET @incoming=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(ISNULL(@ean,N''),N'|',ISNULL(@brand,N''),N'|',ISNULL(@brandname,N''),N'|',ISNULL(@cluster,N''),N'|',ISNULL(@gender,N''),N'|',ISNULL(@batch,N''),N'|',ISNULL(@uid,N''),N'|',@qty,N'|',ISNULL(@unit,0),N'|',ISNULL(@total,0))),2));
 SELECT TOP(1) @existing=stock_snapshot_id,@current=LOWER(CONVERT(varchar(64),HASHBYTES('SHA2_256',CONCAT(ISNULL(ean,N''),N'|',ISNULL(brand_code,N''),N'|',ISNULL(brand_name,N''),N'|',ISNULL(cluster,N''),N'|',ISNULL(gender,N''),N'|',ISNULL(batch_number,N''),N'|',ISNULL(source_uid,N''),N'|',quantity,N'|',ISNULL(unit_cost,0),N'|',ISNULL(total_cost,0))),2)) FROM dbo.stock_snapshots WHERE store_code=@store AND snapshot_date=@date AND source_report_code=@source AND product_code=@product AND item_discriminator=COALESCE(@uid,@batch,@ean,N'') AND line_seq=@line_seq ORDER BY stock_snapshot_id;
 IF @existing IS NOT NULL BEGIN IF @current=@incoming INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'ALREADY_PRESENT',@incoming,N'Identical stock snapshot row already exists.'); ELSE BEGIN INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'CONFLICT',@incoming,N'Stock snapshot identity exists with different content.'); INSERT dbo.import_conflicts(import_file_id,source_lineage_id,store_code,business_date,report_code,business_identity,existing_content_sha256,incoming_content_sha256,safe_difference) VALUES(@file,@lineage,@store,@date,@source,@identity,@current,@incoming,N'Closing-stock values differ. Review and request a controlled restatement.'); END RETURN; END
 INSERT dbo.stock_snapshots(store_code,snapshot_date,product_code,ean,brand_code,brand_name,cluster,gender,batch_number,source_uid,quantity,unit_cost,total_cost,source_lineage_id,source_report_code,line_seq) VALUES(@store,@date,@product,@ean,@brand,@brandname,@cluster,@gender,@batch,@uid,@qty,@unit,@total,@lineage,@source,@line_seq);
 INSERT dbo.import_row_outcomes(import_file_id,source_lineage_id,business_identity,outcome,content_sha256,safe_message) VALUES(@file,@lineage,@identity,'NEW',@incoming,N'New stock snapshot row imported.');
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.persist_stock_snapshot TO etp_store_manager;
