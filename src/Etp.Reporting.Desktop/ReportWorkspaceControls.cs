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
    private readonly ReportWorkspaceDefinition definition;
    private readonly ListBox reportMenu;
    private readonly ContentControl previewHost;
    private readonly TextBlock reportTitle;
    private TextBlock statusText = null!;
    private bool suppressSelectionChanged;

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
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(238) });
        ColumnDefinitions.Add(new ColumnDefinition());

        reportMenu = BuildReportMenu();
        Children.Add(BuildNavigation());

        var body = new Grid { Margin = new Thickness(18, 12, 18, 14) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());

        reportTitle = DsrUi.Text(definition.DisplayName, 24, FontWeights.SemiBold);
        body.Children.Add(reportTitle);

        DateFromPicker = new DatePicker { Width = 142, SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 0) };
        DateToPicker = new DatePicker { Width = 142, SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 0) };
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
        var previewScroll = new ScrollViewer
        {
            Content = previewHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(1)
        };
        AutomationProperties.SetName(previewScroll, "Report preview and results");
        Grid.SetRow(previewScroll, 2); body.Children.Add(previewScroll);
        Grid.SetColumn(body, 1); Children.Add(body);

        if (definition.Reports.Count > 0) reportMenu.SelectedIndex = 0;
        AutomationProperties.SetName(this, $"{definition.DisplayName} report workspace");
    }

    public void SetPreview(UIElement content, string status)
    {
        ArgumentNullException.ThrowIfNull(content);
        previewHost.Content = content;
        statusText.Text = status;
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

    public void ShowLoading(string message)
    {
        previewHost.Content = new LoadingState(message);
        statusText.Text = message;
    }

    public void ShowUnavailable(string title, string message)
    {
        previewHost.Content = new EmptyState(title, message, "Review the relevant source or manual input, then refresh.");
        statusText.Text = message;
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
        actions.Children.Add(ActionButton("Refresh", ReportWorkspaceAction.Refresh, true));
        actions.Children.Add(ActionButton("Export PDF", ReportWorkspaceAction.ExportPdf));
        actions.Children.Add(ActionButton("Export Excel", ReportWorkspaceAction.ExportExcel));
        actions.Children.Add(ActionButton("Generate Pack", ReportWorkspaceAction.GenerateReportPack));
        actions.Children.Add(ActionButton("Open Export Folder", ReportWorkspaceAction.OpenExportFolder));
        root.Children.Add(actions);
        statusText = DsrUi.Text(definition.Description, 10.5, colour: "#687285");
        statusText.Margin = new Thickness(0, 8, 0, 0); Grid.SetRow(statusText, 1); root.Children.Add(statusText);
        container.Child = root;
        return container;
    }

    private Button ActionButton(string label, ReportWorkspaceAction action, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), MinWidth = 86 };
        if (primary) { button.Background = DsrUi.Brush("#2269E8"); button.Foreground = Brushes.White; }
        AutomationProperties.SetName(button, $"{label} current report");
        button.Click += (_, _) => RaiseAction(action);
        return button;
    }

    private void RaiseAction(ReportWorkspaceAction action)
    {
        var from = DateOnly.FromDateTime(DateFromPicker.SelectedDate ?? DateTime.Today);
        var to = DateOnly.FromDateTime(DateToPicker.SelectedDate ?? DateTime.Today);
        ActionRequested?.Invoke(this, new(action, SelectedReport?.Code, from, to, ScopeSelector.SelectedItem?.ToString() ?? "Combined (Titan + Helios)"));
    }
}

public sealed class DailySalesReportWorkspace : Grid
{
    private readonly ContentControl previewHost;
    private WrapPanel availabilityPanel = null!;
    private TextBlock periodText = null!;
    private TextBlock statusText = null!;

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
        var back = ActionButton("Back to Reports", ReportWorkspaceAction.BackToReports);
        Grid.SetColumn(back, 1); titleRow.Children.Add(back);
        Children.Add(titleRow);

        BusinessDatePicker = new DatePicker { Width = 150, SelectedDate = DateTime.Today, Margin = new Thickness(0, 0, 8, 0) };
        ScopeSelector = new ComboBox { Width = 210, SelectedIndex = 0, Margin = new Thickness(0, 0, 12, 0), ItemsSource = new[] { "Combined (Titan + Helios)", "Titan", "Helios" } };
        AutomationProperties.SetName(BusinessDatePicker, "DSR business date");
        AutomationProperties.SetName(ScopeSelector, "DSR store scope");
        BusinessDatePicker.SelectedDateChanged += (_, _) => UpdatePeriodLabel();

        var toolbar = BuildToolbar();
        Grid.SetRow(toolbar, 1); Children.Add(toolbar);

        previewHost = new ContentControl
        {
            Content = new EmptyState("Preview not generated", "Select a business date and choose Refresh Preview."),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        var previewScroll = new ScrollViewer
        {
            Content = previewHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(1)
        };
        AutomationProperties.SetName(previewScroll, "Daily Sales Report preview");
        Grid.SetRow(previewScroll, 2); Children.Add(previewScroll);
        UpdatePeriodLabel();
        AutomationProperties.SetName(this, "Daily Sales Report workspace");
    }

    public void SetReport(DailySalesReportDocument report, IEnumerable<ReportDataAvailability>? availability = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        BusinessDatePicker.SelectedDate = report.BusinessDate.ToDateTime(TimeOnly.MinValue);
        previewHost.Content = new DailySalesReportView(report);
        statusText.Text = $"Preview ready for {report.BusinessDate:dd MMM yyyy}.";
        UpdateAvailability(availability ?? DefaultAvailability(report));
    }

    public void ShowLoading(string message = "Loading Daily Sales Report…")
    {
        previewHost.Content = new LoadingState(message);
        statusText.Text = message;
    }

    public void ShowFailure(string message)
    {
        previewHost.Content = new EmptyState("DSR could not be generated", message, "Correct the issue and choose Refresh Preview.");
        statusText.Text = message;
    }

    public void UpdateAvailability(IEnumerable<ReportDataAvailability> items)
    {
        availabilityPanel.Children.Clear();
        foreach (var item in items)
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

    private UIElement BuildToolbar()
    {
        var container = new Border { Background = DsrUi.Brush("#FFFFFF"), BorderBrush = DsrUi.Brush("#DCE4EF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Margin = new Thickness(0, 10, 0, 0) };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var actions = new WrapPanel();
        actions.Children.Add(BusinessDatePicker); actions.Children.Add(ScopeSelector);
        actions.Children.Add(ActionButton("Refresh Preview", ReportWorkspaceAction.Refresh, true));
        actions.Children.Add(ActionButton("Export PDF", ReportWorkspaceAction.ExportPdf));
        actions.Children.Add(ActionButton("Export Excel", ReportWorkspaceAction.ExportExcel));
        actions.Children.Add(ActionButton("Generate Report Pack", ReportWorkspaceAction.GenerateReportPack));
        actions.Children.Add(ActionButton("Open Export Folder", ReportWorkspaceAction.OpenExportFolder));
        actions.Children.Add(ActionButton("Manual Entry", ReportWorkspaceAction.OpenManualEntry));
        root.Children.Add(actions);
        periodText = DsrUi.Text(string.Empty, 10.5, FontWeights.SemiBold, "#36506F");
        periodText.Margin = new Thickness(0, 8, 0, 0); Grid.SetRow(periodText, 1); root.Children.Add(periodText);
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        availabilityPanel = new WrapPanel();
        statusText = DsrUi.Text("Select a date and refresh the preview.", 10.5, colour: "#687285", align: TextAlignment.Right);
        DockPanel.SetDock(statusText, Dock.Right); footer.Children.Add(statusText); footer.Children.Add(availabilityPanel);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        container.Child = root;
        return container;
    }

    private Button ActionButton(string label, ReportWorkspaceAction action, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), MinWidth = 86 };
        if (primary) { button.Background = DsrUi.Brush("#2269E8"); button.Foreground = Brushes.White; }
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => RaiseAction(action);
        return button;
    }

    private void RaiseAction(ReportWorkspaceAction action)
    {
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
