using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<string, ReportWorkspaceControl> reportWorkspaces = new(StringComparer.OrdinalIgnoreCase);
    private DailySalesReportWorkspace? dsrWorkspace;
    private HelpCentreView? helpCentre;
    private string? focusedWorkspaceKind;
    private object? helpReturnContent;
    private string? helpReturnKind;
    private string? helpReturnTitle;
    private string? helpReturnDescription;
    private string? helpReturnBreadcrumb;
    private bool helpReturnSidebarVisible;

    private void InitializeFocusedWorkspaces()
    {
        FocusedWorkspaceLayer.Visibility = Visibility.Collapsed;
        FocusedWorkspaceHost.Content = null;
    }

    private void ShowFocusedReportWorkspace(string reportCode)
    {
        var report = ProductReportCatalogue.All.Single(x => x.Code.Equals(reportCode, StringComparison.OrdinalIgnoreCase));
        var destination = report.Category.Equals("Stock", StringComparison.OrdinalIgnoreCase) ? "Stock Reports" : "Sales Reports";
        if (!replayingNavigation) navigationHistory.Visit(new WorkspaceLocation(destination, reportCode));

        PageTitle.Text = report.Name;
        PageDescription.Text = report.Description;
        BreadcrumbText.Text = $"Reports / {report.Category} / {report.Name}";
        focusedWorkspaceKind = "report";
        LegacyWorkspaceScroll.Visibility = Visibility.Collapsed;
        FocusedWorkspaceLayer.Visibility = Visibility.Visible;
        HideSidebar();

        if (reportCode.Equals("dsr", StringComparison.OrdinalIgnoreCase))
        {
            dsrWorkspace ??= CreateDsrWorkspace();
            dsrWorkspace.BusinessDatePicker.SelectedDate = ReportTo.SelectedDate ?? ShellBusinessDateSelector.SelectedDate ?? DateTime.Today.AddDays(-1);
            dsrWorkspace.ShowLoading();
            FocusedWorkspaceHost.Content = dsrWorkspace;
            dsrWorkspace.Focus();
            return;
        }

        var definition = ReportWorkspaceRegistry.ForReport(reportCode);
        if (!reportWorkspaces.TryGetValue(definition.Id, out var workspace))
        {
            workspace = new ReportWorkspaceControl(definition);
            workspace.ReportSelected += (_, selected) => RunFocusedReport(selected.Code, workspace);
            workspace.ActionRequested += FocusedReportActionRequested;
            reportWorkspaces.Add(definition.Id, workspace);
        }
        workspace.DateFromPicker.SelectedDate = ReportFrom.SelectedDate;
        workspace.DateToPicker.SelectedDate = ReportTo.SelectedDate;
        workspace.SelectReport(reportCode);
        workspace.ShowLoading($"Loading {report.Name}…");
        FocusedWorkspaceHost.Content = workspace;
        workspace.Focus();
    }

    private DailySalesReportWorkspace CreateDsrWorkspace()
    {
        var workspace = new DailySalesReportWorkspace();
        workspace.ActionRequested += FocusedReportActionRequested;
        return workspace;
    }

    private void RunFocusedReport(string reportCode, ReportWorkspaceControl workspace)
    {
        ApplyWorkspaceScope(workspace.DateFromPicker.SelectedDate, workspace.DateToPicker.SelectedDate, workspace.ScopeSelector.SelectedItem?.ToString());
        RunCatalogueReport_Click(new Button { Tag = reportCode }, new RoutedEventArgs());
    }

    private void FocusedReportActionRequested(object? sender, ReportWorkspaceActionRequest request)
    {
        switch (request.Action)
        {
            case ReportWorkspaceAction.Refresh when request.ReportCode is not null:
                ApplyWorkspaceScope(request.DateFrom.ToDateTime(TimeOnly.MinValue), request.DateTo.ToDateTime(TimeOnly.MinValue), request.Scope);
                RunCatalogueReport_Click(new Button { Tag = request.ReportCode }, new RoutedEventArgs());
                break;
            case ReportWorkspaceAction.ExportPdf:
                ExportPdf_Click(this, new RoutedEventArgs());
                break;
            case ReportWorkspaceAction.ExportExcel:
                ExportExcel_Click(this, new RoutedEventArgs());
                break;
            case ReportWorkspaceAction.GenerateReportPack:
                DailyBusinessDateInput.SelectedDate = request.DateTo.ToDateTime(TimeOnly.MinValue);
                GenerateDailyPack_Click(this, new RoutedEventArgs());
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
        ReportFrom.SelectedDate = from ?? DateTime.Today;
        ReportTo.SelectedDate = to ?? from ?? DateTime.Today;
        StoreFilterInput.Text = scope switch
        {
            "Titan" => "WLMHW",
            "Helios" => "HEMW",
            _ => string.Empty
        };
        ShellBusinessDateSelector.SelectedDate = ReportTo.SelectedDate;
    }

    private void UpdateFocusedReportPreview()
    {
        if (focusedWorkspaceKind != "report" || currentExportMetadata is null) return;
        if (currentDsrReport is not null && dsrWorkspace is not null)
        {
            dsrWorkspace.SetReport(currentDsrReport);
            return;
        }
        if (currentReportCode is null || currentVisualReport is null) return;
        var definition = ReportWorkspaceRegistry.ForReport(currentReportCode);
        if (!reportWorkspaces.TryGetValue(definition.Id, out var workspace)) return;
        workspace.SetPreview(BuildFocusedReportPreview(currentVisualReport, ReportGrid.ItemsSource), ReportResult.Text);
    }

    private static UIElement BuildFocusedReportPreview(VisualReportModel model, System.Collections.IEnumerable? rows)
    {
        var root = new StackPanel();
        var cards = new UniformGrid { Columns = Math.Clamp(model.Kpis.Count, 1, 4), Margin = new Thickness(0, 0, 0, 10) };
        foreach (var kpi in model.Kpis.Take(4))
        {
            var value = IndianNumberFormatter.Format(kpi.Value, kpi.Format, kpi.State);
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = kpi.Label, Foreground = DsrUi.Brush("#5D6873") });
            content.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = DsrUi.Brush(VisualReportTheme.Navy), Margin = new Thickness(0, 4, 0, 0) });
            var card = new Border { Background = DsrUi.Brush("#FFFFFF"), BorderBrush = DsrUi.Brush("#DCE4EF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(12), Margin = new Thickness(3), Child = content };
            AutomationProperties.SetName(card, $"{kpi.Label}: {value}");
            cards.Children.Add(card);
        }
        root.Children.Add(cards);
        foreach (var visual in model.Visuals.Take(2)) root.Children.Add(Visual(visual));
        var control = model.Controls.FirstOrDefault();
        if (control is not null)
            root.Children.Add(new TextBlock { Text = $"Control {control.Status}: {control.Message}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8), Foreground = DsrUi.Brush(control.Status.Equals("Passed", StringComparison.OrdinalIgnoreCase) ? VisualReportTheme.Teal : VisualReportTheme.Red) });
        root.Children.Add(new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = true,
            IsReadOnly = true,
            MinHeight = 220,
            MaxHeight = 520,
            Margin = new Thickness(0, 8, 0, 0),
            HeadersVisibility = DataGridHeadersVisibility.All
        });
        return root;
    }

    private void ShowHelpWorkspace(string? topicId = null, bool contextual = false)
    {
        if (focusedWorkspaceKind != "help")
        {
            helpReturnContent = focusedWorkspaceKind == "report" ? FocusedWorkspaceHost.Content : null;
            helpReturnKind = focusedWorkspaceKind;
            helpReturnTitle = PageTitle.Text;
            helpReturnDescription = PageDescription.Text;
            helpReturnBreadcrumb = BreadcrumbText.Text;
            helpReturnSidebarVisible = ContextSidebar.Visibility == Visibility.Visible;
        }
        helpCentre ??= CreateHelpCentre();
        if (contextual)
        {
            var location = navigationHistory.Current;
            helpCentre.ShowContextHelp(location?.Destination, location?.FeatureCode);
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
            HideFocusedWorkspace();
            if (string.IsNullOrWhiteSpace(request.Destination)) return;
            var destination = request.Destination == "Investigation" ? "Operations Center" : request.Destination;
            NavigateToDestinationWithFeature(destination, request.FeatureCode);
            if (!string.IsNullOrWhiteSpace(request.FeatureCode))
                RunCatalogueReport_Click(new Button { Tag = request.FeatureCode }, new RoutedEventArgs());
        };
        return view;
    }

    private bool CloseFocusedHelp()
    {
        if (focusedWorkspaceKind != "help") return false;
        CloseHelpWorkspace();
        return true;
    }

    private void CloseHelpWorkspace()
    {
        if (helpReturnKind == "report" && helpReturnContent is not null)
        {
            FocusedWorkspaceHost.Content = helpReturnContent;
            FocusedWorkspaceLayer.Visibility = Visibility.Visible;
            LegacyWorkspaceScroll.Visibility = Visibility.Collapsed;
            focusedWorkspaceKind = "report";
        }
        else HideFocusedWorkspace();
        if (helpReturnTitle is not null) PageTitle.Text = helpReturnTitle;
        if (helpReturnDescription is not null) PageDescription.Text = helpReturnDescription;
        if (helpReturnBreadcrumb is not null) BreadcrumbText.Text = helpReturnBreadcrumb;
        if (helpReturnSidebarVisible && currentModuleId != "home") ShowSidebar();
        helpReturnContent = null;
        helpReturnKind = null;
        helpReturnTitle = null;
        helpReturnDescription = null;
        helpReturnBreadcrumb = null;
        helpReturnSidebarVisible = false;
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
