-- Service views: the customer name by role (1.11.1, lane name-privacy, 12 Oct 2026; decision 37 follow-up).
-- 0056 gave v_service_job the customer name by role: Owner and Store Manager (and db_owner, which is how the Owner's own
-- login and the gate run) see it, anyone else (an etp_viewer) reads NULL. The older Service read views that also return a
-- customer_name column gave it to every role that may read them, although no screen reads them any more. This migration
-- restates exactly those four views with CREATE OR ALTER, each one's latest text unchanged except the customer_name
-- expression, which is wrapped in 0056's role rule:
--   v_service_status_view_rows   (0048 section C, the ten status-view line rows)
--   v_service_job_status_current (0048 section C, the interim job list)
--   v_service_status_view_facts  (0050 section D, the status-view lifecycle facts)
--   v_service_pending_current    (0050 section D, the amended pending lists)
-- 0048, 0050 and 0056 have shipped or are checksummed and are never edited.
--
-- The migration runner applies this entire script in one transaction, so it has no BEGIN or COMMIT and no GO.
-- CREATE OR ALTER keeps each view's permissions; the grants below are the ones 0048 and 0050 issued, repeated unchanged, so
-- the role permissions (ExpectedRolePermissions.json) do not change. Columns, their names and their order are unchanged.
SET XACT_ABORT ON;

-- >>> E_SERVICE_NAME_PRIVACY begin
-- Owner: lane name-privacy.
-- IS_MEMBER is evaluated for the caller of each query, also through a view that reads another view. Every reader of these
-- views therefore gets the rule 0056 gives v_service_job; the Owner and the Store Manager see the same rows as before.

-- 1. v_service_status_view_rows, 0048's text; the customer name of each of the ten branches is given by role.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_status_view_rows AS
SELECT ''S014'' report_code,import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N'''') job_order_number,wdcdate status_date,jodate job_date,edd,
  CONVERT(nvarchar(200),brandname) brand,CONVERT(nvarchar(200),variantnumber) model,CONVERT(nvarchar(200),clusterid) product_category,CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END customer_name,sparevalue spare_value,labourcharge labour_charge
FROM dbo.etp_landing_s014
UNION ALL SELECT ''S015'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),indentdate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s015
UNION ALL SELECT ''S016'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),wradate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s016
UNION ALL SELECT ''S017'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),normalrwrdate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s017
UNION ALL SELECT ''S018'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),deliverydate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s018
UNION ALL SELECT ''S031'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jorepairdate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s031
UNION ALL SELECT ''S032'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s032
UNION ALL SELECT ''S033'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),srnissueddate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s033
UNION ALL SELECT ''S034'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jorepairdate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s034
UNION ALL SELECT ''S035'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),srnreturndate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s035');

-- 2. v_service_job_status_current, 0048's text; the final customer_name is given by role (its source rows are too).
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_job_status_current AS
WITH per_view AS (
  SELECT r.job_order_number,r.report_code,r.snapshot_date,r.import_file_id,r.status_date,
    ROW_NUMBER() OVER(PARTITION BY r.job_order_number,r.report_code ORDER BY r.snapshot_date DESC,r.import_file_id DESC) view_rank
  FROM dbo.v_service_job_readings r JOIN dbo.v_service_families c ON c.report_code=r.report_code AND c.status_label IS NOT NULL
), winners AS (
  SELECT p.job_order_number,p.report_code,p.snapshot_date,p.import_file_id,p.status_date,c.status_label,c.lifecycle_rank,
    COUNT(*) OVER(PARTITION BY p.job_order_number) views_held,
    ROW_NUMBER() OVER(PARTITION BY p.job_order_number ORDER BY p.snapshot_date DESC,c.lifecycle_rank DESC,p.import_file_id DESC) job_rank
  FROM per_view p JOIN dbo.v_service_families c ON c.report_code=p.report_code
  WHERE p.view_rank=1
), details AS (
  SELECT s.report_code,s.import_file_id,s.job_order_number,MIN(s.job_date) job_date,MAX(s.edd) edd,MAX(s.brand) brand,
    MAX(s.model) model,MAX(s.product_category) product_category,MAX(s.customer_name) customer_name,
    SUM(s.spare_value) spare_value,SUM(s.labour_charge) labour_charge,COUNT_BIG(*) line_count
  FROM dbo.v_service_status_view_rows s
  WHERE s.job_order_number IS NOT NULL
  GROUP BY s.report_code,s.import_file_id,s.job_order_number
)
SELECT w.job_order_number,w.report_code status_view,w.status_label,w.lifecycle_rank,w.status_date,
  d.job_date,d.edd,d.brand,d.model,d.product_category,CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN d.customer_name END customer_name,d.spare_value,d.labour_charge,
  CONVERT(int,d.line_count) lines,w.snapshot_date,w.import_file_id,CONVERT(int,w.views_held-1) other_lists
FROM winners w JOIN details d ON d.report_code=w.report_code AND d.import_file_id=w.import_file_id
  AND d.job_order_number=w.job_order_number
WHERE w.job_rank=1');

-- 3. v_service_status_view_facts, 0050's text; the customer name of each of the ten branches is given by role.
-- v_service_job (0056) reads its name candidates here; it applies the same rule itself, so nothing changes for it.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_status_view_facts AS
SELECT ''S014'' report_code,import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N'''') job_order_number,jodate job_date,edd,
  CONVERT(nvarchar(200),brandname) brand,CONVERT(nvarchar(200),variantnumber) model,CONVERT(nvarchar(200),clusterid) product_category,CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END customer_name,
  CONVERT(nvarchar(60),guarantee) guarantee,CONVERT(nvarchar(60),customertype) customer_type,indentdate indent_date,CONVERT(nvarchar(100),indentnumber) indent_number,
  srnissueddate srn_issued_date,srnreturndate srn_return_date,srnreceiveddate srn_received_date,CONVERT(nvarchar(60),tostorecode) to_store_code,
  jorepairdate repair_date,deliverydate delivery_date,normalrwrdate rwr_date,CONVERT(nvarchar(200),rwr_reason) rwr_reason,
  wdcdate wdc_date,CONVERT(nvarchar(100),wdcnumber) wdc_number,wradate wra_date,CONVERT(nvarchar(100),radcnumber) radc_number,
  CONVERT(nvarchar(80),repairstatus) repair_status,sparevalue spare_value,labourcharge labour_charge
FROM dbo.etp_landing_s014
UNION ALL SELECT ''S015'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s015
UNION ALL SELECT ''S016'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s016
UNION ALL SELECT ''S017'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s017
UNION ALL SELECT ''S018'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s018
UNION ALL SELECT ''S031'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s031
UNION ALL SELECT ''S032'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s032
UNION ALL SELECT ''S033'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s033
UNION ALL SELECT ''S034'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s034
UNION ALL SELECT ''S035'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN CONVERT(nvarchar(200),customername) END,
  CONVERT(nvarchar(60),guarantee),CONVERT(nvarchar(60),customertype),indentdate,CONVERT(nvarchar(100),indentnumber),
  srnissueddate,srnreturndate,srnreceiveddate,CONVERT(nvarchar(60),tostorecode),jorepairdate,deliverydate,normalrwrdate,CONVERT(nvarchar(200),rwr_reason),
  wdcdate,CONVERT(nvarchar(100),wdcnumber),wradate,CONVERT(nvarchar(100),radcnumber),CONVERT(nvarchar(80),repairstatus),sparevalue,labourcharge
FROM dbo.etp_landing_s035');

-- 4. v_service_pending_current, 0050's text; the final customer_name is given by role.
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
  l.brand,l.model,CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1 THEN l.customer_name END customer_name,l.pending_store,l.snapshot_date,l.import_file_id,
  l.edd,l.jo_status,l.spare_required,l.indent_date,l.repair_date,l.srn_to_status
FROM lists l JOIN dbo.v_service_families f ON f.report_code=l.report_code');

-- Read access as 0048 section C and 0050 grant it (unchanged); no role may write through a view.
GRANT SELECT ON dbo.v_service_status_view_rows TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_status_view_rows TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_job_status_current TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_job_status_current TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_status_view_facts TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_status_view_facts TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_pending_current TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_pending_current TO etp_store_manager,etp_viewer;

-- <<< E_SERVICE_NAME_PRIVACY end
