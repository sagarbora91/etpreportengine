CREATE OR ALTER PROCEDURE dbo.append_etp_landing_s029
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 int,
 @v2 int,
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 decimal(19,4),
 @v7 decimal(19,4),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 nvarchar(max),
 @v15 nvarchar(max),
 @v16 decimal(19,4),
 @v17 nvarchar(max),
 @v18 nvarchar(max),
 @v19 nvarchar(max),
 @v20 decimal(19,4),
 @v21 decimal(19,4),
 @v22 nvarchar(max),
 @v23 nvarchar(max),
 @v24 decimal(19,4),
 @v25 decimal(19,4),
 @v26 decimal(19,4),
 @v27 nvarchar(max),
 @v28 nvarchar(max),
 @v29 nvarchar(max),
 @v30 nvarchar(max),
 @v31 nvarchar(max),
 @v32 date,
 @v33 date,
 @v34 nvarchar(max),
 @v35 nvarchar(max),
 @v36 nvarchar(max),
 @v37 date,
 @v38 date,
 @v39 nvarchar(max),
 @v40 nvarchar(max),
 @v41 nvarchar(max),
 @v42 nvarchar(max),
 @v43 nvarchar(max),
 @v44 nvarchar(max),
 @v45 nvarchar(max),
 @v46 nvarchar(max),
 @v47 nvarchar(max),
 @v48 nvarchar(max),
 @v49 nvarchar(max),
 @v50 nvarchar(max),
 @v51 nvarchar(max),
 @v52 nvarchar(max),
 @v53 nvarchar(max),
 @v54 nvarchar(max),
 @v55 nvarchar(max),
 @v56 nvarchar(max),
 @v57 nvarchar(max),
 @v58 nvarchar(max),
 @v59 nvarchar(max),
 @v60 decimal(19,4),
 @v61 nvarchar(max),
 @v62 nvarchar(max),
 @v63 nvarchar(max),
 @v64 nvarchar(max),
 @v65 nvarchar(max),
 @v66 nvarchar(max),
 @v67 nvarchar(max),
 @v68 nvarchar(max),
 @v69 nvarchar(max),
 @v70 decimal(19,4),
 @v71 nvarchar(max)
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
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code='S029')
  THROW 51422,'The source row does not belong to this report import.',1;
 INSERT dbo.[etp_landing_s029](import_file_id,source_lineage_id,content_key,[store_code],[month],[year],[stockwatch],[brand_name],[brand_code],[ucp],[deduction_charges],[variant],[watch_category],[cluster],[caseno],[module],[wr],[status],[moa],[age],[stock_no],[description],[var_type],[cost_price],[sparecost],[suppliercode],[defect_code],[labour_charge_actual],[labour_charge_probable],[labour_charge_applicable],[defdesc],[group],[groupdes],[srfno],[repeat_return_jobordernumber],[srf_date],[purchasedate],[srn_number],[customer_name],[repeatrtn],[delivered_date],[repair_date],[parts_gd_yes_no],[ra_dc_number],[serial_number],[uid_no],[pancell_advice_number],[empowerment_reason],[encircle_number],[region],[complaint_details],[complaint_description],[customer_id],[cust_channel],[landline_no],[mobile_no],[email],[sc_name],[store_channel],[is_guarantee_applicable],[dealer_booking_reference],[dealer_booking_date],[invoice_no],[invoice_value],[invoice_empowerment_reason],[sap_code],[mechanic_name],[lms_id],[helios_product_description],[prp_number],[repair_location],[jo_booked_by],[jo_delivered_by],[actual_ucp],[uniqnumber])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33,@v34,@v35,@v36,@v37,@v38,@v39,@v40,@v41,@v42,@v43,@v44,@v45,@v46,@v47,@v48,@v49,@v50,@v51,@v52,@v53,@v54,@v55,@v56,@v57,@v58,@v59,@v60,@v61,@v62,@v63,@v64,@v65,@v66,@v67,@v68,@v69,@v70,@v71);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END
-- >>> GRANTS
GRANT EXECUTE ON dbo.append_etp_landing_s029 TO etp_owner,etp_store_manager;
