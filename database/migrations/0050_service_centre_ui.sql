-- Service Centre UI (1.10.0, decision 25, 10 Oct 2026): the job model over the 0048 Service landing tables and read views.
-- Design: docs/roadmap/SERVICE-CENTRE-UI-DESIGN-REVIEW-2026-10-10.md (sections 3, 4 and 6); lane sql (U0 + U1).
--
-- Numbering: 0049 is reserved for the Tally cost-centre migration from PR #3. Until it ships beside this file, the
-- migration contiguity test (MigrationTests) fails on this branch; that is expected, as 0048 noted for 1.9.4.
--
-- The migration runner applies this entire script in one transaction, so it has no BEGIN or COMMIT.
-- Every statement is idempotent: every view is CREATE OR ALTER through EXEC(N'...'), followed by its grants.
-- Nothing here writes data or adds a table, procedure, trigger or index; 0048 is never edited (its text is checksummed).
-- Every view selects columns by name; no view exposes a phone, e-mail or address column (design 1.8). The customer name
-- appears only where the read contract has CustomerName (the Job history header, as the 0048 views do).
-- Each section has one owner and changes only what lies between its own begin and end markers.
SET XACT_ABORT ON;

-- >>> D_SERVICE_UI_READ begin
-- Owner: lane sql. Views over the 0048 C_SERVICE_READ views and the landing tables; the 0048 read rules are inherited:
-- JobList families contribute the latest reading that holds the job (v_service_job_readings), state lists S009/S010
-- contribute only their is_latest reading (v_service_readings), DateLog families contribute the winning reading per
-- business date (v_service_datelog_readings). A raw window therefore never hides consolidated history and the import
-- order never matters. The job key is the exported job order number, trimmed, never padded or re-formatted (Q1).

-- 1. The line rows of the ten status views (S014-S018, S031-S035) with the lifecycle columns the job model needs:
-- the family's own dates (indent, SRN issued/returned/received, repaired, delivered, RWR, DC, RA), the DC/RA/indent
-- document numbers, guarantee and customer type. Model is the variant number and product category the cluster id,
-- as v_service_status_view_rows. Columns by name; no contact column.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_status_view_facts AS
SELECT ''S014'' report_code,import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N'''') job_order_number,jodate job_date,edd,
  CONVERT(nvarchar(200),brandname) brand,CONVERT(nvarchar(200),variantnumber) model,CONVERT(nvarchar(200),clusterid) product_category,CONVERT(nvarchar(200),customername) customer_name,
  CONVERT(nvarchar(60),guarantee) guarantee,CONVERT(nvarchar(60),customertype) customer_type,indentdate indent_date,CONVERT(nvarchar(100),indentnumber) indent_number,
  srnissueddate srn_issued_date,srnreturndate srn_return_date,srnreceiveddate srn_received_date,CONVERT(nvarchar(60),tostorecode) to_store_code,
  jorepairdate repair_date,deliverydate delivery_date,normalrwrdate rwr_date,CONVERT(nvarchar(200),rwr_reason) rwr_reason,
  wdcdate wdc_date,CONVERT(nvarchar(100),wdcnumber) wdc_number,wradate wra_date,CONVERT(nvarchar(100),radcnumber) radc_number,
  CONVERT(nvarchar(80),repairstatus) repair_status,sparevalue spare_value,labourcharge labour_charge
FROM dbo.etp_landing_s014
UNION ALL SELECT ''S015'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CONVERT(nvarchar(200),customername),
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s015
UNION ALL SELECT ''S016'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CONVERT(nvarchar(200),customername),
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s016
UNION ALL SELECT ''S017'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CONVERT(nvarchar(200),customername),
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s017
UNION ALL SELECT ''S018'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CONVERT(nvarchar(200),customername),
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s018
UNION ALL SELECT ''S031'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CONVERT(nvarchar(200),customername),
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s031
UNION ALL SELECT ''S032'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CONVERT(nvarchar(200),customername),
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s032
UNION ALL SELECT ''S033'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CONVERT(nvarchar(200),customername),
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s033
UNION ALL SELECT ''S034'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CONVERT(nvarchar(200),customername),
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s034
UNION ALL SELECT ''S035'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CONVERT(nvarchar(200),customername),
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s035');

-- 2. Every claim to Titan in one shape (design 3.5, Q12): claim_type GPRC (S023 + S041, through v_service_gprc_claims,
-- S041 wins per document), MB (S024), WDC (S025 old header + S039 new header) and WRA (S026 + S040). Each family's rows
-- are first chosen by its own DateLog rule (reading_rank 1); then, per claim type, the new-header family wins per
-- document number: an old-header line is read only when no new-header line has the same document number, and an
-- old-header line without a document number only on a date that has no new-header line (the 1 Jul 2026 join of
-- S025/S039 and S026/S040 is therefore counted once). ucp_value is ucpvalue (S023), ucp_value (S041) or
-- ucpamount / ucp_amount (the others). No settlement state exists in any export (Q9 = A: raised only).
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_claims AS
WITH mb AS (
  SELECT r.import_file_id,w.snapshot_date,r.transdate business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.documentnum))),N'''') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.jonumber))),N'''') job_order_number,CONVERT(nvarchar(100),r.itemid) item_id,r.quantity,
    CONVERT(nvarchar(100),r.accountnum) account_number,r.netamountinctax net_amount_inc_tax,r.ucpamount ucp_value
  FROM dbo.etp_landing_s024 r JOIN dbo.v_service_datelog_readings w ON w.report_code=''S024'' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transdate
), wdc_new AS (
  SELECT r.import_file_id,w.snapshot_date,r.transaction_date business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.document_number))),N'''') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.job_order_number))),N'''') job_order_number,CONVERT(nvarchar(100),r.item_id) item_id,r.quantity,
    CONVERT(nvarchar(100),r.account_number) account_number,r.net_amount_inc_tax,r.ucp_amount ucp_value
  FROM dbo.etp_landing_s039 r JOIN dbo.v_service_datelog_readings w ON w.report_code=''S039'' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transaction_date
), wdc_old AS (
  SELECT r.import_file_id,w.snapshot_date,r.transdate business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.documentnum))),N'''') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.jonumber))),N'''') job_order_number,CONVERT(nvarchar(100),r.itemid) item_id,r.quantity,
    CONVERT(nvarchar(100),r.accountnum) account_number,r.netamountinctax net_amount_inc_tax,r.ucpamount ucp_value
  FROM dbo.etp_landing_s025 r JOIN dbo.v_service_datelog_readings w ON w.report_code=''S025'' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transdate
), wra_new AS (
  SELECT r.import_file_id,w.snapshot_date,r.transaction_date business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.document_number))),N'''') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.job_order_number))),N'''') job_order_number,CONVERT(nvarchar(100),r.item_id) item_id,r.quantity,
    CONVERT(nvarchar(100),r.account_number) account_number,r.net_amount_inc_tax,r.ucp_amount ucp_value
  FROM dbo.etp_landing_s040 r JOIN dbo.v_service_datelog_readings w ON w.report_code=''S040'' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transaction_date
), wra_old AS (
  SELECT r.import_file_id,w.snapshot_date,r.transdate business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.documentnum))),N'''') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.jonumber))),N'''') job_order_number,CONVERT(nvarchar(100),r.itemid) item_id,r.quantity,
    CONVERT(nvarchar(100),r.accountnum) account_number,r.netamountinctax net_amount_inc_tax,r.ucpamount ucp_value
  FROM dbo.etp_landing_s026 r JOIN dbo.v_service_datelog_readings w ON w.report_code=''S026'' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transdate
)
SELECT ''GPRC'' claim_type,g.report_code,g.business_date,g.document_number,g.job_order_number,g.item_id,g.quantity,g.account_number,
  g.net_amount_inc_tax,g.ucp_value,g.snapshot_date,g.import_file_id
FROM dbo.v_service_gprc_claims g
UNION ALL
SELECT ''MB'',''S024'',business_date,document_number,job_order_number,item_id,quantity,account_number,net_amount_inc_tax,ucp_value,snapshot_date,import_file_id FROM mb
UNION ALL
SELECT ''WDC'',''S039'',business_date,document_number,job_order_number,item_id,quantity,account_number,net_amount_inc_tax,ucp_value,snapshot_date,import_file_id FROM wdc_new
UNION ALL
SELECT ''WDC'',''S025'',h.business_date,h.document_number,h.job_order_number,h.item_id,h.quantity,h.account_number,h.net_amount_inc_tax,h.ucp_value,h.snapshot_date,h.import_file_id
FROM wdc_old h
WHERE NOT EXISTS(SELECT 1 FROM wdc_new c WHERE c.document_number=h.document_number)
  AND (h.document_number IS NOT NULL OR NOT EXISTS(SELECT 1 FROM wdc_new c WHERE c.business_date=h.business_date))
UNION ALL
SELECT ''WRA'',''S040'',business_date,document_number,job_order_number,item_id,quantity,account_number,net_amount_inc_tax,ucp_value,snapshot_date,import_file_id FROM wra_new
UNION ALL
SELECT ''WRA'',''S026'',h.business_date,h.document_number,h.job_order_number,h.item_id,h.quantity,h.account_number,h.net_amount_inc_tax,h.ucp_value,h.snapshot_date,h.import_file_id
FROM wra_old h
WHERE NOT EXISTS(SELECT 1 FROM wra_new c WHERE c.document_number=h.document_number)
  AND (h.document_number IS NOT NULL OR NOT EXISTS(SELECT 1 FROM wra_new c WHERE c.business_date=h.business_date))');

-- 3. One row per Service job (design 4.1-4.4). The universe of keys is every job column of every family: the JobList
-- and state-list families, S029 (srfno), S003, S019, S022 and the claim logs. Per job:
--   booking_date = the least of S002/S036/S037 created_date, status-view jodate, S009/S010 jodate, S011 joborder_date
--     and S029 srf_date (they agree where both exist; the least guards against a later re-export);
--   jo_type = S002 jotype_booking_quickbilling as exported (a job without an S002 row reads Booking, Q2);
--   stage = the first rule that fires (4.2, Q3, Q5): DELIVERED (S018, or S029 status Delivered with a delivered date),
--     RWR (S017 or S029 RWR), DC_ISSUED (S014), RA_ISSUED (S016), IN_TRANSIT_BACK (latest S010 holds it at a store other
--     than AW330), READY_FOR_DELIVERY (latest S010 at AW330, or S031/S034), then the latest S009 decides when it holds
--     the job (jostatus SRN_* = SRN_OUT, Indent_Raised or an indent = INDENT_RAISED, else ON_BENCH; the fresher list wins
--     over the cumulative status views), SRN_OUT (an open S011 SRN by the Q5 rule, or S033/S035), INDENT_RAISED (S015),
--     ON_BENCH (S032), BOOKED otherwise;
--   stage_date = the stage's own date; pending_at = S010 pendingstore, S011 to_store, S009 pendingstore, else AW330;
--   claim_raised = a WDC document for a DC job, a WRA document for an RA job, in any live claim reading;
--   is_open = not DELIVERED/RWR and not a claimed DC/RA job (closed by claim, Q3);
--   tat_days = booking to delivered (or to RWR) and tat_repair_days = booking to repaired, closed jobs only;
--   age_days = booking to as_at and days_in_stage = stage_date to as_at, open jobs only, where as_at is the latest
--     Service snapshot date; is_overdue = EDD passed (the per-stage limits without an EDD are applied in C#, Q4);
--   spare_value and labour_charge = the line rows of the current status view (latest reading, then lifecycle rank, as
--     v_service_job_status_current); revenue_* = the winning S003 lines of the job (DateLog rule; Q7);
--   last_reading_date = the latest snapshot date of any list that holds the job.
-- S036/S037 are job lists whose created_date is the booking date (Q10): they contribute keys and the booking date only.
-- Performance (SD-11): the 0048 reading views recompute the reading windows on every reference, and a first version that
-- named them a dozen times took minutes on live. So this view states the live-reading filter itself (the one of
-- v_service_reading_windows: not superseded, truth version 1, Completed batch, an importable Service family, a snapshot
-- date), and applies the three read rules on it directly: JobList (the latest reading holding the job), StateSnapshot
-- (the latest S009/S010 reading, one with rows beating an empty one on the same date) and DateLog for the S003 and S029
-- lines (per business date, the covering reading with the greatest snapshot date, then import file; a reading covers
-- [least row date, greater of snapshot date and greatest row date], as v_service_datelog_readings).
-- ServiceJobModelSqlTests pins that it agrees with the 0048 views.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_job AS
WITH live AS (
  SELECT f.import_file_id,f.report_code,COALESCE(f.period_end,f.business_date) snapshot_date
  FROM dbo.import_files f JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
  WHERE f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Completed''
    AND f.report_code IN(''S002'',''S003'',''S004'',''S006'',''S007'',''S008'',''S009'',''S010'',''S011'',''S012'',''S013'',''S014'',''S015'',''S016'',''S017'',''S018'',''S019'',''S020'',''S021'',''S022'',''S023'',''S024'',''S025'',''S026'',''S029'',''S030'',''S031'',''S032'',''S033'',''S034'',''S035'',''S036'',''S037'',''S039'',''S040'',''S041'')
    AND COALESCE(f.period_end,f.business_date) IS NOT NULL
), job_rows AS (
  SELECT report_code,import_file_id,job_order_number FROM dbo.v_service_status_view_facts
  UNION ALL SELECT ''S002'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N'''') FROM dbo.etp_landing_s002
  UNION ALL SELECT ''S009'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N'''') FROM dbo.etp_landing_s009
  UNION ALL SELECT ''S010'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N'''') FROM dbo.etp_landing_s010
  UNION ALL SELECT ''S011'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N'''') FROM dbo.etp_landing_s011
  UNION ALL SELECT ''S012'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N'''') FROM dbo.etp_landing_s012
  UNION ALL SELECT ''S020'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N'''') FROM dbo.etp_landing_s020
  UNION ALL SELECT ''S021'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N'''') FROM dbo.etp_landing_s021
  UNION ALL SELECT ''S030'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_number))),N'''') FROM dbo.etp_landing_s030
  UNION ALL SELECT ''S036'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N'''') FROM dbo.etp_landing_s036
  UNION ALL SELECT ''S037'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N'''') FROM dbo.etp_landing_s037
), held AS (
  SELECT x.job_order_number,x.report_code,x.import_file_id,x.snapshot_date FROM (
    SELECT j.job_order_number,j.report_code,j.import_file_id,l.snapshot_date,
      ROW_NUMBER() OVER(PARTITION BY j.job_order_number,j.report_code ORDER BY l.snapshot_date DESC,j.import_file_id DESC) rn
    FROM (SELECT DISTINCT report_code,import_file_id,job_order_number FROM job_rows WHERE job_order_number IS NOT NULL) j
    JOIN live l ON l.import_file_id=j.import_file_id AND l.report_code=j.report_code
  ) x WHERE x.rn=1
), state_latest AS (
  SELECT x.report_code,x.import_file_id,x.snapshot_date FROM (
    SELECT l.report_code,l.import_file_id,l.snapshot_date,
      ROW_NUMBER() OVER(PARTITION BY l.report_code ORDER BY l.snapshot_date DESC,
        CASE WHEN EXISTS(SELECT 1 FROM dbo.etp_landing_s009 s WHERE s.import_file_id=l.import_file_id)
               OR EXISTS(SELECT 1 FROM dbo.etp_landing_s010 s WHERE s.import_file_id=l.import_file_id) THEN 1 ELSE 0 END DESC,
        l.import_file_id DESC) rn
    FROM live l WHERE l.report_code IN(''S009'',''S010'')
  ) x WHERE x.rn=1
), facts AS (
  SELECT f.job_order_number,f.report_code,f.import_file_id,f.job_date,f.edd,f.brand,f.model,f.product_category,f.customer_name,f.guarantee,f.customer_type,
    f.indent_date,f.srn_issued_date,f.to_store_code,f.repair_date,f.delivery_date,f.rwr_date,f.rwr_reason,f.wdc_date,f.wdc_number,f.wra_date,f.radc_number,
    f.spare_value,f.labour_charge,h.snapshot_date,fam.lifecycle_rank
  FROM dbo.v_service_status_view_facts f
  JOIN held h ON h.job_order_number=f.job_order_number AND h.report_code=f.report_code AND h.import_file_id=f.import_file_id
  JOIN dbo.v_service_families fam ON fam.report_code=f.report_code
), sv AS (
  SELECT f.job_order_number,MIN(f.job_date) job_date,MAX(f.edd) edd,MAX(f.brand) brand,MAX(f.model) model,MAX(f.product_category) product_category,
    MAX(f.customer_name) customer_name,MAX(f.guarantee) guarantee,MAX(f.customer_type) customer_type,
    MAX(CASE WHEN f.report_code=''S018'' THEN f.delivery_date END) delivery_date,
    MAX(CASE WHEN f.report_code=''S018'' THEN f.repair_date END) delivered_repair_date,
    MAX(CASE WHEN f.report_code=''S017'' THEN f.rwr_date END) rwr_date,
    MAX(CASE WHEN f.report_code=''S017'' THEN f.rwr_reason END) rwr_reason,
    MAX(CASE WHEN f.report_code=''S014'' THEN f.wdc_date END) wdc_date,MAX(CASE WHEN f.report_code=''S014'' THEN f.wdc_number END) wdc_number,
    MAX(CASE WHEN f.report_code=''S016'' THEN f.wra_date END) wra_date,MAX(CASE WHEN f.report_code=''S016'' THEN f.radc_number END) radc_number,
    MAX(CASE WHEN f.report_code IN(''S031'',''S034'') THEN f.repair_date END) pd_repair_date,
    MAX(CASE WHEN f.report_code=''S033'' THEN f.srn_issued_date END) srn_issued_date,
    MAX(CASE WHEN f.report_code IN(''S033'',''S035'') THEN f.to_store_code END) srn_to_store_code,
    MAX(CASE WHEN f.report_code=''S015'' THEN f.indent_date END) indent_date,
    MAX(CASE WHEN f.report_code=''S018'' THEN 1 ELSE 0 END) in_delivered,MAX(CASE WHEN f.report_code=''S017'' THEN 1 ELSE 0 END) in_rwr,
    MAX(CASE WHEN f.report_code=''S014'' THEN 1 ELSE 0 END) in_dc,MAX(CASE WHEN f.report_code=''S016'' THEN 1 ELSE 0 END) in_ra,
    MAX(CASE WHEN f.report_code IN(''S031'',''S034'') THEN 1 ELSE 0 END) in_pd,MAX(CASE WHEN f.report_code IN(''S033'',''S035'') THEN 1 ELSE 0 END) in_srn,
    MAX(CASE WHEN f.report_code=''S015'' THEN 1 ELSE 0 END) in_ir,MAX(CASE WHEN f.report_code=''S032'' THEN 1 ELSE 0 END) in_pr,
    MAX(f.lifecycle_rank) top_rank
  FROM facts f GROUP BY f.job_order_number
), view_money AS (
  SELECT x.job_order_number,x.spare_value,x.labour_charge FROM (
    SELECT f.job_order_number,SUM(f.spare_value) spare_value,SUM(f.labour_charge) labour_charge,
      ROW_NUMBER() OVER(PARTITION BY f.job_order_number ORDER BY MAX(f.snapshot_date) DESC,MAX(f.lifecycle_rank) DESC,f.import_file_id DESC) view_rank
    FROM facts f GROUP BY f.job_order_number,f.report_code,f.import_file_id
  ) x WHERE x.view_rank=1
), lists AS (
  SELECT h.job_order_number,
    MIN(CASE WHEN x.report_code=''S002'' THEN x.created_date END) s002_created,MIN(CASE WHEN x.report_code<>''S002'' THEN x.created_date END) list_created,
    MAX(CASE WHEN x.report_code=''S002'' THEN x.jo_type END) jo_type,
    COALESCE(MAX(CASE WHEN x.report_code=''S002'' THEN x.exported_status END),MAX(x.exported_status)) exported_status,
    MAX(x.brand) brand,MAX(x.product_category) product_category,MAX(x.guarantee) guarantee,MAX(x.customer_type) customer_type
  FROM held h JOIN (
    SELECT ''S002'' report_code,import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N'''') job_order_number,created_date,CONVERT(nvarchar(40),jotype_booking_quickbilling) jo_type,
      CONVERT(nvarchar(60),current_status) exported_status,CONVERT(nvarchar(200),brand) brand,CONVERT(nvarchar(200),clusterid) product_category,
      CONVERT(nvarchar(60),guarantee) guarantee,CONVERT(nvarchar(60),customer_type) customer_type
    FROM dbo.etp_landing_s002
    UNION ALL SELECT ''S036'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N''''),created_date,NULL,CONVERT(nvarchar(60),current_status),CONVERT(nvarchar(200),brand),
      CONVERT(nvarchar(200),clusterid),CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customer_type)
    FROM dbo.etp_landing_s036
    UNION ALL SELECT ''S037'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N''''),created_date,NULL,CONVERT(nvarchar(60),current_status),CONVERT(nvarchar(200),brand),
      CONVERT(nvarchar(200),clusterid),CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customer_type)
    FROM dbo.etp_landing_s037
  ) x ON x.import_file_id=h.import_file_id AND x.report_code=h.report_code AND x.job_order_number=h.job_order_number
  GROUP BY h.job_order_number
), state AS (
  SELECT x.job_order_number,
    MAX(CASE WHEN x.report_code=''S009'' THEN 1 ELSE 0 END) in_s009,MAX(CASE WHEN x.report_code=''S010'' THEN 1 ELSE 0 END) in_s010,
    MIN(CASE WHEN x.report_code=''S009'' THEN x.job_date END) s009_job_date,MIN(CASE WHEN x.report_code=''S010'' THEN x.job_date END) s010_job_date,
    MAX(CASE WHEN x.report_code=''S009'' THEN x.edd END) s009_edd,
    MAX(CASE WHEN x.report_code=''S009'' THEN x.jo_status END) s009_status,
    MAX(CASE WHEN x.report_code=''S009'' THEN x.pending_store END) s009_store,MAX(CASE WHEN x.report_code=''S010'' THEN x.pending_store END) s010_store,
    MAX(CASE WHEN x.report_code=''S009'' THEN x.indent_date END) s009_indent_date,MAX(CASE WHEN x.report_code=''S009'' THEN x.indent_id END) s009_indent_id,
    MAX(CASE WHEN x.report_code=''S010'' THEN x.repair_date END) s010_repair_date,
    COALESCE(MAX(CASE WHEN x.report_code=''S009'' THEN x.spare_required END),MAX(x.spare_required)) spare_required,
    MAX(x.brand) brand,MAX(x.model) model,MAX(x.product_category) product_category,MAX(x.customer_name) customer_name
  FROM state_latest l JOIN (
    SELECT ''S009'' report_code,import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N'''') job_order_number,jodate job_date,edd,CONVERT(nvarchar(60),jostatus) jo_status,
      CONVERT(nvarchar(60),pendingstore) pending_store,indentdate indent_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),indentid))),N'''') indent_id,CONVERT(date,NULL) repair_date,
      NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(200),sparerequired))),N'''') spare_required,CONVERT(nvarchar(200),brand) brand,
      CONVERT(nvarchar(200),variantnumber) model,CONVERT(nvarchar(200),cluster) product_category,CONVERT(nvarchar(200),customername) customer_name
    FROM dbo.etp_landing_s009
    UNION ALL SELECT ''S010'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''''),jodate,CONVERT(date,NULL),CONVERT(nvarchar(60),jostatus),CONVERT(nvarchar(60),pendingstore),
      indentdate,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),indentid))),N''''),repairdate,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(200),sparerequired))),N''''),CONVERT(nvarchar(200),brand),
      CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),cluster),CONVERT(nvarchar(200),customername)
    FROM dbo.etp_landing_s010
  ) x ON x.import_file_id=l.import_file_id AND x.report_code=l.report_code
  WHERE x.job_order_number IS NOT NULL
  GROUP BY x.job_order_number
), srn AS (
  SELECT h.job_order_number,MAX(s.srn_date) srn_date,MIN(s.joborder_date) job_date,MAX(CONVERT(nvarchar(60),s.to_store)) to_store,
    MAX(CONVERT(nvarchar(200),s.brand)) brand,MAX(CONVERT(nvarchar(200),s.customer_name)) customer_name,
    MAX(CASE WHEN s.srn_received_date IS NULL AND s.repaired_date IS NULL AND COALESCE(CONVERT(nvarchar(80),s.to_status),N'''') NOT LIKE ''%Received%'' THEN 1 ELSE 0 END) srn_open
  FROM held h JOIN dbo.etp_landing_s011 s ON s.import_file_id=h.import_file_id AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.joborder_number))),N'''')=h.job_order_number
  WHERE h.report_code=''S011''
  GROUP BY h.job_order_number
), dated AS (
  SELECT ''S003'' report_code,import_file_id,trans_date business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N'''') job_order_number,labour_charge,spare_charge,
    netamount_incl_tax net_incl_tax,CONVERT(nvarchar(100),documentnum) document_number,CONVERT(date,NULL) srf_date,CONVERT(date,NULL) repair_date,
    CONVERT(date,NULL) delivered_date,CONVERT(nvarchar(60),NULL) status,CONVERT(nvarchar(200),NULL) brand
  FROM dbo.etp_landing_s003
  UNION ALL SELECT ''S029'',import_file_id,repair_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),srfno))),N''''),NULL,NULL,NULL,NULL,srf_date,repair_date,delivered_date,
    CONVERT(nvarchar(60),status),CONVERT(nvarchar(200),brand_name)
  FROM dbo.etp_landing_s029
), dated_files AS (
  SELECT d.report_code,d.import_file_id,l.snapshot_date,MIN(d.business_date) window_from,
    CASE WHEN MAX(d.business_date)>l.snapshot_date THEN MAX(d.business_date) ELSE l.snapshot_date END window_to
  FROM dated d JOIN live l ON l.import_file_id=d.import_file_id AND l.report_code=d.report_code
  WHERE d.business_date IS NOT NULL
  GROUP BY d.report_code,d.import_file_id,l.snapshot_date
), won AS (
  SELECT d.job_order_number,
    SUM(CASE WHEN d.report_code=''S003'' THEN d.labour_charge END) revenue_labour_charge,SUM(CASE WHEN d.report_code=''S003'' THEN d.spare_charge END) revenue_spare_charge,
    SUM(CASE WHEN d.report_code=''S003'' THEN d.net_incl_tax END) revenue_net_incl_tax,COUNT(DISTINCT d.document_number) revenue_documents,
    MIN(d.srf_date) srf_date,MAX(d.repair_date) repair_date,MAX(d.delivered_date) delivered_date,
    MAX(CASE WHEN d.status=''Delivered'' AND d.delivered_date IS NOT NULL THEN 1 ELSE 0 END) is_delivered,
    MAX(CASE WHEN d.status IN(''RWR'',''Returned_Without_Repair'') THEN 1 ELSE 0 END) is_rwr,MAX(d.brand) brand
  FROM dated d JOIN dated_files f ON f.import_file_id=d.import_file_id AND f.report_code=d.report_code
  WHERE d.job_order_number IS NOT NULL
    AND NOT EXISTS(SELECT 1 FROM dated_files g WHERE g.report_code=d.report_code AND d.business_date BETWEEN g.window_from AND g.window_to
      AND (g.snapshot_date>f.snapshot_date OR (g.snapshot_date=f.snapshot_date AND g.import_file_id>f.import_file_id)))
  GROUP BY d.job_order_number
), other_rows AS (
  SELECT ''S003'' report_code,import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N'''') job_order_number FROM dbo.etp_landing_s003
  UNION ALL SELECT ''S029'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),srfno))),N'''') FROM dbo.etp_landing_s029
  UNION ALL SELECT ''S019'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jo_number))),N'''') FROM dbo.etp_landing_s019
  UNION ALL SELECT ''S022'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N'''') FROM dbo.etp_landing_s022
  UNION ALL SELECT ''S023'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N'''') FROM dbo.etp_landing_s023
  UNION ALL SELECT ''S024'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N'''') FROM dbo.etp_landing_s024
  UNION ALL SELECT ''S025'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N'''') FROM dbo.etp_landing_s025
  UNION ALL SELECT ''S026'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N'''') FROM dbo.etp_landing_s026
  UNION ALL SELECT ''S039'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_number))),N'''') FROM dbo.etp_landing_s039
  UNION ALL SELECT ''S040'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_number))),N'''') FROM dbo.etp_landing_s040
  UNION ALL SELECT ''S041'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_number))),N'''') FROM dbo.etp_landing_s041
), other_jobs AS (
  SELECT o.job_order_number,MAX(CASE WHEN o.report_code IN(''S025'',''S039'') THEN 1 ELSE 0 END) wdc_claimed,
    MAX(CASE WHEN o.report_code IN(''S026'',''S040'') THEN 1 ELSE 0 END) wra_claimed
  FROM other_rows o JOIN live l ON l.import_file_id=o.import_file_id AND l.report_code=o.report_code
  WHERE o.job_order_number IS NOT NULL
  GROUP BY o.job_order_number
), facts_by_job AS (
  SELECT job_order_number,CONVERT(date,snapshot_date) last_reading_date,CONVERT(date,NULL) sv_job_date,CONVERT(date,NULL) sv_edd,CONVERT(nvarchar(200),NULL) sv_brand,CONVERT(nvarchar(200),NULL) sv_model,CONVERT(nvarchar(200),NULL) sv_product_category,CONVERT(nvarchar(200),NULL) sv_customer_name,CONVERT(nvarchar(200),NULL) sv_guarantee,CONVERT(nvarchar(200),NULL) sv_customer_type,CONVERT(date,NULL) sv_delivery_date,CONVERT(date,NULL) sv_delivered_repair_date,CONVERT(date,NULL) sv_rwr_date,CONVERT(nvarchar(200),NULL) sv_rwr_reason,CONVERT(date,NULL) sv_wdc_date,CONVERT(nvarchar(200),NULL) sv_wdc_number,CONVERT(date,NULL) sv_wra_date,CONVERT(nvarchar(200),NULL) sv_radc_number,CONVERT(date,NULL) sv_pd_repair_date,CONVERT(date,NULL) sv_srn_issued_date,CONVERT(nvarchar(200),NULL) sv_srn_to_store_code,CONVERT(date,NULL) sv_indent_date,CONVERT(int,NULL) sv_in_delivered,CONVERT(int,NULL) sv_in_rwr,CONVERT(int,NULL) sv_in_dc,CONVERT(int,NULL) sv_in_ra,CONVERT(int,NULL) sv_in_pd,CONVERT(int,NULL) sv_in_srn,CONVERT(int,NULL) sv_in_ir,CONVERT(int,NULL) sv_in_pr,CONVERT(decimal(19,4),NULL) vm_spare_value,CONVERT(decimal(19,4),NULL) vm_labour_charge,CONVERT(date,NULL) ls_s002_created,CONVERT(date,NULL) ls_list_created,CONVERT(nvarchar(200),NULL) ls_jo_type,CONVERT(nvarchar(200),NULL) ls_exported_status,CONVERT(nvarchar(200),NULL) ls_brand,CONVERT(nvarchar(200),NULL) ls_product_category,CONVERT(nvarchar(200),NULL) ls_guarantee,CONVERT(nvarchar(200),NULL) ls_customer_type,CONVERT(int,NULL) st9_in_s009,CONVERT(int,NULL) st9_in_s010,CONVERT(date,NULL) st9_s009_job_date,CONVERT(date,NULL) st9_s010_job_date,CONVERT(date,NULL) st9_s009_edd,CONVERT(nvarchar(200),NULL) st9_s009_status,CONVERT(nvarchar(200),NULL) st9_s009_store,CONVERT(nvarchar(200),NULL) st9_s010_store,CONVERT(date,NULL) st9_s009_indent_date,CONVERT(nvarchar(200),NULL) st9_s009_indent_id,CONVERT(date,NULL) st9_s010_repair_date,CONVERT(nvarchar(200),NULL) st9_spare_required,CONVERT(nvarchar(200),NULL) st9_brand,CONVERT(nvarchar(200),NULL) st9_model,CONVERT(nvarchar(200),NULL) st9_product_category,CONVERT(nvarchar(200),NULL) st9_customer_name,CONVERT(date,NULL) srn_srn_date,CONVERT(date,NULL) srn_job_date,CONVERT(nvarchar(200),NULL) srn_to_store,CONVERT(nvarchar(200),NULL) srn_brand,CONVERT(nvarchar(200),NULL) srn_customer_name,CONVERT(int,NULL) srn_srn_open,CONVERT(decimal(19,4),NULL) w_revenue_labour_charge,CONVERT(decimal(19,4),NULL) w_revenue_spare_charge,CONVERT(decimal(19,4),NULL) w_revenue_net_incl_tax,CONVERT(int,NULL) w_revenue_documents,CONVERT(date,NULL) w_srf_date,CONVERT(date,NULL) w_repair_date,CONVERT(date,NULL) w_delivered_date,CONVERT(int,NULL) w_is_delivered,CONVERT(int,NULL) w_is_rwr,CONVERT(nvarchar(200),NULL) w_brand,CONVERT(int,NULL) o_wdc_claimed,CONVERT(int,NULL) o_wra_claimed FROM held
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,job_date),CONVERT(date,edd),CONVERT(nvarchar(200),brand),CONVERT(nvarchar(200),model),CONVERT(nvarchar(200),product_category),CONVERT(nvarchar(200),customer_name),CONVERT(nvarchar(200),guarantee),CONVERT(nvarchar(200),customer_type),CONVERT(date,delivery_date),CONVERT(date,delivered_repair_date),CONVERT(date,rwr_date),CONVERT(nvarchar(200),rwr_reason),CONVERT(date,wdc_date),CONVERT(nvarchar(200),wdc_number),CONVERT(date,wra_date),CONVERT(nvarchar(200),radc_number),CONVERT(date,pd_repair_date),CONVERT(date,srn_issued_date),CONVERT(nvarchar(200),srn_to_store_code),CONVERT(date,indent_date),CONVERT(int,in_delivered),CONVERT(int,in_rwr),CONVERT(int,in_dc),CONVERT(int,in_ra),CONVERT(int,in_pd),CONVERT(int,in_srn),CONVERT(int,in_ir),CONVERT(int,in_pr),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL) FROM sv
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),spare_value),CONVERT(decimal(19,4),labour_charge),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL) FROM view_money
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,s002_created),CONVERT(date,list_created),CONVERT(nvarchar(200),jo_type),CONVERT(nvarchar(200),exported_status),CONVERT(nvarchar(200),brand),CONVERT(nvarchar(200),product_category),CONVERT(nvarchar(200),guarantee),CONVERT(nvarchar(200),customer_type),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL) FROM lists
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,in_s009),CONVERT(int,in_s010),CONVERT(date,s009_job_date),CONVERT(date,s010_job_date),CONVERT(date,s009_edd),CONVERT(nvarchar(200),s009_status),CONVERT(nvarchar(200),s009_store),CONVERT(nvarchar(200),s010_store),CONVERT(date,s009_indent_date),CONVERT(nvarchar(200),s009_indent_id),CONVERT(date,s010_repair_date),CONVERT(nvarchar(200),spare_required),CONVERT(nvarchar(200),brand),CONVERT(nvarchar(200),model),CONVERT(nvarchar(200),product_category),CONVERT(nvarchar(200),customer_name),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL) FROM state
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,srn_date),CONVERT(date,job_date),CONVERT(nvarchar(200),to_store),CONVERT(nvarchar(200),brand),CONVERT(nvarchar(200),customer_name),CONVERT(int,srn_open),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL) FROM srn
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),revenue_labour_charge),CONVERT(decimal(19,4),revenue_spare_charge),CONVERT(decimal(19,4),revenue_net_incl_tax),CONVERT(int,revenue_documents),CONVERT(date,srf_date),CONVERT(date,repair_date),CONVERT(date,delivered_date),CONVERT(int,is_delivered),CONVERT(int,is_rwr),CONVERT(nvarchar(200),brand),CONVERT(int,NULL),CONVERT(int,NULL) FROM won
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,wdc_claimed),CONVERT(int,wra_claimed) FROM other_jobs
), agg AS (
  SELECT u.job_order_number,MAX(u.last_reading_date) last_reading_date,MAX(u.sv_job_date) sv_job_date,MAX(u.sv_edd) sv_edd,MAX(u.sv_brand) sv_brand,MAX(u.sv_model) sv_model,MAX(u.sv_product_category) sv_product_category,MAX(u.sv_customer_name) sv_customer_name,MAX(u.sv_guarantee) sv_guarantee,MAX(u.sv_customer_type) sv_customer_type,MAX(u.sv_delivery_date) sv_delivery_date,MAX(u.sv_delivered_repair_date) sv_delivered_repair_date,MAX(u.sv_rwr_date) sv_rwr_date,MAX(u.sv_rwr_reason) sv_rwr_reason,MAX(u.sv_wdc_date) sv_wdc_date,MAX(u.sv_wdc_number) sv_wdc_number,MAX(u.sv_wra_date) sv_wra_date,MAX(u.sv_radc_number) sv_radc_number,MAX(u.sv_pd_repair_date) sv_pd_repair_date,MAX(u.sv_srn_issued_date) sv_srn_issued_date,MAX(u.sv_srn_to_store_code) sv_srn_to_store_code,MAX(u.sv_indent_date) sv_indent_date,MAX(u.sv_in_delivered) sv_in_delivered,MAX(u.sv_in_rwr) sv_in_rwr,MAX(u.sv_in_dc) sv_in_dc,MAX(u.sv_in_ra) sv_in_ra,MAX(u.sv_in_pd) sv_in_pd,MAX(u.sv_in_srn) sv_in_srn,MAX(u.sv_in_ir) sv_in_ir,MAX(u.sv_in_pr) sv_in_pr,MAX(u.vm_spare_value) vm_spare_value,MAX(u.vm_labour_charge) vm_labour_charge,MAX(u.ls_s002_created) ls_s002_created,MAX(u.ls_list_created) ls_list_created,MAX(u.ls_jo_type) ls_jo_type,MAX(u.ls_exported_status) ls_exported_status,MAX(u.ls_brand) ls_brand,MAX(u.ls_product_category) ls_product_category,MAX(u.ls_guarantee) ls_guarantee,MAX(u.ls_customer_type) ls_customer_type,MAX(u.st9_in_s009) st9_in_s009,MAX(u.st9_in_s010) st9_in_s010,MAX(u.st9_s009_job_date) st9_s009_job_date,MAX(u.st9_s010_job_date) st9_s010_job_date,MAX(u.st9_s009_edd) st9_s009_edd,MAX(u.st9_s009_status) st9_s009_status,MAX(u.st9_s009_store) st9_s009_store,MAX(u.st9_s010_store) st9_s010_store,MAX(u.st9_s009_indent_date) st9_s009_indent_date,MAX(u.st9_s009_indent_id) st9_s009_indent_id,MAX(u.st9_s010_repair_date) st9_s010_repair_date,MAX(u.st9_spare_required) st9_spare_required,MAX(u.st9_brand) st9_brand,MAX(u.st9_model) st9_model,MAX(u.st9_product_category) st9_product_category,MAX(u.st9_customer_name) st9_customer_name,MAX(u.srn_srn_date) srn_srn_date,MAX(u.srn_job_date) srn_job_date,MAX(u.srn_to_store) srn_to_store,MAX(u.srn_brand) srn_brand,MAX(u.srn_customer_name) srn_customer_name,MAX(u.srn_srn_open) srn_srn_open,MAX(u.w_revenue_labour_charge) w_revenue_labour_charge,MAX(u.w_revenue_spare_charge) w_revenue_spare_charge,MAX(u.w_revenue_net_incl_tax) w_revenue_net_incl_tax,MAX(u.w_revenue_documents) w_revenue_documents,MAX(u.w_srf_date) w_srf_date,MAX(u.w_repair_date) w_repair_date,MAX(u.w_delivered_date) w_delivered_date,MAX(u.w_is_delivered) w_is_delivered,MAX(u.w_is_rwr) w_is_rwr,MAX(u.w_brand) w_brand,MAX(u.o_wdc_claimed) o_wdc_claimed,MAX(u.o_wra_claimed) o_wra_claimed
  FROM facts_by_job u WHERE u.job_order_number IS NOT NULL GROUP BY u.job_order_number
), snap AS (
  SELECT MAX(snapshot_date) as_at FROM live
)
SELECT a.job_order_number,bd.booking_date,COALESCE(a.ls_jo_type,N''Booking'') jo_type,a.ls_exported_status exported_status,
  COALESCE(a.sv_brand,a.st9_brand,a.ls_brand,a.srn_brand,a.w_brand) brand,COALESCE(a.sv_model,a.st9_model) model,
  COALESCE(a.sv_product_category,a.st9_product_category,a.ls_product_category) product_category,
  COALESCE(a.sv_customer_name,a.st9_customer_name,a.srn_customer_name) customer_name,
  COALESCE(a.sv_guarantee,a.ls_guarantee) guarantee,COALESCE(a.sv_customer_type,a.ls_customer_type) customer_type,
  COALESCE(a.st9_s009_edd,a.sv_edd) edd,sg.stage,sd.stage_date,
  CASE WHEN sg.stage IN(''DELIVERED'',''RWR'') THEN NULL
       WHEN sg.stage IN(''IN_TRANSIT_BACK'',''READY_FOR_DELIVERY'') AND a.st9_in_s010=1 THEN COALESCE(a.st9_s010_store,N''AW330'')
       WHEN sg.stage=''SRN_OUT'' THEN COALESCE(a.srn_to_store,a.st9_s009_store,a.sv_srn_to_store_code,N''AW330'')
       WHEN a.st9_in_s009=1 THEN COALESCE(a.st9_s009_store,N''AW330'')
       ELSE N''AW330'' END pending_at,
  a.st9_spare_required spare_required,COALESCE(a.st9_s009_indent_date,a.sv_indent_date) indent_date,
  COALESCE(a.srn_srn_date,a.sv_srn_issued_date) srn_date,COALESCE(a.srn_to_store,a.sv_srn_to_store_code) srn_to_store,
  COALESCE(a.sv_delivered_repair_date,a.w_repair_date,a.sv_pd_repair_date,a.st9_s010_repair_date) repair_date,COALESCE(a.sv_delivery_date,a.w_delivered_date) delivery_date,
  a.sv_rwr_date rwr_date,a.sv_rwr_reason rwr_reason,a.sv_wdc_date wdc_date,a.sv_wdc_number wdc_number,a.sv_wra_date wra_date,a.sv_radc_number radc_number,
  CONVERT(bit,CASE WHEN sg.stage=''DC_ISSUED'' THEN COALESCE(a.o_wdc_claimed,0) WHEN sg.stage=''RA_ISSUED'' THEN COALESCE(a.o_wra_claimed,0)
       WHEN COALESCE(a.o_wdc_claimed,0)=1 OR COALESCE(a.o_wra_claimed,0)=1 THEN 1 ELSE 0 END) claim_raised,
  a.vm_spare_value spare_value,a.vm_labour_charge labour_charge,a.w_revenue_labour_charge revenue_labour_charge,a.w_revenue_spare_charge revenue_spare_charge,a.w_revenue_net_incl_tax revenue_net_incl_tax,
  CONVERT(int,COALESCE(a.w_revenue_documents,0)) revenue_documents,
  CASE WHEN sg.stage=''DELIVERED'' THEN DATEDIFF(day,bd.booking_date,COALESCE(a.sv_delivery_date,a.w_delivered_date))
       WHEN sg.stage=''RWR'' THEN DATEDIFF(day,bd.booking_date,COALESCE(a.sv_rwr_date,a.w_repair_date)) END tat_days,
  CASE WHEN sg.stage=''DELIVERED'' THEN DATEDIFF(day,bd.booking_date,COALESCE(a.sv_delivered_repair_date,a.w_repair_date)) END tat_repair_days,
  CASE WHEN sg.stage NOT IN(''DELIVERED'',''RWR'') THEN DATEDIFF(day,bd.booking_date,snap.as_at) END age_days,
  CASE WHEN sg.stage NOT IN(''DELIVERED'',''RWR'') THEN DATEDIFF(day,sd.stage_date,snap.as_at) END days_in_stage,
  CONVERT(bit,CASE WHEN sg.stage NOT IN(''DELIVERED'',''RWR'') AND COALESCE(a.st9_s009_edd,a.sv_edd)<snap.as_at THEN 1 ELSE 0 END) is_overdue,
  CONVERT(bit,CASE WHEN sg.stage IN(''DELIVERED'',''RWR'') THEN 0 WHEN sg.stage=''DC_ISSUED'' AND COALESCE(a.o_wdc_claimed,0)=1 THEN 0
       WHEN sg.stage=''RA_ISSUED'' AND COALESCE(a.o_wra_claimed,0)=1 THEN 0 ELSE 1 END) is_open,
  a.last_reading_date,snap.as_at
FROM agg a
CROSS JOIN snap
CROSS APPLY (SELECT MIN(v.d) booking_date FROM (VALUES(a.ls_s002_created),(a.ls_list_created),(a.sv_job_date),(a.st9_s009_job_date),(a.st9_s010_job_date),(a.srn_job_date),(a.w_srf_date)) v(d)) bd
CROSS APPLY (SELECT
  CASE WHEN a.sv_in_delivered=1 OR a.w_is_delivered=1 THEN ''DELIVERED''
       WHEN a.sv_in_rwr=1 OR a.w_is_rwr=1 THEN ''RWR''
       WHEN a.sv_in_dc=1 THEN ''DC_ISSUED''
       WHEN a.sv_in_ra=1 THEN ''RA_ISSUED''
       WHEN a.st9_in_s010=1 AND COALESCE(a.st9_s010_store,N'''')<>N''AW330'' THEN ''IN_TRANSIT_BACK''
       WHEN a.st9_in_s010=1 OR a.sv_in_pd=1 THEN ''READY_FOR_DELIVERY''
       WHEN a.st9_in_s009=1 THEN
         CASE WHEN a.st9_s009_status LIKE ''SRN%'' THEN ''SRN_OUT''
              WHEN a.st9_s009_status=''Indent_Raised'' OR a.st9_s009_indent_id IS NOT NULL OR a.st9_s009_indent_date IS NOT NULL THEN ''INDENT_RAISED''
              ELSE ''ON_BENCH'' END
       WHEN a.srn_srn_open=1 OR a.sv_in_srn=1 THEN ''SRN_OUT''
       WHEN a.sv_in_ir=1 THEN ''INDENT_RAISED''
       WHEN a.sv_in_pr=1 THEN ''ON_BENCH''
       ELSE ''BOOKED'' END stage) sg
CROSS APPLY (SELECT
  CASE sg.stage WHEN ''DELIVERED'' THEN COALESCE(a.sv_delivery_date,a.w_delivered_date)
       WHEN ''RWR'' THEN COALESCE(a.sv_rwr_date,a.w_repair_date)
       WHEN ''DC_ISSUED'' THEN a.sv_wdc_date
       WHEN ''RA_ISSUED'' THEN a.sv_wra_date
       WHEN ''IN_TRANSIT_BACK'' THEN COALESCE(a.st9_s010_repair_date,a.st9_s010_job_date)
       WHEN ''READY_FOR_DELIVERY'' THEN COALESCE(a.st9_s010_repair_date,a.sv_pd_repair_date,a.st9_s010_job_date)
       WHEN ''SRN_OUT'' THEN COALESCE(a.srn_srn_date,a.sv_srn_issued_date,a.st9_s009_job_date,bd.booking_date)
       WHEN ''INDENT_RAISED'' THEN COALESCE(a.st9_s009_indent_date,a.sv_indent_date,a.st9_s009_job_date,bd.booking_date)
       WHEN ''ON_BENCH'' THEN COALESCE(a.st9_s009_job_date,a.sv_job_date,bd.booking_date)
       ELSE bd.booking_date END stage_date) sd
');

-- 4. The Job history timeline (design 3.4): one row per reading of every family that holds the job, with the family''s
-- own event date, the status text as exported, the pending store, the document number and the amount where a money
-- column exists, grouped per reading (lines counts the line rows). Every live reading is listed, so a job''s history
-- across readings is visible; the screen sorts newest snapshot first. S036/S037 are labelled as job lists (Q10); the
-- 0048 "left the list" events stay in v_service_job_list_events and no longer drive the screen (SD-01).
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_job_timeline AS
WITH rows AS (
  SELECT ''S002'' report_code,import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N'''') job_order_number,created_date event_date,
    CONVERT(nvarchar(200),current_status) status_text,CONVERT(nvarchar(200),NULL) pending_store,CONVERT(nvarchar(100),NULL) document_number,CONVERT(decimal(19,4),NULL) amount
  FROM dbo.etp_landing_s002
  UNION ALL SELECT ''S036'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N''''),created_date,CONVERT(nvarchar(200),current_status),NULL,NULL,NULL FROM dbo.etp_landing_s036
  UNION ALL SELECT ''S037'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N''''),created_date,CONVERT(nvarchar(200),current_status),NULL,NULL,NULL FROM dbo.etp_landing_s037
  UNION ALL SELECT f.report_code,f.import_file_id,f.job_order_number,
    CASE f.report_code WHEN ''S014'' THEN f.wdc_date WHEN ''S015'' THEN f.indent_date WHEN ''S016'' THEN f.wra_date WHEN ''S017'' THEN f.rwr_date
      WHEN ''S018'' THEN f.delivery_date WHEN ''S031'' THEN f.repair_date WHEN ''S032'' THEN f.job_date WHEN ''S033'' THEN f.srn_issued_date
      WHEN ''S034'' THEN f.repair_date WHEN ''S035'' THEN f.srn_return_date END,
    CONVERT(nvarchar(200),f.repair_status),f.to_store_code,
    CASE f.report_code WHEN ''S014'' THEN f.wdc_number WHEN ''S016'' THEN f.radc_number WHEN ''S015'' THEN f.indent_number END,f.spare_value
  FROM dbo.v_service_status_view_facts f
  UNION ALL SELECT ''S009'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''''),jodate,CONVERT(nvarchar(200),jostatus),CONVERT(nvarchar(200),pendingstore),
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),indentid))),N''''),NULL FROM dbo.etp_landing_s009
  UNION ALL SELECT ''S010'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''''),repairdate,CONVERT(nvarchar(200),jostatus),CONVERT(nvarchar(200),pendingstore),NULL,NULL FROM dbo.etp_landing_s010
  UNION ALL SELECT ''S011'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N''''),srn_date,CONVERT(nvarchar(200),to_status),CONVERT(nvarchar(200),to_store),
    CONVERT(nvarchar(100),srndc_number),amount FROM dbo.etp_landing_s011
  UNION ALL SELECT ''S012'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''''),COALESCE(srnrepaireddate,jodate),CONVERT(nvarchar(200),srnstatus),CONVERT(nvarchar(200),tostore),
    CONVERT(nvarchar(100),srnno),total FROM dbo.etp_landing_s012
  UNION ALL SELECT ''S003'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N''''),trans_date,CONVERT(nvarchar(200),job_status),NULL,
    CONVERT(nvarchar(100),documentnum),netamount_incl_tax FROM dbo.etp_landing_s003
  UNION ALL SELECT ''S019'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jo_number))),N''''),repairdate,CONVERT(nvarchar(200),reason),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),previousjo))),N''''),NULL FROM dbo.etp_landing_s019
  UNION ALL SELECT ''S020'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),radate,CONVERT(nvarchar(200),wrastatus_1),CONVERT(nvarchar(200),tolocation),
    CONVERT(nvarchar(100),dcnumber),deductioncharges FROM dbo.etp_landing_s020
  UNION ALL SELECT ''S021'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),dcdate,CONVERT(nvarchar(200),status),CONVERT(nvarchar(200),tolocation),
    CONVERT(nvarchar(100),documentnumber),wdcvalue FROM dbo.etp_landing_s021
  UNION ALL SELECT ''S022'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N''''),invoice_date,CONVERT(nvarchar(200),empowerment_type),NULL,
    CONVERT(nvarchar(100),invoice_number),empowerment_value FROM dbo.etp_landing_s022
  UNION ALL SELECT ''S029'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),srfno))),N''''),COALESCE(delivered_date,repair_date),CONVERT(nvarchar(200),status),NULL,
    CONVERT(nvarchar(100),srn_number),labour_charge_actual FROM dbo.etp_landing_s029
  UNION ALL SELECT ''S030'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_number))),N''''),running_test_date,CONVERT(nvarchar(200),result),NULL,NULL,NULL FROM dbo.etp_landing_s030
  UNION ALL SELECT ''S023'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''''),transdate,CONVERT(nvarchar(200),accountnum),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),documentnum))),N''''),netamountinctax FROM dbo.etp_landing_s023
  UNION ALL SELECT ''S024'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''''),transdate,CONVERT(nvarchar(200),accountnum),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),documentnum))),N''''),netamountinctax FROM dbo.etp_landing_s024
  UNION ALL SELECT ''S025'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''''),transdate,CONVERT(nvarchar(200),accountnum),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),documentnum))),N''''),netamountinctax FROM dbo.etp_landing_s025
  UNION ALL SELECT ''S026'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''''),transdate,CONVERT(nvarchar(200),accountnum),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),documentnum))),N''''),netamountinctax FROM dbo.etp_landing_s026
  UNION ALL SELECT ''S039'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_number))),N''''),transaction_date,CONVERT(nvarchar(200),account_number),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),document_number))),N''''),net_amount_inc_tax FROM dbo.etp_landing_s039
  UNION ALL SELECT ''S040'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_number))),N''''),transaction_date,CONVERT(nvarchar(200),account_number),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),document_number))),N''''),net_amount_inc_tax FROM dbo.etp_landing_s040
  UNION ALL SELECT ''S041'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_number))),N''''),transaction_date,CONVERT(nvarchar(200),account_number),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),document_number))),N''''),net_amount_inc_tax FROM dbo.etp_landing_s041
)
SELECT r.job_order_number,w.snapshot_date,w.source_kind,r.report_code,
  CASE r.report_code WHEN ''S036'' THEN ''Delivery-type jobs'' WHEN ''S037'' THEN ''Repair-type jobs'' ELSE f.list_label END list_label,
  r.event_date,r.status_text,r.pending_store,r.document_number,SUM(r.amount) amount,CONVERT(int,COUNT_BIG(*)) lines,r.import_file_id
FROM rows r JOIN dbo.v_service_readings w ON w.import_file_id=r.import_file_id AND w.report_code=r.report_code
JOIN dbo.v_service_families f ON f.report_code=r.report_code
WHERE r.job_order_number IS NOT NULL
GROUP BY r.job_order_number,w.snapshot_date,w.source_kind,r.report_code,f.list_label,r.event_date,r.status_text,r.pending_store,r.document_number,r.import_file_id');

-- 5. Purchases (design 3.6): one row per invoice line (invoice number, item), S007 created against S008 received.
-- S008 rows follow the DateLog rule (grn_date, reading_rank 1). S007 rows are keyed by invoice number and taken from the
-- latest reading that holds the invoice: an open invoice has no GRN date, so the DateLog rule (which is by grn_date)
-- cannot choose it; for a received S007 row the latest reading holding the invoice is the DateLog winner too. Status is
-- Received when an S008 line exists or the S007 row carries a GRN date, else Open; days_open counts to the GRN date
-- (received) or to the latest Service snapshot date (open). No job column exists on either family (design 1.7).
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_parts AS
WITH created_rows AS (
  SELECT s.import_file_id,w.snapshot_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.invoice_number))),N'''') invoice_number,s.invoice_date,
    CONVERT(nvarchar(100),s.item_id) item_id,s.shipped_quantity,s.received_quantity,s.net_amount,CONVERT(nvarchar(40),s.status) exported_status,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.grn_no))),N'''') grn_number,s.grn_date,s.received_date,CONVERT(nvarchar(60),s.from_location) from_location
  FROM dbo.etp_landing_s007 s JOIN dbo.v_service_readings w ON w.import_file_id=s.import_file_id AND w.report_code=''S007''
), created_ranked AS (
  SELECT c.*,DENSE_RANK() OVER(PARTITION BY c.invoice_number ORDER BY c.snapshot_date DESC,c.import_file_id DESC) reading_rank
  FROM created_rows c WHERE c.invoice_number IS NOT NULL
), created AS (
  SELECT invoice_number,item_id,MAX(snapshot_date) snapshot_date,MIN(invoice_date) invoice_date,SUM(shipped_quantity) shipped_quantity,SUM(received_quantity) received_quantity,
    SUM(net_amount) net_amount,MAX(exported_status) exported_status,MAX(grn_number) grn_number,MAX(grn_date) grn_date,MAX(received_date) received_date,MAX(from_location) from_location
  FROM created_ranked WHERE reading_rank=1 GROUP BY invoice_number,item_id
), received AS (
  SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.invoice_number))),N'''') invoice_number,CONVERT(nvarchar(100),s.item_id) item_id,MAX(w.snapshot_date) snapshot_date,
    MIN(s.invoice_date) invoice_date,SUM(s.shipped_quantity) shipped_quantity,SUM(s.received_quantity) received_quantity,SUM(s.net_amount) net_amount,
    MAX(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.grn_no))),N'''')) grn_number,MAX(s.grn_date) grn_date,MAX(s.received_date) received_date,MAX(CONVERT(nvarchar(60),s.from_location)) from_location
  FROM dbo.etp_landing_s008 s JOIN dbo.v_service_datelog_readings w ON w.report_code=''S008'' AND w.reading_rank=1
    AND w.import_file_id=s.import_file_id AND w.business_date=s.grn_date
  WHERE NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.invoice_number))),N'''') IS NOT NULL
  GROUP BY NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.invoice_number))),N''''),CONVERT(nvarchar(100),s.item_id)
), snap AS (
  SELECT MAX(snapshot_date) as_at FROM dbo.v_service_readings
)
SELECT COALESCE(c.invoice_number,r.invoice_number) invoice_number,COALESCE(c.invoice_date,r.invoice_date) invoice_date,COALESCE(c.item_id,r.item_id) item_id,
  COALESCE(c.shipped_quantity,r.shipped_quantity) shipped_quantity,COALESCE(r.received_quantity,CASE WHEN c.grn_date IS NOT NULL THEN c.received_quantity END) received_quantity,
  COALESCE(c.net_amount,r.net_amount) net_amount,COALESCE(r.grn_number,c.grn_number) grn_number,COALESCE(r.grn_date,c.grn_date) grn_date,COALESCE(r.received_date,c.received_date) received_date,
  CONVERT(varchar(12),CASE WHEN r.invoice_number IS NOT NULL OR c.grn_date IS NOT NULL THEN ''Received'' ELSE ''Open'' END) status,c.exported_status,
  COALESCE(c.from_location,r.from_location) from_location,
  CASE WHEN r.invoice_number IS NOT NULL OR c.grn_date IS NOT NULL
       THEN DATEDIFF(day,COALESCE(c.invoice_date,r.invoice_date),COALESCE(r.grn_date,c.grn_date,r.received_date,c.received_date))
       ELSE DATEDIFF(day,c.invoice_date,snap.as_at) END days_open,
  CASE WHEN c.snapshot_date IS NULL OR r.snapshot_date>c.snapshot_date THEN r.snapshot_date ELSE c.snapshot_date END snapshot_date,snap.as_at
FROM created c FULL OUTER JOIN received r ON r.invoice_number=c.invoice_number AND r.item_id=c.item_id
CROSS JOIN snap');

-- 6. Goods in transit (S013, item level, no job column) under the DateLog rule (stm_date).
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_parts_transit AS
SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.stm_number))),N'''') stm_number,s.stm_date business_date,CONVERT(nvarchar(100),s.item_id) item_id,s.qty_shipped quantity_shipped,
  CONVERT(nvarchar(100),s.invent_location_id_from) from_location,CONVERT(nvarchar(100),s.invent_location_id_to) to_location,s.ucp,w.snapshot_date,s.import_file_id
FROM dbo.etp_landing_s013 s JOIN dbo.v_service_datelog_readings w ON w.report_code=''S013'' AND w.reading_rank=1
  AND w.import_file_id=s.import_file_id AND w.business_date=s.stm_date');

-- 7. The latest closing stock (S006, StateSnapshot rule) as count and value only: items, quantity and quantity x price.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_stock_summary AS
SELECT r.snapshot_date,CONVERT(int,COUNT_BIG(*)) items,SUM(s.quantity) quantity,SUM(s.quantity*s.price) value,r.import_file_id
FROM dbo.etp_landing_s006 s JOIN dbo.v_service_readings r ON r.import_file_id=s.import_file_id AND r.report_code=''S006'' AND r.is_latest=1
GROUP BY r.snapshot_date,r.import_file_id');

-- 8. The 0048 pending view, amended (SD-02, SD-12, Q5). Same columns in the same order as 0048 for the interim screen and
-- query, then the new ones: edd, jo_status, spare_required, indent_date, repair_date, srn_to_status. S009/S010 are the
-- is_latest state snapshot as before. S011 keeps the JobList rule (latest reading per job) but lists only open SRNs: an
-- SRN is closed when srn_received_date is set, or repaired_date is set, or to_status contains Received (Q5).
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_pending_current AS
WITH state_rows AS (
  SELECT ''S009'' report_code,import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N'''') job_order_number,jodate job_date,CONVERT(nvarchar(200),brand) brand,
    CONVERT(nvarchar(200),variantnumber) model,CONVERT(nvarchar(200),customername) customer_name,CONVERT(nvarchar(200),pendingstore) pending_store,
    edd,CONVERT(nvarchar(60),jostatus) jo_status,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(200),sparerequired))),N'''') spare_required,indentdate indent_date,CONVERT(date,NULL) repair_date
  FROM dbo.etp_landing_s009
  UNION ALL SELECT ''S010'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''''),jodate,CONVERT(nvarchar(200),brand),
    CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),customername),CONVERT(nvarchar(200),pendingstore),
    CONVERT(date,NULL),CONVERT(nvarchar(60),jostatus),NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(200),sparerequired))),N''''),indentdate,repairdate
  FROM dbo.etp_landing_s010
), state_lists AS (
  SELECT s.report_code,s.job_order_number,MIN(s.job_date) job_date,MAX(s.brand) brand,MAX(s.model) model,
    MAX(s.customer_name) customer_name,MAX(s.pending_store) pending_store,r.snapshot_date,r.import_file_id,
    MAX(s.edd) edd,MAX(s.jo_status) jo_status,MAX(s.spare_required) spare_required,MAX(s.indent_date) indent_date,MAX(s.repair_date) repair_date,CONVERT(nvarchar(80),NULL) srn_to_status
  FROM state_rows s JOIN dbo.v_service_readings r ON r.import_file_id=s.import_file_id AND r.report_code=s.report_code AND r.is_latest=1
  WHERE s.job_order_number IS NOT NULL
  GROUP BY s.report_code,s.job_order_number,r.snapshot_date,r.import_file_id
), srn_winners AS (
  SELECT job_order_number,snapshot_date,import_file_id,
    ROW_NUMBER() OVER(PARTITION BY job_order_number ORDER BY snapshot_date DESC,import_file_id DESC) job_rank
  FROM dbo.v_service_job_readings WHERE report_code=''S011''
), srn_lists AS (
  SELECT ''S011'' report_code,w.job_order_number,MIN(s.joborder_date) job_date,MAX(CONVERT(nvarchar(200),s.brand)) brand,
    CONVERT(nvarchar(200),NULL) model,MAX(CONVERT(nvarchar(200),s.customer_name)) customer_name,MAX(CONVERT(nvarchar(200),s.to_store)) pending_store,
    w.snapshot_date,w.import_file_id,
    CONVERT(date,NULL) edd,CONVERT(nvarchar(60),NULL) jo_status,CONVERT(nvarchar(200),NULL) spare_required,CONVERT(date,NULL) indent_date,CONVERT(date,NULL) repair_date,
    MAX(CONVERT(nvarchar(80),s.to_status)) srn_to_status
  FROM srn_winners w JOIN dbo.etp_landing_s011 s ON s.import_file_id=w.import_file_id
    AND NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.joborder_number))),N'''')=w.job_order_number
  WHERE w.job_rank=1
    AND s.srn_received_date IS NULL AND s.repaired_date IS NULL AND COALESCE(CONVERT(nvarchar(80),s.to_status),N'''') NOT LIKE ''%Received%''
  GROUP BY w.job_order_number,w.snapshot_date,w.import_file_id
), lists AS (
  SELECT * FROM state_lists UNION ALL SELECT * FROM srn_lists
)
SELECT f.pending_list [list],l.report_code,f.list_label,l.job_order_number,l.job_date,
  CASE WHEN l.job_date IS NULL THEN NULL ELSE DATEDIFF(day,l.job_date,l.snapshot_date) END age_days,
  l.brand,l.model,l.customer_name,l.pending_store,l.snapshot_date,l.import_file_id,
  l.edd,l.jo_status,l.spare_required,l.indent_date,l.repair_date,l.srn_to_status
FROM lists l JOIN dbo.v_service_families f ON f.report_code=l.report_code');
-- Read access as 0048 section C grants it; no role may write through a view.
GRANT SELECT ON dbo.v_service_status_view_facts TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_status_view_facts TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_claims TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_claims TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_job TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_job TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_job_timeline TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_job_timeline TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_parts TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_parts TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_parts_transit TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_parts_transit TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_stock_summary TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_stock_summary TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_pending_current TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_pending_current TO etp_store_manager,etp_viewer;
-- <<< D_SERVICE_UI_READ end
