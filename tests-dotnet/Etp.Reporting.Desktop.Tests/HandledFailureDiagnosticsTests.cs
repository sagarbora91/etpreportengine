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

    [Theory]
    [MemberData(nameof(WorkspaceDiagnostics))]
    public void Handled_workspace_failures_have_stable_privacy_safe_diagnostics(
        string relativePath,
        string sourceName,
        string[] eventIds)
    {
        var source = Read(relativePath);

        foreach (var eventId in eventIds)
            Assert.Contains($"\"{sourceName}\", \"{eventId}\"", source, StringComparison.Ordinal);

        Assert.DoesNotContain("ex.Message", source, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Operations_and_settings_internal_failure_paths_are_diagnostic()
    {
        var operations = Read("Modules/OperationsAdministration/OperationsWorkspaceView.xaml.cs");
        Assert.Contains("\"OperationsAdministration.Operations\", \"OPERATIONS_REFRESH_FAILED\"", operations, StringComparison.Ordinal);
        Assert.Contains("\"OperationsAdministration.Operations\", \"WATCH_FOLDER_SAVE_FAILED\"", operations, StringComparison.Ordinal);
        Assert.Contains("\"OperationsAdministration.Operations\", \"AUTOMATION_RUN_FAILED\"", operations, StringComparison.Ordinal);
        Assert.Contains("\"OperationsAdministration.Operations\", \"REPORT_SCHEDULE_SAVE_FAILED\"", operations, StringComparison.Ordinal);
        Assert.Contains("\"OperationsAdministration.Operations\", \"ISSUE_WORKFLOW_UPDATE_FAILED\"", operations, StringComparison.Ordinal);
        Assert.Contains("\"OperationsAdministration.Maintenance\", failureEventId", operations, StringComparison.Ordinal);
        Assert.Contains("\"BACKUP_RUN_FAILED\"", operations, StringComparison.Ordinal);
        Assert.Contains("\"RECOVERY_DRILL_RUN_FAILED\"", operations, StringComparison.Ordinal);
        Assert.Contains("\"SUPPORT_PACKAGE_RUN_FAILED\"", operations, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.Message", operations, StringComparison.Ordinal);

        Assert.Contains("\"Settings.Store\", \"SETTINGS_LOAD_FAILED\", DesktopDiagnosticSeverity.Warning", Read("Modules/Settings/DesktopSettingsStore.cs"), StringComparison.Ordinal);
        Assert.Contains("\"Settings.Session\", \"SETTINGS_SAVE_FAILED\", DesktopDiagnosticSeverity.Warning", Read("Modules/Settings/DesktopSettingsPresentationSession.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(HelperRoutedWorkspaceDiagnostics))]
    public void Report_daily_and_archive_failures_use_friendly_messages_and_stable_diagnostics(
        string relativePath,
        string sourceName,
        string[] eventIds)
    {
        var source = Read(relativePath);

        Assert.Contains($"DesktopDiagnostics.Record(exception, \"{sourceName}\", eventId", source, StringComparison.Ordinal);
        foreach (var eventId in eventIds)
            Assert.Contains($"\"{eventId}\"", source, StringComparison.Ordinal);

        Assert.DoesNotContain("ex.Message", source, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Daily_workflow_failure_presentation_does_not_expose_unclassified_exception_text()
    {
        var source = Read("Modules/DailyWorkflow/DailyWorkflowPresentationSession.cs");

        Assert.DoesNotContain("exception.Message", source, StringComparison.Ordinal);
        Assert.Equal(
            "Daily report pack failed: The action could not be completed. Technical details are available in the support package.",
            Etp.Reporting.Desktop.Modules.DailyWorkflow.DailyWorkflowPresentationSession.Failed(
                "Daily report pack failed",
                new Exception("server=C:/sensitive/source.xlsx")));
    }

    [Fact]
    public void Desktop_user_surfaces_do_not_bypass_the_central_friendly_error_policy()
    {
        var files = Directory.GetFiles(DesktopRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("DesktopFriendlyError.cs", StringComparison.OrdinalIgnoreCase));

        foreach (var path in files)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("ex.Message", source, StringComparison.Ordinal);
            Assert.DoesNotContain("exception.Message", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Friendly_error_preserves_the_safe_import_source_contract()
    {
        var exception = new ImportSourceException("IMPORT_TYPE_UNSUPPORTED", "Only supported import sources are allowed.");

        Assert.Equal("Only supported import sources are allowed.", DesktopFriendlyError.Describe(exception));
        Assert.Equal(
            "The action could not be completed. Technical details are available in the support package.",
            DesktopFriendlyError.Describe(new Exception("server=C:/sensitive/source.xlsx")));
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine([DesktopRoot, .. relativePath.Split('/')]));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Etp.Reporting.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the ETP repository root.");
    }
}
