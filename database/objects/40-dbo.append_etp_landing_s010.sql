CREATE OR ALTER PROCEDURE dbo.append_etp_landing_s010
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 date,
 @v8 date,
 @v9 date,
 @v10 nvarchar(max),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 decimal(19,4),
 @v14 nvarchar(max),
 @v15 nvarchar(max),
 @v16 nvarchar(max),
 @v17 nvarchar(max),
 @v18 nvarchar(max),
 @v19 nvarchar(max),
 @v20 decimal(19,4),
 @v21 nvarchar(max),
 @v22 nvarchar(max),
 @v23 nvarchar(max),
 @v24 nvarchar(max),
 @v25 nvarchar(max),
 @v26 nvarchar(max),
 @v27 nvarchar(max),
 @v28 nvarchar(max),
 @v29 nvarchar(max),
 @v30 nvarchar(max),
 @v31 decimal(19,4),
 @v32 nvarchar(max),
 @v33 date,
 @v34 nvarchar(max),
 @v35 nvarchar(max)
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
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code='S010')
  THROW 51422,'The source row does not belong to this report import.',1;
 INSERT dbo.[etp_landing_s010](import_file_id,source_lineage_id,content_key,[jonumber],[srnstorecode],[store_code],[d2drefno],[d2ddealername],[storetype],[servicecentername],[dop],[jodate],[repairdate],[variantnumber],[brand],[cluster],[ucp],[uidno],[complaint],[sparerequired],[sparecode],[customername],[mobilenumber],[estimatedcharge],[jostatus],[isguarnatee],[isstockwatch],[customerchannel],[storechannel],[isdealer],[storestatus],[location],[region],[territory],[pendingnoofdays],[indentid],[indentdate],[reasonforpending],[pendingstore])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33,@v34,@v35);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.append_etp_landing_s010 TO etp_owner,etp_store_manager;
