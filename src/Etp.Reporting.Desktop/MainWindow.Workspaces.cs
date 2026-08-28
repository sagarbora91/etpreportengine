using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop;

public partial class MainWindow
{
    private readonly ReportWorkspaceSession reportWorkspaceSession = new();
    private HelpCentreView? helpCentre;
    private string? focusedWorkspaceKind;
    private readonly HelpWorkspaceSession helpWorkspaceSession = new();

    private void InitializeFocusedWorkspaces()
    {
        FocusedWorkspaceLayer.Visibility = Visibility.Collapsed;
        FocusedWorkspaceHost.Content = null;
    }

    private bool ShowFocusedReportWorkspace(string reportCode)
    {
        var report = ProductReportCatalogue.All.Single(x => x.Code.Equals(reportCode, StringComparison.OrdinalIgnoreCase));
        var destination = report.Category.Equals("Stock", StringComparison.OrdinalIgnoreCase) ? "Stock Reports" : "Sales Reports";
        var decision = shell.Navigate(new WorkspaceRoute(destination, reportCode), CurrentShellAccess);
        if (!decision.IsAllowed)
        {
            ApplyNavigationDecision(decision);
            return false;
        }

        PageTitle.Text = report.Name;
        PageDescription.Text = report.Description;
        BreadcrumbText.Text = $"Reports / {report.Category} / {report.Name}";
        focusedWorkspaceKind = "report";
        LegacyWorkspaceScroll.Visibility = Visibility.Collapsed;
        FocusedWorkspaceLayer.Visibility = Visibility.Visible;
        HideSidebar();

        var workspace = reportWorkspaceSession.Activate(
            reportCode,
            reportsWorkspaceView.DateFrom,
            reportsWorkspaceView.DateTo,
            reportsWorkspaceView.DateTo ?? ShellBusinessDateSelector.SelectedDate ?? DateTime.Today.AddDays(-1),
            FocusedReportActionRequested,
            RunFocusedReport);
        FocusedWorkspaceHost.Content = workspace;
        workspace.Focus();
        return true;
    }

    private void RunFocusedReport(string reportCode, ReportWorkspaceControl workspace)
    {
        ApplyWorkspaceScope(workspace.DateFromPicker.SelectedDate, workspace.DateToPicker.SelectedDate, workspace.ScopeSelector.SelectedItem?.ToString());
        _ = reportsWorkspaceView.RunReportAsync(reportCode);
    }

    private void FocusedReportActionRequested(object? sender, ReportWorkspaceActionRequest request)
    {
        switch (request.Action)
        {
            case ReportWorkspaceAction.Refresh when request.ReportCode is not null:
                ApplyWorkspaceScope(request.DateFrom.ToDateTime(TimeOnly.MinValue), request.DateTo.ToDateTime(TimeOnly.MinValue), request.Scope);
                _ = reportsWorkspaceView.RunReportAsync(request.ReportCode);
                break;
            case ReportWorkspaceAction.ExportPdf:
                reportsWorkspaceView.ExportPdf();
                break;
            case ReportWorkspaceAction.ExportExcel:
                reportsWorkspaceView.ExportExcel();
                break;
            case ReportWorkspaceAction.GenerateReportPack:
                dailyWorkflowWorkspace.BusinessDate = request.DateTo.ToDateTime(TimeOnly.MinValue);
                _ = dailyWorkflowWorkspace.GenerateDailyPackAsync();
                break;
            case ReportWorkspaceAction.OpenExportFolder:
                OpenExportFolder();
                break;
            case ReportWorkspaceAction.OpenManualEntry:
                HideFocusedWorkspace();
                NavigateToDestination("Manual Entry");
                break;
            case ReportWorkspaceAction.BackToReports:
                HideFocusedWorkspace();
                NavigateToDestination("Sales Reports");
                break;
        }
    }

    private void ApplyWorkspaceScope(DateTime? from, DateTime? to, string? scope)
    {
        reportsWorkspaceView.ApplyScope(from, to, scope);
        ShellBusinessDateSelector.SelectedDate = reportsWorkspaceView.DateTo;
    }

    private void ShowHelpWorkspace(string? topicId = null, bool contextual = false)
    {
        helpWorkspaceSession.Open(new HelpWorkspaceSnapshot(
            focusedWorkspaceKind == "report" ? FocusedWorkspaceHost.Content : null,
            focusedWorkspaceKind,
            PageTitle.Text,
            PageDescription.Text,
            BreadcrumbText.Text,
            ContextSidebar.Visibility == Visibility.Visible));
        helpCentre ??= CreateHelpCentre();
        if (contextual)
        {
            helpCentre.ShowContextHelp(shell.CurrentRoute.Destination, shell.CurrentRoute.FeatureCode);
        }
        else helpCentre.OpenTopic(topicId ?? HelpCentreRegistry.HomeTopicId);
        focusedWorkspaceKind = "help";
        LegacyWorkspaceScroll.Visibility = Visibility.Collapsed;
        FocusedWorkspaceHost.Content = helpCentre;
        FocusedWorkspaceLayer.Visibility = Visibility.Visible;
        HideSidebar();
        PageTitle.Text = "Help Centre";
        PageDescription.Text = "Guidance for every application area and all supported keyboard shortcuts.";
        BreadcrumbText.Text = "Help";
        helpCentre.Focus();
    }

    private HelpCentreView CreateHelpCentre()
    {
        var view = new HelpCentreView();
        view.CloseRequested += (_, _) => CloseHelpWorkspace();
        view.NavigationRequested += (_, request) =>
        {
            helpWorkspaceSession.Abandon();
            HideFocusedWorkspace();
            if (string.IsNullOrWhiteSpace(request.Destination)) return;
            var destination = request.Destination == "Investigation" ? "Operations Center" : request.Destination;
            var navigated = NavigateToDestinationWithFeature(destination, request.FeatureCode);
            if (navigated && !string.IsNullOrWhiteSpace(request.FeatureCode))
                _ = reportsWorkspaceView.RunReportAsync(request.FeatureCode);
        };
        return view;
    }

    private bool CloseFocusedHelp()
    {
        if (!helpWorkspaceSession.IsOpen) return false;
        CloseHelpWorkspace();
        return true;
    }

    private void CloseHelpWorkspace()
    {
        var returnState = helpWorkspaceSession.Close();
        if (returnState?.CanRestoreFocusedWorkspace == true)
        {
            FocusedWorkspaceHost.Content = returnState.FocusedContent;
            FocusedWorkspaceLayer.Visibility = Visibility.Visible;
            LegacyWorkspaceScroll.Visibility = Visibility.Collapsed;
            focusedWorkspaceKind = returnState.FocusedWorkspaceKind;
        }
        else HideFocusedWorkspace();
        if (returnState?.PageTitle is not null) PageTitle.Text = returnState.PageTitle;
        if (returnState?.PageDescription is not null) PageDescription.Text = returnState.PageDescription;
        if (returnState?.Breadcrumb is not null) BreadcrumbText.Text = returnState.Breadcrumb;
        if (returnState?.WasSidebarVisible == true && CurrentModuleId != "home") ShowSidebar();
    }

    private void HideFocusedWorkspace()
    {
        FocusedWorkspaceLayer.Visibility = Visibility.Collapsed;
        FocusedWorkspaceHost.Content = null;
        LegacyWorkspaceScroll.Visibility = Visibility.Visible;
        focusedWorkspaceKind = null;
    }

    private static void OpenExportFolder()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ETP Reporting Engine", "Exports");
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
    }
}
