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
    internal string? focusedWorkspaceKind;
    private readonly HelpWorkspaceSession helpWorkspaceSession = new();

    private void InitializeFocusedWorkspaces()
    {
        FocusedWorkspaceLayer.Visibility = Visibility.Collapsed;
        FocusedWorkspaceHost.Content = null;
    }

    internal bool ShowFocusedReportWorkspace(string reportCode)
    {
        var report = ProductReportCatalogue.All.Single(x => x.Code.Equals(reportCode, StringComparison.OrdinalIgnoreCase));
        var reportRoute = TaskNavigation.Find("report-" + reportCode)!.Route;
        var decision = shell.CurrentRoute == reportRoute ? NavigationDecision.Allowed(reportRoute, ShellRouteRegistry.Find(reportRoute.Destination)!) : shell.Navigate(reportRoute, CurrentShellAccess);
        if (!decision.IsAllowed)
        {
            ApplyNavigationDecision(decision);
            return false;
        }

        UpdateSection(TaskNavigation.Find("report-" + reportCode)!);
        focusedWorkspaceKind = "report";
        FocusedWorkspaceLayer.Visibility = Visibility.Visible;

        var workspace = reportWorkspaceSession.Activate(
            reportCode,
            reportsWorkspaceView.DateFrom,
            reportsWorkspaceView.DateTo,
            reportsWorkspaceView.DateTo ?? ShellBusinessDateSelector.SelectedDate ?? DateTime.Today.AddDays(-1),
            FocusedReportActionRequested,
            RunFocusedReport, reportsWorkspaceView.StoreScope);
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
                taskNavigator!.ExportCurrentReport(true);
                break;
            case ReportWorkspaceAction.Share:
                taskNavigator!.NavigateTask(TaskNavigation.Find("shared")!);
                ApplicationStatus.Text = "Choose an archived report and recipient to share.";
                break;
            case ReportWorkspaceAction.ExportExcel:
                taskNavigator!.ExportCurrentReport(false);
                break;
            case ReportWorkspaceAction.GenerateReportPack:
                _ = taskNavigator!.GeneratePackAsync(request.DateTo.ToDateTime(TimeOnly.MinValue), request.Scope);
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

    internal void ShowHelpWorkspace(string? topicId = null, bool contextual = false)
    {
        helpWorkspaceSession.Open(new HelpWorkspaceSnapshot(
            FocusedWorkspaceLayer.Visibility == Visibility.Visible ? FocusedWorkspaceHost.Content : null,
            focusedWorkspaceKind,
            PageTitle.Text,
            null,
            null,
            false));
        helpCentre ??= CreateHelpCentre();
        if (contextual)
        {
            helpCentre.ShowContextHelp(shell.CurrentRoute.Destination, shell.CurrentRoute.FeatureCode);
        }
        else helpCentre.OpenTopic(topicId ?? HelpCentreRegistry.HomeTopicId);
        focusedWorkspaceKind = "help";
        FocusedWorkspaceHost.Content = helpCentre;
        FocusedWorkspaceLayer.Visibility = Visibility.Visible;
        PageTitle.Text = "Help Centre";
        helpCentre.Focus();
    }

    private HelpCentreView CreateHelpCentre()
    {
        var view = new HelpCentreView { CanNavigateTopic = topic => HelpTaskRoutes.Find(topic)?.IsAllowed(CurrentShellAccess) == true };
        view.CloseRequested += (_, _) => CloseHelpWorkspace();
        view.NavigationRequested += (_, request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Destination)) return;
            if (HelpTaskRoutes.Find(request.TopicId) is { } task) taskNavigator!.NavigateTask(task);
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
        if (shell.CurrentRoute.TaskId?.StartsWith("help:", StringComparison.Ordinal) == true)
        { helpWorkspaceSession.Abandon(); NavigateHistory(true); return; }
        var returnState = helpWorkspaceSession.Close();
        if (returnState?.CanRestoreFocusedWorkspace == true)
        {
            FocusedWorkspaceHost.Content = returnState.FocusedContent;
            FocusedWorkspaceLayer.Visibility = Visibility.Visible;
            focusedWorkspaceKind = returnState.FocusedWorkspaceKind;
        }
        else HideFocusedWorkspace();
        if (returnState?.PageTitle is not null) PageTitle.Text = returnState.PageTitle;
        taskNavigator!.RestoreBreadcrumbs();
    }

    private void HideFocusedWorkspace()
    {
        FocusedWorkspaceLayer.Visibility = Visibility.Collapsed;
        FocusedWorkspaceHost.Content = null;
        focusedWorkspaceKind = null;
    }

    private static void OpenExportFolder()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ETP Reporting Engine", "Exports");
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
    }
}
