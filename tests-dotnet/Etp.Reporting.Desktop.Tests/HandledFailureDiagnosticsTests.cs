using Etp.Reporting.Import.Batch;

namespace Etp.Reporting.Desktop.Tests;

public sealed class HandledFailureDiagnosticsTests
{
    private static readonly string DesktopRoot = Path.Combine(
        FindRepositoryRoot(), "src", "Etp.Reporting.Desktop");

    public static TheoryData<string, string, string[]> WorkspaceDiagnostics => new()
    {
        { "Modules/Accounting/AccountingWorkspaceView.xaml.cs", "Accounting.Workspace", [
            "ACCOUNTING_REFRESH_FAILED", "ACCOUNTING_PREVIEW_FAILED", "ACCOUNTING_BATCH_SAVE_FAILED",
            "ACCOUNTING_BATCH_APPROVAL_FAILED", "ACCOUNTING_EXPORT_FAILED", "ACCOUNTING_MAPPING_APPROVAL_FAILED"] },
        { "Modules/SourceInbox/SourceInboxWorkspaceView.xaml.cs", "SourceInbox.Workspace", [
            "SOURCE_INBOX_REFRESH_FAILED", "SOURCE_EXTRACTIONS_LOAD_FAILED", "SOURCE_INTAKE_FAILED",
            "SOURCE_OPEN_FAILED", "SOURCE_EXTRACTION_REVIEW_FAILED"] },
        { "Modules/Registers/RegistersWorkspaceView.xaml.cs", "Registers.Workspace", [
            "REGISTER_REFRESH_FAILED", "REGISTER_SAVE_FAILED"] },
        { "Modules/OperationsAdministration/AdministrationWorkspaceView.xaml.cs", "OperationsAdministration.Administration", [
            "ADMINISTRATION_REFRESH_FAILED", "MASTER_VALUE_SAVE_FAILED", "USER_ACCESS_SAVE_FAILED"] },
        { "Modules/OperationsAdministration/InvestigationApprovalsWorkspaceView.xaml.cs", "OperationsAdministration.Investigation", [
            "APPROVAL_REFRESH_FAILED", "INVESTIGATION_SEARCH_FAILED", "ADJUSTMENT_SUBMIT_FAILED", "APPROVAL_DECISION_FAILED"] },
        { "Modules/Settings/SettingsWorkspaceView.xaml.cs", "Settings.Workspace", [
            "DATABASE_HEALTH_CHECK_FAILED", "DATABASE_HEALTH_CHECK_EXCEPTION", "DATABASE_BOOTSTRAP_FAILED",
            "PRODUCT_CONFIGURATION_LOAD_FAILED", "PRODUCT_CONFIGURATION_SAVE_FAILED"] },
        { "Modules/Imports/ImportWorkspaceView.xaml.cs", "Imports.Workspace", [
            "IMPORT_VALIDATION_READ_FAILED", "IMPORT_PERSIST_FAILED", "BATCH_ACCESS_DENIED",
            "BATCH_SOURCE_BLOCKED", "BATCH_START_FAILED"] },
        { "MainWindow.xaml.cs", "Dashboard.Shell", ["DASHBOARD_REFRESH_FAILED"] },
        { "Modules/Dashboard/DashboardView.cs", "Dashboard.Workspace", ["MANAGEMENT_SUMMARY_EXPORT_FAILED"] },
        { "Modules/Reports/ReportPresentationControl.cs", "Reports.Presentation", ["VISUAL_RENDER_FAILED"] }
    };

    public static TheoryData<string, string, string[]> HelperRoutedWorkspaceDiagnostics => new()
    {
        { "Modules/Reports/ReportsWorkspaceView.xaml.cs", "Reports.Workspace", [
            "REPORT_EXCEL_EXPORT_FAILED", "REPORT_PDF_EXPORT_FAILED", "STOCK_REPORT_FAILED",
            "STOCK_MOVEMENT_REPORT_FAILED", "FOCUSED_EXCEPTION_REPORT_FAILED", "MANAGEMENT_TREND_REPORT_FAILED",
            "SALES_REPORT_FAILED", "INVOICE_SUMMARY_FAILED", "INVOICE_DRILLDOWN_FAILED", "DSR_REPORT_FAILED",
            "STAFF_REPORT_FAILED", "SERVICE_REPORT_FAILED", "CASH_RECONCILIATION_FAILED",
            "TENDER_RECONCILIATION_FAILED", "TENDER_DIAGNOSTICS_FAILED", "STOCK_RECONCILIATION_FAILED",
            "PHYSICAL_STOCK_REPORT_FAILED", "DAILY_EXCEPTIONS_REPORT_FAILED"] },
        { "Modules/DailyWorkflow/DailyWorkflowWorkspaceView.xaml.cs", "DailyWorkflow.Workspace", [
            "DAILY_WORKFLOW_LOAD_FAILED", "MANUAL_INPUT_SAVE_FAILED", "PHYSICAL_STOCK_SAVE_FAILED",
            "STAFF_TARGET_SAVE_FAILED", "DAY_FINALISE_FAILED", "DAY_REOPEN_FAILED",
            "DAILY_PACK_GENERATION_FAILED", "COMBINED_PACK_GENERATION_FAILED", "DAILY_CHANGE_REFRESH_FAILED",
            "REPORT_PACK_EXPORT_FAILED"] },
        { "Modules/Archive/ArchiveWorkspaceView.xaml.cs", "Archive.Workspace", [
            "REPORT_ARCHIVE_LOAD_FAILED", "ARCHIVED_GENERATION_OPEN_FAILED", "GENERATION_COMPARISON_FAILED",
            "ARCHIVED_EXCEL_EXPORT_FAILED", "ARCHIVED_PDF_EXPORT_FAILED", "ARCHIVED_ZIP_CREATE_FAILED",
            "WHATSAPP_SHARE_PREPARE_FAILED", "EMAIL_SHARE_PREPARE_FAILED", "SHARING_CONTACTS_LOAD_FAILED",
            "SHARING_CONTACT_SAVE_FAILED"] }
    };

    [Fact]
    public void Friendly_error_preserves_the_safe_import_source_contract()
    {
        var exception = new ImportSourceException("IMPORT_TYPE_UNSUPPORTED", "Only supported import sources are allowed.");

        Assert.Equal("Only supported import sources are allowed.", DesktopFriendlyError.Describe(exception));
        Assert.Equal(
            "The action could not be completed. Technical details are available in the support package.",
            DesktopFriendlyError.Describe(new Exception("server=C:/sensitive/source.xlsx")));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }
}
