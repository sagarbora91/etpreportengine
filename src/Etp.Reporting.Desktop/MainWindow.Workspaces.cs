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
        var destination = report.Category.Equals("Stock", StringComparison.OrdinalIgnoreCase) ? "Stock Reports" : "Sales Reports";
        var reportRoute = TaskNavigation.Find("report-" + reportCode)!.Route;
        var decision = shell.CurrentRoute == reportRoute ? NavigationDecision.Allowed(reportRoute, ShellRouteRegistry.Find(reportRoute.Destination)!) : shell.Navigate(reportRoute, CurrentShellAccess);
        if (!decision.IsAllowed)
        {
            ApplyNavigationDecision(decision);
            return false;
        }

        PageTitle.Text = TaskNavigation.Find("report-" + reportCode)!.Title;
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
                reportsWorkspaceView.ApplyTaskScope(request.ReportCode,request.DateFrom.ToDateTime(TimeOnly.MinValue), request.DateTo.ToDateTime(TimeOnly.MinValue), request.Scope);
                _ = reportsWorkspaceView.RunReportAsync(request.ReportCode);
                break;
            case ReportWorkspaceAction.ExportPdf:
                taskNavigator!.ExportCurrentReport(true);
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
        BreadcrumbLinks.Children.Clear();
        var back = new Button { Content = "← Back", Padding = new Thickness(8,0,8,0) };
        back.Click += (_, _) => CloseHelpWorkspace(); BreadcrumbLinks.Children.Add(back);
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
            LegacyWorkspaceScroll.Visibility = Visibility.Collapsed;
            focusedWorkspaceKind = returnState.FocusedWorkspaceKind;
        }
        else HideFocusedWorkspace();
        if (returnState?.PageTitle is not null) PageTitle.Text = returnState.PageTitle;
        if (returnState?.PageDescription is not null) PageDescription.Text = returnState.PageDescription;
        if (returnState?.Breadcrumb is not null) BreadcrumbText.Text = returnState.Breadcrumb;
        if (returnState?.WasSidebarVisible == true && CurrentModuleId != "home") ShowSidebar();
        taskNavigator!.RestoreBreadcrumbs();
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
