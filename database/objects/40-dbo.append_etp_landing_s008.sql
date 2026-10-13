CREATE OR ALTER PROCEDURE dbo.append_etp_landing_s008
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 date,
 @v2 nvarchar(max),
 @v3 date,
 @v4 nvarchar(max),
 @v5 decimal(19,4),
 @v6 decimal(19,4),
 @v7 decimal(19,4),
 @v8 decimal(19,4),
 @v9 decimal(19,4),
 @v10 nvarchar(max),
 @v11 decimal(19,4),
 @v12 decimal(19,4),
 @v13 decimal(19,4),
 @v14 decimal(19,4),
 @v15 nvarchar(max),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
 @v19 decimal(19,4),
 @v20 nvarchar(max),
 @v21 nvarchar(max),
 @v22 date,
 @v23 nvarchar(max)
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
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code='S008')
  THROW 51422,'The source row does not belong to this report import.',1;
 INSERT dbo.[etp_landing_s008](import_file_id,source_lineage_id,content_key,[from_location],[received_date],[invoice_number],[invoice_date],[item_id],[shipped_quantity],[received_quantity],[ucp],[discount],[net_amount],[hsn_code],[sgst_percentage],[sgst_value],[cgst_percentage],[cgst_value],[ugst_percentage],[ugst_value],[igst_percentage],[igst_value],[taxable_value],[status],[grn_no],[grn_date],[store_code])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.append_etp_landing_s008 TO etp_owner,etp_store_manager;
