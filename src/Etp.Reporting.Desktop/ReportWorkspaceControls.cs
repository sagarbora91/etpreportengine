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
    BackToReports
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
    IReadOnlyList<ProductReportEntry> Reports);

public static class ReportWorkspaceRegistry
{
    private static readonly IReadOnlyDictionary<string, string[]> CategoryMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["sales"] = ["Sales"],
            ["stock"] = ["Stock"],
            ["tender-service"] = ["Tender / Cash", "Service"],
            ["staff"] = ["Staff"],
            ["exceptions"] = ["Exceptions"],
            ["management"] = ["Management"],
            ["investigation"] = ["Investigation"]
        };

    public static IReadOnlyList<ReportWorkspaceDefinition> All { get; } =
    [
        Build("sales", "Sales Analysis", "Daily, store, invoice, return, brand, segment and item reporting."),
        Build("stock", "Stock Control", "Closing, physical, variance, movement and exception stock reporting."),
        Build("tender-service", "Tender, Cash & Service", "Tender controls, cash reconciliation, diagnostics and service reporting."),
        Build("staff", "Staff / CRO", "Staff attribution, performance, targets and contribution."),
        Build("exceptions", "Exception Centre", "Source, mapping, stock, staff and tender findings."),
        Build("management", "Management", "Management trends and report-pack preparation."),
        Build("investigation", "Investigation", "Invoice-level source lineage and evidence.")
    ];

    public static ReportWorkspaceDefinition ForReport(string reportCode) =>
        All.Single(workspace => workspace.Reports.Any(report => report.Code.Equals(reportCode, StringComparison.OrdinalIgnoreCase)));

    private static ReportWorkspaceDefinition Build(string id, string name, string description)
    {
        var categories = CategoryMap[id];
        var reports = ProductReportCatalogue.All
            .Where(report => categories.Contains(report.Category, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        return new(id, name, description, reports);
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
    private ReportPreviewScope CurrentScope => new(DateFromPicker.SelectedDate, DateToPicker.SelectedDate, ScopeSelector.SelectedItem?.ToString(), SelectedReport?.Code);
    public bool HasCurrentPreview => loadedScope is not null && loadedScope == CurrentScope;
    private readonly ReportWorkspaceDefinition definition;
    private readonly ListBox reportMenu;
    private readonly ContentControl previewHost;
    private readonly TextBlock reportTitle;
    private TextBlock statusText = null!;
    private bool suppressSelectionChanged;
    private Action updateToolbar = () => { };
    private bool compactFilters;
    public void FocusPeriod() { if(compactFilters) new Modules.Reports.ReportScopeDialog(this).ShowDialog(); else (DateFromPicker.IsEnabled ? DateFromPicker : DateToPicker).Focus(); }

    public event EventHandler<ReportWorkspaceActionRequest>? ActionRequested;
    public event EventHandler<ProductReportEntry>? ReportSelected;

    public DatePicker DateFromPicker { get; }
    public DatePicker DateToPicker { get; }
    public ComboBox ScopeSelector { get; }
    public ProductReportEntry? SelectedReport => reportMenu.SelectedItem as ProductReportEntry;

    public ReportWorkspaceControl(ReportWorkspaceDefinition definition)
    {
        this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Background = DsrUi.Brush("#F4F7FB");
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        ColumnDefinitions.Add(new ColumnDefinition());

        reportMenu = BuildReportMenu();
        var navigation = BuildNavigation(); navigation.Visibility = Visibility.Collapsed; Children.Add(navigation);

        var body = new Grid { Margin = new Thickness(18, 12, 18, 14) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());

        reportTitle = DsrUi.Text(definition.DisplayName, 24, FontWeights.SemiBold);
        body.Children.Add(reportTitle);

        DateFromPicker = new DatePicker { Width = 180, SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 0) };
        DateToPicker = new DatePicker { Width = 180, SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 0) };
        ScopeSelector = new ComboBox { Width = 190, SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 0), ItemsSource = new[] { "Combined (Titan + Helios)", "Titan", "Helios" } };
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

        if (definition.Reports.Count > 0) reportMenu.SelectedIndex = 0;
        AutomationProperties.SetName(this, $"{definition.DisplayName} report workspace");
        DateFromPicker.SelectedDateChanged += (_, _) => InvalidatePreview();
        DateToPicker.SelectedDateChanged += (_, _) => { if (Modules.Reports.ReportTaskScope.IsSnapshot(SelectedReport?.Code)) DateFromPicker.SelectedDate = DateToPicker.SelectedDate; InvalidatePreview(); };
        ScopeSelector.SelectionChanged += (_, _) => InvalidatePreview();
    }

    public void SetPreview(UIElement content, string status)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (loadingScope is not null && loadingScope != CurrentScope) { InvalidatePreview(); return; }
        previewHost.Content = content;
        statusText.Text = status;
        updateToolbar();
        loadedScope = CurrentScope; foreach (var button in exportActions) button.IsEnabled = true;
    }

    public void SelectReport(string reportCode, bool notify = false)
    {
        var report = definition.Reports.FirstOrDefault(x => x.Code.Equals(reportCode, StringComparison.OrdinalIgnoreCase));
        if (report is null) throw new ArgumentOutOfRangeException(nameof(reportCode), reportCode, "The report is not part of this workspace.");
        suppressSelectionChanged = !notify;
        try
        {
            reportMenu.SelectedItem = report;
            reportTitle.Text = report.Name;
            statusText.Text = report.Description;
        }
        finally { suppressSelectionChanged = false; }
    }

    public void ConfigureTaskScope(string? scope)
    {
        ScopeSelector.ItemsSource = Modules.Reports.ReportTaskScope.RequiresSingleStore(SelectedReport?.Code)
            ? new[] { "Select one store", "Titan", "Helios" } : new[] { "Combined (Titan + Helios)", "Titan", "Helios" };
        SetStoreScope(scope ?? "Combined (Titan + Helios)");
        var snapshot = Modules.Reports.ReportTaskScope.IsSnapshot(SelectedReport?.Code);
        DateFromPicker.IsEnabled = !snapshot;
        DateFromPicker.ToolTip = snapshot ? "Snapshot reports use the displayed end date as their business date." : "Report start date";
        if (snapshot) DateFromPicker.SelectedDate = DateToPicker.SelectedDate;
        updateToolbar();
    }
    public void SetStoreScope(string scope) => ScopeSelector.SelectedItem = Modules.Reports.ReportTaskScope.RequiresSingleStore(SelectedReport?.Code) && scope is not ("Titan" or "Helios") ? "Select one store" : scope;

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

    private UIElement BuildNavigation()
    {
        var panel = new Grid { Background = DsrUi.Brush("#FFFFFF"), Margin = new Thickness(0) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition());
        var heading = DsrUi.Text(definition.DisplayName, 16, FontWeights.SemiBold);
        heading.Margin = new Thickness(8, 6, 8, 12);
        panel.Children.Add(heading);
        Grid.SetRow(reportMenu, 1); panel.Children.Add(reportMenu);
        return panel;
    }

    private void InvalidatePreview()
    {
        updateToolbar();
        loadedScope = null; foreach (var button in exportActions) button.IsEnabled = false;
        previewHost.Content = new EmptyState("Refresh required", "The report date or store changed. Refresh before reviewing or exporting.");
        statusText.Text = "Scope changed — refresh the preview.";
    }

    private ListBox BuildReportMenu()
    {
        var menu = new ListBox
        {
            ItemsSource = definition.Reports,
            DisplayMemberPath = nameof(ProductReportEntry.Name),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(0)
        };
        AutomationProperties.SetName(menu, $"{definition.DisplayName} reports");
        menu.SelectionChanged += (_, _) =>
        {
            if (menu.SelectedItem is not ProductReportEntry report) return;
            reportTitle.Text = report.Name;
            statusText.Text = report.Description;
            if (suppressSelectionChanged) return;
            ReportSelected?.Invoke(this, report);
        };
        return menu;
    }

    private UIElement BuildToolbar()
    {
        var container = new Border { Background = DsrUi.Brush("#FFFFFF"), BorderBrush = DsrUi.Brush("#DCE4EF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0) };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var actions = new WrapPanel();
        actions.Children.Add(DateFromPicker); actions.Children.Add(DateToPicker); actions.Children.Add(ScopeSelector);
        var filters = new Button { Content = "Period & store", Margin = new Thickness(0,0,8,0), Padding = new Thickness(12,6,12,6) };
        filters.Click += (_,_) => new Modules.Reports.ReportScopeDialog(this).ShowDialog();
        AutomationProperties.SetName(filters,"Edit report period and store"); actions.Children.Add(filters);
        actions.Children.Add(ActionButton("Refresh", ReportWorkspaceAction.Refresh, true));
        actions.Children.Add(ReportActionMenu.Create(RaiseAction, exportActions, false));
        root.Children.Add(actions);
        statusText = DsrUi.Text(definition.Description, 12, colour: "#687285"); statusText.Name = "ReportTaskStatus";
        statusText.TextWrapping = TextWrapping.NoWrap; statusText.TextTrimming = TextTrimming.CharacterEllipsis;
        var summaries = new StackPanel { Margin = new Thickness(0,6,0,0) }; var scope = DsrUi.Text("",12);
        summaries.Children.Add(scope); summaries.Children.Add(statusText); Grid.SetRow(summaries,1); root.Children.Add(summaries);
        updateToolbar = () =>
        {
            compactFilters = container.ActualWidth is > 0 and < 780;
            DateFromPicker.Visibility = !compactFilters && DateFromPicker.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            DateToPicker.Visibility = ScopeSelector.Visibility = compactFilters ? Visibility.Collapsed : Visibility.Visible;
            filters.Visibility = scope.Visibility = compactFilters ? Visibility.Visible : Visibility.Collapsed;
            scope.Text = $"{DateFromPicker.SelectedDate:dd MMM yyyy} – {DateToPicker.SelectedDate:dd MMM yyyy} · {ScopeSelector.SelectedItem} · {statusText.Text}";
            scope.TextWrapping = TextWrapping.NoWrap; scope.TextTrimming = TextTrimming.CharacterEllipsis;
            statusText.Visibility = compactFilters ? Visibility.Collapsed : Visibility.Visible;
            container.Padding = new Thickness(compactFilters ? 8 : 12); container.Margin = new Thickness(0,compactFilters ? 0 : 10,0,0);
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
        if (action is ReportWorkspaceAction.ExportPdf or ReportWorkspaceAction.ExportExcel) { exportActions.Add(button); button.IsEnabled = false; }
        button.Click += (_, _) => RaiseAction(action);
        return button;
    }

    private void RaiseAction(ReportWorkspaceAction action)
    {
        if (action is ReportWorkspaceAction.ExportPdf or ReportWorkspaceAction.ExportExcel && !HasCurrentPreview) return;
        var from = DateOnly.FromDateTime(DateFromPicker.SelectedDate ?? DateTime.Today);
        var to = DateOnly.FromDateTime(DateToPicker.SelectedDate ?? DateTime.Today);
        ActionRequested?.Invoke(this, new(action, SelectedReport?.Code, from, to, ScopeSelector.SelectedItem?.ToString() ?? "Combined (Titan + Helios)"));
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
    private WrapPanel availabilityPanel = null!;
    private TextBlock periodText = null!;
    private TextBlock statusText = null!;
    private string availabilityDetails = "Refresh the DSR to check source availability.";

    public event EventHandler<ReportWorkspaceActionRequest>? ActionRequested;

    public DatePicker BusinessDatePicker { get; }
    public ComboBox ScopeSelector { get; }

    public DailySalesReportWorkspace()
    {
        Background = DsrUi.Brush("#F4F7FB");
        Margin = new Thickness(18, 12, 18, 14);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition());
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(DsrUi.Text("Daily Sales Report", 26, FontWeights.SemiBold));
        titles.Children.Add(DsrUi.Text("Select the business date, review availability, preview and export from one screen.", 11.5, colour: "#687285"));
        titleRow.Children.Add(titles);
        titleRow.Visibility = Visibility.Collapsed;
        var back = ActionButton("Back to Reports", ReportWorkspaceAction.BackToReports);
        Grid.SetColumn(back, 1); titleRow.Children.Add(back);
        Children.Add(titleRow);

        BusinessDatePicker = new DatePicker { Width = 180, SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 0) };
        ScopeSelector = new ComboBox { Width = 190, SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 0), ItemsSource = new[] { "Combined (Titan + Helios)", "Titan", "Helios" } };
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
        previewHost.Content = new Modules.Reports.DailySalesFocusedView(report);
        statusText.Text = $"Preview ready for {report.BusinessDate:dd MMM yyyy}.";
        UpdateAvailability(availability ?? DefaultAvailability(report));
        loadedScope = CurrentScope; foreach (var button in exportActions) button.IsEnabled = true;
    }

    public void ShowLoading(string message = "Loading Daily Sales Report…")
    {
        loadingScope = CurrentScope; loadedScope = null; foreach (var button in exportActions) button.IsEnabled = false;
        previewHost.Content = new LoadingState(message);
        statusText.Text = message;
    }

    public void ShowFailure(string message)
    {
        loadedScope = null; foreach (var button in exportActions) button.IsEnabled = false;
        previewHost.Content = new EmptyState("DSR could not be generated", message, "Correct the issue and choose Refresh Preview.");
        statusText.Text = message;
    }

    public void UpdateAvailability(IEnumerable<ReportDataAvailability> items)
    {
        var source = items.ToArray();
        availabilityDetails = string.Join("\n\n",source.Select(item=>$"{item.Label}: {(item.IsAvailable ? "Available" : "Data unavailable")}\n{item.Detail}"));
        availabilityPanel.Children.Clear();
        foreach (var item in source)
        {
            var badge = new Border
            {
                Background = DsrUi.Brush(item.IsAvailable ? "#E8F7F0" : "#FFF4DF"),
                BorderBrush = DsrUi.Brush(item.IsAvailable ? "#80CEAC" : "#E9B45C"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 7, 0),
                Child = DsrUi.Text($"{item.Label}: {(item.IsAvailable ? "Available" : "Data unavailable")}", 10, FontWeights.SemiBold, item.IsAvailable ? "#08764B" : "#9B5C00")
            };
            badge.ToolTip = item.Detail;
            AutomationProperties.SetName(badge, $"{item.Label}. {(item.IsAvailable ? "Available" : "Data unavailable")}. {item.Detail}");
            availabilityPanel.Children.Add(badge);
        }
    }

    private void InvalidatePreview()
    {
        loadedScope = null; foreach (var button in exportActions) button.IsEnabled = false;
        previewHost.Content = new EmptyState("Refresh required", "The DSR date or store changed. Refresh before reviewing or exporting.");
        statusText.Text = "Scope changed — refresh the preview.";
    }

    private UIElement BuildToolbar()
    {
        var container = new Border { Background = DsrUi.Brush("#FFFFFF"), BorderBrush = DsrUi.Brush("#DCE4EF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0) };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var actions = new WrapPanel();
        actions.Children.Add(BusinessDatePicker); actions.Children.Add(ScopeSelector);
        ScopeSelector.Visibility = Visibility.Collapsed;
        actions.Children.Add(new TextBlock { Text = "Titan + Helios", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,12,0) });
        actions.Children.Add(ActionButton("Refresh Preview", ReportWorkspaceAction.Refresh, true));
        actions.Children.Add(ReportActionMenu.Create(RaiseAction, exportActions, true));
        var availability = new Button { Content = "Availability", Margin = new Thickness(8,0,0,0), Padding = new Thickness(10,6,10,6) };
        availability.Click += (_,_) => new StatusDetailsDialog(Window.GetWindow(this),availabilityDetails).ShowDialog();
        AutomationProperties.SetName(availability,"Read DSR source availability"); actions.Children.Add(availability);
        root.Children.Add(actions);
        periodText = DsrUi.Text(string.Empty, 10.5, FontWeights.SemiBold, "#36506F");
        periodText.Margin = new Thickness(0, 8, 0, 0); Grid.SetRow(periodText, 1); root.Children.Add(periodText);
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        availabilityPanel = new WrapPanel { Visibility = Visibility.Collapsed };
        statusText = DsrUi.Text("Select a date and refresh the preview.", 10.5, colour: "#687285", align: TextAlignment.Right);
        statusText.Name = "DsrTaskStatus"; statusText.TextWrapping = TextWrapping.NoWrap; statusText.TextTrimming = TextTrimming.CharacterEllipsis;
        DockPanel.SetDock(statusText, Dock.Right); footer.Children.Add(statusText); footer.Children.Add(availabilityPanel);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        SizeChanged += (_,_) =>
        {
            var shortWindow = ActualHeight < 300;
            periodText.Visibility = footer.Visibility = shortWindow ? Visibility.Collapsed : Visibility.Visible;
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
        if (action is ReportWorkspaceAction.ExportPdf or ReportWorkspaceAction.ExportExcel) { exportActions.Add(button); button.IsEnabled = false; }
        button.Click += (_, _) => RaiseAction(action);
        return button;
    }

    private void RaiseAction(ReportWorkspaceAction action)
    {
        if (action is ReportWorkspaceAction.ExportPdf or ReportWorkspaceAction.ExportExcel && !HasCurrentPreview) return;
        var date = DateOnly.FromDateTime(BusinessDatePicker.SelectedDate ?? DateTime.Today);
        ActionRequested?.Invoke(this, new(action, "dsr", date, date, ScopeSelector.SelectedItem?.ToString() ?? "Combined (Titan + Helios)"));
    }

    private void UpdatePeriodLabel()
    {
        var date = DateOnly.FromDateTime(BusinessDatePicker.SelectedDate ?? DateTime.Today);
        periodText.Text = ReportingPeriodLabels.ForDate(date);
    }

    private static IReadOnlyList<ReportDataAvailability> DefaultAvailability(DailySalesReportDocument report) =>
    [
        new("Sales", report.CombinedFtd is not null, report.CombinedFtd is null ? "No canonical sales were available." : "Canonical NETVALUE sales are available."),
        new("Walk-ins", report.WalkIns is not null, report.WalkIns is null ? "Enter combined walk-ins in Manual Entry." : "Combined walk-ins are available."),
        new("LY comparison", report.Stores.All(store => store.Periods.All(period => period.MissingSourceNote is null)), "Missing prior-year periods remain visibly unavailable."),
        new("Service", report.Service.Total is not null, report.Service.Total is null ? "Service source or manual input is required." : "Service values are available."),
        new("Targets", report.Targets.Any(target => target.MonthlyTarget is not null), report.Targets.Any(target => target.MonthlyTarget is not null) ? "At least one monthly target is available." : "Monthly targets require Manual Entry.")
    ];
}
