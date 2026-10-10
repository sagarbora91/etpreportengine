extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Etp.Reporting.Reporting;
using EtpApplication::Etp.Reporting.Application.Service;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>
/// Service Pending board (1.10.0, design review 3.3): every open job grouped by stage in stage order, longest in stage
/// first, with the age band, the overdue rule of Q4 and five numbers. Delivered, returned and claimed DC/RA jobs never
/// appear (Q3, Q8). The query gives the rows (IServiceReportQuery.LoadPendingBoardAsync); the screen-side rules are
/// <see cref="ServicePendingBoardRules"/>. A row opens Job history (double-click, Enter or the button). Export writes
/// one sheet per stage shown.
/// </summary>
public sealed class ServicePendingBoardView : ServiceScreenView
{
    public const string OpenJobText = "Open job history";
    public const string AllAgesLabel = "All ages";
    public const string AllBrandsLabel = "All brands";
    public const string AllGuaranteesLabel = "All guarantee";
    public const string AllJobTypesLabel = "Booking and Quick Billing";

    private readonly Dictionary<string, CheckBox> stageBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ComboBox ageBandFilter = new() { MinWidth = 130, MinHeight = 44, Margin = new Thickness(0, 0, 12, 0) };
    private readonly CheckBox overdueOnlyFilter = new() { Content = "Overdue only", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), MinHeight = 44 };
    private readonly ComboBox brandFilter = new() { MinWidth = 160, MinHeight = 44, Margin = new Thickness(0, 0, 12, 0) };
    private readonly ComboBox guaranteeFilter = new() { MinWidth = 140, MinHeight = 44, Margin = new Thickness(0, 0, 12, 0) };
    private readonly ComboBox joTypeFilter = new() { MinWidth = 200, MinHeight = 44, Margin = new Thickness(0, 0, 12, 0) };
    private readonly Button openJobButton = new() { Content = OpenJobText, MinHeight = 44, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
    private readonly WrapPanel cards = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
    private readonly Action<string>? openJob;
    private readonly ServiceExcelPackExport packExport;
    private string? requestedBrand;
    private string? requestedGuarantee;
    private bool populating;

    public ServicePendingBoardView(Func<IServiceReportQuery> query, ServiceExcelExport export,
        ServiceExcelPackExport? packExport = null, Action<string>? openJob = null)
        : base("Service pending board",
            "Every open job by stage, longest in stage first. Overdue means the EDD has passed or, without an EDD, the job has been in its stage longer than the stage limit (bench 7, indent 15, SRN out 30, in transit 15, ready 7 days). Delivered and returned jobs and DC/RA jobs with a claim are not on the board. Tick stages to limit the board; Export to Excel writes one sheet per stage shown.",
            "Service.PendingBoard", "SERVICE_PENDING_BOARD_LOAD_FAILED", query, export)
    {
        this.openJob = openJob;
        this.packExport = packExport ?? ((path, document) => new Reports.ReportExportCoordinator().ExportPackExcelAsync(path, document));

        Summary.Children.Add(cards);

        var stages = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        stages.Children.Add(new TextBlock { Text = "Stages (none ticked = all)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        foreach (var stage in ServicePendingBoardRules.BoardStages)
        {
            var label = ServiceStages.Label(stage);
            var box = new CheckBox { Content = label, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            AutomationProperties.SetName(box, "Stage " + label);
            box.Checked += async (_, _) => await OnFilterChangedAsync();
            box.Unchecked += async (_, _) => await OnFilterChangedAsync();
            stageBoxes[stage] = box;
            stages.Children.Add(box);
        }
        Summary.Children.Add(stages);

        AddFilter("Age", ageBandFilter, "Age band", [new(null, AllAgesLabel), .. ServiceAgeing.Bands.Select(band => new ServiceListChoice(band, band + " days"))]);
        AutomationProperties.SetName(overdueOnlyFilter, "Overdue only");
        overdueOnlyFilter.Checked += async (_, _) => await OnFilterChangedAsync();
        overdueOnlyFilter.Unchecked += async (_, _) => await OnFilterChangedAsync();
        FilterBar.Children.Add(overdueOnlyFilter);
        AddFilter("Brand", brandFilter, "Brand", [new(null, AllBrandsLabel)]);
        AddFilter("Guarantee", guaranteeFilter, "Guarantee", [new(null, AllGuaranteesLabel)]);
        AddFilter("Job type", joTypeFilter, "Job type",
            [new(null, AllJobTypesLabel), new(ServiceJobTypes.Booking, ServiceJobTypes.Booking), new(ServiceJobTypes.QuickBilling, ServiceJobTypes.QuickBilling)]);
        openJobButton.Click += (_, _) => OpenSelected();
        openJobButton.Visibility = openJob is null ? Visibility.Collapsed : Visibility.Visible;
        FilterBar.Children.Add(openJobButton);

        SetColumns(
        [
            new("Job number", Width: 150), new("Stage", Width: 220), new("Booked on", DisplayFormat: "dd MMM yyyy"),
            new("Days since booking", "#,##0", Width: 100), new("Days in stage", "#,##0", Width: 90), new("EDD", DisplayFormat: "dd MMM yyyy"),
            new("Overdue by (days)", "#,##0", Width: 100), new("Age band", Width: 80), new("Brand"), new("Model", Width: 150),
            new("Product", Width: 120), new("Guarantee", Width: 100), new("Customer type", Width: 120), new("Job type", Width: 110),
            new("Pending at", Width: 120), new("Spare required", Width: 200), new("Last reading", DisplayFormat: "dd MMM yyyy")
        ]);
        Table.GroupStyle.Add(GroupStyle.Default);
        Table.SelectionChanged += (_, _) => openJobButton.IsEnabled = openJob is not null && Table.SelectedItem is ServiceGridRow;
        Table.MouseDoubleClick += (_, _) => OpenSelected();
        Table.KeyDown += (_, args) => { if (args.Key == Key.Enter && OpenSelected()) args.Handled = true; };
    }

    /// <summary>Every job on the board before the filters, in board order.</summary>
    public IReadOnlyList<ServicePendingBoardRow> Board { get; private set; } = [];

    /// <summary>The groups of the rows shown (after the filters), in stage order.</summary>
    public IReadOnlyList<ServicePendingGroup> Groups { get; private set; } = [];

    /// <summary>The five numbers, over the whole board; null until loaded.</summary>
    public ServicePendingNumbers? Numbers { get; private set; }

    /// <summary>The snapshot date the board was built for (the query's), once loaded.</summary>
    public DateOnly? AsAt { get; private set; }

    public bool CanOpenJob => openJob is not null;

    public IReadOnlyCollection<string> SelectedStages
    {
        get => stageBoxes.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray();
        set
        {
            populating = true;
            try { foreach (var pair in stageBoxes) pair.Value.IsChecked = value.Contains(pair.Key, StringComparer.OrdinalIgnoreCase); }
            finally { populating = false; }
        }
    }

    public string? SelectedAgeBand
    {
        get => (ageBandFilter.SelectedItem as ServiceListChoice)?.Code;
        set => SelectChoice(ageBandFilter, value);
    }

    public bool OverdueOnly
    {
        get => overdueOnlyFilter.IsChecked == true;
        set { populating = true; try { overdueOnlyFilter.IsChecked = value; } finally { populating = false; } }
    }

    /// <summary>A brand choice; it applies once the board has loaded the brand into the list.</summary>
    public string? SelectedBrand
    {
        get => (brandFilter.SelectedItem as ServiceListChoice)?.Code;
        set { requestedBrand = value; SelectChoice(brandFilter, value); }
    }

    public string? SelectedGuarantee
    {
        get => (guaranteeFilter.SelectedItem as ServiceListChoice)?.Code;
        set { requestedGuarantee = value; SelectChoice(guaranteeFilter, value); }
    }

    public string? SelectedJoType
    {
        get => (joTypeFilter.SelectedItem as ServiceListChoice)?.Code;
        set => SelectChoice(joTypeFilter, value);
    }

    public ServicePendingFilter Filter => new(SelectedStages, SelectedAgeBand, OverdueOnly, requestedBrand, requestedGuarantee, SelectedJoType);

    /// <summary>Opens Job history for the selected row. False when nothing is selected or the shell gave no navigation.</summary>
    public bool OpenSelected()
    {
        if (openJob is null || Table.SelectedItem is not ServiceGridRow { Source: ServicePendingBoardRow row }) return false;
        openJob(row.JobOrderNumber);
        return true;
    }

    protected override async Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(IServiceReportQuery source)
    {
        var board = await source.LoadPendingBoardAsync();
        AsAt = board.AsAt;
        Board = ServicePendingBoardRules.Rows(board.Rows);
        Numbers = ServicePendingBoardRules.Numbers(Board);
        ShowNumbers(Numbers);
        RefillChoices(brandFilter, AllBrandsLabel, Board.Select(row => row.Brand), requestedBrand);
        RefillChoices(guaranteeFilter, AllGuaranteesLabel, Board.Select(row => row.Guarantee), requestedGuarantee);
        var visible = ServicePendingBoardRules.Apply(Board, Filter);
        Groups = ServicePendingBoardRules.Groups(visible);
        return visible.Select(ToRow).ToArray();
    }

    protected override Task LoadExtrasAsync(IServiceReportQuery source, Func<bool> isCurrent)
    {
        // The frame has set ItemsSource; group the grid by stage so each group has a header in stage order.
        Table.Items.GroupDescriptions.Clear();
        Table.Items.GroupDescriptions.Add(new PropertyGroupDescription("Source.StageLabel"));
        return Task.CompletedTask;
    }

    protected override void ClearExtras()
    {
        Board = []; Groups = []; Numbers = null; AsAt = null; cards.Children.Clear(); openJobButton.IsEnabled = false;
    }

    public static ServiceGridRow ToRow(ServicePendingBoardRow row) =>
        new(row,
        [
            row.JobOrderNumber, row.StageLabel, row.BookingDate, row.DaysSinceBooking, row.DaysInStage, row.Edd,
            row.OverdueBy, row.AgeBand, row.Brand, row.Model, row.ProductCategory, row.Guarantee,
            row.CustomerType, row.JoType, row.PendingAt, row.SpareRequired, row.LastReadingDate
        ]);

    /// <summary>The export: one sheet per stage shown, the board's columns, the as-at line as the control text.</summary>
    public ReportPackDocument BuildExportDocument(DateTimeOffset generatedUtc)
    {
        var columns = BuildExportData().Columns;
        var (from, to) = ExportPeriod;
        var tables = Groups.Select(group => new ReportPackTable(group.Label,
                $"{group.Rows.Count:N0} job{(group.Rows.Count == 1 ? "" : "s")}", AsAtText,
                new ExcelReportData(columns, group.Rows.Select(row => ToRow(row).Cells).ToArray())))
            .ToArray();
        return new ReportPackDocument(ExportName, from, to, "Read only", "service-ui-1", AsAtText, generatedUtc, tables);
    }

    public override Task ExportToPathAsync(string path) => packExport(path, BuildExportDocument(DateTimeOffset.UtcNow));

    private void ShowNumbers(ServicePendingNumbers numbers)
    {
        cards.Children.Clear();
        cards.Children.Add(Card("Open jobs", numbers.OpenJobs, "on the board", "SecondaryText"));
        cards.Children.Add(Card("Overdue", numbers.Overdue, "EDD passed or over the stage limit", numbers.Overdue > 0 ? "Critical" : "Success"));
        cards.Children.Add(Card("Over 30 days", numbers.Over30Days, "since booking", numbers.Over30Days > 0 ? "Critical" : "Success"));
        cards.Children.Add(Card("In transit", numbers.InTransit, "sent back after repair", "SecondaryText"));
        cards.Children.Add(Card("Parts awaited", numbers.PartsAwaited, "indent raised", "SecondaryText"));
    }

    private static KpiCard Card(string label, int value, string secondary, string accent) =>
        new(label, value.ToString("N0"), secondary, accent) { MinWidth = 160 };

    private void AddFilter(string label, ComboBox combo, string automationName, IReadOnlyList<ServiceListChoice> choices)
    {
        FilterBar.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        AutomationProperties.SetName(combo, automationName);
        combo.ItemsSource = choices;
        combo.SelectedIndex = 0;
        combo.SelectionChanged += async (_, _) => await OnFilterChangedAsync();
        FilterBar.Children.Add(combo);
    }

    private async Task OnFilterChangedAsync()
    {
        if (populating || !IsLoaded) return;
        await ActivateAsync();
    }

    private void SelectChoice(ComboBox combo, string? code)
    {
        populating = true;
        try
        {
            var choices = (IReadOnlyList<ServiceListChoice>)combo.ItemsSource;
            combo.SelectedItem = choices.FirstOrDefault(choice => string.Equals(choice.Code, code, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
        }
        finally { populating = false; }
    }

    private void RefillChoices(ComboBox combo, string allLabel, IEnumerable<string?> values, string? requested)
    {
        populating = true;
        try
        {
            var choices = new List<ServiceListChoice> { new(null, allLabel) };
            choices.AddRange(values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).Select(value => new ServiceListChoice(value, value)));
            combo.ItemsSource = choices;
            combo.SelectedItem = choices.FirstOrDefault(choice => string.Equals(choice.Code, requested, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
        }
        finally { populating = false; }
    }

    protected override string EmptyRowsText => Board.Count == 0
        ? "No open jobs. Every job the Service Centre exported is delivered, returned or closed by claim."
        : "No open jobs match these filters.";

    protected override string Summarise(int count) =>
        $"{count:N0} open job{(count == 1 ? "" : "s")} in {Groups.Count:N0} stage group{(Groups.Count == 1 ? "" : "s")} · stage order, longest in stage first.";

    protected override string ExportName => "Service pending board";

    protected override (DateOnly From, DateOnly To) ExportPeriod
    {
        get
        {
            var asAt = AsAt ?? DateOnly.FromDateTime(DateTime.Today);
            return (asAt, asAt);
        }
    }
}
