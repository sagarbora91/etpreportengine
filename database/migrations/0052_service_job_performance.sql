-- Service job performance (1.10.0, lane perf, 10 Oct 2026): v_service_job without the COUNT(DISTINCT) plan.
-- Design: docs/roadmap/SERVICE-CENTRE-UI-DESIGN-REVIEW-2026-10-10.md 4.1 and 6.5 (SD-11, "0052 if over about 1 s").
--
-- Numbering: 0049 is reserved for the Tally cost-centre migration (PR #3) and 0051 for the staff-target permission
-- migration; until both ship beside this file the migration contiguity test (MigrationTests) fails on this branch.
--
-- Why (read-only plan analysis on live, EtpReporting as at 3 Oct 2026, 3,158 jobs): 0050's v_service_job took 3.0 s to
-- execute (4.7-5.1 s with its 1.6 s compile). 2.3 s of the 3.1 s CPU was one operator: the COUNT(DISTINCT
-- document_number) in the won CTE made the optimiser split won into two aggregates over a spool and join them back on
-- the job key; the NOT EXISTS of the DateLog rule estimates 1.5 rows, so that join became nested loops over a lazy spool
-- (2,968 x 2,968 = 8.8 million spool rows, 157,000 worktable reads). The landing-table reads are small (18 MB in all,
-- cached) and the import_file_id indexes are already used, so supporting indexes would save at most about a tenth;
-- a materialised job table (design 4.1 service_job_index) is not needed at this size.
-- Fix: won_rows ranks each (job, document number) once with ROW_NUMBER and won counts rank 1 of the non-null document
-- numbers, which is COUNT(DISTINCT document_number) by definition. Nothing else in the view changes; its columns, rows
-- and values are identical (same CHECKSUM_AGG over every column of all 3,158 live rows). After: 0.93-1.05 s to execute,
-- 0.7-0.9 s for the open jobs, 0.35 s for one job (110-PERF-DONE.md).
--
-- The migration runner applies this entire script in one transaction, so it has no BEGIN or COMMIT. It is idempotent:
-- CREATE OR ALTER through EXEC(N'...') keeps the view's permissions, and the grants of 0050 are stated again unchanged.
-- Nothing here writes data or adds a table, procedure, trigger or index; 0048 and 0050 are never edited.
SET XACT_ABORT ON;

-- >>> E_SERVICE_JOB_PERFORMANCE begin
-- Owner: lane perf. v_service_job exactly as 0050 section D_SERVICE_UI_READ defines it, except the won CTE (above).
-- ServiceJobPerformanceTextTests pins that the two texts differ only there.
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
), won_rows AS (
  SELECT d.report_code,d.job_order_number,d.labour_charge,d.spare_charge,d.net_incl_tax,d.document_number,d.srf_date,d.repair_date,d.delivered_date,d.status,d.brand,
    ROW_NUMBER() OVER(PARTITION BY d.job_order_number,d.document_number ORDER BY d.import_file_id) document_rank
  FROM dated d JOIN dated_files f ON f.import_file_id=d.import_file_id AND f.report_code=d.report_code
  WHERE d.job_order_number IS NOT NULL
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

GRANT SELECT ON dbo.v_service_job TO etp_viewer,etp_store_manager,etp_owner;
DENY INSERT,UPDATE,DELETE ON dbo.v_service_job TO etp_store_manager,etp_viewer;
-- <<< E_SERVICE_JOB_PERFORMANCE end
