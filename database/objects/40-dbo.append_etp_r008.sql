CREATE OR ALTER PROCEDURE dbo.append_etp_r008
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 date,
 @v4 nvarchar(max),
 @v5 int,
 @v6 nvarchar(max),
 @v7 date,
 @v8 decimal(19,4),
 @v9 date,
 @v10 decimal(19,4),
 @v11 decimal(19,4),
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 date,
 @v15 date
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
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code='R008')
  THROW 51422,'The source row does not belong to this report import.',1;
 INSERT dbo.[etp_r008](import_file_id,source_lineage_id,content_key,[store_code],[storename],[bankdepositnumber],[transactiondate],[trans_type],[invoice_year],[invoice_number],[invoice_date],[amount],[bankedon],[bankedamount],[unbanked_amount],[deposit_slipno],[cc_chequeno],[deposit_date],[createdate])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.append_etp_r008 TO etp_owner,etp_store_manager;
