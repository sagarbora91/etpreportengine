CREATE OR ALTER VIEW dbo.v_service_job_timeline AS
WITH rows AS (
  SELECT 'S002' report_code,import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N'') job_order_number,created_date event_date,
    CONVERT(nvarchar(200),current_status) status_text,CONVERT(nvarchar(200),NULL) pending_store,CONVERT(nvarchar(100),NULL) document_number,CONVERT(decimal(19,4),NULL) amount
  FROM dbo.etp_landing_s002
  UNION ALL SELECT 'S036',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N''),created_date,CONVERT(nvarchar(200),current_status),NULL,NULL,NULL FROM dbo.etp_landing_s036
  UNION ALL SELECT 'S037',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_no))),N''),created_date,CONVERT(nvarchar(200),current_status),NULL,NULL,NULL FROM dbo.etp_landing_s037
  UNION ALL SELECT f.report_code,f.import_file_id,f.job_order_number,
    CASE f.report_code WHEN 'S014' THEN f.wdc_date WHEN 'S015' THEN f.indent_date WHEN 'S016' THEN f.wra_date WHEN 'S017' THEN f.rwr_date
      WHEN 'S018' THEN f.delivery_date WHEN 'S031' THEN f.repair_date WHEN 'S032' THEN f.job_date WHEN 'S033' THEN f.srn_issued_date
      WHEN 'S034' THEN f.repair_date WHEN 'S035' THEN f.srn_return_date END,
    CONVERT(nvarchar(200),f.repair_status),f.to_store_code,
    CASE f.report_code WHEN 'S014' THEN f.wdc_number WHEN 'S016' THEN f.radc_number WHEN 'S015' THEN f.indent_number END,f.spare_value
  FROM dbo.v_service_status_view_facts f
  UNION ALL SELECT 'S009',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''),jodate,CONVERT(nvarchar(200),jostatus),CONVERT(nvarchar(200),pendingstore),
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),indentid))),N''),NULL FROM dbo.etp_landing_s009
  UNION ALL SELECT 'S010',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''),repairdate,CONVERT(nvarchar(200),jostatus),CONVERT(nvarchar(200),pendingstore),NULL,NULL FROM dbo.etp_landing_s010
  UNION ALL SELECT 'S011',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N''),srn_date,CONVERT(nvarchar(200),to_status),CONVERT(nvarchar(200),to_store),
    CONVERT(nvarchar(100),srndc_number),amount FROM dbo.etp_landing_s011
  UNION ALL SELECT 'S012',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''),COALESCE(srnrepaireddate,jodate),CONVERT(nvarchar(200),srnstatus),CONVERT(nvarchar(200),tostore),
    CONVERT(nvarchar(100),srnno),total FROM dbo.etp_landing_s012
  UNION ALL SELECT 'S003',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N''),trans_date,CONVERT(nvarchar(200),job_status),NULL,
    CONVERT(nvarchar(100),documentnum),netamount_incl_tax FROM dbo.etp_landing_s003
  UNION ALL SELECT 'S019',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jo_number))),N''),repairdate,CONVERT(nvarchar(200),reason),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),previousjo))),N''),NULL FROM dbo.etp_landing_s019
  UNION ALL SELECT 'S020',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''),radate,CONVERT(nvarchar(200),wrastatus_1),CONVERT(nvarchar(200),tolocation),
    CONVERT(nvarchar(100),dcnumber),deductioncharges FROM dbo.etp_landing_s020
  UNION ALL SELECT 'S021',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''),dcdate,CONVERT(nvarchar(200),status),CONVERT(nvarchar(200),tolocation),
    CONVERT(nvarchar(100),documentnumber),wdcvalue FROM dbo.etp_landing_s021
  UNION ALL SELECT 'S022',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),joborder_number))),N''),invoice_date,CONVERT(nvarchar(200),empowerment_type),NULL,
    CONVERT(nvarchar(100),invoice_number),empowerment_value FROM dbo.etp_landing_s022
  UNION ALL SELECT 'S029',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),srfno))),N''),COALESCE(delivered_date,repair_date),CONVERT(nvarchar(200),status),NULL,
    CONVERT(nvarchar(100),srn_number),labour_charge_actual FROM dbo.etp_landing_s029
  UNION ALL SELECT 'S030',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_number))),N''),running_test_date,CONVERT(nvarchar(200),result),NULL,NULL,NULL FROM dbo.etp_landing_s030
  UNION ALL SELECT 'S023',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''),transdate,CONVERT(nvarchar(200),accountnum),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),documentnum))),N''),netamountinctax FROM dbo.etp_landing_s023
  UNION ALL SELECT 'S024',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''),transdate,CONVERT(nvarchar(200),accountnum),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),documentnum))),N''),netamountinctax FROM dbo.etp_landing_s024
  UNION ALL SELECT 'S025',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''),transdate,CONVERT(nvarchar(200),accountnum),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),documentnum))),N''),netamountinctax FROM dbo.etp_landing_s025
  UNION ALL SELECT 'S026',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jonumber))),N''),transdate,CONVERT(nvarchar(200),accountnum),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),documentnum))),N''),netamountinctax FROM dbo.etp_landing_s026
  UNION ALL SELECT 'S039',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_number))),N''),transaction_date,CONVERT(nvarchar(200),account_number),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),document_number))),N''),net_amount_inc_tax FROM dbo.etp_landing_s039
  UNION ALL SELECT 'S040',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_number))),N''),transaction_date,CONVERT(nvarchar(200),account_number),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),document_number))),N''),net_amount_inc_tax FROM dbo.etp_landing_s040
  UNION ALL SELECT 'S041',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),job_order_number))),N''),transaction_date,CONVERT(nvarchar(200),account_number),NULL,
    NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),document_number))),N''),net_amount_inc_tax FROM dbo.etp_landing_s041
)
SELECT r.job_order_number,w.snapshot_date,w.source_kind,r.report_code,
  CASE r.report_code WHEN 'S036' THEN 'Delivery-type jobs' WHEN 'S037' THEN 'Repair-type jobs' ELSE f.list_label END list_label,
  r.event_date,r.status_text,r.pending_store,r.document_number,SUM(r.amount) amount,CONVERT(int,COUNT_BIG(*)) lines,r.import_file_id
FROM rows r JOIN dbo.v_service_readings w ON w.import_file_id=r.import_file_id AND w.report_code=r.report_code
JOIN dbo.v_service_families f ON f.report_code=r.report_code
WHERE r.job_order_number IS NOT NULL
GROUP BY r.job_order_number,w.snapshot_date,w.source_kind,r.report_code,f.list_label,r.event_date,r.status_text,r.pending_store,r.document_number,r.import_file_id
-- >>> GRANTS
DENY DELETE ON dbo.v_service_job_timeline TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_job_timeline TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_job_timeline TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_job_timeline TO etp_owner,etp_store_manager,etp_viewer;
