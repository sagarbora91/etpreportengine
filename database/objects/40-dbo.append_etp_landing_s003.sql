CREATE OR ALTER PROCEDURE dbo.append_etp_landing_s003
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 int,
 @v4 nvarchar(max),
 @v5 decimal(19,4),
 @v6 decimal(19,4),
 @v7 decimal(19,4),
 @v8 decimal(19,4),
 @v9 decimal(19,4),
 @v10 decimal(19,4),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 nvarchar(max),
 @v15 nvarchar(max),
 @v16 nvarchar(max),
 @v17 nvarchar(max),
 @v18 nvarchar(max),
 @v19 nvarchar(max),
 @v20 nvarchar(max),
 @v21 nvarchar(max),
 @v22 nvarchar(max),
 @v23 nvarchar(max),
 @v24 nvarchar(max),
 @v25 nvarchar(max),
 @v26 date,
 @v27 nvarchar(max),
 @v28 nvarchar(max),
 @v29 nvarchar(max),
 @v30 decimal(19,4),
 @v31 nvarchar(max),
 @v32 decimal(19,4),
 @v33 decimal(19,4),
 @v34 decimal(19,4),
 @v35 decimal(19,4),
 @v36 decimal(19,4),
 @v37 decimal(19,4),
 @v38 decimal(19,4),
 @v39 decimal(19,4),
 @v40 decimal(19,4),
 @v41 decimal(19,4),
 @v42 decimal(19,4),
 @v43 nvarchar(max),
 @v44 nvarchar(max),
 @v45 nvarchar(max),
 @v46 decimal(19,4),
 @v47 decimal(19,4),
 @v48 decimal(19,4),
 @v49 nvarchar(max),
 @v50 nvarchar(max),
 @v51 nvarchar(max),
 @v52 nvarchar(max),
 @v53 nvarchar(max),
 @v54 nvarchar(max)
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
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code='S003')
  THROW 51422,'The source row does not belong to this report import.',1;
 INSERT dbo.[etp_landing_s003](import_file_id,source_lineage_id,content_key,[transaction_id],[job_status],[item_id],[line_num],[terminal_id],[quantity],[ucp_amount],[tax_amount],[net_amount],[price],[disc_amount],[joborder_number],[sc_type],[store_code],[sc_name],[state],[territory],[region],[accountnum],[identification_number],[name],[cust_name],[product_description],[orignal_doc_number],[document_type],[documentnum],[trans_date],[gstin_no],[to_gstin],[to_location],[gross_amount],[hsn_code],[taxable],[cgst_rate],[cgst_value],[sgst_rate],[sgst_value],[sales_paymnet_diffrence],[total_tax],[igst_rate],[igst_value],[ccess_rate],[ccess_value],[store_channel],[cluster_id],[brand_id],[netamount_incl_tax],[labour_charge],[spare_charge],[invoice_type],[irn_number],[einvoicecancelledreason],[einvoicecancellationstatus],[brand_name],[vartype])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33,@v34,@v35,@v36,@v37,@v38,@v39,@v40,@v41,@v42,@v43,@v44,@v45,@v46,@v47,@v48,@v49,@v50,@v51,@v52,@v53,@v54);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.append_etp_landing_s003 TO etp_owner,etp_store_manager;
