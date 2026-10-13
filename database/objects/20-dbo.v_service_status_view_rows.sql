CREATE OR ALTER VIEW dbo.v_service_status_view_rows AS
SELECT 'S014' report_code,import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N'') job_order_number,wdcdate status_date,jodate job_date,edd,
  CONVERT(nvarchar(200),brandname) brand,CONVERT(nvarchar(200),variantnumber) model,CONVERT(nvarchar(200),clusterid) product_category,CASE WHEN IS_MEMBER(N'etp_owner')=1 OR IS_MEMBER(N'etp_store_manager')=1 OR IS_MEMBER(N'db_owner')=1 THEN CONVERT(nvarchar(200),customername) END customer_name,sparevalue spare_value,labourcharge labour_charge
FROM dbo.etp_landing_s014
UNION ALL SELECT 'S015',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''),indentdate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N'etp_owner')=1 OR IS_MEMBER(N'etp_store_manager')=1 OR IS_MEMBER(N'db_owner')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s015
UNION ALL SELECT 'S016',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''),wradate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N'etp_owner')=1 OR IS_MEMBER(N'etp_store_manager')=1 OR IS_MEMBER(N'db_owner')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s016
UNION ALL SELECT 'S017',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''),normalrwrdate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N'etp_owner')=1 OR IS_MEMBER(N'etp_store_manager')=1 OR IS_MEMBER(N'db_owner')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s017
UNION ALL SELECT 'S018',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''),deliverydate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N'etp_owner')=1 OR IS_MEMBER(N'etp_store_manager')=1 OR IS_MEMBER(N'db_owner')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s018
UNION ALL SELECT 'S031',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''),jorepairdate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N'etp_owner')=1 OR IS_MEMBER(N'etp_store_manager')=1 OR IS_MEMBER(N'db_owner')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s031
UNION ALL SELECT 'S032',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''),jodate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N'etp_owner')=1 OR IS_MEMBER(N'etp_store_manager')=1 OR IS_MEMBER(N'db_owner')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s032
UNION ALL SELECT 'S033',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''),srnissueddate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N'etp_owner')=1 OR IS_MEMBER(N'etp_store_manager')=1 OR IS_MEMBER(N'db_owner')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s033
UNION ALL SELECT 'S034',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''),jorepairdate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N'etp_owner')=1 OR IS_MEMBER(N'etp_store_manager')=1 OR IS_MEMBER(N'db_owner')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s034
UNION ALL SELECT 'S035',import_file_id,NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(100),jobordernumber))),N''),srnreturndate,jodate,edd,
  CONVERT(nvarchar(200),brandname),CONVERT(nvarchar(200),variantnumber),CONVERT(nvarchar(200),clusterid),CASE WHEN IS_MEMBER(N'etp_owner')=1 OR IS_MEMBER(N'etp_store_manager')=1 OR IS_MEMBER(N'db_owner')=1 THEN CONVERT(nvarchar(200),customername) END,sparevalue,labourcharge
FROM dbo.etp_landing_s035
-- >>> GRANTS
DENY DELETE ON dbo.v_service_status_view_rows TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_status_view_rows TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_status_view_rows TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_status_view_rows TO etp_owner,etp_store_manager,etp_viewer;
