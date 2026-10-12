extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
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
    private readonly ComboBox ageBandFilter = new() { MinWidth = 110, Margin = ServiceUi.Gap(0, 0, ServiceUi.S8, 0) };
    private readonly CheckBox overdueOnlyFilter = new() { Content = "Overdue only", VerticalAlignment = VerticalAlignment.Center, Margin = ServiceUi.Gap(0, 0, ServiceUi.S8, 0) };
    private readonly ComboBox brandFilter = new() { MinWidth = 130, Margin = ServiceUi.Gap(0, 0, ServiceUi.S8, 0) };
    private readonly ComboBox guaranteeFilter = new() { MinWidth = 110, Margin = ServiceUi.Gap(0, 0, ServiceUi.S8, 0) };
    private readonly ComboBox joTypeFilter = new() { MinWidth = 180, Margin = ServiceUi.Gap(0, 0, ServiceUi.S8, 0) };
    private readonly Button openJobButton = new() { Content = OpenJobText, Margin = ServiceUi.Gap(0, 0, ServiceUi.S8, 0), IsEnabled = false };
    private readonly WrapPanel cards = new() { Orientation = Orientation.Horizontal, Margin = ServiceUi.Gap(0, ServiceUi.S4, 0, ServiceUi.S4) };
    private readonly Dictionary<string, TextBlock> stageCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string>? openJob;
    private readonly ServiceExcelPackExport packExport;
    private string? requestedBrand;
    private string? requestedGuarantee;
    private bool populating;

    public ServicePendingBoardView(Func<IServiceReportQuery> query, ServiceExcelExport export,
        ServiceExcelPackExport? packExport = null, Action<string>? openJob = null)
        : base("Service pending board",
            "Every open job by stage, longest in stage first. Overdue means the promised date (EDD) has passed or, without an EDD, the job has been in its stage longer than the stage limit (bench 7, indent 15, SRN out 30, in transit 15, ready 7 days). Delivered and returned jobs and DC/RA jobs with a claim are not on the board. Tick a stage to show only that stage (none ticked shows all); Export to Excel writes one sheet per stage shown. Double-click a row or press Enter to open the job history.",
            "Service.PendingBoard", "SERVICE_PENDING_BOARD_LOAD_FAILED", query, export)
    {
        this.openJob = openJob;
        this.packExport = packExport ?? ((path, document) => new Reports.ReportExportCoordinator().ExportPackExcelAsync(path, document));

        Summary.Children.Add(cards);

        // Stage filter = toggle chips with counts, in one row (SVC-14): ticking shows only that stage, none ticked shows all.
        var stages = new WrapPanel { Orientation = Orientation.Horizontal, Margin = ServiceUi.Gap(0, 0, 0, ServiceUi.S4) };
        stages.Children.Add(ServiceUi.Text("Stages", "FieldLabelText", "PrimaryText"));
        ((TextBlock)stages.Children[0]).VerticalAlignment = VerticalAlignment.Center;
        ((TextBlock)stages.Children[0]).Margin = ServiceUi.Gap(0, 0, ServiceUi.S8, 0);
        foreach (var stage in ServicePendingBoardRules.BoardStages)
        {
            var label = ServiceStages.Label(stage);
            var count = ServiceUi.Text("0", "CaptionText", "SecondaryText");
            count.FontWeight = FontWeights.SemiBold; count.VerticalAlignment = VerticalAlignment.Center; count.Margin = ServiceUi.Gap(ServiceUi.S4, 0, 0, 0);
            stageCounts[stage] = count;
            var face = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = ServiceUi.StageDot(stage);
            dot.Margin = ServiceUi.Gap(0, 0, ServiceUi.S4, 0);
            var name = ServiceUi.Text(label, "BodyText");
            name.VerticalAlignment = VerticalAlignment.Center;
            face.Children.Add(dot); face.Children.Add(name); face.Children.Add(count);
            var box = new CheckBox { Content = face, VerticalContentAlignment = VerticalAlignment.Center, Margin = ServiceUi.Gap(0, 0, ServiceUi.S8, 0) };
            box.SetResourceReference(FrameworkElement.StyleProperty, "FilterChip");
            AutomationProperties.SetName(box, "Stage " + label);
            box.Checked += async (_, _) => await OnFilterChangedAsync();
            box.Unchecked += async (_, _) => await OnFilterChangedAsync();
            stageBoxes[stage] = box;
            stages.Children.Add(box);
        }
        Summary.Children.Add(stages);
        PlaceFiltersBelowSummary();

        AddFilter("Age since booking", ageBandFilter, "Age band (days since booking)", [new(null, AllAgesLabel), .. ServiceAgeing.Bands.Select(band => new ServiceListChoice(band, band + " days")),
            new(ServicePendingBoardRules.Over15Days, "Over 15 days")]);
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

        // The 10 default columns fit 1366 px (SVC-16); the rest stay in the rows and the Excel export, hidden by default.
        // "Days in stage" is the primary age (bold), "Days since booking" the secondary one; overdue prints "+N d" in Critical (SVC-13).
        SetColumns(
        [
            new("Job number", Width: 150), new("Days in stage", "#,##0", Width: 100, Strong: true),
            new("Overdue", "#,##0", OverdueFormat, 90, TextBrush: "Critical", Strong: true), new("Promised (EDD)", DisplayFormat: "dd MMM yyyy", Width: 130),
            new("Booked on", DisplayFormat: "dd MMM yyyy"), new("Days since booking", "#,##0", Width: 110, TextBrush: "SecondaryText"),
            new("Brand", Width: 120), new("Model", Width: 150), new("Spare required", Width: 200), new("Pending at", Width: 120),
            new("Stage", Width: 220, Visible: false), new("Age band", Width: 80, Visible: false), new("Product", Width: 120, Visible: false),
            new("Guarantee", Width: 100, Visible: false), new("Customer type", Width: 120, Visible: false), new("Job type", Width: 110, Visible: false),
            new("Last reading", DisplayFormat: "dd MMM yyyy", Visible: false)
        ]);
        Table.GroupStyle.Add(StageGroupStyle());
        Table.RowStyle = OverdueRowStyle(Table);
        Table.SelectionChanged += (_, _) => openJobButton.IsEnabled = openJob is not null && Table.SelectedItem is ServiceGridRow;
        Table.MouseDoubleClick += (_, args) => { if (IsOnRow(args.OriginalSource)) OpenSelected(); };
        Table.KeyDown += (_, args) => { if (args.Key == Key.Enter && OpenSelected()) args.Handled = true; };
    }

    /// <summary>"+12 d" when overdue; the third section prints the not-applicable dash for 0.</summary>
    public const string OverdueFormat = "+#,##0\" d\";-#,##0\" d\";\"—\"";

    /// <summary>Overdue by more than this many days tints the row <c>CriticalSoft</c>; any overdue tints it <c>WarningSoft</c> (module rule 4).</summary>
    public const int CriticalOverdueDays = 7;

    /// <summary>The tint token of a row: null when not overdue.</summary>
    public static string? RowTintKey(ServicePendingBoardRow row) =>
        row.OverdueBy is > CriticalOverdueDays ? "CriticalSoft" : row.OverdueBy is > 0 ? "WarningSoft" : null;

    private sealed class RowTintConverter(string key) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is ServicePendingBoardRow row && RowTintKey(row) == key;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }

    private static Style OverdueRowStyle(DataGrid grid)
    {
        var style = new Style(typeof(DataGridRow)) { BasedOn = grid.TryFindResource(typeof(DataGridRow)) as Style };
        foreach (var key in new[] { "WarningSoft", "CriticalSoft" })
        {
            var trigger = new DataTrigger { Binding = new Binding("Source") { Converter = new RowTintConverter(key) }, Value = true };
            trigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(key)));
            style.Triggers.Add(trigger);
        }
        // Declared after the tints so hover and selection still show on a tinted row.
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("AccentSoft")));
        style.Triggers.Add(hover);
        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("SelectedRow")));
        style.Triggers.Add(selected);
        return style;
    }

    /// <summary>The stage of a group header: its label back to the stage code.</summary>
    public static string StageOfLabel(string label) =>
        ServicePendingBoardRules.BoardStages.FirstOrDefault(stage => string.Equals(ServiceStages.Label(stage), label, StringComparison.Ordinal)) ?? label;

    /// <summary>"7 jobs · oldest 35 days in stage" for one group header (SVC-12).</summary>
    public static string DescribeGroup(string label, IReadOnlyList<ServicePendingBoardRow> rows)
    {
        var oldest = rows.Max(row => row.DaysInStage);
        var jobs = ServiceDisplay.Text($"{rows.Count:N0} job{(rows.Count == 1 ? "" : "s")}");
        return oldest is { } days ? ServiceDisplay.Text($"{jobs} · oldest {days:N0} days in stage") : jobs;
    }

    private sealed class GroupHeaderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is not CollectionViewGroup group) return "";
            var label = group.Name?.ToString() ?? "";
            var stage = StageOfLabel(label);
            return (parameter as string) switch
            {
                "number" => ServiceUi.StageNumber(stage),
                "label" => label,
                "brush" => ServiceUi.StageBrush(stage),
                _ => DescribeGroup(label, group.Items.Select(item => (item as ServiceGridRow)?.Source).OfType<ServicePendingBoardRow>().ToArray())
            };
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>The group header: a band in SurfaceSecondary with the numbered stage dot, the stage name SemiBold, then the count and oldest age.</summary>
    private static GroupStyle StageGroupStyle()
    {
        var converter = new GroupHeaderConverter();
        FrameworkElementFactory Text(string parameter, bool strong)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding { Converter = converter, ConverterParameter = parameter });
            text.SetResourceReference(FrameworkElement.StyleProperty, strong ? "CardTitleText" : "CaptionText");
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            text.SetValue(FrameworkElement.MarginProperty, ServiceUi.Gap(0, 0, ServiceUi.S8, 0));
            return text;
        }
        var number = new FrameworkElementFactory(typeof(TextBlock));
        number.SetBinding(TextBlock.TextProperty, new Binding { Converter = converter, ConverterParameter = "number" });
        number.SetResourceReference(FrameworkElement.StyleProperty, "CaptionText");
        number.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
        number.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        number.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        number.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var dot = new FrameworkElementFactory(typeof(Border));
        dot.SetBinding(Border.BackgroundProperty, new Binding { Converter = converter, ConverterParameter = "brush" });
        dot.SetValue(FrameworkElement.WidthProperty, 20.0);
        dot.SetValue(FrameworkElement.HeightProperty, 20.0);
        dot.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        dot.SetValue(FrameworkElement.MarginProperty, ServiceUi.Gap(0, 0, ServiceUi.S8, 0));
        dot.AppendChild(number);
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.AppendChild(dot);
        row.AppendChild(Text("label", true));
        row.AppendChild(Text("info", false));
        var band = new FrameworkElementFactory(typeof(Border));
        band.SetResourceReference(Border.BackgroundProperty, "SurfaceSecondary");
        band.SetValue(Border.PaddingProperty, ServiceUi.Gap(ServiceUi.S8, ServiceUi.S4, ServiceUi.S8, ServiceUi.S4));
        band.AppendChild(row);
        return new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = band } };
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
        ShowStageCounts();
        RefillChoices(brandFilter, AllBrandsLabel, Board.Select(row => row.Brand), requestedBrand);
        RefillChoices(guaranteeFilter, AllGuaranteesLabel, Board.Select(row => row.Guarantee), requestedGuarantee);
        return FilteredRows();
    }

    /// <summary>The loaded board through the current filters; sets <see cref="Groups"/>.</summary>
    private IReadOnlyList<ServiceGridRow> FilteredRows()
    {
        var visible = ServicePendingBoardRules.Apply(Board, Filter);
        Groups = ServicePendingBoardRules.Groups(visible);
        return visible.Select(ToRow).ToArray();
    }

    /// <summary>
    /// R-UI-19: re-applies the filters to the board already loaded, without a query (nothing the board asks the database
    /// depends on a filter). Keeps the selected job selected when it is still shown. False when there is no loaded board
    /// or a load is running; Refresh and the first load query.
    /// </summary>
    public bool ApplyFilters()
    {
        if (IsLoading || AsAt is null) return false;
        var selectedJob = (Table.SelectedItem as ServiceGridRow)?.Source is ServicePendingBoardRow selected ? selected.JobOrderNumber : null;
        ShowRows(FilteredRows());
        GroupByStage();
        if (selectedJob is not null)
            Table.SelectedItem = Rows.FirstOrDefault(row => ((ServicePendingBoardRow)row.Source).JobOrderNumber == selectedJob);
        return true;
    }

    protected override Task LoadExtrasAsync(IServiceReportQuery source, Func<bool> isCurrent)
    {
        // The frame has set ItemsSource; group the grid by stage so each group has a header in stage order.
        GroupByStage();
        return Task.CompletedTask;
    }

    private void GroupByStage()
    {
        Table.Items.GroupDescriptions.Clear();
        Table.Items.GroupDescriptions.Add(new PropertyGroupDescription("Source.StageLabel"));
    }

    protected override void ClearExtras()
    {
        Board = []; Groups = []; Numbers = null; AsAt = null; cards.Children.Clear(); openJobButton.IsEnabled = false;
    }

    /// <summary>The row's cells in column order: the 10 default columns first, then the hidden ones (also in the export).</summary>
    public static ServiceGridRow ToRow(ServicePendingBoardRow row) =>
        new(row,
        [
            row.JobOrderNumber, row.DaysInStage, row.OverdueBy, row.Edd, row.BookingDate, row.DaysSinceBooking,
            row.Brand, row.Model, row.SpareRequired, row.PendingAt,
            row.StageLabel, row.AgeBand, row.ProductCategory, row.Guarantee, row.CustomerType, row.JoType, row.LastReadingDate
        ]);

    /// <summary>The export: one sheet per stage shown, the board's columns, the as-at line as the control text.</summary>
    public ReportPackDocument BuildExportDocument(DateTimeOffset generatedUtc)
    {
        var columns = BuildExportData().Columns;
        var (from, to) = ExportPeriod;
        var tables = Groups.Select(group => new ReportPackTable(group.Label,
                ServiceDisplay.Text($"{group.Rows.Count:N0} job{(group.Rows.Count == 1 ? "" : "s")}"), AsAtText,
                new ExcelReportData(columns, group.Rows.Select(row => ToRow(row).Cells).ToArray())))
            .ToArray();
        return new ReportPackDocument(ExportName, from, to, "Read only", ExportRuleVersion, AsAtText, generatedUtc, tables);
    }

    public override Task ExportToPathAsync(string path) => packExport(path, BuildExportDocument(DateTimeOffset.UtcNow));

    private void ShowStageCounts()
    {
        foreach (var (stage, text) in stageCounts)
            text.Text = ServiceDisplay.Whole(Board.Count(row => string.Equals(row.Stage, stage, StringComparison.OrdinalIgnoreCase)));
    }

    private void ShowNumbers(ServicePendingNumbers numbers)
    {
        cards.Children.Clear();
        cards.Children.Add(Card("Open jobs", numbers.OpenJobs, "on the board", ServiceUi.StripeNone));
        cards.Children.Add(Card("Overdue", numbers.Overdue, "promised date passed or over the stage limit", numbers.Overdue > 0 ? ServiceUi.StripeActToday : ServiceUi.StripeFine));
        cards.Children.Add(Card("Over 30 days", numbers.Over30Days, "since booking", numbers.Over30Days > 0 ? ServiceUi.StripeActToday : ServiceUi.StripeFine));
        cards.Children.Add(Card("On the way back", numbers.InTransit, "sent back after repair", ServiceUi.StripeNone));
        cards.Children.Add(Card("Waiting for parts", numbers.PartsAwaited, "indent raised", ServiceUi.StripeNone));
    }

    private static KpiCard Card(string label, int value, string secondary, string accent) =>
        new(label, ServiceDisplay.Whole(value), secondary, accent) { MinWidth = 170 };

    private void AddFilter(string label, ComboBox combo, string automationName, IReadOnlyList<ServiceListChoice> choices)
    {
        FilterBar.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = ServiceUi.Gap(0, 0, ServiceUi.S8, 0) });
        AutomationProperties.SetName(combo, automationName);
        combo.ItemsSource = choices;
        combo.SelectedIndex = 0;
        combo.SelectionChanged += async (_, _) => await OnFilterChangedAsync();
        FilterBar.Children.Add(combo);
    }

    private async Task OnFilterChangedAsync()
    {
        if (populating) return;
        // A user's pick in the Brand / Guarantee lists is the requested value the next load keeps (R-UI-02): the
        // reload refills both lists from the board and re-selects the requested value.
        requestedBrand = SelectedBrand;
        requestedGuarantee = SelectedGuarantee;
        if (!IsLoaded) return;
        // R-UI-19: the board query does not depend on any filter, so a filter change re-filters the loaded rows.
        if (ApplyFilters()) return;
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
        ServiceDisplay.Text($"{count:N0} open job{(count == 1 ? "" : "s")} in {Groups.Count:N0} stage group{(Groups.Count == 1 ? "" : "s")} · stage order, longest in stage first.");

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
