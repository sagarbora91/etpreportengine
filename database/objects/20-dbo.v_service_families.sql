CREATE OR ALTER VIEW dbo.v_service_families AS
SELECT v.report_code,v.read_rule,v.date_column,v.list_label,v.pending_list,v.status_label,v.lifecycle_rank
FROM (VALUES
 ('S002','JobList',NULL,'Job booking',NULL,NULL,NULL),
 ('S003','DateLog','trans_date','RevenueReport',NULL,NULL,NULL),
 ('S004','DateLog','billingdate','TenderCollectionDetailed',NULL,NULL,NULL),
 ('S006','StateSnapshot',NULL,'Closing stock',NULL,NULL,NULL),
 ('S007','DateLog','grn_date','PurchaseRegister_CREATED',NULL,NULL,NULL),
 ('S008','DateLog','grn_date','PurchaseRegister_RECCIVED',NULL,NULL,NULL),
 ('S009','StateSnapshot',NULL,'Pending repair','PENDING_REPAIR',NULL,NULL),
 ('S010','StateSnapshot',NULL,'Pending delivery','PENDING_DELIVERY',NULL,NULL),
 ('S011','JobList',NULL,'SRN status','SRN_STATUS',NULL,NULL),
 ('S012','JobList',NULL,'SRN history',NULL,NULL,NULL),
 ('S013','DateLog','stm_date','GIT',NULL,NULL,NULL),
 ('S014','JobList',NULL,'Status DC',NULL,'DC',5),
 ('S015','JobList',NULL,'Status IR',NULL,'IR',2),
 ('S016','JobList',NULL,'Status RA',NULL,'RA',6),
 ('S017','JobList',NULL,'Status RWR',NULL,'RWR',8),
 ('S018','JobList',NULL,'Status DELIVERED',NULL,'DELIVERED',10),
 ('S019','DateLog','repairdate','RepeatReturn',NULL,NULL,NULL),
 ('S020','JobList',NULL,'Replacement',NULL,NULL,NULL),
 ('S021','JobList',NULL,'Depreciation',NULL,NULL,NULL),
 ('S022','DateLog','invoice_date','EmpowermentReport',NULL,NULL,NULL),
 ('S023','DateLog','transdate','GPRC_Report',NULL,NULL,NULL),
 ('S024','DateLog','transdate','GPRC_MB_Report',NULL,NULL,NULL),
 ('S025','DateLog','transdate','GPRC_WDC',NULL,NULL,NULL),
 ('S026','DateLog','transdate','GPRC_WRA_Report',NULL,NULL,NULL),
 ('S029','DateLog','repair_date','DeftranReport',NULL,NULL,NULL),
 ('S030','JobList',NULL,'Running tests',NULL,NULL,NULL),
 ('S031','JobList',NULL,'Status PD',NULL,'PD',9),
 ('S032','JobList',NULL,'Status PR',NULL,'PR',1),
 ('S033','JobList',NULL,'Status SRN',NULL,'SRN',3),
 ('S034','JobList',NULL,'Status REPAIRED',NULL,'REPAIRED',7),
 ('S035','JobList',NULL,'Status SRNINV',NULL,'SRNINV',4),
 ('S036','JobList',NULL,'Delivery report',NULL,NULL,NULL),
 ('S037','JobList',NULL,'Repair report',NULL,NULL,NULL),
 ('S039','DateLog','transaction_date','WD_Claim',NULL,NULL,NULL),
 ('S040','DateLog','transaction_date','WRA_Claim',NULL,NULL,NULL),
 ('S041','DateLog','transaction_date','GPRC_Claim',NULL,NULL,NULL)
) v(report_code,read_rule,date_column,list_label,pending_list,status_label,lifecycle_rank)
-- >>> GRANTS
DENY DELETE ON dbo.v_service_families TO etp_store_manager,etp_viewer;
DENY INSERT ON dbo.v_service_families TO etp_store_manager,etp_viewer;
DENY UPDATE ON dbo.v_service_families TO etp_store_manager,etp_viewer;
GRANT SELECT ON dbo.v_service_families TO etp_owner,etp_store_manager,etp_viewer;
