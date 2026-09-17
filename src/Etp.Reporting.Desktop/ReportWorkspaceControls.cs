using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop;

public enum ReportWorkspaceAction
{
    Refresh,
    ExportPdf,
    ExportExcel,
    GenerateReportPack,
    OpenExportFolder,
    OpenManualEntry,
    BackToReports,
    Share
}

public sealed record ReportWorkspaceActionRequest(
    ReportWorkspaceAction Action,
    string? ReportCode,
    DateOnly DateFrom,
    DateOnly DateTo,
    string Scope);

public sealed record ReportDataAvailability(string Label, bool IsAvailable, string Detail);

public sealed record ReportWorkspaceDefinition(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<ProductReportEntry> Reports)
{
    public static ReportWorkspaceDefinition ForReport(string code)
    {
        var report = ProductReportCatalogue.All.Single(r => r.Code == code);
        return new(report.Code, report.Name, report.Description, [report]);
    }
}

public static class ReportingPeriodLabels
{
    public static string ForDate(DateOnly businessDate)
    {
        var yearStart = businessDate.Month >= 4
            ? new DateOnly(businessDate.Year, 4, 1)
            : new DateOnly(businessDate.Year - 1, 4, 1);
        var monthStart = new DateOnly(businessDate.Year, businessDate.Month, 1);
        return $"FTD {businessDate:dd MMM yyyy}  ·  MTD {monthStart:dd MMM}–{businessDate:dd MMM yyyy}  ·  YTD {yearStart:dd MMM yyyy}–{businessDate:dd MMM yyyy}";
    }
}

public sealed class ReportWorkspaceControl : Grid
{
    private readonly List<Control> exportActions = [];
    private ReportPreviewScope? loadingScope;
    private ReportPreviewScope? loadedScope;
    private Func<string>? queryFilterSignature;
    private readonly Expander queryFilters = new() { Header = "Filters", Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock appliedScope = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    private ReportPreviewScope CurrentScope => new(DateFromPicker.SelectedDate, DateToPicker.SelectedDate, ScopeSelector.SelectedItem?.ToString(), SelectedReport?.Code, queryFilterSignature?.Invoke());
    public bool HasCurrentPreview => loadedScope is not null && loadedScope == CurrentScope;
    private readonly ReportWorkspaceDefinition definition;
    private ProductReportEntry? selectedReport;
    private readonly ContentControl previewHost;
    private readonly TextBlock reportTitle;
    private TextBlock statusText = null!;
    private Action updateToolbar = () => { };
    private bool compactFilters;
    public void FocusPeriod() { if(compactFilters) new Modules.Reports.ReportScopeDialog(this).ShowDialog(); else (DateFromPicker.IsEnabled ? DateFromPicker : DateToPicker).Focus(); }

    public event EventHandler<ReportWorkspaceActionRequest>? ActionRequested;
    public event EventHandler<ProductReportEntry>? ReportSelected;

    public DatePicker DateFromPicker { get; }
    public DatePicker DateToPicker { get; }
    public ComboBox ScopeSelector { get; }
    public ProductReportEntry? SelectedReport => selectedReport;

    public ReportWorkspaceControl(ReportWorkspaceDefinition definition)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Background = DsrUi.Brush("SurfaceSecondary");
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        ColumnDefinitions.Add(new ColumnDefinition());



        var body = new Grid { Margin = new Thickness(8, 4, 8, 4) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());

        reportTitle = DsrUi.Text(definition.DisplayName, 24, FontWeights.SemiBold);
        body.Children.Add(reportTitle);

        DateFromPicker = new DatePicker { Width = 180, SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 0) };
        DateToPicker = new DatePicker { Width = 180, SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 0) };
        ScopeSelector = new ComboBox { Width = 190, SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 0), ItemsSource = new[] { "Both stores", "Titan World", "Helios" } };
        AutomationProperties.SetName(DateFromPicker, "Report start date");
        AutomationProperties.SetName(DateToPicker, "Report end date");
        AutomationProperties.SetName(ScopeSelector, "Report store scope");
        var toolbar = BuildToolbar();
        Grid.SetRow(toolbar, 1); body.Children.Add(toolbar);

        previewHost = new ContentControl
        {
            Content = new EmptyState("Select a report", "Choose a report from the menu to preview it here."),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        previewHost.Margin = new Thickness(0, 8, 0, 0);
        AutomationProperties.SetName(previewHost, "Report preview and results");
        Grid.SetRow(previewHost, 2); body.Children.Add(previewHost);
        reportTitle.Visibility = Visibility.Collapsed;
        Grid.SetColumn(body, 1); Children.Add(body);

        if (definition.Reports.Count > 0) selectedReport = definition.Reports[0];
        AutomationProperties.SetName(this, $"{definition.DisplayName} report workspace");
        DateFromPicker.SelectedDateChanged += (_, _) => InvalidatePreview();
        DateToPicker.SelectedDateChanged += (_, _) => { if (Modules.Reports.ReportTaskScope.IsSnapshot(SelectedReport?.Code)) DateFromPicker.SelectedDate = DateToPicker.SelectedDate; InvalidatePreview(); };
        ScopeSelector.SelectionChanged += (_, _) => InvalidatePreview();
    }

    public void AttachQueryFilters(FrameworkElement panel, Func<string> signature)
    {
        queryFilterSignature = signature;
        queryFilters.Content = panel;
        queryFilters.Visibility = Visibility.Visible;
        ShowLoading("Loading report…");
    }

    public void InvalidateQueryFilters() => InvalidatePreview();

    public void SetPreview(UIElement content, string status, string? scope = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (loadingScope is not null && loadingScope != CurrentScope) { InvalidatePreview(); return; }
        previewHost.Content = content;
        statusText.Text = status;
        appliedScope.Text = scope ?? string.Empty;
        appliedScope.Visibility = string.IsNullOrEmpty(scope) ? Visibility.Collapsed : Visibility.Visible;
        updateToolbar();
        loadedScope = CurrentScope; foreach (var button in exportActions) button.IsEnabled = true;
    }

    public void SelectReport(string reportCode, bool notify = false)
    {
        var report = definition.Reports.FirstOrDefault(x => x.Code.Equals(reportCode, StringComparison.OrdinalIgnoreCase));
        if (report is null) throw new ArgumentOutOfRangeException(nameof(reportCode), reportCode, "The report is not part of this workspace.");
        selectedReport = report;
        if (notify) ReportSelected?.Invoke(this,report);
        reportTitle.Text = report.Name;
        statusText.Text = report.Description;
    }

    public void ConfigureTaskScope(string? scope)
    {
        ScopeSelector.ItemsSource = Modules.Reports.ReportTaskScope.RequiresSingleStore(SelectedReport?.Code)
            ? new[] { "Select one store", "Titan World", "Helios" } : new[] { "Both stores", "Titan World", "Helios" };
        SetStoreScope(scope ?? "Both stores");
        var snapshot = Modules.Reports.ReportTaskScope.IsSnapshot(SelectedReport?.Code);
        DateFromPicker.IsEnabled = !snapshot;
        DateFromPicker.ToolTip = snapshot ? "Snapshot reports use the displayed end date as their business date." : "Report start date";
        if (snapshot) DateFromPicker.SelectedDate = DateToPicker.SelectedDate;
        updateToolbar();
    }
    public void SetStoreScope(string scope)
    {
        scope = scope == "Titan" ? "Titan World" : scope.StartsWith("Combined",StringComparison.Ordinal) ? "Both stores" : scope;
        if (SelectedReport?.Code == "cash" && scope is not ("Titan World" or "Helios")) scope = "Titan World";
        ScopeSelector.SelectedItem = Modules.Reports.ReportTaskScope.RequiresSingleStore(SelectedReport?.Code) && scope is not ("Titan World" or "Helios") ? "Select one store" : scope;
    }

    public void ShowLoading(string message)
    {
        loadingScope = CurrentScope; loadedScope = null; foreach (var button in exportActions) button.IsEnabled = false;
        previewHost.Content = new LoadingState(message);
        statusText.Text = message;
        updateToolbar();
    }

    public void ShowUnavailable(string title, string message)
    {
        loadedScope = null; foreach (var button in exportActions) button.IsEnabled = false;
        previewHost.Content = new EmptyState(title, message, "Review the relevant source or manual input, then refresh.");
        statusText.Text = message;
        updateToolbar();
    }



    private void InvalidatePreview()
    {
        updateToolbar();
        loadedScope = null; foreach (var button in exportActions) button.IsEnabled = false;
        previewHost.Content = new EmptyState("Refresh required", "The report date, store or query filters changed. Apply filters or refresh before reviewing or exporting.");
        appliedScope.Text = "Applied scope: refresh required.";
        statusText.Text = "Scope changed — refresh the preview.";
    }



    private UIElement BuildToolbar()
    {
        var container = new Border { Background = DsrUi.Brush("Surface"), BorderBrush = DsrUi.Brush("SecondaryText"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0) };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var actions = new WrapPanel();
        actions.Children.Add(DateFromPicker); actions.Children.Add(DateToPicker); actions.Children.Add(ScopeSelector);
        var filters = new Button { Content = "Period & store", Margin = new Thickness(0,0,8,0), Padding = new Thickness(12,6,12,6) };
        filters.Click += (_,_) => new Modules.Reports.ReportScopeDialog(this).ShowDialog();
        AutomationProperties.SetName(filters,"Edit report period and store"); filters.Visibility = Visibility.Collapsed;
        actions.Children.Add(ActionButton("Refresh", ReportWorkspaceAction.Refresh, true));
        var pdf = ActionButton("Export PDF", ReportWorkspaceAction.ExportPdf);
        var excel = ActionButton("Export Excel", ReportWorkspaceAction.ExportExcel);
        var actionMenu = ReportActionMenu.Create(RaiseAction, exportActions, false);
        actions.Children.Add(pdf); actions.Children.Add(excel); actions.Children.Add(actionMenu);
        root.Children.Add(actions);
        statusText = DsrUi.Text(definition.Description, 12, colour: "SecondaryText"); statusText.Name = "ReportTaskStatus";
        statusText.TextWrapping = TextWrapping.Wrap; statusText.TextTrimming = TextTrimming.None;
        var summaries = new StackPanel { Margin = new Thickness(0,6,0,0) }; var scope = DsrUi.Text("",12);
        summaries.Children.Add(scope); summaries.Children.Add(statusText); Grid.SetRow(summaries,1); root.Children.Add(summaries);
        AutomationProperties.SetName(queryFilters, "Report query filters");
        AutomationProperties.SetName(appliedScope, "Applied report scope");
        Grid.SetRow(queryFilters,2); root.Children.Add(queryFilters);
        Grid.SetRow(appliedScope,3); root.Children.Add(appliedScope);
        updateToolbar = () =>
        {
            var compact = container.ActualWidth > 0 && container.ActualWidth < 1000;
            compactFilters = false;
            DateFromPicker.Visibility = !compactFilters && DateFromPicker.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            DateToPicker.Visibility = DateFromPicker.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            ScopeSelector.Visibility = Visibility.Collapsed;
            filters.Visibility = scope.Visibility = compactFilters ? Visibility.Visible : Visibility.Collapsed;
            scope.Text = $"{DateFromPicker.SelectedDate:dd MMM yyyy} – {DateToPicker.SelectedDate:dd MMM yyyy} · {ScopeSelector.SelectedItem} · {statusText.Text}";
            scope.TextWrapping = TextWrapping.Wrap; scope.TextTrimming = TextTrimming.None;
            statusText.Visibility = compactFilters ? Visibility.Collapsed : Visibility.Visible;
            pdf.Visibility = excel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            actionMenu.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            container.Padding = new Thickness(compact ? 8 : 12); container.Margin = new Thickness(0,compact ? 0 : 10,0,0);
        };
        container.SizeChanged += (_,_) => updateToolbar();
        container.Child = root;
        return container;
    }

    private Button ActionButton(string label, ReportWorkspaceAction action, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), MinWidth = 86 };
        if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        AutomationProperties.SetName(button, $"{label} current report");
        if (action is ReportWorkspaceAction.ExportPdf or ReportWorkspaceAction.ExportExcel or ReportWorkspaceAction.Share) { exportActions.Add(button); button.IsEnabled = false; }
        button.Click += (_, _) => RaiseAction(action);
        return button;
    }

    private void RaiseAction(ReportWorkspaceAction action)
    {
        if (action is ReportWorkspaceAction.ExportPdf or ReportWorkspaceAction.ExportExcel or ReportWorkspaceAction.Share && !HasCurrentPreview) return;
        var from = DateOnly.FromDateTime(DateFromPicker.SelectedDate ?? DateTime.Today);
        var to = DateOnly.FromDateTime(DateToPicker.SelectedDate ?? DateTime.Today);
        ActionRequested?.Invoke(this, new(action, SelectedReport?.Code, from, to, ScopeSelector.SelectedItem?.ToString() ?? "Both stores"));
    }
}

public sealed class DailySalesReportWorkspace : Grid
{
    private readonly List<Control> exportActions = [];
    private ReportPreviewScope? loadingScope;
    private ReportPreviewScope? loadedScope;
    private ReportPreviewScope CurrentScope => new(BusinessDatePicker.SelectedDate, BusinessDatePicker.SelectedDate, ScopeSelector.SelectedItem?.ToString(), "dsr");
    public bool HasCurrentPreview => loadedScope is not null && loadedScope == CurrentScope;
    private readonly ContentControl previewHost;
    private TextBlock periodText = null!;
    private TextBlock statusText = null!;
    private DailySalesReportDocument? currentDocument;
    private bool showMatrix;
    private string availabilityDetails = "Refresh the DSR to check source availability.";

    public event EventHandler<ReportWorkspaceActionRequest>? ActionRequested;

    public DatePicker BusinessDatePicker { get; }
    public ComboBox ScopeSelector { get; }

    public DailySalesReportWorkspace()
    {
        Background = DsrUi.Brush("SurfaceSecondary");
        Margin = new Thickness(8, 4, 8, 4);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition());
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(DsrUi.Text("Daily Sales Report", 26, FontWeights.SemiBold));
        titles.Children.Add(DsrUi.Text("Select the business date, review availability, preview and export from one screen.", 11.5, colour: "SecondaryText"));
        titleRow.Children.Add(titles);
        titleRow.Visibility = Visibility.Collapsed;
        var back = ActionButton("Back to Reports", ReportWorkspaceAction.BackToReports);
        Grid.SetColumn(back, 1); titleRow.Children.Add(back);
        Children.Add(titleRow);

        BusinessDatePicker = new DatePicker { Width = 180, SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 0) };
        ScopeSelector = new ComboBox { Width = 190, SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 0), ItemsSource = new[] { "Both stores", "Titan World", "Helios" } };
        AutomationProperties.SetName(BusinessDatePicker, "DSR business date");
        AutomationProperties.SetName(ScopeSelector, "DSR store scope");
        BusinessDatePicker.SelectedDateChanged += (_, _) => UpdatePeriodLabel();

        var toolbar = BuildToolbar();
        Grid.SetRow(toolbar, 1); Children.Add(toolbar);

        previewHost = new ContentControl
        {
            Content = new EmptyState("Preview not generated", "Select a business date and choose Refresh Preview."),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        previewHost.Margin = new Thickness(0,8,0,0);
        AutomationProperties.SetName(previewHost, "Daily Sales Report preview");
        Grid.SetRow(previewHost, 2); Children.Add(previewHost);
        UpdatePeriodLabel();
        AutomationProperties.SetName(this, "Daily Sales Report workspace");
        BusinessDatePicker.SelectedDateChanged += (_, _) => InvalidatePreview();
        ScopeSelector.SelectionChanged += (_, _) => InvalidatePreview();
    }

    public void SetReport(DailySalesReportDocument report, IEnumerable<ReportDataAvailability>? availability = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (loadingScope is not null && (loadingScope != CurrentScope || BusinessDatePicker.SelectedDate?.Date != report.BusinessDate.ToDateTime(TimeOnly.MinValue))) { InvalidatePreview(); return; }
        BusinessDatePicker.SelectedDate = report.BusinessDate.ToDateTime(TimeOnly.MinValue);
        currentDocument = report;
        previewHost.Content = report.EveningSheets.Count == 0 ? new Modules.Reports.DailySalesFocusedView(report) : showMatrix ? new Modules.Reports.EveningDsrView(report) : new Modules.Reports.TodaySalesView(report);
        statusText.Text = $"Preview ready for {report.BusinessDate:dd MMM yyyy}.";
        UpdateAvailability(availability ?? DefaultAvailability(report));
        loadedScope = CurrentScope; foreach (var button in exportActions) button.IsEnabled = true;
    }

    public void ShowLoading(string message = "Loading Daily Sales Report…")
    {
        currentDocument = null;
        loadingScope = CurrentScope; loadedScope = null; foreach (var button in exportActions) button.IsEnabled = false;
        previewHost.Content = new LoadingState(message);
        statusText.Text = message;
    }

    public void ShowFailure(string message)
    {
        currentDocument = null;
        loadedScope = null; foreach (var button in exportActions) button.IsEnabled = false;
        previewHost.Content = new EmptyState("DSR could not be generated", message, "Correct the issue and choose Refresh Preview.");
        statusText.Text = message;
    }

    public void UpdateAvailability(IEnumerable<ReportDataAvailability> items)
    {
        var source = items.ToArray();
        availabilityDetails = string.Join("\n\n",source.Select(item=>$"{item.Label}: {(item.IsAvailable ? "Available" : "Data unavailable")}\n{item.Detail}"));
    }

    private void InvalidatePreview()
    {
        currentDocument = null;
        loadedScope = null; foreach (var button in exportActions) button.IsEnabled = false;
        previewHost.Content = new EmptyState("Refresh required", "The DSR date or store changed. Refresh before reviewing or exporting.");
        statusText.Text = "Scope changed — refresh the preview.";
    }

    private UIElement BuildToolbar()
    {
        var container = new Border { Background = DsrUi.Brush("Surface"), BorderBrush = DsrUi.Brush("SecondaryText"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0) };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var actions = new WrapPanel();
        BusinessDatePicker.Visibility = Visibility.Collapsed;
        actions.Children.Add(BusinessDatePicker); actions.Children.Add(ScopeSelector);
        ScopeSelector.Visibility = Visibility.Collapsed;
        actions.Children.Add(new TextBlock { Text = "Both stores", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,12,0) });
        actions.Children.Add(ActionButton("Refresh Preview", ReportWorkspaceAction.Refresh, true));
        actions.Children.Add(ActionButton("Export PDF", ReportWorkspaceAction.ExportPdf, true));
        actions.Children.Add(ActionButton("Share", ReportWorkspaceAction.Share, true));
        actions.Children.Add(ActionButton("Export Excel", ReportWorkspaceAction.ExportExcel));
        var matrix = new Button { Content = "Full matrix", Margin = new Thickness(0,0,8,0), IsEnabled = false };
        exportActions.Add(matrix);
        matrix.Click += (_,_) => { if (HasCurrentPreview && currentDocument is not null) { showMatrix = !showMatrix; matrix.Content = showMatrix ? "Summary" : "Full matrix"; previewHost.Content = showMatrix ? new Modules.Reports.EveningDsrView(currentDocument) : new Modules.Reports.TodaySalesView(currentDocument); } };
        actions.Children.Add(matrix);
        var availability = new Button { Content = "Availability", Margin = new Thickness(8,0,0,0), Padding = new Thickness(10,6,10,6) };
        availability.Click += (_,_) => new StatusDetailsDialog(Window.GetWindow(this),availabilityDetails).ShowDialog();
        AutomationProperties.SetName(availability,"Read DSR source availability"); actions.Children.Add(availability);
        root.Children.Add(actions);
        periodText = DsrUi.Text(string.Empty, 10.5, FontWeights.SemiBold, "PrimaryText");
        periodText.Margin = new Thickness(0, 8, 0, 0); Grid.SetRow(periodText, 1); root.Children.Add(periodText);
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        statusText = DsrUi.Text("Select a date and refresh the preview.", 10.5, colour: "SecondaryText", align: TextAlignment.Right);
        statusText.Name = "DsrTaskStatus"; statusText.TextWrapping = TextWrapping.Wrap; statusText.TextTrimming = TextTrimming.None;
        DockPanel.SetDock(statusText, Dock.Right); footer.Children.Add(statusText);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        SizeChanged += (_,_) =>
        {
            var shortWindow = ActualWidth < 1000;
            periodText.Visibility = Visibility.Visible; footer.Visibility = Visibility.Collapsed;
            container.Padding = new Thickness(shortWindow ? 8 : 12); container.Margin = new Thickness(0,shortWindow ? 0 : 10,0,0);
        };
        container.Child = root;
        return container;
    }

    private Button ActionButton(string label, ReportWorkspaceAction action, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), MinWidth = 86 };
        if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
        AutomationProperties.SetName(button, label);
        if (action is ReportWorkspaceAction.ExportPdf or ReportWorkspaceAction.ExportExcel or ReportWorkspaceAction.Share) { exportActions.Add(button); button.IsEnabled = false; }
        button.Click += (_, _) => RaiseAction(action);
        return button;
    }

    private void RaiseAction(ReportWorkspaceAction action)
    {
        if (action is ReportWorkspaceAction.ExportPdf or ReportWorkspaceAction.ExportExcel or ReportWorkspaceAction.Share && !HasCurrentPreview) return;
        var date = DateOnly.FromDateTime(BusinessDatePicker.SelectedDate ?? DateTime.Today);
        ActionRequested?.Invoke(this, new(action, "dsr", date, date, ScopeSelector.SelectedItem?.ToString() ?? "Both stores"));
    }

    private void UpdatePeriodLabel()
    {
        var date = DateOnly.FromDateTime(BusinessDatePicker.SelectedDate ?? DateTime.Today);
        periodText.Text = ReportingPeriodLabels.ForDate(date);
    }

    private static IReadOnlyList<ReportDataAvailability> DefaultAvailability(DailySalesReportDocument report) =>
    [
        new("Sales", report.CombinedFtd is not null, report.CombinedFtd is null ? "No recorded sales were available." : "recorded NETVALUE sales are available."),
        new("Walk-ins", report.WalkIns is not null, report.WalkIns is null ? "Enter combined walk-ins in Manual Entry." : "Combined walk-ins are available."),
        new("LY comparison", report.Stores.All(store => store.Periods.All(period => period.MissingSourceNote is null)), "Missing prior-year periods remain visibly unavailable."),
        new("Service", report.Service.Total is not null, report.Service.Total is null ? "Service source or manual input is required." : "Service values are available."),
        new("Targets", report.Targets.Any(target => target.MonthlyTarget is not null), report.Targets.Any(target => target.MonthlyTarget is not null) ? "At least one monthly target is available." : "Monthly targets require Manual Entry.")
    ];
}
