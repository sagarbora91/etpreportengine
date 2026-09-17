-- Integrate Phase One imports with the Phase Four least-privilege boundary.
-- Procedure bodies use static ownership chaining; staff retain no protected-table DML.
SET XACT_ABORT ON;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r001
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 decimal(19,4),
 @v12 date,
 @v13 int,
 @v14 decimal(19,4),
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
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
 @v42 nvarchar(max),
 @v43 nvarchar(max),
 @v44 nvarchar(max),
 @v45 int
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R001'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r001](import_file_id,source_lineage_id,content_key,[trans_type],[store_code],[store_name],[store_type],[channel],[region],[state],[city],[invnumber],[customer_name],[customer_phone],[invoicequantity],[invoicedate],[invoice_year],[cash],[card],[cheque],[loyalty_points],[gv],[creditnote_redeem],[excess_gv],[round_off],[no_refund],[others],[tata_gv],[giftcard],[tatacliq],[gyftr],[paytm],[heliosomni],[advancerdeem],[bhimupi],[phonepe],[bharatpe],[bajajfin],[razorpay],[paymenttype24],[paymenttype25],[issued_creditnote],[cash_refund],[cheque_rtgs_refund],[netvalue],[encircle],[storetimestamp],[referencenumber],[referenceyear])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33,@v34,@v35,@v36,@v37,@v38,@v39,@v40,@v41,@v42,@v43,@v44,@v45);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r001] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r001 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r002
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 date,
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 nvarchar(max),
 @v15 nvarchar(max),
 @v16 nvarchar(max),
 @v17 nvarchar(max),
 @v18 decimal(19,4),
 @v19 decimal(19,4),
 @v20 decimal(19,4),
 @v21 decimal(19,4),
 @v22 decimal(19,4),
 @v23 decimal(19,4),
 @v24 decimal(19,4),
 @v25 decimal(19,4),
 @v26 decimal(19,4),
 @v27 nvarchar(max),
 @v28 nvarchar(max),
 @v29 decimal(19,4),
 @v30 decimal(19,4),
 @v31 decimal(19,4),
 @v32 nvarchar(max),
 @v33 decimal(19,4),
 @v34 nvarchar(max),
 @v35 date,
 @v36 nvarchar(max),
 @v37 nvarchar(max),
 @v38 nvarchar(max),
 @v39 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R002'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r002](import_file_id,source_lineage_id,content_key,[trans_type],[storetype],[region],[store_code],[storename],[city],[state],[customernumber],[customer_name],[customer_phone],[invnumber],[invdate],[signet_no],[ulpnumber],[brand],[cluster],[gender],[itemnumber],[qty],[ucp],[grossucp],[sch_discounts],[netgross],[pre_discounts],[othrchrgs],[netamount],[tax],[storetimestamp],[hsncode],[sgst_utgst_value],[csgt_value],[igst_value],[remarks],[netvalue],[invrefno],[invrefdate],[brandname],[channel],[scheme_reference_number],[online])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33,@v34,@v35,@v36,@v37,@v38,@v39);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r002] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r002 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r003
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 date,
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 decimal(19,4),
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
 @v19 nvarchar(max),
 @v20 decimal(19,4),
 @v21 decimal(19,4),
 @v22 decimal(19,4),
 @v23 nvarchar(max),
 @v24 decimal(19,4),
 @v25 decimal(19,4),
 @v26 nvarchar(max),
 @v27 date,
 @v28 nvarchar(max),
 @v29 nvarchar(max),
 @v30 nvarchar(max),
 @v31 nvarchar(max),
 @v32 nvarchar(max),
 @v33 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R003'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r003](import_file_id,source_lineage_id,content_key,[source_transaction_type],[store_code],[store_name],[store_type],[channel],[region],[city],[invoice_number],[transaction_date],[product_code],[brand],[brand_name],[cluster],[gender],[source_quantity],[ucp],[grossucp],[scheme_discount],[netgross],[activation_details],[user_discount],[other_charges],[source_net_amount],[user_discount_details],[tax],[source_net_value],[invoice_ref_no],[invoice_ref_date],[customernumber],[customer_name],[customer_phone],[ulp_no],[eastimestamp],[storetimestamp])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r003] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r003 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r004
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 decimal(19,4),
 @v14 decimal(19,4),
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
 @v19 decimal(19,4),
 @v20 nvarchar(max),
 @v21 int,
 @v22 date,
 @v23 decimal(19,4),
 @v24 decimal(19,4),
 @v25 decimal(19,4),
 @v26 nvarchar(max),
 @v27 date,
 @v28 nvarchar(max),
 @v29 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R004'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r004](import_file_id,source_lineage_id,content_key,[trans_type],[store_code],[region],[state],[city],[tolocation],[to_region],[to_state],[to_city],[employeeid],[brand],[itemnumber],[hsncode],[taxableamount],[cgst],[csgt_value],[sgst_utgst],[sgst_utgst_value],[igst],[igst_value],[docno],[invoice_year],[documentdate],[qty],[ucp],[grossucp],[purinvno],[purinvdate],[remark],[order_type])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r004] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r004 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r005
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 int,
 @v12 date,
 @v13 decimal(19,4),
 @v14 decimal(19,4),
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
 @v19 decimal(19,4),
 @v20 nvarchar(max),
 @v21 nvarchar(max),
 @v22 nvarchar(max),
 @v23 nvarchar(max),
 @v24 date,
 @v25 nvarchar(max),
 @v26 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R005'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r005](import_file_id,source_lineage_id,content_key,[trans_type],[store_code],[region],[state],[city],[tolocation],[to_region],[to_state],[to_city],[employeeid],[docno],[invoice_year],[documentdate],[qty],[grossucp],[taxableamount],[stmvalue],[csgt_value],[sgst_utgst_value],[igst_value],[remarks],[order_type],[couriername],[couriernumber],[courierdate],[airwaybillno],[dispatchtype])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r005] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r005 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r006
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 int,
 @v12 date,
 @v13 decimal(19,4),
 @v14 decimal(19,4),
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
 @v19 decimal(19,4),
 @v20 nvarchar(max),
 @v21 date,
 @v22 nvarchar(max),
 @v23 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R006'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r006](import_file_id,source_lineage_id,content_key,[trans_type],[store_code],[region],[state],[city],[fromlocation],[from_region],[from_state],[from_city],[employeeid],[docnumber],[invoice_year],[documentdate],[qty],[grossucp],[discount],[taxableamount],[csgt_value],[sgst_utgst_value],[igst_value],[refdocnumber],[refdocdate],[remark],[order_type])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r006] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r006 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r007
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 int,
 @v12 date,
 @v13 nvarchar(max),
 @v14 nvarchar(max),
 @v15 nvarchar(max),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
 @v19 decimal(19,4),
 @v20 decimal(19,4),
 @v21 decimal(19,4),
 @v22 decimal(19,4),
 @v23 decimal(19,4),
 @v24 decimal(19,4),
 @v25 decimal(19,4),
 @v26 decimal(19,4),
 @v27 date,
 @v28 nvarchar(max),
 @v29 nvarchar(max),
 @v30 date,
 @v31 nvarchar(max),
 @v32 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R007'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r007](import_file_id,source_lineage_id,content_key,[trans_type],[store_code],[region],[state],[city],[fromlocation],[employeeid],[itemnumber],[hsncode],[brand],[docnumber],[invoice_year],[documentdate],[from_region],[from_state],[from_city],[qty],[ucp_value],[grossucp],[discount],[cgst],[csgt_value],[sgst_utgst],[sgst_utgst_value],[igst],[igst_value],[netvalue],[refdocdate],[refdocnumber],[purinvno],[purinvdate],[remark],[order_type])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r007] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r007 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r008
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
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R008'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r008](import_file_id,source_lineage_id,content_key,[store_code],[storename],[bankdepositnumber],[transactiondate],[trans_type],[invoice_year],[invoice_number],[invoice_date],[amount],[bankedon],[bankedamount],[unbanked_amount],[deposit_slipno],[cc_chequeno],[deposit_date],[createdate])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r008] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r008 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r009
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 int,
 @v3 date,
 @v4 decimal(19,4),
 @v5 decimal(19,4),
 @v6 decimal(19,4),
 @v7 decimal(19,4),
 @v8 decimal(19,4),
 @v9 decimal(19,4),
 @v10 decimal(19,4),
 @v11 decimal(19,4),
 @v12 decimal(19,4),
 @v13 decimal(19,4)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R009'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r009](import_file_id,source_lineage_id,content_key,[store_code],[store_name],[invoice_year],[transactiondate],[cash],[card],[cheque_dd],[total],[cash_deposited],[cc_deposited],[cheque_dd_deposited],[cash_difference],[card_difference],[cheque_dd_difference])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r009] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r009 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r010
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 nvarchar(max),
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
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
 @v43 decimal(19,4),
 @v44 decimal(19,4),
 @v45 decimal(19,4),
 @v46 decimal(19,4),
 @v47 decimal(19,4),
 @v48 decimal(19,4)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R010'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r010](import_file_id,source_lineage_id,content_key,[store_code],[store_name],[store_type],[channel],[region],[city],[itemnumber],[hsn_code],[brand],[brandname],[cluster],[gender],[ean_category],[lotnumber],[uid],[retailbin],[servicebin],[replacementbin],[defectivebin],[instibin],[ecomm],[other1],[other2],[other3],[other4],[other5],[other6],[other7],[other8],[other9],[other10],[other11],[other12],[other13],[other14],[other15],[other16],[other17],[other18],[other19],[other20],[other21],[other22],[other23],[other24],[other25],[ucp],[closingbalance],[totalucp])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33,@v34,@v35,@v36,@v37,@v38,@v39,@v40,@v41,@v42,@v43,@v44,@v45,@v46,@v47,@v48);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r010] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r010 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r011
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
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
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 nvarchar(max),
 @v19 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''CLOSING_STOCK'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r011](import_file_id,source_lineage_id,content_key,[store_code],[store_name],[store_type],[channel],[region],[state],[city],[snapshot_date],[product_code],[hsn_code],[itemdescription],[ean],[brand_code],[cluster],[gender],[quantity],[unit_cost],[total_cost],[batch_number],[source_uid])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r011] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r011 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r012
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 date,
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 date,
 @v12 nvarchar(max),
 @v13 decimal(19,4),
 @v14 nvarchar(max),
 @v15 date,
 @v16 decimal(19,4),
 @v17 date,
 @v18 nvarchar(max),
 @v19 nvarchar(max),
 @v20 decimal(19,4)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R012'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r012](import_file_id,source_lineage_id,content_key,[store_code],[store_name],[store_type],[channel],[region],[state],[city],[creditnotenumber],[creditnotedate],[ref_grnno],[cn_refdocno],[expirydate],[issueto],[creditnoteamount],[invoicenumber],[invoicedate],[invoicevalue],[redeemed_date],[redeemed_by],[issued_cnno],[issued_cnamt])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r012] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r012 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r013
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 nvarchar(max),
 @v15 nvarchar(max),
 @v16 nvarchar(max),
 @v17 date,
 @v18 decimal(19,4),
 @v19 decimal(19,4),
 @v20 decimal(19,4),
 @v21 decimal(19,4),
 @v22 decimal(19,4),
 @v23 decimal(19,4),
 @v24 decimal(19,4),
 @v25 decimal(19,4),
 @v26 nvarchar(max),
 @v27 date
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R013'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r013](import_file_id,source_lineage_id,content_key,[source_transaction_type],[store_code],[store_name],[store_type],[channel],[region],[city],[product_code],[brand],[brandname],[cluster],[gender],[cro_number],[cro_name],[customer_name],[customer_phone],[invoice_number],[transaction_date],[source_quantity],[ucp],[grossucp],[scheme_discount],[netgross],[pre_discount],[source_net_amount],[source_net_value],[invrefno],[invrefdate])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r013] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r013 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r014
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 int,
 @v3 date,
 @v4 decimal(19,4),
 @v5 decimal(19,4),
 @v6 decimal(19,4),
 @v7 decimal(19,4),
 @v8 decimal(19,4),
 @v9 decimal(19,4),
 @v10 decimal(19,4),
 @v11 decimal(19,4),
 @v12 decimal(19,4),
 @v13 decimal(19,4),
 @v14 decimal(19,4),
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
 @v19 decimal(19,4),
 @v20 decimal(19,4),
 @v21 decimal(19,4),
 @v22 decimal(19,4),
 @v23 decimal(19,4),
 @v24 decimal(19,4),
 @v25 decimal(19,4),
 @v26 decimal(19,4),
 @v27 decimal(19,4),
 @v28 decimal(19,4)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R014'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r014](import_file_id,source_lineage_id,content_key,[store_code],[store_name],[invoice_year],[invoice_date],[cash],[creditcard],[cn_utilised],[giftcard],[cheque],[round_off],[no_refund],[tata_gv],[loyalty],[cn_issued],[cash_refund],[tatacliq],[gyftr],[paytm],[heliosomni],[paymenttype19],[paymenttype20],[paymenttype21],[paymenttype22],[paymenttype23],[paymenttype24],[paymenttype25],[paymenttype26],[omni],[total_revenue])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r014] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r014 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r015
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 date,
 @v8 nvarchar(max),
 @v9 date,
 @v10 decimal(19,4),
 @v11 decimal(19,4),
 @v12 decimal(19,4),
 @v13 decimal(19,4),
 @v14 decimal(19,4),
 @v15 decimal(19,4),
 @v16 nvarchar(max),
 @v17 nvarchar(max),
 @v18 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R015'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r015](import_file_id,source_lineage_id,content_key,[store_code],[store_name],[channel],[region],[state],[city],[encircle_no],[encircle_enrol_date],[invoicenumber],[invoicedate],[invoicequantity],[grossucp],[sch_discounts],[netgross],[pre_discounts],[netamount],[customernumber],[customer_name],[customer_phone])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r015] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r015 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r016
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 date,
 @v11 decimal(19,4),
 @v12 decimal(19,4),
 @v13 nvarchar(max),
 @v14 nvarchar(max),
 @v15 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R016'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r016](import_file_id,source_lineage_id,content_key,[store_code],[store_name],[store_type],[channel],[region],[state],[city],[encircle_number],[docnumber],[customer_name],[transaction_date],[invoice_amount],[loyality_points],[approval_number],[otp_number],[refinvoicenumber])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r016] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r016 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r017
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 date,
 @v8 decimal(19,4),
 @v9 decimal(19,4),
 @v10 decimal(19,4),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 decimal(19,4)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R017'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r017](import_file_id,source_lineage_id,content_key,[store_code],[store_name],[store_type],[channel],[region],[city],[invnumber],[invdate],[qty],[totalbillvalue],[totalgcamtredeemd],[giftcardno],[approvalnumber],[gccount])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r017] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r017 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r018
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
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R018'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r018](import_file_id,source_lineage_id,content_key,[document_number],[store_code],[store_name],[transaction_type],[doc_invoice_no],[doc_invoice_date],[invoice_year],[reference_doc],[reference_date],[issue_state_name],[issue_gstn_no],[issue_state_code],[recipient_state_name],[recipient_gstn_no],[recipient_state_code],[item_number],[hsn_code],[qty],[uom],[ucp],[gross_ucp],[discounts],[net_amount],[taxable_value],[cgst_rate],[cgst_amount],[sgst_utgst_rate],[sgst_utgst_amount],[igst_rate],[igst_amount],[cess_rate],[cess_amount],[customer_name],[customernumber])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r018] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r018 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r019
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 date,
 @v5 decimal(19,4),
 @v6 date,
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 nvarchar(max),
 @v15 decimal(19,4),
 @v16 nvarchar(max),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
 @v19 decimal(19,4),
 @v20 decimal(19,4),
 @v21 decimal(19,4),
 @v22 decimal(19,4),
 @v23 decimal(19,4),
 @v24 decimal(19,4),
 @v25 decimal(19,4),
 @v26 decimal(19,4),
 @v27 decimal(19,4)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R019'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r019](import_file_id,source_lineage_id,content_key,[store_code],[store_name],[transaction_type],[doc_invoice_no],[doc_invoice_date],[reference_doc],[reference_date],[issue_state_name],[issue_gstn_no],[issue_state_code],[recipient_state_name],[recipient_gstn_no],[recipient_state_code],[item_number],[hsn_code],[qty],[uom],[ucp],[gross_ucp],[discounts],[net_amount],[taxable_value],[cgst_rate],[cgst_amount],[sgst_rate],[sgst_amount],[igst_rate],[igst_amount])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r019] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r019 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r020
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 date,
 @v9 decimal(19,4),
 @v10 nvarchar(max),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 decimal(19,4),
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
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
 @v32 decimal(19,4),
 @v33 decimal(19,4),
 @v34 decimal(19,4),
 @v35 decimal(19,4)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R020'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r020](import_file_id,source_lineage_id,content_key,[channel],[type],[region],[store_code],[storename],[city],[state],[invnumber],[invdate],[invoice_amount],[doc_type_no],[agencyname],[creditcardno],[approvalnumber],[cashamount],[cardamount],[chequeamount],[gvamount],[gcamount],[creditnote],[loyaltypoints],[round_off],[no_refund],[tata_gv],[refund],[paymenttype15],[gyftr],[paytm],[heliosomni],[paymenttype19],[paymenttype20],[paymenttype21],[paymenttype22],[paymenttype23],[paymenttype24],[paymenttype25])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33,@v34,@v35);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r020] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r020 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r021
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 int,
 @v11 decimal(19,4),
 @v12 decimal(19,4),
 @v13 decimal(19,4),
 @v14 decimal(19,4),
 @v15 decimal(19,4),
 @v16 nvarchar(max),
 @v17 nvarchar(max),
 @v18 date,
 @v19 date,
 @v20 decimal(19,4),
 @v21 decimal(19,4),
 @v22 decimal(19,4)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R021'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r021](import_file_id,source_lineage_id,content_key,[store_code],[store_name],[region],[state],[city],[location],[from_region],[from_state],[from_city],[doc_number],[invoice_year],[totalqty],[totalvalue],[discount],[netvalue],[tax],[remark],[ref_document_number],[ref_document_date],[physc_recv_date],[tcschargepercentage],[tcsamount],[total_final_value])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r021] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r021 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r022
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 decimal(19,4),
 @v12 date,
 @v13 int,
 @v14 decimal(19,4),
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
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
 @v42 nvarchar(max),
 @v43 nvarchar(max),
 @v44 nvarchar(max),
 @v45 int
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R022'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r022](import_file_id,source_lineage_id,content_key,[source_transaction_type],[store_code],[store_name],[store_type],[channel],[region],[state],[city],[invoice_number],[customer_name],[customer_phone],[source_invoice_quantity],[transaction_date],[invoice_year],[tender_cash],[tender_card],[tender_cheque],[tender_loyalty_points],[tender_gift_voucher],[tender_credit_note_redeemed],[tender_excess_gv],[tender_round_off],[tender_no_refund],[tender_others],[tender_tata_gv],[tender_gift_card],[tender_tatacliq],[tender_gyftr],[tender_paytm],[tender_helios_omni],[tender_advance_redeem],[tender_bhim_upi],[tender_phonepe],[tender_bharatpe],[tender_bajaj_finance],[tender_razorpay],[tender_payment_type24],[tender_payment_type25],[tender_issued_credit_note],[tender_cash_refund],[tender_cheque_rtgs_refund],[source_net_value],[encircle],[source_store_timestamp],[reference_invoice_number],[referenceyear])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33,@v34,@v35,@v36,@v37,@v38,@v39,@v40,@v41,@v42,@v43,@v44,@v45);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r022] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r022 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r023
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 decimal(19,4),
 @v8 decimal(19,4),
 @v9 date,
 @v10 date,
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 decimal(19,4)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R023'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r023](import_file_id,source_lineage_id,content_key,[businessruleid],[businessrulename],[versionid],[strategy_id],[brand_code],[brand_name],[cluster],[discount_amt],[discount_percentage],[start_date],[end_date],[strategyname],[itemnumber],[ucp])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r023] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r023 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r024
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 date,
 @v9 decimal(19,4),
 @v10 decimal(19,4),
 @v11 decimal(19,4),
 @v12 decimal(19,4),
 @v13 decimal(19,4),
 @v14 decimal(19,4),
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
 @v19 decimal(19,4),
 @v20 decimal(19,4),
 @v21 decimal(19,4),
 @v22 decimal(19,4),
 @v23 nvarchar(max),
 @v24 date,
 @v25 nvarchar(max),
 @v26 nvarchar(max),
 @v27 nvarchar(max),
 @v28 nvarchar(max),
 @v29 nvarchar(max),
 @v30 nvarchar(max),
 @v31 nvarchar(max),
 @v32 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R024'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r024](import_file_id,source_lineage_id,content_key,[trans_type],[store_code],[storename],[store_type],[channel],[region],[city],[inv_number],[inv_date],[qty],[grossucp],[sch_discounts],[netgross],[pre_discounts],[netamount],[sgst_utgst_value],[csgt_value],[igst_value],[cess_value],[tax],[tax_inc],[tax_exc],[netvalue],[invrefno],[invrefdate],[customer_no],[customer_name],[customer_phone],[ulp_number],[customer_gstin_no],[customer_address],[store_timestamp],[eas_timestamp])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r024] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r024 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r025
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 date,
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
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
 @v32 decimal(19,4),
 @v33 decimal(19,4),
 @v34 decimal(19,4),
 @v35 nvarchar(max),
 @v36 date,
 @v37 nvarchar(max),
 @v38 nvarchar(max),
 @v39 nvarchar(max),
 @v40 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R025'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r025](import_file_id,source_lineage_id,content_key,[source_transaction_type],[store_code],[storename],[storetype],[channel],[region],[city],[product_code],[hsn_code],[source_brand_code],[source_brand_name],[brand_segment_code],[gender_code],[invoice_number],[transaction_date],[source_quantity],[source_ucp],[source_gross_ucp],[scheme_discount],[user_discount],[helios_creditnote],[promo_gc],[netgross],[pre_discount],[source_net_amount],[sgst_utgst],[sgst_utgst_value],[csgt],[csgt_value],[igst],[igst_value],[cess],[cess_value],[source_tax_amount],[source_net_value],[reference_invoice_number],[reference_invoice_date],[customer_name],[customer_phone],[ulpnumber],[source_store_timestamp])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33,@v34,@v35,@v36,@v37,@v38,@v39,@v40);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r025] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r025 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r026
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 date,
 @v10 decimal(19,4),
 @v11 nvarchar(max),
 @v12 date,
 @v13 nvarchar(max),
 @v14 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R026'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r026](import_file_id,source_lineage_id,content_key,[transaction_type],[store_code],[store_sap_code],[store_type],[channel],[city],[state],[region],[prp_doc_no],[prp_doc_date],[prp_amount],[jo_no],[jo_date],[prp_variant_no],[prp_scrap_item_code])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r026] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r026 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r027
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 date,
 @v9 nvarchar(max),
 @v10 decimal(19,4),
 @v11 nvarchar(max),
 @v12 date,
 @v13 decimal(19,4),
 @v14 nvarchar(max),
 @v15 date,
 @v16 nvarchar(max),
 @v17 decimal(19,4),
 @v18 nvarchar(max),
 @v19 date,
 @v20 nvarchar(max),
 @v21 decimal(19,4),
 @v22 nvarchar(max),
 @v23 date
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R027'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r027](import_file_id,source_lineage_id,content_key,[warehouse],[store_sap_code],[store_type],[channel],[city],[state],[region],[prp_doc_no],[prp_doc_date],[prp_variant_no],[prp_amount],[etp_invoice_no],[etp_invoice_date],[prp_amount_redeemed],[stock_reciept_doc_no],[stock_reciept_doc_date],[reciept_scrap_item_code],[receipt_scrap_item_value],[stm_purchase_return_doc_no],[stm_purchase_return_doc_date],[stm_scrap_item_code],[stm_scrap_item_value],[stm_courier_doc_no],[stm_courier_doc_date])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r027] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r027 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r028
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 date,
 @v14 decimal(19,4),
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
 @v19 decimal(19,4),
 @v20 decimal(19,4),
 @v21 nvarchar(max),
 @v22 decimal(19,4),
 @v23 decimal(19,4),
 @v24 decimal(19,4),
 @v25 decimal(19,4),
 @v26 decimal(19,4),
 @v27 decimal(19,4),
 @v28 decimal(19,4),
 @v29 decimal(19,4),
 @v30 nvarchar(max),
 @v31 date,
 @v32 decimal(19,4),
 @v33 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R028'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r028](import_file_id,source_lineage_id,content_key,[store_code],[store_name],[store_type],[channel],[region],[state],[city],[itemnumber],[brand],[brandname],[cluster],[gender],[invnumber],[invdate],[qty],[ucp],[grossucp],[sch_discount],[netgross],[pre_discount],[netamount],[hsn_code],[tax],[csgt],[csgt_value],[sgst_utgst],[sgst_utgst_value],[igst],[igst_value],[netvalue],[purinvno],[purinvdate],[purprice],[invoicereferencenumber])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r028] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r028 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r029
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 int,
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 date,
 @v7 decimal(19,4),
 @v8 date
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R029'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r029](import_file_id,source_lineage_id,content_key,[store_code],[store_name],[bankdepositno],[invoice_year],[trans_type],[invoicenumber],[trans_date],[bankedamount],[bankeddate])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r029] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r029 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_r030
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 date,
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 date,
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 nvarchar(max),
 @v19 nvarchar(max),
 @v20 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''STOCK_LEDGER'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_r030](import_file_id,source_lineage_id,content_key,[source_transaction_type],[store_code],[store_name],[product_code],[hsn_code],[brand],[brandname],[cluster],[gender],[document_number],[document_date],[from_location],[to_location],[ref_documentnumber],[ref_documentdate],[opening_quantity],[transaction_quantity],[closing_quantity],[city],[state],[location])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_r030] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_r030 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_r031
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 nvarchar(max),
 @v11 nvarchar(max),
 @v12 nvarchar(max),
 @v13 nvarchar(max),
 @v14 date,
 @v15 decimal(19,4),
 @v16 decimal(19,4),
 @v17 decimal(19,4),
 @v18 decimal(19,4),
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
 @v30 nvarchar(max),
 @v31 date,
 @v32 nvarchar(max),
 @v33 nvarchar(max),
 @v34 nvarchar(max),
 @v35 nvarchar(max),
 @v36 nvarchar(max),
 @v37 date,
 @v38 nvarchar(max),
 @v39 nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''R031'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_r031](import_file_id,source_lineage_id,content_key,[trans_type],[store_code],[storename],[storetype],[channel],[region],[city],[itemnumber],[hsncode],[brand],[brandname],[cluster],[gender],[invnumber],[invdate],[qty],[ucp],[grossucp],[sch_discounts],[netgross],[pre_discounts],[netamount],[sgst_utgst],[sgst_utgst_value],[csgt],[csgt_value],[igst],[igst_value],[tax],[netvalue],[invrefno],[invrefdate],[customer_name],[customer_phone],[ulpnumber],[storetimestamp],[etpadvorder],[etpadvdate],[ordertype],[ocshipmentno])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12,@v13,@v14,@v15,@v16,@v17,@v18,@v19,@v20,@v21,@v22,@v23,@v24,@v25,@v26,@v27,@v28,@v29,@v30,@v31,@v32,@v33,@v34,@v35,@v36,@v37,@v38,@v39);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_r031] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_r031 TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.append_etp_landing_sor_ageing
 @file bigint,@lineage bigint,@key varchar(80),
 @v0 nvarchar(max),
 @v1 nvarchar(max),
 @v2 nvarchar(max),
 @v3 nvarchar(max),
 @v4 nvarchar(max),
 @v5 nvarchar(max),
 @v6 nvarchar(max),
 @v7 nvarchar(max),
 @v8 nvarchar(max),
 @v9 nvarchar(max),
 @v10 date,
 @v11 decimal(19,4),
 @v12 decimal(19,4)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=''SOR_AGEING'')
  THROW 51422,''The source row does not belong to this report import.'',1;
 INSERT dbo.[etp_landing_sor_ageing](import_file_id,source_lineage_id,content_key,[store_code],[store_name],[itemnumber],[hsn_code],[brand],[brandname],[cluster],[gender],[gr_number],[purchaseinvno],[purchaseinvdt],[available_stk],[numberofdays])
 VALUES(@file,@lineage,@key,@v0,@v1,@v2,@v3,@v4,@v5,@v6,@v7,@v8,@v9,@v10,@v11,@v12);
 INSERT dbo.etp_import_content(import_file_id,source_row_number,content_key)
 SELECT @file,source_row_number,@key FROM dbo.source_lineage WHERE source_lineage_id=@lineage;
END');

DENY INSERT,UPDATE,DELETE ON dbo.[etp_landing_sor_ageing] TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.append_etp_landing_sor_ageing TO etp_store_manager,etp_owner;

DENY INSERT,UPDATE,DELETE ON dbo.etp_import_content TO etp_store_manager,etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.staff TO etp_store_manager,etp_viewer;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.replace_import_facts_internal
 @previous_file_id bigint,@replacement_file_id bigint,@user nvarchar(100),@reason nvarchar(500)
AS
BEGIN
 SET NOCOUNT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51421,''Owner permission is required for corrective restatement.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 SET @user=SUSER_SNAME();
 IF LEN(LTRIM(RTRIM(@reason)))=0 THROW 51039,''A restatement reason is required.'',1;
 DECLARE @store varchar(30),@date date,@report varchar(30),@newStore varchar(30),@newDate date,@newReport varchar(30),@restatement bigint,@start date,@end date,@newStart date,@newEnd date;
 SELECT @store=store_code,@date=business_date,@report=report_code,@start=COALESCE(period_start,business_date),@end=COALESCE(period_end,business_date) FROM dbo.import_files WITH(UPDLOCK,HOLDLOCK)
  WHERE import_file_id=@previous_file_id AND is_superseded=0;
 SELECT @newStore=store_code,@newDate=business_date,@newReport=report_code,@newStart=COALESCE(period_start,business_date),@newEnd=COALESCE(period_end,business_date) FROM dbo.import_files WHERE import_file_id=@replacement_file_id;
 IF @report IS NULL THROW 51040,''The previous current import file was not found.'',1;
 IF @store<>@newStore OR (@newStart>@start OR @newEnd<@end) OR @report<>@newReport THROW 51041,''A restatement must replace the same store and report type and cover the previous date range.'',1;
 IF EXISTS(SELECT 1 FROM dbo.daily_reporting_days WHERE store_code=@store AND business_date BETWEEN @newStart AND @newEnd AND status=''LOCKED'')
   THROW 51042,''Reopen the finalised business date before applying a restatement.'',1;

 INSERT dbo.import_restatements(store_code,business_date,report_code,previous_import_file_id,replacement_import_file_id,requested_by,reason,impact_summary)
 VALUES(@store,@date,@report,@previous_file_id,@replacement_file_id,@user,@reason,N''Previous canonical facts archived; replacement facts become the only current reporting generation.'');
 SET @restatement=SCOPE_IDENTITY();

 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''SalesLine'',l.source_lineage_id,
   (SELECT l.sales_line_id,l.sales_invoice_id,l.line_identifier,l.product_code,l.source_transaction_type,l.source_quantity,l.source_gross_amount,l.source_net_amount,l.source_tax_amount,l.source_brand_code,l.source_brand_name,l.brand_segment,l.currency_code FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_lines l JOIN dbo.source_lineage s ON s.source_lineage_id=l.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''InvoiceControl'',c.source_lineage_id,
   (SELECT c.sales_invoice_control_id,c.sales_invoice_id,c.source_transaction_type,c.source_invoice_quantity,c.source_net_value,c.currency_code FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_invoice_controls c JOIN dbo.source_lineage s ON s.source_lineage_id=c.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''Tender'',t.source_lineage_id,
   (SELECT t.sales_tender_id,t.sales_invoice_id,t.tender_type,t.source_amount,t.currency_code,t.is_reporting_eligible,t.exclusion_reason FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_tenders t JOIN dbo.source_lineage s ON s.source_lineage_id=t.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''StockMovement'',m.source_lineage_id,
   (SELECT m.stock_movement_id,m.store_code,m.document_number,m.invoice_year,m.document_date,m.product_code,m.source_transaction_type,m.from_location,m.to_location,m.opening_quantity,m.transaction_quantity,m.closing_quantity FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.stock_movements m JOIN dbo.source_lineage s ON s.source_lineage_id=m.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''StockSnapshot'',p.source_lineage_id,
   (SELECT p.stock_snapshot_id,p.store_code,p.snapshot_date,p.product_code,p.ean,p.brand_code,p.brand_name,p.cluster,p.gender,p.batch_number,p.source_uid,p.quantity,p.unit_cost,p.total_cost FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.stock_snapshots p JOIN dbo.source_lineage s ON s.source_lineage_id=p.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 INSERT dbo.restatement_fact_archive(import_restatement_id,fact_type,previous_source_lineage_id,fact_json)
 SELECT @restatement,''SalesEnrichment'',e.source_lineage_id,
   (SELECT e.sales_line_enrichment_id,e.enrichment_type,e.store_code,e.transaction_date,e.document_number,e.product_code,e.source_transaction_type,e.source_quantity,e.source_net_value,e.source_gross_value,e.content_key,e.invoice_year,e.staff_name,e.source_cro_number,e.scheme_discount,e.user_discount,e.pre_discount,e.other_charges,e.activation_details,e.user_discount_details,e.match_status FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
 FROM dbo.sales_line_enrichments e JOIN dbo.source_lineage s ON s.source_lineage_id=e.source_lineage_id WHERE s.import_file_id=@previous_file_id;

 UPDATE e SET matched_sales_line_id=NULL,match_status=''Missing''
 FROM dbo.sales_line_enrichments e JOIN dbo.sales_lines l ON l.sales_line_id=e.matched_sales_line_id
 JOIN dbo.source_lineage s ON s.source_lineage_id=l.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE e FROM dbo.sales_line_enrichments e JOIN dbo.source_lineage s ON s.source_lineage_id=e.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE l FROM dbo.sales_lines l JOIN dbo.source_lineage s ON s.source_lineage_id=l.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE c FROM dbo.sales_invoice_controls c JOIN dbo.source_lineage s ON s.source_lineage_id=c.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE t FROM dbo.sales_tenders t JOIN dbo.source_lineage s ON s.source_lineage_id=t.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE m FROM dbo.stock_movements m JOIN dbo.source_lineage s ON s.source_lineage_id=m.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 DELETE p FROM dbo.stock_snapshots p JOIN dbo.source_lineage s ON s.source_lineage_id=p.source_lineage_id WHERE s.import_file_id=@previous_file_id;
 UPDATE dbo.import_files SET is_superseded=1,superseded_by_import_file_id=@replacement_file_id,superseded_utc=SYSUTCDATETIME(),superseded_by=@user,restatement_reason=@reason
 WHERE import_file_id=@previous_file_id;
END');

REVOKE EXECUTE ON dbo.replace_import_facts_internal FROM public,etp_store_manager,etp_viewer;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.prepare_import_restatement
 @previous_file_id bigint,@replacement_file_id bigint,@user nvarchar(100),@reason nvarchar(500)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51421,''Owner permission is required for corrective restatement.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF @previous_file_id=@replacement_file_id OR NOT EXISTS(
   SELECT 1 FROM dbo.import_files old WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_files fresh WITH(UPDLOCK,HOLDLOCK) ON fresh.import_file_id=@replacement_file_id
   JOIN dbo.import_batches b ON b.import_batch_id=fresh.import_batch_id
   WHERE old.import_file_id=@previous_file_id AND old.is_superseded=0 AND fresh.is_superseded=0
     AND fresh.data_truth_version=1 AND b.status=''Processing''
     AND old.store_code=fresh.store_code AND old.report_code=fresh.report_code
     AND COALESCE(fresh.period_start,fresh.business_date)<=COALESCE(old.period_start,old.business_date)
     AND COALESCE(fresh.period_end,fresh.business_date)>=COALESCE(old.period_end,old.business_date))
  THROW 51423,''The replacement must be an open import covering the same store and report.'',1;
 IF NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(@reason,CHAR(9),'' ''),CHAR(10),'' ''),CHAR(13),'' ''))),'''') IS NULL
  THROW 51039,''A restatement reason is required.'',1;
 EXEC dbo.replace_import_facts_internal @previous_file_id,@replacement_file_id,@user,@reason;
END');

REVOKE EXECUTE ON dbo.prepare_import_restatement FROM etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.prepare_import_restatement TO etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.promote_import_superset @previous_file_id bigint,@replacement_file_id bigint
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF @previous_file_id=@replacement_file_id OR NOT EXISTS(
   SELECT 1 FROM dbo.import_files old WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_files fresh WITH(UPDLOCK,HOLDLOCK) ON fresh.import_file_id=@replacement_file_id
   JOIN dbo.import_batches b ON b.import_batch_id=fresh.import_batch_id
   WHERE old.import_file_id=@previous_file_id AND old.is_superseded=0 AND fresh.is_superseded=0
     AND fresh.data_truth_version=1 AND b.status=''Processing''
     AND old.store_code=fresh.store_code AND old.report_code=fresh.report_code
     AND COALESCE(fresh.period_start,fresh.business_date)<=COALESCE(old.period_start,old.business_date)
     AND COALESCE(fresh.period_end,fresh.business_date)>=COALESCE(old.period_end,old.business_date))
  THROW 51423,''The replacement must be an open import covering the same store and report.'',1;
 DECLARE @store varchar(30),@date date,@report varchar(30),@version smallint,@restatement bigint,@actor nvarchar(100)=SUSER_SNAME();
 SELECT @store=store_code,@date=business_date,@report=report_code,@version=data_truth_version FROM dbo.import_files WHERE import_file_id=@previous_file_id;
 IF @version=0 BEGIN
  IF COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1 AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
   THROW 51424,''Legacy source upgrades require Owner permission.'',1;
  IF EXISTS(SELECT 1 FROM dbo.import_files old JOIN dbo.import_files fresh ON fresh.import_file_id=@replacement_file_id
    WHERE old.import_file_id=@previous_file_id AND old.source_sha256<>fresh.source_sha256)
   THROW 51424,''Changed legacy sources require explicit Owner restatement.'',1;
  EXEC dbo.replace_import_facts_internal @previous_file_id,@replacement_file_id,@actor,N''Original legacy source upgraded.'';
  RETURN;
 END;
 IF EXISTS(SELECT 1 FROM dbo.etp_import_content old WHERE old.import_file_id=@previous_file_id
   AND NOT EXISTS(SELECT 1 FROM dbo.etp_import_content fresh WHERE fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key))
  THROW 51424,''Only a complete unchanged superset can be promoted automatically.'',1;
 -- Content keys are caller input: independently compare the typed source values.
 IF @report=''R001'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r001] old
   LEFT JOIN dbo.[etp_landing_r001] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[trans_type] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[store_type] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[invnumber] COLLATE Latin1_General_100_BIN2,old.[customer_name] COLLATE Latin1_General_100_BIN2,old.[customer_phone] COLLATE Latin1_General_100_BIN2,old.[invoicequantity],old.[invoicedate],old.[invoice_year],old.[cash],old.[card],old.[cheque],old.[loyalty_points],old.[gv],old.[creditnote_redeem],old.[excess_gv],old.[round_off],old.[no_refund],old.[others],old.[tata_gv],old.[giftcard],old.[tatacliq],old.[gyftr],old.[paytm],old.[heliosomni],old.[advancerdeem],old.[bhimupi],old.[phonepe],old.[bharatpe],old.[bajajfin],old.[razorpay],old.[paymenttype24],old.[paymenttype25],old.[issued_creditnote],old.[cash_refund],old.[cheque_rtgs_refund],old.[netvalue],old.[encircle] COLLATE Latin1_General_100_BIN2,old.[referencenumber] COLLATE Latin1_General_100_BIN2,old.[referenceyear] EXCEPT SELECT fresh.[trans_type] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[store_type] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[invnumber] COLLATE Latin1_General_100_BIN2,fresh.[customer_name] COLLATE Latin1_General_100_BIN2,fresh.[customer_phone] COLLATE Latin1_General_100_BIN2,fresh.[invoicequantity],fresh.[invoicedate],fresh.[invoice_year],fresh.[cash],fresh.[card],fresh.[cheque],fresh.[loyalty_points],fresh.[gv],fresh.[creditnote_redeem],fresh.[excess_gv],fresh.[round_off],fresh.[no_refund],fresh.[others],fresh.[tata_gv],fresh.[giftcard],fresh.[tatacliq],fresh.[gyftr],fresh.[paytm],fresh.[heliosomni],fresh.[advancerdeem],fresh.[bhimupi],fresh.[phonepe],fresh.[bharatpe],fresh.[bajajfin],fresh.[razorpay],fresh.[paymenttype24],fresh.[paymenttype25],fresh.[issued_creditnote],fresh.[cash_refund],fresh.[cheque_rtgs_refund],fresh.[netvalue],fresh.[encircle] COLLATE Latin1_General_100_BIN2,fresh.[referencenumber] COLLATE Latin1_General_100_BIN2,fresh.[referenceyear])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R002'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r002] old
   LEFT JOIN dbo.[etp_landing_r002] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[trans_type] COLLATE Latin1_General_100_BIN2,old.[storetype] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[storename] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[customernumber] COLLATE Latin1_General_100_BIN2,old.[customer_name] COLLATE Latin1_General_100_BIN2,old.[customer_phone] COLLATE Latin1_General_100_BIN2,old.[invnumber] COLLATE Latin1_General_100_BIN2,old.[invdate],old.[signet_no] COLLATE Latin1_General_100_BIN2,old.[ulpnumber] COLLATE Latin1_General_100_BIN2,old.[brand] COLLATE Latin1_General_100_BIN2,old.[cluster] COLLATE Latin1_General_100_BIN2,old.[gender] COLLATE Latin1_General_100_BIN2,old.[itemnumber] COLLATE Latin1_General_100_BIN2,old.[qty],old.[ucp],old.[grossucp],old.[sch_discounts],old.[netgross],old.[pre_discounts],old.[othrchrgs],old.[netamount],old.[tax],old.[hsncode] COLLATE Latin1_General_100_BIN2,old.[sgst_utgst_value],old.[csgt_value],old.[igst_value],old.[remarks] COLLATE Latin1_General_100_BIN2,old.[netvalue],old.[invrefno] COLLATE Latin1_General_100_BIN2,old.[invrefdate],old.[brandname] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[scheme_reference_number] COLLATE Latin1_General_100_BIN2,old.[online] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[trans_type] COLLATE Latin1_General_100_BIN2,fresh.[storetype] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[storename] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[customernumber] COLLATE Latin1_General_100_BIN2,fresh.[customer_name] COLLATE Latin1_General_100_BIN2,fresh.[customer_phone] COLLATE Latin1_General_100_BIN2,fresh.[invnumber] COLLATE Latin1_General_100_BIN2,fresh.[invdate],fresh.[signet_no] COLLATE Latin1_General_100_BIN2,fresh.[ulpnumber] COLLATE Latin1_General_100_BIN2,fresh.[brand] COLLATE Latin1_General_100_BIN2,fresh.[cluster] COLLATE Latin1_General_100_BIN2,fresh.[gender] COLLATE Latin1_General_100_BIN2,fresh.[itemnumber] COLLATE Latin1_General_100_BIN2,fresh.[qty],fresh.[ucp],fresh.[grossucp],fresh.[sch_discounts],fresh.[netgross],fresh.[pre_discounts],fresh.[othrchrgs],fresh.[netamount],fresh.[tax],fresh.[hsncode] COLLATE Latin1_General_100_BIN2,fresh.[sgst_utgst_value],fresh.[csgt_value],fresh.[igst_value],fresh.[remarks] COLLATE Latin1_General_100_BIN2,fresh.[netvalue],fresh.[invrefno] COLLATE Latin1_General_100_BIN2,fresh.[invrefdate],fresh.[brandname] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[scheme_reference_number] COLLATE Latin1_General_100_BIN2,fresh.[online] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R003'' AND EXISTS(SELECT 1 FROM dbo.[etp_r003] old
   LEFT JOIN dbo.[etp_r003] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[source_transaction_type] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[store_type] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[invoice_number] COLLATE Latin1_General_100_BIN2,old.[transaction_date],old.[product_code] COLLATE Latin1_General_100_BIN2,old.[brand] COLLATE Latin1_General_100_BIN2,old.[brand_name] COLLATE Latin1_General_100_BIN2,old.[cluster] COLLATE Latin1_General_100_BIN2,old.[gender] COLLATE Latin1_General_100_BIN2,old.[source_quantity],old.[ucp],old.[grossucp],old.[scheme_discount],old.[netgross],old.[activation_details] COLLATE Latin1_General_100_BIN2,old.[user_discount],old.[other_charges],old.[source_net_amount],old.[user_discount_details] COLLATE Latin1_General_100_BIN2,old.[tax],old.[source_net_value],old.[invoice_ref_no] COLLATE Latin1_General_100_BIN2,old.[invoice_ref_date],old.[customernumber] COLLATE Latin1_General_100_BIN2,old.[customer_name] COLLATE Latin1_General_100_BIN2,old.[customer_phone] COLLATE Latin1_General_100_BIN2,old.[ulp_no] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[source_transaction_type] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[store_type] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[invoice_number] COLLATE Latin1_General_100_BIN2,fresh.[transaction_date],fresh.[product_code] COLLATE Latin1_General_100_BIN2,fresh.[brand] COLLATE Latin1_General_100_BIN2,fresh.[brand_name] COLLATE Latin1_General_100_BIN2,fresh.[cluster] COLLATE Latin1_General_100_BIN2,fresh.[gender] COLLATE Latin1_General_100_BIN2,fresh.[source_quantity],fresh.[ucp],fresh.[grossucp],fresh.[scheme_discount],fresh.[netgross],fresh.[activation_details] COLLATE Latin1_General_100_BIN2,fresh.[user_discount],fresh.[other_charges],fresh.[source_net_amount],fresh.[user_discount_details] COLLATE Latin1_General_100_BIN2,fresh.[tax],fresh.[source_net_value],fresh.[invoice_ref_no] COLLATE Latin1_General_100_BIN2,fresh.[invoice_ref_date],fresh.[customernumber] COLLATE Latin1_General_100_BIN2,fresh.[customer_name] COLLATE Latin1_General_100_BIN2,fresh.[customer_phone] COLLATE Latin1_General_100_BIN2,fresh.[ulp_no] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R004'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r004] old
   LEFT JOIN dbo.[etp_landing_r004] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[trans_type] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[tolocation] COLLATE Latin1_General_100_BIN2,old.[to_region] COLLATE Latin1_General_100_BIN2,old.[to_state] COLLATE Latin1_General_100_BIN2,old.[to_city] COLLATE Latin1_General_100_BIN2,old.[employeeid] COLLATE Latin1_General_100_BIN2,old.[brand] COLLATE Latin1_General_100_BIN2,old.[itemnumber] COLLATE Latin1_General_100_BIN2,old.[hsncode] COLLATE Latin1_General_100_BIN2,old.[taxableamount],old.[cgst],old.[csgt_value],old.[sgst_utgst],old.[sgst_utgst_value],old.[igst],old.[igst_value],old.[docno] COLLATE Latin1_General_100_BIN2,old.[invoice_year],old.[documentdate],old.[qty],old.[ucp],old.[grossucp],old.[purinvno] COLLATE Latin1_General_100_BIN2,old.[purinvdate],old.[remark] COLLATE Latin1_General_100_BIN2,old.[order_type] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[trans_type] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[tolocation] COLLATE Latin1_General_100_BIN2,fresh.[to_region] COLLATE Latin1_General_100_BIN2,fresh.[to_state] COLLATE Latin1_General_100_BIN2,fresh.[to_city] COLLATE Latin1_General_100_BIN2,fresh.[employeeid] COLLATE Latin1_General_100_BIN2,fresh.[brand] COLLATE Latin1_General_100_BIN2,fresh.[itemnumber] COLLATE Latin1_General_100_BIN2,fresh.[hsncode] COLLATE Latin1_General_100_BIN2,fresh.[taxableamount],fresh.[cgst],fresh.[csgt_value],fresh.[sgst_utgst],fresh.[sgst_utgst_value],fresh.[igst],fresh.[igst_value],fresh.[docno] COLLATE Latin1_General_100_BIN2,fresh.[invoice_year],fresh.[documentdate],fresh.[qty],fresh.[ucp],fresh.[grossucp],fresh.[purinvno] COLLATE Latin1_General_100_BIN2,fresh.[purinvdate],fresh.[remark] COLLATE Latin1_General_100_BIN2,fresh.[order_type] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R005'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r005] old
   LEFT JOIN dbo.[etp_landing_r005] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[trans_type] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[tolocation] COLLATE Latin1_General_100_BIN2,old.[to_region] COLLATE Latin1_General_100_BIN2,old.[to_state] COLLATE Latin1_General_100_BIN2,old.[to_city] COLLATE Latin1_General_100_BIN2,old.[employeeid] COLLATE Latin1_General_100_BIN2,old.[docno] COLLATE Latin1_General_100_BIN2,old.[invoice_year],old.[documentdate],old.[qty],old.[grossucp],old.[taxableamount],old.[stmvalue],old.[csgt_value],old.[sgst_utgst_value],old.[igst_value],old.[remarks] COLLATE Latin1_General_100_BIN2,old.[order_type] COLLATE Latin1_General_100_BIN2,old.[couriername] COLLATE Latin1_General_100_BIN2,old.[couriernumber] COLLATE Latin1_General_100_BIN2,old.[courierdate],old.[airwaybillno] COLLATE Latin1_General_100_BIN2,old.[dispatchtype] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[trans_type] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[tolocation] COLLATE Latin1_General_100_BIN2,fresh.[to_region] COLLATE Latin1_General_100_BIN2,fresh.[to_state] COLLATE Latin1_General_100_BIN2,fresh.[to_city] COLLATE Latin1_General_100_BIN2,fresh.[employeeid] COLLATE Latin1_General_100_BIN2,fresh.[docno] COLLATE Latin1_General_100_BIN2,fresh.[invoice_year],fresh.[documentdate],fresh.[qty],fresh.[grossucp],fresh.[taxableamount],fresh.[stmvalue],fresh.[csgt_value],fresh.[sgst_utgst_value],fresh.[igst_value],fresh.[remarks] COLLATE Latin1_General_100_BIN2,fresh.[order_type] COLLATE Latin1_General_100_BIN2,fresh.[couriername] COLLATE Latin1_General_100_BIN2,fresh.[couriernumber] COLLATE Latin1_General_100_BIN2,fresh.[courierdate],fresh.[airwaybillno] COLLATE Latin1_General_100_BIN2,fresh.[dispatchtype] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R006'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r006] old
   LEFT JOIN dbo.[etp_landing_r006] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[trans_type] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[fromlocation] COLLATE Latin1_General_100_BIN2,old.[from_region] COLLATE Latin1_General_100_BIN2,old.[from_state] COLLATE Latin1_General_100_BIN2,old.[from_city] COLLATE Latin1_General_100_BIN2,old.[employeeid] COLLATE Latin1_General_100_BIN2,old.[docnumber] COLLATE Latin1_General_100_BIN2,old.[invoice_year],old.[documentdate],old.[qty],old.[grossucp],old.[discount],old.[taxableamount],old.[csgt_value],old.[sgst_utgst_value],old.[igst_value],old.[refdocnumber] COLLATE Latin1_General_100_BIN2,old.[refdocdate],old.[remark] COLLATE Latin1_General_100_BIN2,old.[order_type] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[trans_type] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[fromlocation] COLLATE Latin1_General_100_BIN2,fresh.[from_region] COLLATE Latin1_General_100_BIN2,fresh.[from_state] COLLATE Latin1_General_100_BIN2,fresh.[from_city] COLLATE Latin1_General_100_BIN2,fresh.[employeeid] COLLATE Latin1_General_100_BIN2,fresh.[docnumber] COLLATE Latin1_General_100_BIN2,fresh.[invoice_year],fresh.[documentdate],fresh.[qty],fresh.[grossucp],fresh.[discount],fresh.[taxableamount],fresh.[csgt_value],fresh.[sgst_utgst_value],fresh.[igst_value],fresh.[refdocnumber] COLLATE Latin1_General_100_BIN2,fresh.[refdocdate],fresh.[remark] COLLATE Latin1_General_100_BIN2,fresh.[order_type] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R007'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r007] old
   LEFT JOIN dbo.[etp_landing_r007] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[trans_type] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[fromlocation] COLLATE Latin1_General_100_BIN2,old.[employeeid] COLLATE Latin1_General_100_BIN2,old.[itemnumber] COLLATE Latin1_General_100_BIN2,old.[hsncode] COLLATE Latin1_General_100_BIN2,old.[brand] COLLATE Latin1_General_100_BIN2,old.[docnumber] COLLATE Latin1_General_100_BIN2,old.[invoice_year],old.[documentdate],old.[from_region] COLLATE Latin1_General_100_BIN2,old.[from_state] COLLATE Latin1_General_100_BIN2,old.[from_city] COLLATE Latin1_General_100_BIN2,old.[qty],old.[ucp_value],old.[grossucp],old.[discount],old.[cgst],old.[csgt_value],old.[sgst_utgst],old.[sgst_utgst_value],old.[igst],old.[igst_value],old.[netvalue],old.[refdocdate],old.[refdocnumber] COLLATE Latin1_General_100_BIN2,old.[purinvno] COLLATE Latin1_General_100_BIN2,old.[purinvdate],old.[remark] COLLATE Latin1_General_100_BIN2,old.[order_type] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[trans_type] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[fromlocation] COLLATE Latin1_General_100_BIN2,fresh.[employeeid] COLLATE Latin1_General_100_BIN2,fresh.[itemnumber] COLLATE Latin1_General_100_BIN2,fresh.[hsncode] COLLATE Latin1_General_100_BIN2,fresh.[brand] COLLATE Latin1_General_100_BIN2,fresh.[docnumber] COLLATE Latin1_General_100_BIN2,fresh.[invoice_year],fresh.[documentdate],fresh.[from_region] COLLATE Latin1_General_100_BIN2,fresh.[from_state] COLLATE Latin1_General_100_BIN2,fresh.[from_city] COLLATE Latin1_General_100_BIN2,fresh.[qty],fresh.[ucp_value],fresh.[grossucp],fresh.[discount],fresh.[cgst],fresh.[csgt_value],fresh.[sgst_utgst],fresh.[sgst_utgst_value],fresh.[igst],fresh.[igst_value],fresh.[netvalue],fresh.[refdocdate],fresh.[refdocnumber] COLLATE Latin1_General_100_BIN2,fresh.[purinvno] COLLATE Latin1_General_100_BIN2,fresh.[purinvdate],fresh.[remark] COLLATE Latin1_General_100_BIN2,fresh.[order_type] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R008'' AND EXISTS(SELECT 1 FROM dbo.[etp_r008] old
   LEFT JOIN dbo.[etp_r008] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[storename] COLLATE Latin1_General_100_BIN2,old.[bankdepositnumber] COLLATE Latin1_General_100_BIN2,old.[transactiondate],old.[trans_type] COLLATE Latin1_General_100_BIN2,old.[invoice_year],old.[invoice_number] COLLATE Latin1_General_100_BIN2,old.[invoice_date],old.[amount],old.[bankedon],old.[bankedamount],old.[unbanked_amount],old.[deposit_slipno] COLLATE Latin1_General_100_BIN2,old.[cc_chequeno] COLLATE Latin1_General_100_BIN2,old.[deposit_date],old.[createdate] EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[storename] COLLATE Latin1_General_100_BIN2,fresh.[bankdepositnumber] COLLATE Latin1_General_100_BIN2,fresh.[transactiondate],fresh.[trans_type] COLLATE Latin1_General_100_BIN2,fresh.[invoice_year],fresh.[invoice_number] COLLATE Latin1_General_100_BIN2,fresh.[invoice_date],fresh.[amount],fresh.[bankedon],fresh.[bankedamount],fresh.[unbanked_amount],fresh.[deposit_slipno] COLLATE Latin1_General_100_BIN2,fresh.[cc_chequeno] COLLATE Latin1_General_100_BIN2,fresh.[deposit_date],fresh.[createdate])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R009'' AND EXISTS(SELECT 1 FROM dbo.[etp_r009] old
   LEFT JOIN dbo.[etp_r009] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[invoice_year],old.[transactiondate],old.[cash],old.[card],old.[cheque_dd],old.[total],old.[cash_deposited],old.[cc_deposited],old.[cheque_dd_deposited],old.[cash_difference],old.[card_difference],old.[cheque_dd_difference] EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[invoice_year],fresh.[transactiondate],fresh.[cash],fresh.[card],fresh.[cheque_dd],fresh.[total],fresh.[cash_deposited],fresh.[cc_deposited],fresh.[cheque_dd_deposited],fresh.[cash_difference],fresh.[card_difference],fresh.[cheque_dd_difference])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R010'' AND EXISTS(SELECT 1 FROM dbo.[etp_r010] old
   LEFT JOIN dbo.[etp_r010] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[store_type] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[itemnumber] COLLATE Latin1_General_100_BIN2,old.[hsn_code] COLLATE Latin1_General_100_BIN2,old.[brand] COLLATE Latin1_General_100_BIN2,old.[brandname] COLLATE Latin1_General_100_BIN2,old.[cluster] COLLATE Latin1_General_100_BIN2,old.[gender] COLLATE Latin1_General_100_BIN2,old.[ean_category] COLLATE Latin1_General_100_BIN2,old.[lotnumber] COLLATE Latin1_General_100_BIN2,old.[uid] COLLATE Latin1_General_100_BIN2,old.[retailbin],old.[servicebin],old.[replacementbin],old.[defectivebin],old.[instibin],old.[ecomm],old.[other1],old.[other2],old.[other3],old.[other4],old.[other5],old.[other6],old.[other7],old.[other8],old.[other9],old.[other10],old.[other11],old.[other12],old.[other13],old.[other14],old.[other15],old.[other16],old.[other17],old.[other18],old.[other19],old.[other20],old.[other21],old.[other22],old.[other23],old.[other24],old.[other25],old.[ucp],old.[closingbalance],old.[totalucp] EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[store_type] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[itemnumber] COLLATE Latin1_General_100_BIN2,fresh.[hsn_code] COLLATE Latin1_General_100_BIN2,fresh.[brand] COLLATE Latin1_General_100_BIN2,fresh.[brandname] COLLATE Latin1_General_100_BIN2,fresh.[cluster] COLLATE Latin1_General_100_BIN2,fresh.[gender] COLLATE Latin1_General_100_BIN2,fresh.[ean_category] COLLATE Latin1_General_100_BIN2,fresh.[lotnumber] COLLATE Latin1_General_100_BIN2,fresh.[uid] COLLATE Latin1_General_100_BIN2,fresh.[retailbin],fresh.[servicebin],fresh.[replacementbin],fresh.[defectivebin],fresh.[instibin],fresh.[ecomm],fresh.[other1],fresh.[other2],fresh.[other3],fresh.[other4],fresh.[other5],fresh.[other6],fresh.[other7],fresh.[other8],fresh.[other9],fresh.[other10],fresh.[other11],fresh.[other12],fresh.[other13],fresh.[other14],fresh.[other15],fresh.[other16],fresh.[other17],fresh.[other18],fresh.[other19],fresh.[other20],fresh.[other21],fresh.[other22],fresh.[other23],fresh.[other24],fresh.[other25],fresh.[ucp],fresh.[closingbalance],fresh.[totalucp])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''CLOSING_STOCK'' AND EXISTS(SELECT 1 FROM dbo.[etp_r011] old
   LEFT JOIN dbo.[etp_r011] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[store_type] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[snapshot_date],old.[product_code] COLLATE Latin1_General_100_BIN2,old.[hsn_code] COLLATE Latin1_General_100_BIN2,old.[itemdescription] COLLATE Latin1_General_100_BIN2,old.[ean] COLLATE Latin1_General_100_BIN2,old.[brand_code] COLLATE Latin1_General_100_BIN2,old.[cluster] COLLATE Latin1_General_100_BIN2,old.[gender] COLLATE Latin1_General_100_BIN2,old.[quantity],old.[unit_cost],old.[total_cost],old.[batch_number] COLLATE Latin1_General_100_BIN2,old.[source_uid] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[store_type] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[snapshot_date],fresh.[product_code] COLLATE Latin1_General_100_BIN2,fresh.[hsn_code] COLLATE Latin1_General_100_BIN2,fresh.[itemdescription] COLLATE Latin1_General_100_BIN2,fresh.[ean] COLLATE Latin1_General_100_BIN2,fresh.[brand_code] COLLATE Latin1_General_100_BIN2,fresh.[cluster] COLLATE Latin1_General_100_BIN2,fresh.[gender] COLLATE Latin1_General_100_BIN2,fresh.[quantity],fresh.[unit_cost],fresh.[total_cost],fresh.[batch_number] COLLATE Latin1_General_100_BIN2,fresh.[source_uid] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R012'' AND EXISTS(SELECT 1 FROM dbo.[etp_r012] old
   LEFT JOIN dbo.[etp_r012] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[store_type] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[creditnotenumber] COLLATE Latin1_General_100_BIN2,old.[creditnotedate],old.[ref_grnno] COLLATE Latin1_General_100_BIN2,old.[cn_refdocno] COLLATE Latin1_General_100_BIN2,old.[expirydate],old.[issueto] COLLATE Latin1_General_100_BIN2,old.[creditnoteamount],old.[invoicenumber] COLLATE Latin1_General_100_BIN2,old.[invoicedate],old.[invoicevalue],old.[redeemed_date],old.[redeemed_by] COLLATE Latin1_General_100_BIN2,old.[issued_cnno] COLLATE Latin1_General_100_BIN2,old.[issued_cnamt] EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[store_type] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[creditnotenumber] COLLATE Latin1_General_100_BIN2,fresh.[creditnotedate],fresh.[ref_grnno] COLLATE Latin1_General_100_BIN2,fresh.[cn_refdocno] COLLATE Latin1_General_100_BIN2,fresh.[expirydate],fresh.[issueto] COLLATE Latin1_General_100_BIN2,fresh.[creditnoteamount],fresh.[invoicenumber] COLLATE Latin1_General_100_BIN2,fresh.[invoicedate],fresh.[invoicevalue],fresh.[redeemed_date],fresh.[redeemed_by] COLLATE Latin1_General_100_BIN2,fresh.[issued_cnno] COLLATE Latin1_General_100_BIN2,fresh.[issued_cnamt])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R013'' AND EXISTS(SELECT 1 FROM dbo.[etp_r013] old
   LEFT JOIN dbo.[etp_r013] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[source_transaction_type] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[store_type] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[product_code] COLLATE Latin1_General_100_BIN2,old.[brand] COLLATE Latin1_General_100_BIN2,old.[brandname] COLLATE Latin1_General_100_BIN2,old.[cluster] COLLATE Latin1_General_100_BIN2,old.[gender] COLLATE Latin1_General_100_BIN2,old.[cro_number] COLLATE Latin1_General_100_BIN2,old.[cro_name] COLLATE Latin1_General_100_BIN2,old.[customer_name] COLLATE Latin1_General_100_BIN2,old.[customer_phone] COLLATE Latin1_General_100_BIN2,old.[invoice_number] COLLATE Latin1_General_100_BIN2,old.[transaction_date],old.[source_quantity],old.[ucp],old.[grossucp],old.[scheme_discount],old.[netgross],old.[pre_discount],old.[source_net_amount],old.[source_net_value],old.[invrefno] COLLATE Latin1_General_100_BIN2,old.[invrefdate] EXCEPT SELECT fresh.[source_transaction_type] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[store_type] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[product_code] COLLATE Latin1_General_100_BIN2,fresh.[brand] COLLATE Latin1_General_100_BIN2,fresh.[brandname] COLLATE Latin1_General_100_BIN2,fresh.[cluster] COLLATE Latin1_General_100_BIN2,fresh.[gender] COLLATE Latin1_General_100_BIN2,fresh.[cro_number] COLLATE Latin1_General_100_BIN2,fresh.[cro_name] COLLATE Latin1_General_100_BIN2,fresh.[customer_name] COLLATE Latin1_General_100_BIN2,fresh.[customer_phone] COLLATE Latin1_General_100_BIN2,fresh.[invoice_number] COLLATE Latin1_General_100_BIN2,fresh.[transaction_date],fresh.[source_quantity],fresh.[ucp],fresh.[grossucp],fresh.[scheme_discount],fresh.[netgross],fresh.[pre_discount],fresh.[source_net_amount],fresh.[source_net_value],fresh.[invrefno] COLLATE Latin1_General_100_BIN2,fresh.[invrefdate])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R014'' AND EXISTS(SELECT 1 FROM dbo.[etp_r014] old
   LEFT JOIN dbo.[etp_r014] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[invoice_year],old.[invoice_date],old.[cash],old.[creditcard],old.[cn_utilised],old.[giftcard],old.[cheque],old.[round_off],old.[no_refund],old.[tata_gv],old.[loyalty],old.[cn_issued],old.[cash_refund],old.[tatacliq],old.[gyftr],old.[paytm],old.[heliosomni],old.[paymenttype19],old.[paymenttype20],old.[paymenttype21],old.[paymenttype22],old.[paymenttype23],old.[paymenttype24],old.[paymenttype25],old.[paymenttype26],old.[omni],old.[total_revenue] EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[invoice_year],fresh.[invoice_date],fresh.[cash],fresh.[creditcard],fresh.[cn_utilised],fresh.[giftcard],fresh.[cheque],fresh.[round_off],fresh.[no_refund],fresh.[tata_gv],fresh.[loyalty],fresh.[cn_issued],fresh.[cash_refund],fresh.[tatacliq],fresh.[gyftr],fresh.[paytm],fresh.[heliosomni],fresh.[paymenttype19],fresh.[paymenttype20],fresh.[paymenttype21],fresh.[paymenttype22],fresh.[paymenttype23],fresh.[paymenttype24],fresh.[paymenttype25],fresh.[paymenttype26],fresh.[omni],fresh.[total_revenue])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R015'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r015] old
   LEFT JOIN dbo.[etp_landing_r015] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[encircle_no] COLLATE Latin1_General_100_BIN2,old.[encircle_enrol_date],old.[invoicenumber] COLLATE Latin1_General_100_BIN2,old.[invoicedate],old.[invoicequantity],old.[grossucp],old.[sch_discounts],old.[netgross],old.[pre_discounts],old.[netamount],old.[customernumber] COLLATE Latin1_General_100_BIN2,old.[customer_name] COLLATE Latin1_General_100_BIN2,old.[customer_phone] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[encircle_no] COLLATE Latin1_General_100_BIN2,fresh.[encircle_enrol_date],fresh.[invoicenumber] COLLATE Latin1_General_100_BIN2,fresh.[invoicedate],fresh.[invoicequantity],fresh.[grossucp],fresh.[sch_discounts],fresh.[netgross],fresh.[pre_discounts],fresh.[netamount],fresh.[customernumber] COLLATE Latin1_General_100_BIN2,fresh.[customer_name] COLLATE Latin1_General_100_BIN2,fresh.[customer_phone] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R016'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r016] old
   LEFT JOIN dbo.[etp_landing_r016] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[store_type] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[encircle_number] COLLATE Latin1_General_100_BIN2,old.[docnumber] COLLATE Latin1_General_100_BIN2,old.[customer_name] COLLATE Latin1_General_100_BIN2,old.[transaction_date],old.[invoice_amount],old.[loyality_points],old.[approval_number] COLLATE Latin1_General_100_BIN2,old.[otp_number] COLLATE Latin1_General_100_BIN2,old.[refinvoicenumber] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[store_type] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[encircle_number] COLLATE Latin1_General_100_BIN2,fresh.[docnumber] COLLATE Latin1_General_100_BIN2,fresh.[customer_name] COLLATE Latin1_General_100_BIN2,fresh.[transaction_date],fresh.[invoice_amount],fresh.[loyality_points],fresh.[approval_number] COLLATE Latin1_General_100_BIN2,fresh.[otp_number] COLLATE Latin1_General_100_BIN2,fresh.[refinvoicenumber] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R017'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r017] old
   LEFT JOIN dbo.[etp_landing_r017] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[store_type] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[invnumber] COLLATE Latin1_General_100_BIN2,old.[invdate],old.[qty],old.[totalbillvalue],old.[totalgcamtredeemd],old.[giftcardno] COLLATE Latin1_General_100_BIN2,old.[approvalnumber] COLLATE Latin1_General_100_BIN2,old.[gccount] EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[store_type] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[invnumber] COLLATE Latin1_General_100_BIN2,fresh.[invdate],fresh.[qty],fresh.[totalbillvalue],fresh.[totalgcamtredeemd],fresh.[giftcardno] COLLATE Latin1_General_100_BIN2,fresh.[approvalnumber] COLLATE Latin1_General_100_BIN2,fresh.[gccount])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R018'' AND EXISTS(SELECT 1 FROM dbo.[etp_r018] old
   LEFT JOIN dbo.[etp_r018] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[document_number] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[transaction_type] COLLATE Latin1_General_100_BIN2,old.[doc_invoice_no] COLLATE Latin1_General_100_BIN2,old.[doc_invoice_date],old.[invoice_year],old.[reference_doc],old.[reference_date],old.[issue_state_name] COLLATE Latin1_General_100_BIN2,old.[issue_gstn_no] COLLATE Latin1_General_100_BIN2,COALESCE(CONVERT(nvarchar(max),TRY_CONVERT(int,old.[issue_state_code])),old.[issue_state_code]) COLLATE Latin1_General_100_BIN2,old.[recipient_state_name] COLLATE Latin1_General_100_BIN2,old.[recipient_gstn_no] COLLATE Latin1_General_100_BIN2,COALESCE(CONVERT(nvarchar(max),TRY_CONVERT(int,old.[recipient_state_code])),old.[recipient_state_code]) COLLATE Latin1_General_100_BIN2,old.[item_number] COLLATE Latin1_General_100_BIN2,old.[hsn_code] COLLATE Latin1_General_100_BIN2,old.[qty],old.[uom] COLLATE Latin1_General_100_BIN2,old.[ucp],old.[gross_ucp],old.[discounts],old.[net_amount],old.[taxable_value],old.[cgst_rate],old.[cgst_amount],old.[sgst_utgst_rate],old.[sgst_utgst_amount],old.[igst_rate],old.[igst_amount],old.[cess_rate],old.[cess_amount],old.[customer_name] COLLATE Latin1_General_100_BIN2,old.[customernumber] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[document_number] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[transaction_type] COLLATE Latin1_General_100_BIN2,fresh.[doc_invoice_no] COLLATE Latin1_General_100_BIN2,fresh.[doc_invoice_date],fresh.[invoice_year],fresh.[reference_doc],fresh.[reference_date],fresh.[issue_state_name] COLLATE Latin1_General_100_BIN2,fresh.[issue_gstn_no] COLLATE Latin1_General_100_BIN2,COALESCE(CONVERT(nvarchar(max),TRY_CONVERT(int,fresh.[issue_state_code])),fresh.[issue_state_code]) COLLATE Latin1_General_100_BIN2,fresh.[recipient_state_name] COLLATE Latin1_General_100_BIN2,fresh.[recipient_gstn_no] COLLATE Latin1_General_100_BIN2,COALESCE(CONVERT(nvarchar(max),TRY_CONVERT(int,fresh.[recipient_state_code])),fresh.[recipient_state_code]) COLLATE Latin1_General_100_BIN2,fresh.[item_number] COLLATE Latin1_General_100_BIN2,fresh.[hsn_code] COLLATE Latin1_General_100_BIN2,fresh.[qty],fresh.[uom] COLLATE Latin1_General_100_BIN2,fresh.[ucp],fresh.[gross_ucp],fresh.[discounts],fresh.[net_amount],fresh.[taxable_value],fresh.[cgst_rate],fresh.[cgst_amount],fresh.[sgst_utgst_rate],fresh.[sgst_utgst_amount],fresh.[igst_rate],fresh.[igst_amount],fresh.[cess_rate],fresh.[cess_amount],fresh.[customer_name] COLLATE Latin1_General_100_BIN2,fresh.[customernumber] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R019'' AND EXISTS(SELECT 1 FROM dbo.[etp_r019] old
   LEFT JOIN dbo.[etp_r019] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[transaction_type] COLLATE Latin1_General_100_BIN2,old.[doc_invoice_no] COLLATE Latin1_General_100_BIN2,old.[doc_invoice_date],old.[reference_doc],old.[reference_date],old.[issue_state_name] COLLATE Latin1_General_100_BIN2,old.[issue_gstn_no] COLLATE Latin1_General_100_BIN2,COALESCE(CONVERT(nvarchar(max),TRY_CONVERT(int,old.[issue_state_code])),old.[issue_state_code]) COLLATE Latin1_General_100_BIN2,old.[recipient_state_name] COLLATE Latin1_General_100_BIN2,old.[recipient_gstn_no] COLLATE Latin1_General_100_BIN2,COALESCE(CONVERT(nvarchar(max),TRY_CONVERT(int,old.[recipient_state_code])),old.[recipient_state_code]) COLLATE Latin1_General_100_BIN2,old.[item_number] COLLATE Latin1_General_100_BIN2,old.[hsn_code] COLLATE Latin1_General_100_BIN2,old.[qty],old.[uom] COLLATE Latin1_General_100_BIN2,old.[ucp],old.[gross_ucp],old.[discounts],old.[net_amount],old.[taxable_value],old.[cgst_rate],old.[cgst_amount],old.[sgst_rate],old.[sgst_amount],old.[igst_rate],old.[igst_amount] EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[transaction_type] COLLATE Latin1_General_100_BIN2,fresh.[doc_invoice_no] COLLATE Latin1_General_100_BIN2,fresh.[doc_invoice_date],fresh.[reference_doc],fresh.[reference_date],fresh.[issue_state_name] COLLATE Latin1_General_100_BIN2,fresh.[issue_gstn_no] COLLATE Latin1_General_100_BIN2,COALESCE(CONVERT(nvarchar(max),TRY_CONVERT(int,fresh.[issue_state_code])),fresh.[issue_state_code]) COLLATE Latin1_General_100_BIN2,fresh.[recipient_state_name] COLLATE Latin1_General_100_BIN2,fresh.[recipient_gstn_no] COLLATE Latin1_General_100_BIN2,COALESCE(CONVERT(nvarchar(max),TRY_CONVERT(int,fresh.[recipient_state_code])),fresh.[recipient_state_code]) COLLATE Latin1_General_100_BIN2,fresh.[item_number] COLLATE Latin1_General_100_BIN2,fresh.[hsn_code] COLLATE Latin1_General_100_BIN2,fresh.[qty],fresh.[uom] COLLATE Latin1_General_100_BIN2,fresh.[ucp],fresh.[gross_ucp],fresh.[discounts],fresh.[net_amount],fresh.[taxable_value],fresh.[cgst_rate],fresh.[cgst_amount],fresh.[sgst_rate],fresh.[sgst_amount],fresh.[igst_rate],fresh.[igst_amount])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R020'' AND EXISTS(SELECT 1 FROM dbo.[etp_r020] old
   LEFT JOIN dbo.[etp_r020] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[channel] COLLATE Latin1_General_100_BIN2,old.[type] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[storename] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[invnumber] COLLATE Latin1_General_100_BIN2,old.[invdate],old.[invoice_amount],old.[doc_type_no] COLLATE Latin1_General_100_BIN2,old.[agencyname] COLLATE Latin1_General_100_BIN2,old.[creditcardno] COLLATE Latin1_General_100_BIN2,old.[approvalnumber] COLLATE Latin1_General_100_BIN2,old.[cashamount],old.[cardamount],old.[chequeamount],old.[gvamount],old.[gcamount],old.[creditnote],old.[loyaltypoints],old.[round_off],old.[no_refund],old.[tata_gv],old.[refund],old.[paymenttype15],old.[gyftr],old.[paytm],old.[heliosomni],old.[paymenttype19],old.[paymenttype20],old.[paymenttype21],old.[paymenttype22],old.[paymenttype23],old.[paymenttype24],old.[paymenttype25] EXCEPT SELECT fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[type] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[storename] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[invnumber] COLLATE Latin1_General_100_BIN2,fresh.[invdate],fresh.[invoice_amount],fresh.[doc_type_no] COLLATE Latin1_General_100_BIN2,fresh.[agencyname] COLLATE Latin1_General_100_BIN2,fresh.[creditcardno] COLLATE Latin1_General_100_BIN2,fresh.[approvalnumber] COLLATE Latin1_General_100_BIN2,fresh.[cashamount],fresh.[cardamount],fresh.[chequeamount],fresh.[gvamount],fresh.[gcamount],fresh.[creditnote],fresh.[loyaltypoints],fresh.[round_off],fresh.[no_refund],fresh.[tata_gv],fresh.[refund],fresh.[paymenttype15],fresh.[gyftr],fresh.[paytm],fresh.[heliosomni],fresh.[paymenttype19],fresh.[paymenttype20],fresh.[paymenttype21],fresh.[paymenttype22],fresh.[paymenttype23],fresh.[paymenttype24],fresh.[paymenttype25])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R021'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r021] old
   LEFT JOIN dbo.[etp_landing_r021] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[location] COLLATE Latin1_General_100_BIN2,old.[from_region] COLLATE Latin1_General_100_BIN2,old.[from_state] COLLATE Latin1_General_100_BIN2,old.[from_city] COLLATE Latin1_General_100_BIN2,old.[doc_number] COLLATE Latin1_General_100_BIN2,old.[invoice_year],old.[totalqty],old.[totalvalue],old.[discount],old.[netvalue],old.[tax],old.[remark] COLLATE Latin1_General_100_BIN2,old.[ref_document_number] COLLATE Latin1_General_100_BIN2,old.[ref_document_date],old.[physc_recv_date],old.[tcschargepercentage],old.[tcsamount],old.[total_final_value] EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[location] COLLATE Latin1_General_100_BIN2,fresh.[from_region] COLLATE Latin1_General_100_BIN2,fresh.[from_state] COLLATE Latin1_General_100_BIN2,fresh.[from_city] COLLATE Latin1_General_100_BIN2,fresh.[doc_number] COLLATE Latin1_General_100_BIN2,fresh.[invoice_year],fresh.[totalqty],fresh.[totalvalue],fresh.[discount],fresh.[netvalue],fresh.[tax],fresh.[remark] COLLATE Latin1_General_100_BIN2,fresh.[ref_document_number] COLLATE Latin1_General_100_BIN2,fresh.[ref_document_date],fresh.[physc_recv_date],fresh.[tcschargepercentage],fresh.[tcsamount],fresh.[total_final_value])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R022'' AND EXISTS(SELECT 1 FROM dbo.[etp_r022] old
   LEFT JOIN dbo.[etp_r022] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[source_transaction_type] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[store_type] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[invoice_number] COLLATE Latin1_General_100_BIN2,old.[customer_name] COLLATE Latin1_General_100_BIN2,old.[customer_phone] COLLATE Latin1_General_100_BIN2,old.[source_invoice_quantity],old.[transaction_date],old.[invoice_year],old.[tender_cash],old.[tender_card],old.[tender_cheque],old.[tender_loyalty_points],old.[tender_gift_voucher],old.[tender_credit_note_redeemed],old.[tender_excess_gv],old.[tender_round_off],old.[tender_no_refund],old.[tender_others],old.[tender_tata_gv],old.[tender_gift_card],old.[tender_tatacliq],old.[tender_gyftr],old.[tender_paytm],old.[tender_helios_omni],old.[tender_advance_redeem],old.[tender_bhim_upi],old.[tender_phonepe],old.[tender_bharatpe],old.[tender_bajaj_finance],old.[tender_razorpay],old.[tender_payment_type24],old.[tender_payment_type25],old.[tender_issued_credit_note],old.[tender_cash_refund],old.[tender_cheque_rtgs_refund],old.[source_net_value],old.[encircle] COLLATE Latin1_General_100_BIN2,old.[reference_invoice_number] COLLATE Latin1_General_100_BIN2,old.[referenceyear] EXCEPT SELECT fresh.[source_transaction_type] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[store_type] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[invoice_number] COLLATE Latin1_General_100_BIN2,fresh.[customer_name] COLLATE Latin1_General_100_BIN2,fresh.[customer_phone] COLLATE Latin1_General_100_BIN2,fresh.[source_invoice_quantity],fresh.[transaction_date],fresh.[invoice_year],fresh.[tender_cash],fresh.[tender_card],fresh.[tender_cheque],fresh.[tender_loyalty_points],fresh.[tender_gift_voucher],fresh.[tender_credit_note_redeemed],fresh.[tender_excess_gv],fresh.[tender_round_off],fresh.[tender_no_refund],fresh.[tender_others],fresh.[tender_tata_gv],fresh.[tender_gift_card],fresh.[tender_tatacliq],fresh.[tender_gyftr],fresh.[tender_paytm],fresh.[tender_helios_omni],fresh.[tender_advance_redeem],fresh.[tender_bhim_upi],fresh.[tender_phonepe],fresh.[tender_bharatpe],fresh.[tender_bajaj_finance],fresh.[tender_razorpay],fresh.[tender_payment_type24],fresh.[tender_payment_type25],fresh.[tender_issued_credit_note],fresh.[tender_cash_refund],fresh.[tender_cheque_rtgs_refund],fresh.[source_net_value],fresh.[encircle] COLLATE Latin1_General_100_BIN2,fresh.[reference_invoice_number] COLLATE Latin1_General_100_BIN2,fresh.[referenceyear])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R023'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r023] old
   LEFT JOIN dbo.[etp_landing_r023] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[businessruleid] COLLATE Latin1_General_100_BIN2,old.[businessrulename] COLLATE Latin1_General_100_BIN2,old.[versionid] COLLATE Latin1_General_100_BIN2,old.[strategy_id] COLLATE Latin1_General_100_BIN2,old.[brand_code] COLLATE Latin1_General_100_BIN2,old.[brand_name] COLLATE Latin1_General_100_BIN2,old.[cluster] COLLATE Latin1_General_100_BIN2,old.[discount_amt],old.[discount_percentage],old.[start_date],old.[end_date],old.[strategyname] COLLATE Latin1_General_100_BIN2,old.[itemnumber] COLLATE Latin1_General_100_BIN2,old.[ucp] EXCEPT SELECT fresh.[businessruleid] COLLATE Latin1_General_100_BIN2,fresh.[businessrulename] COLLATE Latin1_General_100_BIN2,fresh.[versionid] COLLATE Latin1_General_100_BIN2,fresh.[strategy_id] COLLATE Latin1_General_100_BIN2,fresh.[brand_code] COLLATE Latin1_General_100_BIN2,fresh.[brand_name] COLLATE Latin1_General_100_BIN2,fresh.[cluster] COLLATE Latin1_General_100_BIN2,fresh.[discount_amt],fresh.[discount_percentage],fresh.[start_date],fresh.[end_date],fresh.[strategyname] COLLATE Latin1_General_100_BIN2,fresh.[itemnumber] COLLATE Latin1_General_100_BIN2,fresh.[ucp])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R024'' AND EXISTS(SELECT 1 FROM dbo.[etp_r024] old
   LEFT JOIN dbo.[etp_r024] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[trans_type] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[storename] COLLATE Latin1_General_100_BIN2,old.[store_type] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[inv_number] COLLATE Latin1_General_100_BIN2,old.[inv_date],old.[qty],old.[grossucp],old.[sch_discounts],old.[netgross],old.[pre_discounts],old.[netamount],old.[sgst_utgst_value],old.[csgt_value],old.[igst_value],old.[cess_value],old.[tax],old.[tax_inc],old.[tax_exc],old.[netvalue],old.[invrefno] COLLATE Latin1_General_100_BIN2,old.[invrefdate],old.[customer_no] COLLATE Latin1_General_100_BIN2,old.[customer_name] COLLATE Latin1_General_100_BIN2,old.[customer_phone] COLLATE Latin1_General_100_BIN2,old.[ulp_number] COLLATE Latin1_General_100_BIN2,old.[customer_gstin_no] COLLATE Latin1_General_100_BIN2,old.[customer_address] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[trans_type] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[storename] COLLATE Latin1_General_100_BIN2,fresh.[store_type] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[inv_number] COLLATE Latin1_General_100_BIN2,fresh.[inv_date],fresh.[qty],fresh.[grossucp],fresh.[sch_discounts],fresh.[netgross],fresh.[pre_discounts],fresh.[netamount],fresh.[sgst_utgst_value],fresh.[csgt_value],fresh.[igst_value],fresh.[cess_value],fresh.[tax],fresh.[tax_inc],fresh.[tax_exc],fresh.[netvalue],fresh.[invrefno] COLLATE Latin1_General_100_BIN2,fresh.[invrefdate],fresh.[customer_no] COLLATE Latin1_General_100_BIN2,fresh.[customer_name] COLLATE Latin1_General_100_BIN2,fresh.[customer_phone] COLLATE Latin1_General_100_BIN2,fresh.[ulp_number] COLLATE Latin1_General_100_BIN2,fresh.[customer_gstin_no] COLLATE Latin1_General_100_BIN2,fresh.[customer_address] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R025'' AND EXISTS(SELECT 1 FROM dbo.[etp_r025] old
   LEFT JOIN dbo.[etp_r025] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[source_transaction_type] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[storename] COLLATE Latin1_General_100_BIN2,old.[storetype] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[product_code] COLLATE Latin1_General_100_BIN2,old.[hsn_code] COLLATE Latin1_General_100_BIN2,old.[source_brand_code] COLLATE Latin1_General_100_BIN2,old.[source_brand_name] COLLATE Latin1_General_100_BIN2,old.[brand_segment_code] COLLATE Latin1_General_100_BIN2,old.[gender_code] COLLATE Latin1_General_100_BIN2,old.[invoice_number] COLLATE Latin1_General_100_BIN2,old.[transaction_date],old.[source_quantity],old.[source_ucp],old.[source_gross_ucp],old.[scheme_discount],old.[user_discount],old.[helios_creditnote],old.[promo_gc],old.[netgross],old.[pre_discount],old.[source_net_amount],old.[sgst_utgst],old.[sgst_utgst_value],old.[csgt],old.[csgt_value],old.[igst],old.[igst_value],old.[cess],old.[cess_value],old.[source_tax_amount],old.[source_net_value],old.[reference_invoice_number] COLLATE Latin1_General_100_BIN2,old.[reference_invoice_date],old.[customer_name] COLLATE Latin1_General_100_BIN2,old.[customer_phone] COLLATE Latin1_General_100_BIN2,old.[ulpnumber] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[source_transaction_type] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[storename] COLLATE Latin1_General_100_BIN2,fresh.[storetype] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[product_code] COLLATE Latin1_General_100_BIN2,fresh.[hsn_code] COLLATE Latin1_General_100_BIN2,fresh.[source_brand_code] COLLATE Latin1_General_100_BIN2,fresh.[source_brand_name] COLLATE Latin1_General_100_BIN2,fresh.[brand_segment_code] COLLATE Latin1_General_100_BIN2,fresh.[gender_code] COLLATE Latin1_General_100_BIN2,fresh.[invoice_number] COLLATE Latin1_General_100_BIN2,fresh.[transaction_date],fresh.[source_quantity],fresh.[source_ucp],fresh.[source_gross_ucp],fresh.[scheme_discount],fresh.[user_discount],fresh.[helios_creditnote],fresh.[promo_gc],fresh.[netgross],fresh.[pre_discount],fresh.[source_net_amount],fresh.[sgst_utgst],fresh.[sgst_utgst_value],fresh.[csgt],fresh.[csgt_value],fresh.[igst],fresh.[igst_value],fresh.[cess],fresh.[cess_value],fresh.[source_tax_amount],fresh.[source_net_value],fresh.[reference_invoice_number] COLLATE Latin1_General_100_BIN2,fresh.[reference_invoice_date],fresh.[customer_name] COLLATE Latin1_General_100_BIN2,fresh.[customer_phone] COLLATE Latin1_General_100_BIN2,fresh.[ulpnumber] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R026'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r026] old
   LEFT JOIN dbo.[etp_landing_r026] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[transaction_type] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_sap_code] COLLATE Latin1_General_100_BIN2,old.[store_type] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[prp_doc_no] COLLATE Latin1_General_100_BIN2,old.[prp_doc_date],old.[prp_amount],old.[jo_no] COLLATE Latin1_General_100_BIN2,old.[jo_date],old.[prp_variant_no] COLLATE Latin1_General_100_BIN2,old.[prp_scrap_item_code] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[transaction_type] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_sap_code] COLLATE Latin1_General_100_BIN2,fresh.[store_type] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[prp_doc_no] COLLATE Latin1_General_100_BIN2,fresh.[prp_doc_date],fresh.[prp_amount],fresh.[jo_no] COLLATE Latin1_General_100_BIN2,fresh.[jo_date],fresh.[prp_variant_no] COLLATE Latin1_General_100_BIN2,fresh.[prp_scrap_item_code] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R027'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r027] old
   LEFT JOIN dbo.[etp_landing_r027] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[warehouse] COLLATE Latin1_General_100_BIN2,old.[store_sap_code] COLLATE Latin1_General_100_BIN2,old.[store_type] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[prp_doc_no] COLLATE Latin1_General_100_BIN2,old.[prp_doc_date],old.[prp_variant_no] COLLATE Latin1_General_100_BIN2,old.[prp_amount],old.[etp_invoice_no] COLLATE Latin1_General_100_BIN2,old.[etp_invoice_date],old.[prp_amount_redeemed],old.[stock_reciept_doc_no] COLLATE Latin1_General_100_BIN2,old.[stock_reciept_doc_date],old.[reciept_scrap_item_code] COLLATE Latin1_General_100_BIN2,old.[receipt_scrap_item_value],old.[stm_purchase_return_doc_no] COLLATE Latin1_General_100_BIN2,old.[stm_purchase_return_doc_date],old.[stm_scrap_item_code] COLLATE Latin1_General_100_BIN2,old.[stm_scrap_item_value],old.[stm_courier_doc_no] COLLATE Latin1_General_100_BIN2,old.[stm_courier_doc_date] EXCEPT SELECT fresh.[warehouse] COLLATE Latin1_General_100_BIN2,fresh.[store_sap_code] COLLATE Latin1_General_100_BIN2,fresh.[store_type] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[prp_doc_no] COLLATE Latin1_General_100_BIN2,fresh.[prp_doc_date],fresh.[prp_variant_no] COLLATE Latin1_General_100_BIN2,fresh.[prp_amount],fresh.[etp_invoice_no] COLLATE Latin1_General_100_BIN2,fresh.[etp_invoice_date],fresh.[prp_amount_redeemed],fresh.[stock_reciept_doc_no] COLLATE Latin1_General_100_BIN2,fresh.[stock_reciept_doc_date],fresh.[reciept_scrap_item_code] COLLATE Latin1_General_100_BIN2,fresh.[receipt_scrap_item_value],fresh.[stm_purchase_return_doc_no] COLLATE Latin1_General_100_BIN2,fresh.[stm_purchase_return_doc_date],fresh.[stm_scrap_item_code] COLLATE Latin1_General_100_BIN2,fresh.[stm_scrap_item_value],fresh.[stm_courier_doc_no] COLLATE Latin1_General_100_BIN2,fresh.[stm_courier_doc_date])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R028'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r028] old
   LEFT JOIN dbo.[etp_landing_r028] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[store_type] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[state] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[itemnumber] COLLATE Latin1_General_100_BIN2,old.[brand] COLLATE Latin1_General_100_BIN2,old.[brandname] COLLATE Latin1_General_100_BIN2,old.[cluster] COLLATE Latin1_General_100_BIN2,old.[gender] COLLATE Latin1_General_100_BIN2,old.[invnumber] COLLATE Latin1_General_100_BIN2,old.[invdate],old.[qty],old.[ucp],old.[grossucp],old.[sch_discount],old.[netgross],old.[pre_discount],old.[netamount],old.[hsn_code] COLLATE Latin1_General_100_BIN2,old.[tax],old.[csgt],old.[csgt_value],old.[sgst_utgst],old.[sgst_utgst_value],old.[igst],old.[igst_value],old.[netvalue],old.[purinvno] COLLATE Latin1_General_100_BIN2,old.[purinvdate],old.[purprice],old.[invoicereferencenumber] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[store_type] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[state] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[itemnumber] COLLATE Latin1_General_100_BIN2,fresh.[brand] COLLATE Latin1_General_100_BIN2,fresh.[brandname] COLLATE Latin1_General_100_BIN2,fresh.[cluster] COLLATE Latin1_General_100_BIN2,fresh.[gender] COLLATE Latin1_General_100_BIN2,fresh.[invnumber] COLLATE Latin1_General_100_BIN2,fresh.[invdate],fresh.[qty],fresh.[ucp],fresh.[grossucp],fresh.[sch_discount],fresh.[netgross],fresh.[pre_discount],fresh.[netamount],fresh.[hsn_code] COLLATE Latin1_General_100_BIN2,fresh.[tax],fresh.[csgt],fresh.[csgt_value],fresh.[sgst_utgst],fresh.[sgst_utgst_value],fresh.[igst],fresh.[igst_value],fresh.[netvalue],fresh.[purinvno] COLLATE Latin1_General_100_BIN2,fresh.[purinvdate],fresh.[purprice],fresh.[invoicereferencenumber] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R029'' AND EXISTS(SELECT 1 FROM dbo.[etp_r029] old
   LEFT JOIN dbo.[etp_r029] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[bankdepositno] COLLATE Latin1_General_100_BIN2,old.[invoice_year],old.[trans_type] COLLATE Latin1_General_100_BIN2,old.[invoicenumber] COLLATE Latin1_General_100_BIN2,old.[trans_date],old.[bankedamount],old.[bankeddate] EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[bankdepositno] COLLATE Latin1_General_100_BIN2,fresh.[invoice_year],fresh.[trans_type] COLLATE Latin1_General_100_BIN2,fresh.[invoicenumber] COLLATE Latin1_General_100_BIN2,fresh.[trans_date],fresh.[bankedamount],fresh.[bankeddate])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''STOCK_LEDGER'' AND EXISTS(SELECT 1 FROM dbo.[etp_r030] old
   LEFT JOIN dbo.[etp_r030] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[source_transaction_type] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[product_code] COLLATE Latin1_General_100_BIN2,old.[document_number] COLLATE Latin1_General_100_BIN2,old.[document_date],old.[from_location] COLLATE Latin1_General_100_BIN2,old.[to_location] COLLATE Latin1_General_100_BIN2,old.[opening_quantity],old.[transaction_quantity],old.[closing_quantity] EXCEPT SELECT fresh.[source_transaction_type] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[product_code] COLLATE Latin1_General_100_BIN2,fresh.[document_number] COLLATE Latin1_General_100_BIN2,fresh.[document_date],fresh.[from_location] COLLATE Latin1_General_100_BIN2,fresh.[to_location] COLLATE Latin1_General_100_BIN2,fresh.[opening_quantity],fresh.[transaction_quantity],fresh.[closing_quantity])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''R031'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_r031] old
   LEFT JOIN dbo.[etp_landing_r031] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[trans_type] COLLATE Latin1_General_100_BIN2,old.[store_code] COLLATE Latin1_General_100_BIN2,old.[storename] COLLATE Latin1_General_100_BIN2,old.[storetype] COLLATE Latin1_General_100_BIN2,old.[channel] COLLATE Latin1_General_100_BIN2,old.[region] COLLATE Latin1_General_100_BIN2,old.[city] COLLATE Latin1_General_100_BIN2,old.[itemnumber] COLLATE Latin1_General_100_BIN2,old.[hsncode] COLLATE Latin1_General_100_BIN2,old.[brand] COLLATE Latin1_General_100_BIN2,old.[brandname] COLLATE Latin1_General_100_BIN2,old.[cluster] COLLATE Latin1_General_100_BIN2,old.[gender] COLLATE Latin1_General_100_BIN2,old.[invnumber] COLLATE Latin1_General_100_BIN2,old.[invdate],old.[qty],old.[ucp],old.[grossucp],old.[sch_discounts],old.[netgross],old.[pre_discounts],old.[netamount],old.[sgst_utgst],old.[sgst_utgst_value],old.[csgt],old.[csgt_value],old.[igst],old.[igst_value],old.[tax],old.[netvalue],old.[invrefno] COLLATE Latin1_General_100_BIN2,old.[invrefdate],old.[customer_name] COLLATE Latin1_General_100_BIN2,old.[customer_phone] COLLATE Latin1_General_100_BIN2,old.[ulpnumber] COLLATE Latin1_General_100_BIN2,old.[etpadvorder] COLLATE Latin1_General_100_BIN2,old.[etpadvdate],old.[ordertype] COLLATE Latin1_General_100_BIN2,old.[ocshipmentno] COLLATE Latin1_General_100_BIN2 EXCEPT SELECT fresh.[trans_type] COLLATE Latin1_General_100_BIN2,fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[storename] COLLATE Latin1_General_100_BIN2,fresh.[storetype] COLLATE Latin1_General_100_BIN2,fresh.[channel] COLLATE Latin1_General_100_BIN2,fresh.[region] COLLATE Latin1_General_100_BIN2,fresh.[city] COLLATE Latin1_General_100_BIN2,fresh.[itemnumber] COLLATE Latin1_General_100_BIN2,fresh.[hsncode] COLLATE Latin1_General_100_BIN2,fresh.[brand] COLLATE Latin1_General_100_BIN2,fresh.[brandname] COLLATE Latin1_General_100_BIN2,fresh.[cluster] COLLATE Latin1_General_100_BIN2,fresh.[gender] COLLATE Latin1_General_100_BIN2,fresh.[invnumber] COLLATE Latin1_General_100_BIN2,fresh.[invdate],fresh.[qty],fresh.[ucp],fresh.[grossucp],fresh.[sch_discounts],fresh.[netgross],fresh.[pre_discounts],fresh.[netamount],fresh.[sgst_utgst],fresh.[sgst_utgst_value],fresh.[csgt],fresh.[csgt_value],fresh.[igst],fresh.[igst_value],fresh.[tax],fresh.[netvalue],fresh.[invrefno] COLLATE Latin1_General_100_BIN2,fresh.[invrefdate],fresh.[customer_name] COLLATE Latin1_General_100_BIN2,fresh.[customer_phone] COLLATE Latin1_General_100_BIN2,fresh.[ulpnumber] COLLATE Latin1_General_100_BIN2,fresh.[etpadvorder] COLLATE Latin1_General_100_BIN2,fresh.[etpadvdate],fresh.[ordertype] COLLATE Latin1_General_100_BIN2,fresh.[ocshipmentno] COLLATE Latin1_General_100_BIN2)))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF @report=''SOR_AGEING'' AND EXISTS(SELECT 1 FROM dbo.[etp_landing_sor_ageing] old
   LEFT JOIN dbo.[etp_landing_sor_ageing] fresh ON fresh.import_file_id=@replacement_file_id AND fresh.content_key=old.content_key
   WHERE old.import_file_id=@previous_file_id AND (fresh.etp_row_id IS NULL OR EXISTS(SELECT old.[store_code] COLLATE Latin1_General_100_BIN2,old.[store_name] COLLATE Latin1_General_100_BIN2,old.[itemnumber] COLLATE Latin1_General_100_BIN2,old.[hsn_code] COLLATE Latin1_General_100_BIN2,old.[brand] COLLATE Latin1_General_100_BIN2,old.[brandname] COLLATE Latin1_General_100_BIN2,old.[cluster] COLLATE Latin1_General_100_BIN2,old.[gender] COLLATE Latin1_General_100_BIN2,old.[gr_number] COLLATE Latin1_General_100_BIN2,old.[purchaseinvno] COLLATE Latin1_General_100_BIN2,old.[purchaseinvdt],old.[available_stk],old.[numberofdays] EXCEPT SELECT fresh.[store_code] COLLATE Latin1_General_100_BIN2,fresh.[store_name] COLLATE Latin1_General_100_BIN2,fresh.[itemnumber] COLLATE Latin1_General_100_BIN2,fresh.[hsn_code] COLLATE Latin1_General_100_BIN2,fresh.[brand] COLLATE Latin1_General_100_BIN2,fresh.[brandname] COLLATE Latin1_General_100_BIN2,fresh.[cluster] COLLATE Latin1_General_100_BIN2,fresh.[gender] COLLATE Latin1_General_100_BIN2,fresh.[gr_number] COLLATE Latin1_General_100_BIN2,fresh.[purchaseinvno] COLLATE Latin1_General_100_BIN2,fresh.[purchaseinvdt],fresh.[available_stk],fresh.[numberofdays])))
  THROW 51424,''The proposed superset changes protected source values.'',1;
 IF EXISTS(SELECT 1 FROM dbo.source_lineage l WHERE l.import_file_id=@previous_file_id AND (EXISTS(SELECT 1 FROM dbo.sales_lines x WHERE x.source_lineage_id=l.source_lineage_id) OR
      EXISTS(SELECT 1 FROM dbo.sales_invoice_controls x WHERE x.source_lineage_id=l.source_lineage_id) OR
      EXISTS(SELECT 1 FROM dbo.sales_tenders x WHERE x.source_lineage_id=l.source_lineage_id) OR
      EXISTS(SELECT 1 FROM dbo.stock_movements x WHERE x.source_lineage_id=l.source_lineage_id) OR
      EXISTS(SELECT 1 FROM dbo.stock_snapshots x WHERE x.source_lineage_id=l.source_lineage_id) OR
      EXISTS(SELECT 1 FROM dbo.sales_line_enrichments x WHERE x.source_lineage_id=l.source_lineage_id))
   AND NOT EXISTS(SELECT 1 FROM dbo.etp_import_content k WHERE k.import_file_id=@previous_file_id AND k.source_row_number=l.source_row_number))
  THROW 51424,''Canonical facts without a source manifest require Owner review.'',1;
 IF EXISTS(SELECT 1 FROM dbo.daily_reporting_days d JOIN dbo.import_files f ON f.import_file_id=@replacement_file_id
   WHERE d.store_code=f.store_code AND d.business_date BETWEEN COALESCE(f.period_start,f.business_date) AND COALESCE(f.period_end,f.business_date) AND d.status=''LOCKED'')
  THROW 51042,''Reopen the finalised business date before promoting a source.'',1;
 INSERT dbo.import_restatements(store_code,business_date,report_code,previous_import_file_id,replacement_import_file_id,requested_by,reason,impact_summary)
 VALUES(@store,@date,@report,@previous_file_id,@replacement_file_id,@actor,N''Later export includes the complete earlier export.'',N''Unchanged canonical facts retained and linked to the equivalent replacement source rows.'');
 SET @restatement=SCOPE_IDENTITY();
 -- Preserve values and row identities even if a direct caller commits immediately.
 DECLARE @lineage TABLE(previous_id bigint PRIMARY KEY,replacement_id bigint NOT NULL);
 MERGE dbo.source_lineage AS target USING(
   SELECT l.source_lineage_id previous_id,l.sheet_name,knew.source_row_number,l.source_record_type
   FROM dbo.source_lineage l JOIN dbo.etp_import_content kold
    ON kold.import_file_id=@previous_file_id AND kold.source_row_number=l.source_row_number
   JOIN dbo.etp_import_content knew ON knew.import_file_id=@replacement_file_id AND knew.content_key=kold.content_key
   WHERE l.import_file_id=@previous_file_id AND (EXISTS(SELECT 1 FROM dbo.sales_lines x WHERE x.source_lineage_id=l.source_lineage_id) OR
      EXISTS(SELECT 1 FROM dbo.sales_invoice_controls x WHERE x.source_lineage_id=l.source_lineage_id) OR
      EXISTS(SELECT 1 FROM dbo.sales_tenders x WHERE x.source_lineage_id=l.source_lineage_id) OR
      EXISTS(SELECT 1 FROM dbo.stock_movements x WHERE x.source_lineage_id=l.source_lineage_id) OR
      EXISTS(SELECT 1 FROM dbo.stock_snapshots x WHERE x.source_lineage_id=l.source_lineage_id) OR
      EXISTS(SELECT 1 FROM dbo.sales_line_enrichments x WHERE x.source_lineage_id=l.source_lineage_id))
 ) source ON 1=0
 WHEN NOT MATCHED THEN INSERT(import_file_id,sheet_name,source_row_number,source_record_type)
  VALUES(@replacement_file_id,source.sheet_name,source.source_row_number,source.source_record_type)
 OUTPUT source.previous_id,inserted.source_lineage_id INTO @lineage;
 UPDATE fact SET source_lineage_id=m.replacement_id FROM dbo.sales_lines fact JOIN @lineage m ON m.previous_id=fact.source_lineage_id;
 UPDATE fact SET source_lineage_id=m.replacement_id FROM dbo.sales_invoice_controls fact JOIN @lineage m ON m.previous_id=fact.source_lineage_id;
 UPDATE fact SET source_lineage_id=m.replacement_id FROM dbo.sales_tenders fact JOIN @lineage m ON m.previous_id=fact.source_lineage_id;
 UPDATE fact SET source_lineage_id=m.replacement_id FROM dbo.stock_movements fact JOIN @lineage m ON m.previous_id=fact.source_lineage_id;
 UPDATE fact SET source_lineage_id=m.replacement_id FROM dbo.stock_snapshots fact JOIN @lineage m ON m.previous_id=fact.source_lineage_id;
 UPDATE fact SET source_lineage_id=m.replacement_id FROM dbo.sales_line_enrichments fact JOIN @lineage m ON m.previous_id=fact.source_lineage_id;
 UPDATE dbo.import_files SET is_superseded=1,superseded_by_import_file_id=@replacement_file_id,superseded_utc=SYSUTCDATETIME(),
   superseded_by=@actor,restatement_reason=N''Later export includes the complete earlier export.'' WHERE import_file_id=@previous_file_id;
END');

GRANT EXECUTE ON dbo.promote_import_superset TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.complete_duplicate_import @file bigint,@previous bigint
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF @file=@previous OR NOT EXISTS(SELECT 1 FROM dbo.import_files f JOIN dbo.import_files old
   ON old.import_file_id=@previous AND old.is_superseded=0 AND old.data_truth_version=1
     AND old.store_code=f.store_code AND old.report_code=f.report_code
     AND COALESCE(old.period_start,old.business_date)<=COALESCE(f.period_end,f.business_date)
     AND COALESCE(old.period_end,old.business_date)>=COALESCE(f.period_start,f.business_date)
   WHERE f.import_file_id=@file)
  THROW 51425,''A matching current source is required for duplicate classification.'',1;
 IF EXISTS(SELECT 1 FROM dbo.etp_import_content incoming WHERE incoming.import_file_id=@file AND NOT EXISTS(
   SELECT 1 FROM dbo.etp_import_content existing JOIN dbo.import_files old ON old.import_file_id=existing.import_file_id
   JOIN dbo.import_files fresh ON fresh.import_file_id=@file
   WHERE old.import_file_id<>@file AND old.is_superseded=0 AND old.data_truth_version=1
     AND old.store_code=fresh.store_code AND old.report_code=fresh.report_code
     AND COALESCE(old.period_start,old.business_date)<=COALESCE(fresh.period_end,fresh.business_date)
     AND COALESCE(old.period_end,old.business_date)>=COALESCE(fresh.period_start,fresh.business_date)
     AND existing.content_key=incoming.content_key))
  THROW 51425,''The duplicate contains source content not already present.'',1;
 IF EXISTS(SELECT 1 FROM dbo.source_lineage s WHERE s.import_file_id=@file AND (
   EXISTS(SELECT 1 FROM dbo.sales_lines x WHERE x.source_lineage_id=s.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.sales_invoice_controls x WHERE x.source_lineage_id=s.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.sales_tenders x WHERE x.source_lineage_id=s.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.sales_line_enrichments x WHERE x.source_lineage_id=s.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.stock_movements x WHERE x.source_lineage_id=s.source_lineage_id) OR
   EXISTS(SELECT 1 FROM dbo.stock_snapshots x WHERE x.source_lineage_id=s.source_lineage_id)))
  THROW 51425,''Duplicate classification cannot discard canonical facts.'',1;
 UPDATE dbo.import_files SET is_superseded=1,superseded_by_import_file_id=@previous,superseded_utc=SYSUTCDATETIME(),
   superseded_by=SUSER_SNAME(),restatement_reason=N''Duplicate content; all rows already present.'' WHERE import_file_id=@file;
 UPDATE b SET status=''Completed'',source_row_count=(SELECT COUNT(*) FROM dbo.etp_import_content WHERE import_file_id=@file),completed_utc=SYSUTCDATETIME()
 FROM dbo.import_batches b JOIN dbo.import_files f ON f.import_batch_id=b.import_batch_id WHERE f.import_file_id=@file;
END');

GRANT EXECUTE ON dbo.complete_duplicate_import TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.persist_phase_one_enrichment
 @file bigint,@report varchar(10),@store varchar(30),@doc nvarchar(80),@date date,@product nvarchar(80),@type nvarchar(80),
 @qty decimal(19,4),@net decimal(19,4),@gross decimal(19,4),@cro nvarchar(80),@name nvarchar(200),
 @scheme decimal(19,4),@userDiscount decimal(19,4),@pre decimal(19,4),@other decimal(19,4),
 @activation nvarchar(500),@details nvarchar(500),@lineage bigint,@key varchar(80)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF @@TRANCOUNT=0 THROW 51422,''Import writes require an enclosing transaction.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.import_files f WITH(UPDLOCK,HOLDLOCK)
   JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
   WHERE f.import_file_id=@file AND f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Processing'')
  THROW 51422,''The source import is not open for writing.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_lineage l JOIN dbo.import_files f ON f.import_file_id=l.import_file_id
   WHERE l.source_lineage_id=@lineage AND l.import_file_id=@file AND f.report_code=@report AND f.store_code=@store
    AND @date BETWEEN COALESCE(f.period_start,f.business_date) AND COALESCE(f.period_end,f.business_date))
  THROW 51422,''The enrichment does not belong to this source import.'',1;
 DECLARE @matches int,@line bigint;
 SELECT @matches=COUNT(*),@line=CASE WHEN COUNT(*)=1 THEN MAX(l.sales_line_id) END
 FROM dbo.sales_lines l JOIN dbo.sales_invoices i ON i.sales_invoice_id=l.sales_invoice_id
 WHERE i.store_code=@store AND i.document_number=@doc AND i.transaction_date=@date AND l.product_code=@product;
 IF NOT EXISTS(SELECT 1 FROM dbo.sales_line_enrichments WITH(UPDLOCK,HOLDLOCK) WHERE enrichment_type=@report AND store_code=@store AND content_key=@key)
 BEGIN
  INSERT dbo.sales_line_enrichments(enrichment_type,store_code,transaction_date,document_number,product_code,
    source_transaction_type,source_quantity,source_net_value,source_gross_value,source_cro_number,staff_name,
    scheme_discount,user_discount,pre_discount,other_charges,activation_details,user_discount_details,matched_sales_line_id,match_status,source_lineage_id,content_key,invoice_year)
  VALUES(@report,@store,@date,@doc,@product,@type,@qty,@net,@gross,@cro,@name,@scheme,@userDiscount,@pre,@other,@activation,@details,@line,
    CASE @matches WHEN 0 THEN ''Missing'' WHEN 1 THEN ''Matched'' ELSE ''Ambiguous'' END,@lineage,@key,YEAR(@date)+CASE WHEN MONTH(@date)>=4 THEN 1 ELSE 0 END);
 END;
 IF NULLIF(LTRIM(RTRIM(@cro)),N'''') IS NOT NULL
  MERGE dbo.staff WITH(HOLDLOCK) AS target USING(SELECT @store store_code,@cro staff_code) source
  ON target.store_code=source.store_code AND target.staff_code=source.staff_code
  WHEN MATCHED AND target.staff_name=target.staff_code AND NULLIF(LTRIM(RTRIM(@name)),N'''') IS NOT NULL
   THEN UPDATE SET staff_name=@name,modified_utc=SYSUTCDATETIME(),modified_by=SUSER_SNAME()
  WHEN NOT MATCHED THEN INSERT(store_code,staff_code,staff_name,active) VALUES(@store,@cro,COALESCE(NULLIF(LTRIM(RTRIM(@name)),N''''),@cro),1);
END');

GRANT EXECUTE ON dbo.persist_phase_one_enrichment TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.save_evening_brand
 @requested int,@store varchar(30),@label nvarchar(100),@order int,@codes nvarchar(max)
AS BEGIN
 SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF NULLIF(LTRIM(RTRIM(@store)),'''') IS NULL OR NULLIF(LTRIM(RTRIM(@label)),'''') IS NULL
   OR @label IN(N''Other / unmapped'',N''VOL'',N''VALUE'',N''AUPT'',N''AVPT'',N''RETAIL WALKIN'',N''INVOICE'',N''CONVERSION %'',N''WCC WALKIN'',N''WCC SALES'',N''WDC BILLS'')
  THROW 51426,''Enter a store and a non-reserved brand label.'',1;
 IF ISJSON(@codes)<>1 OR LEFT(LTRIM(@codes),1)<>N''['' OR EXISTS(SELECT 1 FROM OPENJSON(@codes) WHERE type<>1 OR LEN(value)>100 OR LEN(LTRIM(RTRIM(value)))=0)
  THROW 51426,''Enter valid source brand codes.'',1;
 BEGIN TRY
  BEGIN TRANSACTION;
  DECLARE @id int=@requested;
  IF @id=0 BEGIN
   INSERT dbo.brand_rows(store_code,row_label,sort_order,modified_by) VALUES(@store,@label,@order,SUSER_SNAME());
   SET @id=SCOPE_IDENTITY();
  END ELSE BEGIN
   UPDATE dbo.brand_rows SET row_label=@label,sort_order=@order,modified_by=SUSER_SNAME(),modified_utc=SYSUTCDATETIME()
   WHERE brand_row_id=@id AND store_code=@store;
   IF @@ROWCOUNT<>1 THROW 51401,''The brand row no longer exists in this store.'',1;
  END;
  IF EXISTS(SELECT 1 FROM OPENJSON(@codes) j JOIN dbo.brand_row_codes b WITH(UPDLOCK,HOLDLOCK)
    ON b.store_code=@store AND b.source_brand=j.value WHERE b.brand_row_id<>@id)
   THROW 51402,''This source brand is already assigned. Remove it from the other row first.'',1;
  DELETE dbo.brand_row_codes WHERE brand_row_id=@id;
  INSERT dbo.brand_row_codes(store_code,source_brand,brand_row_id) SELECT DISTINCT @store,CONVERT(nvarchar(100),value),@id FROM OPENJSON(@codes);
  EXEC dbo.record_operational_audit ''MasterDataChange'',''Succeeded'',N''Evening brand mapping changed'',N''database'';
  COMMIT TRANSACTION;
 END TRY
 BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  THROW;
 END CATCH;
END');

DENY INSERT,UPDATE,DELETE ON dbo.brand_rows TO etp_store_manager,etp_viewer;
DENY INSERT,UPDATE,DELETE ON dbo.brand_row_codes TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.save_evening_brand TO etp_store_manager,etp_owner;

EXEC(N'CREATE OR ALTER PROCEDURE dbo.link_document_to_import @document bigint,@hash char(64),@report varchar(30),@store varchar(30),@date date
AS BEGIN

            SET NOCOUNT ON; SET XACT_ABORT ON;
 IF COALESCE(IS_ROLEMEMBER(''etp_store_manager''),0)<>1 AND COALESCE(IS_ROLEMEMBER(''etp_owner''),0)<>1
    AND COALESCE(IS_SRVROLEMEMBER(''sysadmin''),0)<>1
  THROW 51420,''Owner or Store Manager permission is required.'',1;
 IF NOT EXISTS(SELECT 1 FROM dbo.source_documents WHERE source_document_id=@document AND source_sha256=@hash)
  THROW 51427,''The retained document hash must match its source import.'',1;

            BEGIN TRANSACTION;
            INSERT dbo.source_document_import_links(source_document_id,import_file_id)
            SELECT @document,f.import_file_id FROM dbo.import_files f
            WHERE f.source_sha256=@hash AND f.data_truth_version=1 AND f.report_code=@report
              AND (@store IS NULL OR f.store_code=@store) AND (@date IS NULL OR f.period_end=@date)
              AND NOT EXISTS(SELECT 1 FROM dbo.source_document_import_links l WITH(UPDLOCK,HOLDLOCK)
                  WHERE l.source_document_id=@document AND l.import_file_id=f.import_file_id);
            UPDATE d SET import_file_id=f.import_file_id,period_start=f.period_start,period_end=f.period_end,report_code=@report,store_code=COALESCE(@store,f.store_code),business_date=COALESCE(@date,f.business_date),
              lifecycle_status=''IMPORTED'',last_status_by=SUSER_SNAME(),last_status_utc=SYSUTCDATETIME(),safe_message=N''ETP source validated and imported into canonical data.''
            FROM dbo.source_documents d CROSS APPLY(SELECT TOP(1) f.* FROM dbo.import_files f
              WHERE f.source_sha256=@hash AND f.data_truth_version=1 AND f.report_code=@report
                AND (@store IS NULL OR f.store_code=@store) AND (@date IS NULL OR f.period_end=@date)
              ORDER BY f.import_file_id DESC) f WHERE d.source_document_id=@document;
            COMMIT TRANSACTION;
            
END');

DENY INSERT,UPDATE,DELETE ON dbo.source_document_import_links TO etp_store_manager,etp_viewer;
GRANT EXECUTE ON dbo.link_document_to_import TO etp_store_manager,etp_owner;

-- A legacy extraction object, if retained by an upgrade, receives no staff write surface.
IF OBJECT_ID(N'dbo.document_extractions',N'U') IS NOT NULL
 EXEC(N'REVOKE INSERT,UPDATE,DELETE ON dbo.document_extractions FROM etp_store_manager,etp_viewer;');
