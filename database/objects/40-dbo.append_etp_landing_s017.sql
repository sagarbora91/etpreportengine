CREATE OR ALTER PROCEDURE dbo.append_etp_landing_s017
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 int,
 @v2 int,
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 date,
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
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
 @v21 decimal(19,4),
 @v22 nvarchar(max),
 @v23 nvarchar(max),
 @v24 date,
 @v25 nvarchar(max),
 @v26 nvarchar(max),
 @v27 nvarchar(max),
 @v28 date,
 @v29 nvarchar(max),
 @v30 nvarchar(max),
 @v31 nvarchar(max),
 @v32 nvarchar(max),
 @v33 decimal(19,4),
 @v34 nvarchar(max),
 @v35 nvarchar(max),
 @v36 date,
 @v37 nvarchar(max),
 @v38 nvarchar(max),
 @v39 nvarchar(max),
 @v40 nvarchar(max),
 @v41 nvarchar(max),
 @v42 nvarchar(max),
 @v43 nvarchar(max),
 @v44 nvarchar(max),
 @v45 nvarchar(max),
 @v46 nvarchar(max),
 @v47 nvarchar(max),
 @v48 date,
 @v49 nvarchar(max),
 @v50 nvarchar(max),
 @v51 nvarchar(max),
 @v52 nvarchar(max),
 @v53 nvarchar(max),
 @v54 nvarchar(max),
 @v55 date,
 @v56 date,
 @v57 date,
 @v58 date,
 @v59 nvarchar(max),
 @v60 nvarchar(max),
 @v61 date,
 @v62 nvarchar(max),
 @v63 nvarchar(max),
 @v64 decimal(19,4),
 @v65 decimal(19,4),
 @v66 nvarchar(max),
 @v67 nvarchar(max),
 @v68 date,
 @v69 nvarchar(max),
 @v70 date,
 @v71 decimal(19,4),
 @v72 nvarchar(max),
 @v73 decimal(19,4),
 @v74 nvarchar(max),
 @v75 date,
 @v76 nvarchar(max),
 @v77 nvarchar(max),
 @v78 nvarchar(max),
 @v79 nvarchar(max),
 @v80 nvarchar(max),
 @v81 nvarchar(max),
 @v82 nvarchar(max)
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
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code='S017')
  THROW 51422,'The source row does not belong to this report import.',1;
 INSERT dbo.[etp_landing_s017](import_file_id,source_lineage_id,content_key,[region],[month],[year],[scname],[store_code],[location],[jobordernumber],[jodate],[customername],[customerid],[customertype],[mobilenumber],[email],[sapcode],[endcustomername],[endcustomercontactnumber],[dealercafnumber],[dealercafdate],[d2duniquenumber],[channeltype],[variantnumber],[ucp],[clusterid],[brandname],[dop],[age],[uidnumber],[encirclenumber],[edd],[stockwatch],[guarantee],[complaintdetails],[complaintdescription],[advanceamount],[watchcondition],[indentnumber],[indentdate],[moa],[sparepartsused],[casenumber],[module],[waterresistance],[vartype],[defectcode],[defectdescription],[defectgroup],[defectgroupdescription],[suppliercode],[jorepairdate],[mechanicresponsible],[isrtpassed],[faileddueto],[repeatreturnreason],[numberoftimes_rr],[srnnumber],[srnoutdate],[srnissueddate],[srnreturndate],[srnreceiveddate],[tostorecode],[repairstatus],[deliverydate],[cashmemonumber],[customerchannel],[sparevalue],[labourcharge],[pancelladvicenumber],[radcnumber],[wradate],[wdcnumber],[wdcdate],[radcdeductioncharge],[empowermentapplied],[empowermentvalue],[normalrwrnumber],[normalrwrdate],[rwr_reason],[remarks],[empowermentreason],[radcempowermentapplied],[radcempowermentreason],[prp_number],[lms_code])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33,@v34,@v35,@v36,@v37,@v38,@v39,@v40,@v41,@v42,@v43,@v44,@v45,@v46,@v47,@v48,@v49,@v50,@v51,@v52,@v53,@v54,@v55,@v56,@v57,@v58,@v59,@v60,@v61,@v62,@v63,@v64,@v65,@v66,@v67,@v68,@v69,@v70,@v71,@v72,@v73,@v74,@v75,@v76,@v77,@v78,@v79,@v80,@v81,@v82);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.append_etp_landing_s017 TO etp_owner,etp_store_manager;
