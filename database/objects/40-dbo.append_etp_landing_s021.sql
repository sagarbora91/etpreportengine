CREATE OR ALTER PROCEDURE dbo.append_etp_landing_s021
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 int,
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 date,
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 date,
 @v10 date,
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 date,
 @v15 decimal(19,4),
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
 @v26 nvarchar(max),
 @v27 nvarchar(max),
 @v28 nvarchar(max),
 @v29 nvarchar(max),
 @v30 nvarchar(max),
 @v31 nvarchar(max),
 @v32 nvarchar(max),
 @v33 decimal(19,4),
 @v34 nvarchar(max),
 @v35 decimal(19,4),
 @v36 decimal(19,4),
 @v37 decimal(19,4),
 @v38 nvarchar(max),
 @v39 nvarchar(max),
 @v40 nvarchar(max),
 @v41 nvarchar(max),
 @v42 nvarchar(max),
 @v43 nvarchar(max),
 @v44 date,
 @v45 nvarchar(max),
 @v46 nvarchar(max),
 @v47 nvarchar(max),
 @v48 nvarchar(max),
 @v49 nvarchar(max),
 @v50 nvarchar(max),
 @v51 nvarchar(max)
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
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code='S021')
  THROW 51422,'The source row does not belong to this report import.',1;
 INSERT dbo.[etp_landing_s021](import_file_id,source_lineage_id,content_key,[linenumber],[status],[status_1],[hoadvicenumber],[documentnumber],[moa],[dop],[deliverychallannumber],[indentid],[indentdate],[dcdate],[cancellationdate],[cancellationreason],[wdcstatus],[bookingdate],[deductioncharges],[customerid],[customername],[mobilenumber],[raisedby],[store_code],[storetype],[storename],[storestatus],[city],[location],[region],[territory],[jobordernumber],[watchvariant],[clusterid],[brandname],[brandcode],[spareucp],[sparename],[depreciationpercent],[watchucp],[wdcvalue],[empowermentapplied],[reason],[franchiseename],[producttype],[docketno],[tolocation],[receiveddate],[couriername],[d2drefno],[dealersapcode],[dealerchannel],[storesapcode],[storechannel],[wdctype])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33,@v34,@v35,@v36,@v37,@v38,@v39,@v40,@v41,@v42,@v43,@v44,@v45,@v46,@v47,@v48,@v49,@v50,@v51);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.append_etp_landing_s021 TO etp_owner,etp_store_manager;
