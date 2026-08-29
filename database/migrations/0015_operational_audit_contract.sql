SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.operational_audit DROP CONSTRAINT CK_operational_audit_type;
ALTER TABLE dbo.operational_audit ADD CONSTRAINT CK_operational_audit_type CHECK
 (event_type IN ('ApplicationStart','SessionStart','ConnectionTest','ImportBatch','ImportFailed','ReportRun','ExportExcel','ExportPdf','DatabaseSetup','SupportPackage',
                 'ManualInput','DayFinalised','DayReopened','ReportPack','Backup','RestoreDrill','ConfigurationChange','MappingProfileChange','Restatement','StockCount','StaffTarget',
                 'UserAdministration','MasterDataChange','AutomationRun','ReportArchive','DocumentIntake','DocumentExtraction','DocumentExtractionReview','SharingContactChange',
                 'RegisterEntry','ShareInitiated','ReportPackage','Approval','Adjustment','AccountingBatch','AccountingExport','ImportConflict','IssueWorkflow','VisualRender'));

COMMIT TRANSACTION;
