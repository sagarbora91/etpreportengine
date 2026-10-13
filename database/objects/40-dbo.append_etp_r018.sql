CREATE OR ALTER PROCEDURE dbo.append_etp_r018
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 date,
 @v6 int,
 @v7 decimal(19,4),
 @v8 date,
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 nvarchar(max),
 @v15 nvarchar(max),
 @v16 nvarchar(max),
 @v17 decimal(19,4),
 @v18 nvarchar(max),
 @v19 decimal(19,4),
 @v20 decimal(19,4),
 @v21 decimal(19,4),
 @v22 decimal(19,4),
 @v23 decimal(19,4),
 @v24 decimal(19,4),
 @v25 decimal(19,4),
 @v26 decimal(19,4),
 @v27 decimal(19,4),
 @v28 decimal(19,4),
 @v29 decimal(19,4),
 @v30 decimal(19,4),
 @v31 decimal(19,4),
 @v32 nvarchar(max),
 @v33 nvarchar(max)
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
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code='R018')
  THROW 51422,'The source row does not belong to this report import.',1;
 INSERT dbo.[etp_r018](import_file_id,source_lineage_id,content_key,[document_number],[store_code],[store_name],[transaction_type],[doc_invoice_no],[doc_invoice_date],[invoice_year],[reference_doc],[reference_date],[issue_state_name],[issue_gstn_no],[issue_state_code],[recipient_state_name],[recipient_gstn_no],[recipient_state_code],[item_number],[hsn_code],[qty],[uom],[ucp],[gross_ucp],[discounts],[net_amount],[taxable_value],[cgst_rate],[cgst_amount],[sgst_utgst_rate],[sgst_utgst_amount],[igst_rate],[igst_amount],[cess_rate],[cess_amount],[customer_name],[customernumber])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.append_etp_r018 TO etp_owner,etp_store_manager;
