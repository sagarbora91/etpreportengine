-- Service view speed (1.11.4, 12 Oct 2026). The VM acceptance timing of 1.11.3 (a copy of live, SQL Server 2025 RTM, 4 GB)
-- measured, median of 3 full reads: v_service_money_changes 58.9 s for 0 rows, v_service_s004_daily 5.0 s, v_service_job
-- 2.45 s, v_service_parts 0.98 s; the night-one health run skipped DB-06 (TIME_BUDGET) because counting the Service screens'
-- views did not finish inside the Database area's 10 s. Root causes, measured read-only on live (EtpReporting, 0048) with
-- the view texts as plain queries (E:\ETP\Reference\Work in progress 2026-10-12\1114-SERVICE-SPEED.md):
--   1. v_service_datelog_readings (0048) recomputes the reading windows through v_service_reading_windows, a 36-table
--      UNION ALL, twice per reference. A consumer that joins it to a landing table gets a nested-loops plan that runs the
--      whole chain once per outer row: s004_daily scanned etp_landing_s004 1,928 times (5.7 M page reads); money_changes'
--      LEFT JOIN of the view to itself (the rank-2 reading) plus the correlated restatement lookup did the same over S003
--      and S004 5,108 times (50 M page reads, 53 s CPU). The 36-table expansion is also why v_service_job took 3 s to compile.
--   2. v_service_job (0056) joins its 3,158 job rows to the name CTE last; with an estimate of 52 rows the optimizer used
--      nested loops over a table spool, about 10 M spool rows (1.7 s of the 2.3 s full read).
-- The fixes keep every view's columns, their order, types and rows (proved on live and on the Service fixtures, see
-- ServiceViewSpeedSqlTests):
--   A. v_service_datelog_readings takes the windows of the DateLog readings from its own dated rows (the same least and
--      greatest row date and the same live-reading filter as v_service_reading_windows), so it reads 15 tables, not 87.
--   B. v_service_s004_daily joins the winning readings to S004 with a hash join (each side read once).
--   C. v_service_money_changes takes the rank-2 reading with a window function and the restated reading with a set join
--      ranked per date, instead of a self-join and a correlated TOP (1).
--   D. v_service_parts reads the live S007 readings and the as-at date directly (as v_service_job does) instead of through
--      v_service_readings, and hash-joins the winning S008 readings and the created/received full outer join.
--   F. v_service_claims (0050 text) hash-joins each claim log to its winning readings. Besides reading each log once, the
--      hint fixes the join order of every query that reads the view, which takes v_service_job's compile (it reads the
--      claims) from about 3 s to about 1 s; DB-06 and a cold Jobs screen pay that compile.
--   E. v_service_job carries the customer name as one more branch of facts_by_job (MAX per job) instead of a final LEFT
--      JOIN; every job with a name is already a job of the view, so no row is added. The role rule of 0056/0057 is kept:
--      Owner, Store Manager and db_owner read the name, anyone else NULL.
-- No index is added: with each landing table read once, the existing import_file_id indexes are enough.
-- Join hints (HASH) are used only where a filter on the view can never remove every equality of the hinted join; the
-- integration test reads each view with the filters the screens use.
--
-- 0001-0060 are never edited. The migration runner applies this entire script in one transaction, so it has no BEGIN or
-- COMMIT and no GO. CREATE OR ALTER keeps each view's permissions; the grants below are the ones 0048, 0050, 0052 and 0056
-- issued, repeated unchanged, so the role permissions (ExpectedRolePermissions.json) do not change.
SET XACT_ABORT ON;

-- A. The DateLog rule (0048 text and columns): for each family and business date D that any live reading holds, every live
-- reading whose window [least row date, greater of snapshot date and greatest row date] contains D, ranked by snapshot
-- date then import file. The window of a reading is taken from its own dated rows here.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_datelog_readings AS
WITH dated AS (
  SELECT ''S003'' report_code,import_file_id,trans_date business_date FROM dbo.etp_landing_s003 WHERE trans_date IS NOT NULL GROUP BY import_file_id,trans_date
  UNION ALL SELECT ''S004'',import_file_id,billingdate FROM dbo.etp_landing_s004 WHERE billingdate IS NOT NULL GROUP BY import_file_id,billingdate
  UNION ALL SELECT ''S007'',import_file_id,grn_date FROM dbo.etp_landing_s007 WHERE grn_date IS NOT NULL GROUP BY import_file_id,grn_date
  UNION ALL SELECT ''S008'',import_file_id,grn_date FROM dbo.etp_landing_s008 WHERE grn_date IS NOT NULL GROUP BY import_file_id,grn_date
  UNION ALL SELECT ''S013'',import_file_id,stm_date FROM dbo.etp_landing_s013 WHERE stm_date IS NOT NULL GROUP BY import_file_id,stm_date
  UNION ALL SELECT ''S019'',import_file_id,repairdate FROM dbo.etp_landing_s019 WHERE repairdate IS NOT NULL GROUP BY import_file_id,repairdate
  UNION ALL SELECT ''S022'',import_file_id,invoice_date FROM dbo.etp_landing_s022 WHERE invoice_date IS NOT NULL GROUP BY import_file_id,invoice_date
  UNION ALL SELECT ''S023'',import_file_id,transdate FROM dbo.etp_landing_s023 WHERE transdate IS NOT NULL GROUP BY import_file_id,transdate
  UNION ALL SELECT ''S024'',import_file_id,transdate FROM dbo.etp_landing_s024 WHERE transdate IS NOT NULL GROUP BY import_file_id,transdate
  UNION ALL SELECT ''S025'',import_file_id,transdate FROM dbo.etp_landing_s025 WHERE transdate IS NOT NULL GROUP BY import_file_id,transdate
  UNION ALL SELECT ''S026'',import_file_id,transdate FROM dbo.etp_landing_s026 WHERE transdate IS NOT NULL GROUP BY import_file_id,transdate
  UNION ALL SELECT ''S029'',import_file_id,repair_date FROM dbo.etp_landing_s029 WHERE repair_date IS NOT NULL GROUP BY import_file_id,repair_date
  UNION ALL SELECT ''S039'',import_file_id,transaction_date FROM dbo.etp_landing_s039 WHERE transaction_date IS NOT NULL GROUP BY import_file_id,transaction_date
  UNION ALL SELECT ''S040'',import_file_id,transaction_date FROM dbo.etp_landing_s040 WHERE transaction_date IS NOT NULL GROUP BY import_file_id,transaction_date
  UNION ALL SELECT ''S041'',import_file_id,transaction_date FROM dbo.etp_landing_s041 WHERE transaction_date IS NOT NULL GROUP BY import_file_id,transaction_date
), win AS (
  SELECT d.report_code,d.import_file_id,COALESCE(f.period_end,f.business_date) snapshot_date,MIN(d.business_date) window_from,MAX(d.business_date) greatest_date
  FROM dated d JOIN dbo.import_files f ON f.import_file_id=d.import_file_id AND f.report_code=d.report_code
  JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
  WHERE f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Completed'' AND COALESCE(f.period_end,f.business_date) IS NOT NULL
  GROUP BY d.report_code,d.import_file_id,COALESCE(f.period_end,f.business_date)
), held AS (
  SELECT DISTINCT d.report_code,d.business_date
  FROM dated d JOIN win w ON w.import_file_id=d.import_file_id AND w.report_code=d.report_code
)
SELECT h.report_code,h.business_date,w.import_file_id,w.snapshot_date,
  ROW_NUMBER() OVER(PARTITION BY h.report_code,h.business_date ORDER BY w.snapshot_date DESC,w.import_file_id DESC) reading_rank
FROM held h JOIN win w ON w.report_code=h.report_code
  AND h.business_date BETWEEN w.window_from AND CASE WHEN w.greatest_date>w.snapshot_date THEN w.greatest_date ELSE w.snapshot_date END');

-- B. S004 tender collection under the DateLog rule (0048 text; the join is a hash join).
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_s004_daily AS
WITH winning AS (
  SELECT r.billingdate business_date,w.snapshot_date,w.import_file_id,r.cashamount,r.cardamount,r.upi,r.bharatpe,
    TRY_CONVERT(decimal(19,4),REPLACE(CONVERT(nvarchar(60),r.phonepe),N'','',N'''')) phonepe,
    r.chequeamount,r.rtgsamount,r.advanceamount
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s004 r
    ON w.import_file_id=r.import_file_id AND w.business_date=r.billingdate
  WHERE w.report_code=''S004'' AND w.reading_rank=1
)
SELECT x.business_date,t.tender,SUM(t.amount) amount,CONVERT(int,COUNT_BIG(*)) row_count,MAX(x.snapshot_date) snapshot_date,MAX(x.import_file_id) import_file_id
FROM winning x CROSS APPLY (VALUES
  (''CASH'',x.cashamount),(''CARD'',x.cardamount),
  (''UPI'',CASE WHEN x.upi IS NULL AND x.bharatpe IS NULL AND x.phonepe IS NULL THEN NULL ELSE COALESCE(x.upi,0)+COALESCE(x.bharatpe,0)+COALESCE(x.phonepe,0) END),
  (''CHEQUE'',x.chequeamount),(''RTGS'',x.rtgsamount),(''ADVANCE'',x.advanceamount)
) t(tender,amount)
GROUP BY x.business_date,t.tender');

-- C. Money changes, S003 and S004 (0048 rule and columns): the previous reading is the restated (superseded) reading whose
-- window covers the date, the greatest such import file first, otherwise the rank-2 covering live reading.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_money_changes AS
WITH ranked AS (
  SELECT report_code,business_date,import_file_id,snapshot_date,reading_rank,
    MAX(CASE WHEN reading_rank=2 THEN import_file_id END) OVER(PARTITION BY report_code,business_date) second_import_file_id,
    MAX(CASE WHEN reading_rank=2 THEN snapshot_date END) OVER(PARTITION BY report_code,business_date) second_snapshot_date
  FROM dbo.v_service_datelog_readings WHERE report_code IN(''S003'',''S004'') AND reading_rank<=2
), amounts AS (
  SELECT ''S003'' report_code,import_file_id,trans_date business_date,SUM(netamount_incl_tax) amount
  FROM dbo.etp_landing_s003 WHERE trans_date IS NOT NULL GROUP BY import_file_id,trans_date
  UNION ALL
  SELECT ''S004'',import_file_id,billingdate,SUM(totalamount)
  FROM dbo.etp_landing_s004 WHERE billingdate IS NOT NULL GROUP BY import_file_id,billingdate
), restated AS (
  SELECT f.superseded_by_import_file_id current_import_file_id,f.import_file_id,f.report_code,
    COALESCE(f.period_end,f.business_date) snapshot_date,MIN(a.business_date) window_from,MAX(a.business_date) greatest_date
  FROM dbo.import_files f JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
  JOIN amounts a ON a.report_code=f.report_code AND a.import_file_id=f.import_file_id
  WHERE f.is_superseded=1 AND f.data_truth_version=1 AND b.status=''Completed'' AND f.report_code IN(''S003'',''S004'')
    AND COALESCE(f.period_end,f.business_date) IS NOT NULL
  GROUP BY f.superseded_by_import_file_id,f.import_file_id,f.report_code,COALESCE(f.period_end,f.business_date)
), matched AS (
  SELECT c.report_code,c.business_date,c.import_file_id current_import_file_id,c.snapshot_date current_snapshot_date,
    c.second_import_file_id,c.second_snapshot_date,x.import_file_id restated_import_file_id,x.snapshot_date restated_snapshot_date,
    ROW_NUMBER() OVER(PARTITION BY c.report_code,c.business_date ORDER BY x.import_file_id DESC) restated_rank
  FROM ranked c LEFT JOIN restated x ON x.current_import_file_id=c.import_file_id AND x.report_code=c.report_code
    AND c.business_date BETWEEN x.window_from AND CASE WHEN x.greatest_date>x.snapshot_date THEN x.greatest_date ELSE x.snapshot_date END
  WHERE c.reading_rank=1
), previous AS (
  SELECT report_code,business_date,current_import_file_id,current_snapshot_date,
    COALESCE(restated_import_file_id,second_import_file_id) import_file_id,COALESCE(restated_snapshot_date,second_snapshot_date) snapshot_date
  FROM matched
  WHERE restated_rank=1 AND (restated_import_file_id IS NOT NULL OR second_import_file_id IS NOT NULL)
), pairs AS (
  SELECT v.report_code,v.business_date,v.snapshot_date previous_snapshot_date,v.import_file_id previous_import_file_id,
    COALESCE(pa.amount,0) previous_amount,v.current_snapshot_date,v.current_import_file_id,
    COALESCE(ca.amount,0) current_amount
  FROM previous v
  LEFT JOIN amounts ca ON ca.report_code=v.report_code AND ca.import_file_id=v.current_import_file_id AND ca.business_date=v.business_date
  LEFT JOIN amounts pa ON pa.report_code=v.report_code AND pa.import_file_id=v.import_file_id AND pa.business_date=v.business_date
)
SELECT report_code,business_date,previous_snapshot_date,previous_import_file_id,previous_amount,
  current_snapshot_date,current_import_file_id,current_amount,current_amount-previous_amount difference
FROM pairs WHERE current_amount<>previous_amount');

-- D. Purchases (0050 text and columns), with the live S007 readings and the as-at date read directly.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_parts AS
WITH live AS (
  SELECT f.import_file_id,f.report_code,COALESCE(f.period_end,f.business_date) snapshot_date
  FROM dbo.import_files f JOIN dbo.import_batches b ON b.import_batch_id=f.import_batch_id
  WHERE f.is_superseded=0 AND f.data_truth_version=1 AND b.status=''Completed''
    AND f.report_code IN(''S002'',''S003'',''S004'',''S006'',''S007'',''S008'',''S009'',''S010'',''S011'',''S012'',''S013'',''S014'',''S015'',''S016'',''S017'',''S018'',''S019'',''S020'',''S021'',''S022'',''S023'',''S024'',''S025'',''S026'',''S029'',''S030'',''S031'',''S032'',''S033'',''S034'',''S035'',''S036'',''S037'',''S039'',''S040'',''S041'')
    AND COALESCE(f.period_end,f.business_date) IS NOT NULL
), created_rows AS (
  SELECT s.import_file_id,w.snapshot_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.invoice_number))),N'''') invoice_number,s.invoice_date,
    CONVERT(nvarchar(100),s.item_id) item_id,s.shipped_quantity,s.received_quantity,s.net_amount,CONVERT(nvarchar(40),s.status) exported_status,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.grn_no))),N'''') grn_number,s.grn_date,s.received_date,CONVERT(nvarchar(60),s.from_location) from_location
  FROM dbo.etp_landing_s007 s JOIN live w ON w.import_file_id=s.import_file_id AND w.report_code=''S007''
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
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s008 s ON w.report_code=''S008'' AND w.reading_rank=1
    AND w.import_file_id=s.import_file_id AND w.business_date=s.grn_date
  WHERE NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.invoice_number))),N'''') IS NOT NULL
  GROUP BY NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),s.invoice_number))),N''''),CONVERT(nvarchar(100),s.item_id)
), snap AS (
  SELECT MAX(snapshot_date) as_at FROM live
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
FROM created c FULL OUTER HASH JOIN received r ON r.invoice_number=c.invoice_number AND r.item_id=c.item_id
CROSS JOIN snap');

-- F. Claims (0050 text and columns); each claim log is hash-joined to its winning readings.
EXEC(N'CREATE OR ALTER VIEW dbo.v_service_claims AS
WITH mb AS (
  SELECT r.import_file_id,w.snapshot_date,r.transdate business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.documentnum))),N'''') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.jonumber))),N'''') job_order_number,CONVERT(nvarchar(100),r.itemid) item_id,r.quantity,
    CONVERT(nvarchar(100),r.accountnum) account_number,r.netamountinctax net_amount_inc_tax,r.ucpamount ucp_value
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s024 r ON w.report_code=''S024'' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transdate
), wdc_new AS (
  SELECT r.import_file_id,w.snapshot_date,r.transaction_date business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.document_number))),N'''') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.job_order_number))),N'''') job_order_number,CONVERT(nvarchar(100),r.item_id) item_id,r.quantity,
    CONVERT(nvarchar(100),r.account_number) account_number,r.net_amount_inc_tax,r.ucp_amount ucp_value
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s039 r ON w.report_code=''S039'' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transaction_date
), wdc_old AS (
  SELECT r.import_file_id,w.snapshot_date,r.transdate business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.documentnum))),N'''') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.jonumber))),N'''') job_order_number,CONVERT(nvarchar(100),r.itemid) item_id,r.quantity,
    CONVERT(nvarchar(100),r.accountnum) account_number,r.netamountinctax net_amount_inc_tax,r.ucpamount ucp_value
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s025 r ON w.report_code=''S025'' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transdate
), wra_new AS (
  SELECT r.import_file_id,w.snapshot_date,r.transaction_date business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.document_number))),N'''') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.job_order_number))),N'''') job_order_number,CONVERT(nvarchar(100),r.item_id) item_id,r.quantity,
    CONVERT(nvarchar(100),r.account_number) account_number,r.net_amount_inc_tax,r.ucp_amount ucp_value
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s040 r ON w.report_code=''S040'' AND w.reading_rank=1
    AND w.import_file_id=r.import_file_id AND w.business_date=r.transaction_date
), wra_old AS (
  SELECT r.import_file_id,w.snapshot_date,r.transdate business_date,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.documentnum))),N'''') document_number,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),r.jonumber))),N'''') job_order_number,CONVERT(nvarchar(100),r.itemid) item_id,r.quantity,
    CONVERT(nvarchar(100),r.accountnum) account_number,r.netamountinctax net_amount_inc_tax,r.ucpamount ucp_value
  FROM dbo.v_service_datelog_readings w INNER HASH JOIN dbo.etp_landing_s026 r ON w.report_code=''S026'' AND w.reading_rank=1
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
  AND (h.document_number IS NOT NULL OR NOT EXISTS(SELECT 1 FROM wra_new c WHERE c.business_date=h.business_date))
  AND NOT EXISTS(SELECT 1 FROM wdc_old c WHERE c.document_number=h.document_number)
  AND NOT EXISTS(SELECT 1 FROM wdc_new c WHERE c.document_number=h.document_number)');

-- E. v_service_job, 0056's text with one change: names is a branch of facts_by_job (nm_customer_name) and agg takes its
-- MAX, instead of LEFT JOIN names at the end. Everything else, including won_rows and the role rule, is 0056's.
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
  SELECT x.job_order_number,MAX(l.snapshot_date) state_snapshot,
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
  SELECT h.job_order_number,MAX(CASE WHEN s.srn_received_date IS NULL AND s.repaired_date IS NULL AND COALESCE(CONVERT(nvarchar(80),s.to_status),N'''') NOT LIKE ''%Received%'' THEN s.srn_date END) srn_date,MAX(s.srn_date) srn_any_date,MIN(s.joborder_date) job_date,MAX(CONVERT(nvarchar(60),s.to_store)) to_store,
    MAX(CONVERT(nvarchar(200),s.brand)) brand,MAX(CONVERT(nvarchar(200),s.customer_name)) customer_name,
    MAX(CASE WHEN s.srn_received_date IS NULL AND s.repaired_date IS NULL AND COALESCE(CONVERT(nvarchar(80),s.to_status),N'''') NOT LIKE ''%Received%'' THEN 1 ELSE 0 END) srn_open,
    MAX(CASE WHEN (s.srn_received_date IS NOT NULL OR s.repaired_date IS NOT NULL OR COALESCE(CONVERT(nvarchar(80),s.to_status),N'''') LIKE ''%Received%'') THEN COALESCE(s.srn_received_date,s.repaired_date) END) srn_back_date,
    MAX(CASE WHEN (s.srn_received_date IS NOT NULL OR s.repaired_date IS NOT NULL OR COALESCE(CONVERT(nvarchar(80),s.to_status),N'''') LIKE ''%Received%'') AND CONVERT(nvarchar(80),s.to_status) LIKE ''%DC%'' THEN 1 ELSE 0 END) srn_closed_dc
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
), won_rows AS (
  SELECT d.report_code,d.job_order_number,d.labour_charge,d.spare_charge,d.net_incl_tax,d.document_number,d.srf_date,d.repair_date,d.delivered_date,d.status,d.brand,
    ROW_NUMBER() OVER(PARTITION BY d.job_order_number,d.document_number ORDER BY d.import_file_id) document_rank
  FROM dated d JOIN dated_files f ON f.import_file_id=d.import_file_id AND f.report_code=d.report_code
  WHERE d.job_order_number IS NOT NULL AND d.business_date IS NOT NULL
    AND NOT EXISTS(SELECT 1 FROM dated_files g WHERE g.report_code=d.report_code AND d.business_date BETWEEN g.window_from AND g.window_to
      AND (g.snapshot_date>f.snapshot_date OR (g.snapshot_date=f.snapshot_date AND g.import_file_id>f.import_file_id)))
), won AS (
  SELECT d.job_order_number,
    SUM(CASE WHEN d.report_code=''S003'' THEN d.labour_charge END) revenue_labour_charge,SUM(CASE WHEN d.report_code=''S003'' THEN d.spare_charge END) revenue_spare_charge,
    SUM(CASE WHEN d.report_code=''S003'' THEN d.net_incl_tax END) revenue_net_incl_tax,SUM(CASE WHEN d.document_number IS NOT NULL AND d.document_rank=1 THEN 1 ELSE 0 END) revenue_documents,
    MIN(d.srf_date) srf_date,MAX(d.repair_date) repair_date,MAX(d.delivered_date) delivered_date,
    MAX(CASE WHEN d.status=''Delivered'' AND d.delivered_date IS NOT NULL THEN 1 ELSE 0 END) is_delivered,
    MAX(CASE WHEN d.status IN(''RWR'',''Returned_Without_Repair'') THEN 1 ELSE 0 END) is_rwr,MAX(d.brand) brand
  FROM won_rows d
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
  SELECT o.job_order_number
  FROM other_rows o JOIN live l ON l.import_file_id=o.import_file_id AND l.report_code=o.report_code
  WHERE o.job_order_number IS NOT NULL
  GROUP BY o.job_order_number
), claimed AS (
  SELECT c.job_order_number,MAX(CASE WHEN c.claim_type=''WDC'' THEN 1 ELSE 0 END) wdc_claimed,MAX(CASE WHEN c.claim_type=''WRA'' THEN 1 ELSE 0 END) wra_claimed,
    MIN(CASE WHEN c.claim_type=''WDC'' THEN c.business_date END) wdc_claim_date,MIN(CASE WHEN c.claim_type=''WRA'' THEN c.business_date END) wra_claim_date
  FROM dbo.v_service_claims c
  WHERE c.claim_type IN(''WDC'',''WRA'') AND c.job_order_number IS NOT NULL
  GROUP BY c.job_order_number
), name_rows AS (
  SELECT report_code,import_file_id,job_order_number,customer_name FROM dbo.v_service_status_view_facts
  UNION ALL SELECT ''S009'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''''),CONVERT(nvarchar(200),customername) FROM dbo.etp_landing_s009
  UNION ALL SELECT ''S010'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''''),CONVERT(nvarchar(200),customername) FROM dbo.etp_landing_s010
  UNION ALL SELECT ''S011'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N''''),CONVERT(nvarchar(200),customer_name) FROM dbo.etp_landing_s011
  UNION ALL SELECT ''S012'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''''),CONVERT(nvarchar(200),customername) FROM dbo.etp_landing_s012
  UNION ALL SELECT ''S020'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),CONVERT(nvarchar(200),customername) FROM dbo.etp_landing_s020
  UNION ALL SELECT ''S021'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''''),CONVERT(nvarchar(200),customername) FROM dbo.etp_landing_s021
  UNION ALL SELECT ''S029'',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),srfno))),N''''),CONVERT(nvarchar(200),customer_name) FROM dbo.etp_landing_s029
), names AS (
  SELECT x.job_order_number,x.customer_name FROM (
    SELECT n.job_order_number,n.customer_name,
      ROW_NUMBER() OVER(PARTITION BY n.job_order_number ORDER BY l.snapshot_date DESC,n.import_file_id DESC,n.customer_name DESC) name_rank
    FROM (SELECT report_code,import_file_id,job_order_number,NULLIF(LTRIM(RTRIM(customer_name)),N'''') customer_name FROM name_rows) n
    JOIN live l ON l.import_file_id=n.import_file_id AND l.report_code=n.report_code
    WHERE n.job_order_number IS NOT NULL AND n.customer_name IS NOT NULL
  ) x WHERE x.name_rank=1
), facts_by_job AS (
  SELECT job_order_number,CONVERT(date,CASE WHEN report_code IN(''S009'',''S010'') THEN NULL ELSE snapshot_date END) last_reading_date,CONVERT(date,NULL) sv_job_date,CONVERT(date,NULL) sv_edd,CONVERT(nvarchar(200),NULL) sv_brand,CONVERT(nvarchar(200),NULL) sv_model,CONVERT(nvarchar(200),NULL) sv_product_category,CONVERT(nvarchar(200),NULL) sv_customer_name,CONVERT(nvarchar(200),NULL) sv_guarantee,CONVERT(nvarchar(200),NULL) sv_customer_type,CONVERT(date,NULL) sv_delivery_date,CONVERT(date,NULL) sv_delivered_repair_date,CONVERT(date,NULL) sv_rwr_date,CONVERT(nvarchar(200),NULL) sv_rwr_reason,CONVERT(date,NULL) sv_wdc_date,CONVERT(nvarchar(200),NULL) sv_wdc_number,CONVERT(date,NULL) sv_wra_date,CONVERT(nvarchar(200),NULL) sv_radc_number,CONVERT(date,NULL) sv_pd_repair_date,CONVERT(date,NULL) sv_srn_issued_date,CONVERT(nvarchar(200),NULL) sv_srn_to_store_code,CONVERT(date,NULL) sv_indent_date,CONVERT(int,NULL) sv_in_delivered,CONVERT(int,NULL) sv_in_rwr,CONVERT(int,NULL) sv_in_dc,CONVERT(int,NULL) sv_in_ra,CONVERT(int,NULL) sv_in_pd,CONVERT(int,NULL) sv_in_srn,CONVERT(int,NULL) sv_in_ir,CONVERT(int,NULL) sv_in_pr,CONVERT(decimal(19,4),NULL) vm_spare_value,CONVERT(decimal(19,4),NULL) vm_labour_charge,CONVERT(date,NULL) ls_s002_created,CONVERT(date,NULL) ls_list_created,CONVERT(nvarchar(200),NULL) ls_jo_type,CONVERT(nvarchar(200),NULL) ls_exported_status,CONVERT(nvarchar(200),NULL) ls_brand,CONVERT(nvarchar(200),NULL) ls_product_category,CONVERT(nvarchar(200),NULL) ls_guarantee,CONVERT(nvarchar(200),NULL) ls_customer_type,CONVERT(int,NULL) st9_in_s009,CONVERT(int,NULL) st9_in_s010,CONVERT(date,NULL) st9_s009_job_date,CONVERT(date,NULL) st9_s010_job_date,CONVERT(date,NULL) st9_s009_edd,CONVERT(nvarchar(200),NULL) st9_s009_status,CONVERT(nvarchar(200),NULL) st9_s009_store,CONVERT(nvarchar(200),NULL) st9_s010_store,CONVERT(date,NULL) st9_s009_indent_date,CONVERT(nvarchar(200),NULL) st9_s009_indent_id,CONVERT(date,NULL) st9_s010_repair_date,CONVERT(nvarchar(200),NULL) st9_spare_required,CONVERT(nvarchar(200),NULL) st9_brand,CONVERT(nvarchar(200),NULL) st9_model,CONVERT(nvarchar(200),NULL) st9_product_category,CONVERT(nvarchar(200),NULL) st9_customer_name,CONVERT(date,NULL) srn_srn_date,CONVERT(date,NULL) srn_job_date,CONVERT(nvarchar(200),NULL) srn_to_store,CONVERT(nvarchar(200),NULL) srn_brand,CONVERT(nvarchar(200),NULL) srn_customer_name,CONVERT(int,NULL) srn_srn_open,CONVERT(date,NULL) srn_srn_back_date,CONVERT(int,NULL) srn_srn_closed_dc,CONVERT(decimal(19,4),NULL) w_revenue_labour_charge,CONVERT(decimal(19,4),NULL) w_revenue_spare_charge,CONVERT(decimal(19,4),NULL) w_revenue_net_incl_tax,CONVERT(int,NULL) w_revenue_documents,CONVERT(date,NULL) w_srf_date,CONVERT(date,NULL) w_repair_date,CONVERT(date,NULL) w_delivered_date,CONVERT(int,NULL) w_is_delivered,CONVERT(int,NULL) w_is_rwr,CONVERT(nvarchar(200),NULL) w_brand,CONVERT(int,NULL) o_wdc_claimed,CONVERT(int,NULL) o_wra_claimed,CONVERT(date,NULL) srn_srn_any_date,CONVERT(date,NULL) o_wdc_claim_date,CONVERT(date,NULL) o_wra_claim_date,CONVERT(nvarchar(200),NULL) nm_customer_name FROM held
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,job_date),CONVERT(date,edd),CONVERT(nvarchar(200),brand),CONVERT(nvarchar(200),model),CONVERT(nvarchar(200),product_category),CONVERT(nvarchar(200),customer_name),CONVERT(nvarchar(200),guarantee),CONVERT(nvarchar(200),customer_type),CONVERT(date,delivery_date),CONVERT(date,delivered_repair_date),CONVERT(date,rwr_date),CONVERT(nvarchar(200),rwr_reason),CONVERT(date,wdc_date),CONVERT(nvarchar(200),wdc_number),CONVERT(date,wra_date),CONVERT(nvarchar(200),radc_number),CONVERT(date,pd_repair_date),CONVERT(date,srn_issued_date),CONVERT(nvarchar(200),srn_to_store_code),CONVERT(date,indent_date),CONVERT(int,in_delivered),CONVERT(int,in_rwr),CONVERT(int,in_dc),CONVERT(int,in_ra),CONVERT(int,in_pd),CONVERT(int,in_srn),CONVERT(int,in_ir),CONVERT(int,in_pr),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL) FROM sv
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),spare_value),CONVERT(decimal(19,4),labour_charge),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL) FROM view_money
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,s002_created),CONVERT(date,list_created),CONVERT(nvarchar(200),jo_type),CONVERT(nvarchar(200),exported_status),CONVERT(nvarchar(200),brand),CONVERT(nvarchar(200),product_category),CONVERT(nvarchar(200),guarantee),CONVERT(nvarchar(200),customer_type),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL) FROM lists
  UNION ALL
  SELECT job_order_number,CONVERT(date,state_snapshot),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,in_s009),CONVERT(int,in_s010),CONVERT(date,s009_job_date),CONVERT(date,s010_job_date),CONVERT(date,s009_edd),CONVERT(nvarchar(200),s009_status),CONVERT(nvarchar(200),s009_store),CONVERT(nvarchar(200),s010_store),CONVERT(date,s009_indent_date),CONVERT(nvarchar(200),s009_indent_id),CONVERT(date,s010_repair_date),CONVERT(nvarchar(200),spare_required),CONVERT(nvarchar(200),brand),CONVERT(nvarchar(200),model),CONVERT(nvarchar(200),product_category),CONVERT(nvarchar(200),customer_name),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL) FROM state
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,srn_date),CONVERT(date,job_date),CONVERT(nvarchar(200),to_store),CONVERT(nvarchar(200),brand),CONVERT(nvarchar(200),customer_name),CONVERT(int,srn_open),CONVERT(date,srn_back_date),CONVERT(int,srn_closed_dc),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,srn_any_date),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL) FROM srn
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),revenue_labour_charge),CONVERT(decimal(19,4),revenue_spare_charge),CONVERT(decimal(19,4),revenue_net_incl_tax),CONVERT(int,revenue_documents),CONVERT(date,srf_date),CONVERT(date,repair_date),CONVERT(date,delivered_date),CONVERT(int,is_delivered),CONVERT(int,is_rwr),CONVERT(nvarchar(200),brand),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL) FROM won
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL) FROM other_jobs
  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,wdc_claimed),CONVERT(int,wra_claimed),CONVERT(date,NULL),CONVERT(date,wdc_claim_date),CONVERT(date,wra_claim_date),CONVERT(nvarchar(200),NULL) FROM claimed

  UNION ALL
  SELECT job_order_number,CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(decimal(19,4),NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(nvarchar(200),NULL),CONVERT(int,NULL),CONVERT(int,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(date,NULL),CONVERT(nvarchar(200),customer_name) FROM names), agg AS (
  SELECT u.job_order_number,MAX(u.last_reading_date) last_reading_date,MAX(u.sv_job_date) sv_job_date,MAX(u.sv_edd) sv_edd,MAX(u.sv_brand) sv_brand,MAX(u.sv_model) sv_model,MAX(u.sv_product_category) sv_product_category,MAX(u.sv_customer_name) sv_customer_name,MAX(u.sv_guarantee) sv_guarantee,MAX(u.sv_customer_type) sv_customer_type,MAX(u.sv_delivery_date) sv_delivery_date,MAX(u.sv_delivered_repair_date) sv_delivered_repair_date,MAX(u.sv_rwr_date) sv_rwr_date,MAX(u.sv_rwr_reason) sv_rwr_reason,MAX(u.sv_wdc_date) sv_wdc_date,MAX(u.sv_wdc_number) sv_wdc_number,MAX(u.sv_wra_date) sv_wra_date,MAX(u.sv_radc_number) sv_radc_number,MAX(u.sv_pd_repair_date) sv_pd_repair_date,MAX(u.sv_srn_issued_date) sv_srn_issued_date,MAX(u.sv_srn_to_store_code) sv_srn_to_store_code,MAX(u.sv_indent_date) sv_indent_date,MAX(u.sv_in_delivered) sv_in_delivered,MAX(u.sv_in_rwr) sv_in_rwr,MAX(u.sv_in_dc) sv_in_dc,MAX(u.sv_in_ra) sv_in_ra,MAX(u.sv_in_pd) sv_in_pd,MAX(u.sv_in_srn) sv_in_srn,MAX(u.sv_in_ir) sv_in_ir,MAX(u.sv_in_pr) sv_in_pr,MAX(u.vm_spare_value) vm_spare_value,MAX(u.vm_labour_charge) vm_labour_charge,MAX(u.ls_s002_created) ls_s002_created,MAX(u.ls_list_created) ls_list_created,MAX(u.ls_jo_type) ls_jo_type,MAX(u.ls_exported_status) ls_exported_status,MAX(u.ls_brand) ls_brand,MAX(u.ls_product_category) ls_product_category,MAX(u.ls_guarantee) ls_guarantee,MAX(u.ls_customer_type) ls_customer_type,MAX(u.st9_in_s009) st9_in_s009,MAX(u.st9_in_s010) st9_in_s010,MAX(u.st9_s009_job_date) st9_s009_job_date,MAX(u.st9_s010_job_date) st9_s010_job_date,MAX(u.st9_s009_edd) st9_s009_edd,MAX(u.st9_s009_status) st9_s009_status,MAX(u.st9_s009_store) st9_s009_store,MAX(u.st9_s010_store) st9_s010_store,MAX(u.st9_s009_indent_date) st9_s009_indent_date,MAX(u.st9_s009_indent_id) st9_s009_indent_id,MAX(u.st9_s010_repair_date) st9_s010_repair_date,MAX(u.st9_spare_required) st9_spare_required,MAX(u.st9_brand) st9_brand,MAX(u.st9_model) st9_model,MAX(u.st9_product_category) st9_product_category,MAX(u.st9_customer_name) st9_customer_name,MAX(u.srn_srn_date) srn_srn_date,MAX(u.srn_job_date) srn_job_date,MAX(u.srn_to_store) srn_to_store,MAX(u.srn_brand) srn_brand,MAX(u.srn_customer_name) srn_customer_name,MAX(u.srn_srn_open) srn_srn_open,MAX(u.srn_srn_back_date) srn_srn_back_date,MAX(u.srn_srn_closed_dc) srn_srn_closed_dc,MAX(u.w_revenue_labour_charge) w_revenue_labour_charge,MAX(u.w_revenue_spare_charge) w_revenue_spare_charge,MAX(u.w_revenue_net_incl_tax) w_revenue_net_incl_tax,MAX(u.w_revenue_documents) w_revenue_documents,MAX(u.w_srf_date) w_srf_date,MAX(u.w_repair_date) w_repair_date,MAX(u.w_delivered_date) w_delivered_date,MAX(u.w_is_delivered) w_is_delivered,MAX(u.w_is_rwr) w_is_rwr,MAX(u.w_brand) w_brand,MAX(u.o_wdc_claimed) o_wdc_claimed,MAX(u.o_wra_claimed) o_wra_claimed,MAX(u.srn_srn_any_date) srn_srn_any_date,MAX(u.o_wdc_claim_date) o_wdc_claim_date,MAX(u.o_wra_claim_date) o_wra_claim_date,MAX(u.nm_customer_name) nm_customer_name
  FROM facts_by_job u WHERE u.job_order_number IS NOT NULL GROUP BY u.job_order_number
), snap AS (
  SELECT MAX(snapshot_date) as_at FROM live
)
SELECT a.job_order_number,bd.booking_date,COALESCE(a.ls_jo_type,N''Booking'') jo_type,a.ls_exported_status exported_status,
  COALESCE(a.sv_brand,a.st9_brand,a.ls_brand,a.srn_brand,a.w_brand) brand,COALESCE(a.sv_model,a.st9_model) model,
  COALESCE(a.sv_product_category,a.st9_product_category,a.ls_product_category) product_category,
  CASE WHEN IS_MEMBER(N''etp_owner'')=1 OR IS_MEMBER(N''etp_store_manager'')=1 OR IS_MEMBER(N''db_owner'')=1
       THEN a.nm_customer_name END customer_name,
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
  CASE WHEN sg.stage NOT IN(''DELIVERED'',''RWR'') THEN DATEDIFF(day,bd.booking_date,COALESCE(a.last_reading_date,snap.as_at)) END age_days,
  CASE WHEN sg.stage NOT IN(''DELIVERED'',''RWR'') THEN DATEDIFF(day,sd.stage_date,COALESCE(a.last_reading_date,snap.as_at)) END days_in_stage,
  CONVERT(bit,CASE WHEN sg.stage IN(''DELIVERED'',''RWR'') THEN 0 WHEN sg.stage=''DC_ISSUED'' AND COALESCE(a.o_wdc_claimed,0)=1 THEN 0
       WHEN sg.stage=''RA_ISSUED'' AND COALESCE(a.o_wra_claimed,0)=1 THEN 0 WHEN COALESCE(a.st9_s009_edd,a.sv_edd)<COALESCE(a.last_reading_date,snap.as_at) THEN 1 ELSE 0 END) is_overdue,
  CONVERT(bit,CASE WHEN sg.stage IN(''DELIVERED'',''RWR'') THEN 0 WHEN sg.stage=''DC_ISSUED'' AND COALESCE(a.o_wdc_claimed,0)=1 THEN 0
       WHEN sg.stage=''RA_ISSUED'' AND COALESCE(a.o_wra_claimed,0)=1 THEN 0 ELSE 1 END) is_open,
  a.last_reading_date,snap.as_at,
  CASE sg.stage WHEN ''DC_ISSUED'' THEN a.o_wdc_claim_date WHEN ''RA_ISSUED'' THEN a.o_wra_claim_date END claim_date,
  CASE WHEN sg.stage IN(''DELIVERED'',''RWR'') THEN sd.stage_date WHEN sg.stage=''DC_ISSUED'' AND COALESCE(a.o_wdc_claimed,0)=1 THEN a.o_wdc_claim_date
       WHEN sg.stage=''RA_ISSUED'' AND COALESCE(a.o_wra_claimed,0)=1 THEN a.o_wra_claim_date END closed_date
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
         CASE WHEN a.st9_s009_status LIKE ''SRN%'' AND a.st9_s009_status<>''SRN_For_Repair'' THEN ''SRN_OUT''
              WHEN a.st9_s009_status=''Indent_Raised'' OR a.st9_s009_indent_id IS NOT NULL OR a.st9_s009_indent_date IS NOT NULL THEN ''INDENT_RAISED''
              ELSE ''ON_BENCH'' END
       WHEN a.srn_srn_open=1 OR (a.sv_in_srn=1 AND a.srn_srn_open IS NULL) THEN ''SRN_OUT''
       WHEN a.sv_in_ir=1 THEN ''INDENT_RAISED''
       WHEN a.sv_in_pr=1 THEN ''ON_BENCH''
       WHEN a.srn_srn_open=0 AND a.srn_srn_closed_dc=1 THEN ''DC_ISSUED''
       WHEN a.srn_srn_open=0 THEN ''READY_FOR_DELIVERY''
       ELSE ''BOOKED'' END stage) sg
CROSS APPLY (SELECT
  CASE sg.stage WHEN ''DELIVERED'' THEN COALESCE(a.sv_delivery_date,a.w_delivered_date)
       WHEN ''RWR'' THEN COALESCE(a.sv_rwr_date,a.w_repair_date)
       WHEN ''DC_ISSUED'' THEN COALESCE(a.sv_wdc_date,a.srn_srn_back_date,a.srn_srn_any_date)
       WHEN ''RA_ISSUED'' THEN a.sv_wra_date
       WHEN ''IN_TRANSIT_BACK'' THEN COALESCE(a.st9_s010_repair_date,a.st9_s010_job_date)
       WHEN ''READY_FOR_DELIVERY'' THEN COALESCE(a.st9_s010_repair_date,a.sv_pd_repair_date,a.st9_s010_job_date,a.srn_srn_back_date,a.srn_srn_any_date)
       WHEN ''SRN_OUT'' THEN COALESCE(a.srn_srn_date,a.sv_srn_issued_date,a.st9_s009_job_date,bd.booking_date)
       WHEN ''INDENT_RAISED'' THEN COALESCE(a.st9_s009_indent_date,a.sv_indent_date,a.st9_s009_job_date,bd.booking_date)
       WHEN ''ON_BENCH'' THEN COALESCE(a.st9_s009_job_date,a.sv_job_date,bd.booking_date)
       ELSE bd.booking_date END stage_date) sd');

GRANT SELECT ON dbo.v_service_datelog_readings TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_datelog_readings TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_s004_daily TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_s004_daily TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_money_changes TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_money_changes TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_parts TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_parts TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_claims TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_claims TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_job TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_job TO etp_store_manager,etp_viewer;
