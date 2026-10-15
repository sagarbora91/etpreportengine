extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Etp.Reporting.Reporting;
using Microsoft.Win32;
using ServiceCentre = EtpApplication::Etp.Reporting.Application.Stores.ServiceCentre;
using ServiceCentreScope = EtpApplication::Etp.Reporting.Application.Service.ServiceCentreScope;
using ServiceFreshnessChip = EtpApplication::Etp.Reporting.Application.Service.ServiceFreshnessChip;
using Etp.Reporting.Desktop.Modules.Reports;
using ServiceRefresh = EtpApplication::Etp.Reporting.Application.Service.ServiceRefresh;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>Writes one table to an Excel workbook. Production uses the report exporter (IReportExportCoordinator).</summary>
public delegate Task ServiceExcelExport(string path, ExcelReportMetadata metadata, ExcelReportData data);

/// <summary>Writes several tables to one Excel workbook, one sheet each (the Pending board: one sheet per stage).</summary>
public delegate Task ServiceExcelPackExport(string path, ReportPackDocument document);

/// <summary>One column of a Service grid: its header, its Excel number format and its display format.</summary>
/// <remarks>
/// A column whose Excel format starts with "#" holds numbers and is right-aligned (SVC-33). <paramref name="Visible"/> false keeps the
/// column in the export and the rows but leaves it out of the grid by default (the Pending board's 10 default columns, SVC-16).
/// </remarks>
public sealed record ServiceColumn(string Header, string NumberFormat = "General", string? DisplayFormat = null, double Width = 120, bool Visible = true, bool? RightAligned = null, string? TextBrush = null, bool Strong = false, string? NonZeroBrush = null, string? ZeroBrush = null)
{
    public bool IsNumber => RightAligned ?? NumberFormat.StartsWith('#');
}

/// <summary>One grid row. The cells are in column order and are what the grid shows and the export writes.</summary>
public sealed record ServiceGridRow(object Source, IReadOnlyList<object?> Cells);

/// <summary>
/// The shared frame of the read-only Service screens (Service interim, decision 15; Service rail, 1.10.0):
/// a title, the Service Centre's catalogue name (1.12 A4), the "Service data as at" line with the freshness strip under it (one chip per
/// family group, design 3.7), a filter bar, an optional summary panel (cards), one grid and Export.
/// Every activation re-queries; a slower earlier load never overwrites a newer one (revision counter).
/// The shell's store selection is ignored. 1.12.1 P1: with two or more Service Centres a "Service Centre" picker beside the
/// title (All Service Centres, then each centre) scopes the query to one centre; the choice is shared by every Service screen
/// (<see cref="ServiceCentreServices.SelectedStoreCode"/>), and a line under the title says what the choice does not cover.
/// </summary>
public abstract class ServiceScreenView : UserControl
{
    /// <summary>The label before the catalogue is read, and when it holds no Service Centre or cannot be read (1.12 A4).</summary>
    public const string DefaultCentreLabel = "Service Centre";
    public const string NoDataText = "No Service data imported yet";

    private readonly Func<ServiceReportQuery> query;
    private readonly ServiceExcelExport export;
    private readonly string title;
    private readonly string diagnosticsSource;
    private readonly string diagnosticsEvent;
    /// <summary>The least height of the content (frame, one grid row band and the footer) before the page scrolls as one (125 % and 150 % Windows scaling).</summary>
    public const double MinGridHeight = 260;

    private readonly TextBlock asAt = ServiceUi.Text("", "BodyText");
    private readonly TextBlock centreCaption = ServiceUi.Caption(DefaultCentreLabel + ReadOnlySuffix);
    private const string ReadOnlySuffix = " · read only";
    /// <summary>The caption beside the picker (the picker names the centre itself).</summary>
    private const string ReadOnlyText = "read only";
    private readonly ComboBox centrePicker = new() { MinWidth = 220, Visibility = Visibility.Collapsed };
    private readonly TextBlock scopeNote = ServiceUi.Caption("");
    private bool fillingCentres;
    private readonly WrapPanel freshnessSummary = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel freshnessStrip = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    private readonly Button allGroupsLink = ServiceUi.MakeButton("All groups", "QuietButton");
    private readonly TextBlock status = ServiceUi.Caption("");
    private readonly Button exportButton = ServiceUi.MakeButton("Export to Excel");
    private readonly Grid root = new();
    private readonly ScrollViewer scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel heading = new();
    private IReadOnlyList<ServiceColumn> columns = [];
    private int revision;

    protected ServiceScreenView(string title, string intro, string diagnosticsSource, string diagnosticsEvent,
        Func<ServiceReportQuery> query, ServiceExcelExport export)
    {
        this.title = title;
        this.diagnosticsSource = diagnosticsSource;
        this.diagnosticsEvent = diagnosticsEvent;
        this.query = query ?? throw new ArgumentNullException(nameof(query));
        this.export = export ?? throw new ArgumentNullException(nameof(export));
        exportButton.IsEnabled = false;
        var space4 = ServiceUi.Space("Space4", 4);
        var space8 = ServiceUi.Space("Space8", 8);

        root.Margin = ServiceUi.Gap(0, 0, 0, 0);
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star), MinHeight = MinGridHeight });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });

        // Band 1: title left, actions right (SVC-02, SVC-04). The paragraph that used to follow is behind the (i).
        var titleRow = new DockPanel { LastChildFill = false, Margin = ServiceUi.Gap(0, space4, 0, space8) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(actions, Dock.Right);
        var refresh = ServiceUi.MakeButton("Refresh", "PrimaryButton");
        refresh.Margin = ServiceUi.Gap(0, 0, space8, 0);
        refresh.Click += async (_, _) => await ActivateAsync();
        exportButton.Click += async (_, _) => await ExportWithDialogAsync();
        actions.Children.Add(refresh);
        actions.Children.Add(exportButton);
        var titleBlock = ServiceUi.SectionTitle(title);
        titleBlock.VerticalAlignment = VerticalAlignment.Center;
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        var titleLeft = new StackPanel { Orientation = Orientation.Horizontal };
        titleLeft.Children.Add(titleBlock);
        titleLeft.Children.Add(ServiceUi.InfoMark(intro));
        // 1.12.1 P1: the Service Centre picker (shown only with two or more centres) sits before the read-only caption.
        AutomationProperties.SetName(centrePicker, "Service Centre");
        centrePicker.VerticalAlignment = VerticalAlignment.Center;
        centrePicker.Margin = ServiceUi.Gap(ServiceUi.S12, 0, 0, 0);
        centrePicker.SelectionChanged += async (_, _) => await OnCentreChosenAsync();
        titleLeft.Children.Add(centrePicker);
        var centre = centreCaption;
        centre.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        centre.VerticalAlignment = VerticalAlignment.Center; centre.Margin = ServiceUi.Gap(ServiceUi.S12, 0, 0, 0);
        titleLeft.Children.Add(centre);
        DockPanel.SetDock(titleLeft, Dock.Left);
        titleRow.Children.Add(actions);
        titleRow.Children.Add(titleLeft);
        heading.Children.Add(titleRow);
        // 1.12.1 P1: while one centre is chosen, what the choice does not cover (shown under the title).
        scopeNote.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        scopeNote.TextWrapping = TextWrapping.Wrap;
        scopeNote.Margin = ServiceUi.Gap(0, 0, 0, space4);
        scopeNote.Visibility = Visibility.Collapsed;
        heading.Children.Add(scopeNote);

        // Band 2: one freshness line (SVC-01): the as-at text, a summary chip, only the chips that need a look, and a link to all groups.
        AutomationProperties.SetName(freshnessSummary, "Service data freshness summary");
        asAt.Margin = ServiceUi.Gap(0, 0, space8, 0);
        asAt.VerticalAlignment = VerticalAlignment.Center;
        allGroupsLink.Margin = ServiceUi.Gap(space4, 0, 0, 0);
        allGroupsLink.Visibility = Visibility.Collapsed;
        allGroupsLink.Click += (_, _) => ToggleAllGroups();
        AutomationProperties.SetName(freshnessStrip, "Service data freshness");
        freshnessSummary.Margin = ServiceUi.Gap(0, 0, 0, space4);
        freshnessStrip.Margin = ServiceUi.Gap(0, 0, 0, space4);
        heading.Children.Add(freshnessSummary);
        heading.Children.Add(freshnessStrip);

        FilterBar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = ServiceUi.Gap(0, space4, 0, space4) };
        FilterBar.Loaded += (_, _) => ServiceUi.ApplyTargetHeight(FilterBar);
        heading.Children.Add(FilterBar);
        Summary = new StackPanel();
        heading.Children.Add(Summary);
        HeaderPanel = new StackPanel();
        heading.Children.Add(HeaderPanel);
        NumbersPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        heading.Children.Add(NumbersPanel);
        // The screen's status line is the grid's caption: once, directly above the grid (SVC-04, ST6).
        status.Margin = ServiceUi.Gap(0, space4, 0, space4);
        heading.Children.Add(status);
        root.Children.Add(heading);

        TablePresentation.Configure(Table);
        AutomationProperties.SetName(Table, title);
        Grid.SetRow(Table, 1);
        root.Children.Add(Table);

        Footer = new StackPanel();
        Grid.SetRow(Footer, 2);
        root.Children.Add(Footer);

        // Below the minimum height (Windows scaling 125 % / 150 %) the whole page scrolls instead of squeezing the grid away.
        scroller.Content = root;
        scroller.SizeChanged += (_, _) => FitHeight();
        heading.SizeChanged += (_, _) => FitHeight();
        Footer.SizeChanged += (_, _) => FitHeight();
        Content = scroller;
        AutomationProperties.SetName(this, title);
    }

    /// <summary>Puts the filter bar under the screen's numbers instead of above them (the Pending board: numbers, stages, then filters).</summary>
    protected void PlaceFiltersBelowSummary()
    {
        heading.Children.Remove(FilterBar);
        heading.Children.Insert(heading.Children.IndexOf(Summary) + 1, FilterBar);
    }

    /// <summary>Hides the frame's grid and gives its row back (Service today shows its numbers as cards, not twice).</summary>
    protected void HideGrid()
    {
        Table.Visibility = Visibility.Collapsed;
        root.RowDefinitions[1].MinHeight = 0;
    }

    private void FitHeight()
    {
        var needed = heading.ActualHeight + Footer.ActualHeight + (Table.Visibility == Visibility.Collapsed ? 0 : MinGridHeight);
        var height = Math.Max(scroller.ActualHeight, needed);
        if (height > 0 && (double.IsNaN(root.Height) || Math.Abs(root.Height - height) > 0.5)) root.Height = height;
    }

    private void ToggleAllGroups()
    {
        var show = freshnessStrip.Visibility != Visibility.Visible;
        freshnessStrip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        allGroupsLink.Content = show ? "Hide groups" : $"All {Freshness.Count} groups";
    }

    /// <summary>True while the full per-group strip is open (the "all groups" link).</summary>
    public bool AllGroupsOpen => freshnessStrip.Visibility == Visibility.Visible;

    protected WrapPanel FilterBar { get; }
    /// <summary>Between the status line and the grid: the job history header card (1.10.0).</summary>
    protected StackPanel HeaderPanel { get; }
    /// <summary>Sits between the filter bar and the actions: a screen's numbers (Service Today cards, Pending board KPI cards).</summary>
    protected StackPanel Summary { get; }
    /// <summary>The numbers strip above the grid (KpiCard per number, design 6.4); empty on the interim screens.</summary>
    protected WrapPanel NumbersPanel { get; }
    protected StackPanel Footer { get; }
    /// <summary>The refresh log the last activation read (newest first); empty before the first load or without data.</summary>
    protected IReadOnlyList<ServiceRefresh> Refreshes { get; private set; } = [];
    /// <summary>The freshness chips of the last activation, in strip order (design 3.7).</summary>
    public IReadOnlyList<ServiceFreshnessChip> Freshness { get; private set; } = [];
    /// <summary>The strip's text when the export dates could not be read, with the diagnostics reference (R-UI-18); empty otherwise.</summary>
    public string FreshnessNote { get; private set; } = "";
    /// <summary>The date the freshness colours are measured from (passed to LoadFreshnessAsync as asOf); tests pin it.</summary>
    public Func<DateOnly> FreshnessToday { get; set; } = () => DateOnly.FromDateTime(DateTime.Today);
    public DataGrid Table { get; } = new() { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };
    public IReadOnlyList<ServiceGridRow> Rows { get; private set; } = [];
    public IReadOnlyList<string> ColumnHeaders => columns.Select(column => column.Header).ToArray();
    public string AsAtText => asAt.Text;
    /// <summary>The screen's own status line. Each change is raised as <see cref="StatusChanged"/> so the shell can show it in the application status line instead of the previous workspace's text (SD-10).</summary>
    public string StatusText { get => status.Text; private set { status.Text = value; StatusChanged?.Invoke(this, value); } }
    public event EventHandler<string>? StatusChanged;
    public bool IsLoading { get; private set; }
    public bool HasData { get; private set; }
    protected string Title => title;

    private sealed class ZeroStateConverter : IValueConverter
    {
        public static readonly ZeroStateConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is null ? "none" : System.Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture) == 0m ? "zero" : "nonzero";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>"—" is "not applicable" in every Service grid; an empty cell is never used (module rule 7).</summary>
    public const string NotApplicable = "—";

    protected void SetColumns(IReadOnlyList<ServiceColumn> value)
    {
        columns = value;
        Table.Columns.Clear();
        for (var index = 0; index < value.Count; index++)
        {
            var column = new DataGridTextColumn
            {
                Header = value[index].Header,
                Binding = new Binding($"Cells[{index}]") { StringFormat = value[index].DisplayFormat, ConverterCulture = ServiceDisplay.Culture, TargetNullValue = NotApplicable },
                Width = value[index].Width,
                Visibility = value[index].Visible ? Visibility.Visible : Visibility.Collapsed
            };
            if (value[index].IsNumber || value[index].TextBrush is not null || value[index].Strong || value[index].NonZeroBrush is not null)
            {
                var right = new Style(typeof(TextBlock));
                if (value[index].IsNumber) right.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
                if (value[index].TextBrush is { } brush) right.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension(brush)));
                if (value[index].Strong) right.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
                // A money check's difference: non-zero is the loudest thing on the screen, matched is quietly fine (SVC-28).
                foreach (var (state, toneBrush) in new[] { ("nonzero", value[index].NonZeroBrush), ("zero", value[index].ZeroBrush) })
                {
                    if (toneBrush is null) continue;
                    var trigger = new DataTrigger { Binding = new Binding($"Cells[{index}]") { Converter = ZeroStateConverter.Instance }, Value = state };
                    trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension(toneBrush)));
                    if (state == "nonzero") trigger.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
                    right.Triggers.Add(trigger);
                }
                column.ElementStyle = right;
            }
            if (value[index].IsNumber)
            {
                var header = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)) { BasedOn = Table.TryFindResource(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)) as Style };
                header.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Right));
                column.HeaderStyle = header;
            }
            Table.Columns.Add(column);
        }
    }

    /// <summary>
    /// Replaces the shown rows without re-querying (the Pending board re-filters its loaded rows, R-UI-19): the grid, the
    /// Export button and the status line follow the new rows. Does nothing while a load is running; that load applies the
    /// screen's current filters itself.
    /// </summary>
    protected void ShowRows(IReadOnlyList<ServiceGridRow> rows)
    {
        if (IsLoading) return;
        Rows = rows; Table.ItemsSource = rows; exportButton.IsEnabled = rows.Count > 0;
        StatusText = rows.Count == 0 ? EmptyRowsText : Summarise(rows.Count);
    }

    /// <summary>Re-reads the refresh log and then this screen's rows.</summary>
    public async Task ActivateAsync()
    {
        var current = ++revision;
        IsLoading = true;
        StatusText = "Loading Service data…";
        Rows = []; Table.ItemsSource = null; exportButton.IsEnabled = false;
        try
        {
            // 1.12 A4: the name beside the title comes from the store catalogue (no store code in a title).
            var centres = await ServiceCentreServices.LoadOrEmptyAsync();
            if (current != revision) return;
            var source = ScopeToChosenCentre(query(), centres);
            var refreshes = await source.LoadRefreshesAsync();
            if (current != revision) return;
            HasData = refreshes.Count > 0;
            Refreshes = refreshes;
            asAt.Text = DescribeRefreshes(refreshes);
            // R-UI-15: the strip is shown only for the current activation, and a freshness failure leaves it empty
            // (recorded) instead of failing the whole screen.
            IReadOnlyList<ServiceFreshnessChip> chips = [];
            var freshnessNote = "";
            if (HasData)
            {
                try { chips = await source.LoadFreshnessAsync(FreshnessToday()); }
                catch (Exception freshnessFailure)
                {
                    freshnessNote = DesktopDiagnostics.WithReference("The export dates could not be read.",
                        DesktopDiagnostics.Record(freshnessFailure, diagnosticsSource, diagnosticsEvent + "_FRESHNESS"));
                }
            }
            if (current != revision) return;
            ShowFreshness(chips, freshnessNote);
            if (!HasData && !LoadsWithoutReadings) { StatusText = NoDataText + ". Import the Service Centre files on Import → Import folder."; ClearExtras(); return; }
            var request = PrepareLoad();
            if (request is not null) { StatusText = request; ClearExtras(); return; }
            var loaded = await LoadRowsAsync(source);
            if (current != revision) return;
            Rows = loaded; Table.ItemsSource = loaded; exportButton.IsEnabled = loaded.Count > 0;
            StatusText = loaded.Count == 0 ? EmptyRowsText : Summarise(loaded.Count);
            await LoadExtrasAsync(source, () => current == revision);
        }
        catch (Exception exception)
        {
            if (current != revision) return;
            StatusText = FailureText($"{title} could not be loaded. ", exception, diagnosticsSource, diagnosticsEvent);
        }
        finally { if (current == revision) IsLoading = false; }
    }

    /// <summary>
    /// 1.12.1 P1: fills the picker (two or more centres and a query that can be scoped) and returns the query scoped to the
    /// chosen centre, or the query itself for all centres. Sets the label beside the title and the line under it.
    /// </summary>
    private ServiceReportQuery ScopeToChosenCentre(ServiceReportQuery source, IReadOnlyList<ServiceCentre> centres)
    {
        var canScope = centres.Count >= 2 && ServiceCentreServices.Scope(source, centres[0].StoreCode) is not null;
        // 1.12.1 P8: the choice is one centre, "Not matched to any centre" (its own code) or all centres.
        var selection = canScope ? ServiceCentreChoices.Resolve(centres, ServiceCentreServices.SelectedStoreCode) : ServiceCentreSelection.All;
        var scoped = selection.StoreCode is null ? null : ServiceCentreServices.Scope(source, selection.StoreCode);
        if (scoped is null) selection = ServiceCentreSelection.All;
        FillCentrePicker(canScope ? ServiceCentreServices.Choices(centres) : [], selection.StoreCode);
        ChosenCentreCode = selection.StoreCode;
        ShowCentre(selection.StoreCode is null ? ServiceCentreServices.Label(centres) : selection.Name);
        ShowScopeNote(selection.StoreCode is null ? ""
            : selection.NotMatched ? ServiceCentreScope.NotMatchedScopeNote() : CentreScopeNote(CentreLabel));
        return scoped ?? source;
    }

    private void FillCentrePicker(IReadOnlyList<ServiceListChoice> choices, string? code)
    {
        fillingCentres = true;
        try
        {
            centrePicker.ItemsSource = choices;
            centrePicker.SelectedItem = choices.FirstOrDefault(choice => string.Equals(choice.Code, code, StringComparison.OrdinalIgnoreCase))
                ?? choices.FirstOrDefault();
            centrePicker.Visibility = choices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { fillingCentres = false; }
    }

    private async Task OnCentreChosenAsync()
    {
        if (fillingCentres || centrePicker.SelectedItem is not ServiceListChoice choice) return;
        if (string.Equals(choice.Code, ChosenCentreCode, StringComparison.OrdinalIgnoreCase)) return;
        ServiceCentreServices.SelectedStoreCode = choice.Code;
        await ActivateAsync();
    }

    private void ShowScopeNote(string text)
    {
        scopeNote.Text = text;
        scopeNote.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The store code of the Service Centre this screen shows, or null for all of them (1.12.1 P1).</summary>
    public string? ChosenCentreCode { get; private set; }

    /// <summary>True while the Service Centre picker is offered (two or more centres and a query that can be scoped).</summary>
    public bool CentrePickerVisible => centrePicker.Visibility == Visibility.Visible;

    /// <summary>The picker's choices in order (empty while it is hidden).</summary>
    public IReadOnlyList<ServiceListChoice> CentreChoices =>
        CentrePickerVisible && centrePicker.ItemsSource is IReadOnlyList<ServiceListChoice> choices ? choices : [];

    /// <summary>Chooses a Service Centre as the picker does (null = all) and reloads; a test may call it.</summary>
    public Task ChooseCentreAsync(string? storeCode)
    {
        ServiceCentreServices.SelectedStoreCode = string.IsNullOrWhiteSpace(storeCode) ? null : storeCode.Trim();
        return ActivateAsync();
    }

    /// <summary>The line under the title while one centre is chosen; empty for all centres.</summary>
    public string CentreScopeText => scopeNote.Text;

    /// <summary>What the centre choice covers on this screen (the Money check and Job history say their own).</summary>
    protected virtual string CentreScopeNote(string centreName) => ServiceCentreServices.ScopeNote(centreName);

    /// <summary>The Service Centre this screen names beside its title and in its exports' scope (1.12 A4).</summary>
    public string CentreLabel { get; private set; } = DefaultCentreLabel;

    /// <summary>Sets the label beside the title (a test may call it; the activation sets it from the catalogue).</summary>
    public void ShowCentre(string label)
    {
        CentreLabel = string.IsNullOrWhiteSpace(label) ? DefaultCentreLabel : label.Trim();
        // 1.12.1 P1: the picker names the centre itself, so the caption beside it only says "read only".
        centreCaption.Text = CentrePickerVisible ? ReadOnlyText : CentreLabel + ReadOnlySuffix;
    }

    /// <summary>The export rule version of every 1.10.0 Service export (R-UI-20).</summary>
    public const string ExportRuleVersion = "service-ui-1";

    /// <summary>
    /// True when a mouse event came from a grid row, not from a column header or the scroll bar (R-UI-08): a double-click
    /// on a header sorts and must not open the previously selected job.
    /// </summary>
    public static bool IsOnRow(object? source)
    {
        var node = source as DependencyObject;
        while (node is not null)
        {
            if (node is DataGridRow) return true;
            if (node is DataGrid or System.Windows.Controls.Primitives.DataGridColumnHeader or System.Windows.Controls.Primitives.ScrollBar) return false;
            node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    /// <summary>The "Service data as at" line. The latest reading of any Service family sets the date.</summary>
    public static string DescribeRefreshes(IReadOnlyList<ServiceRefresh> refreshes)
    {
        if (refreshes.Count == 0) return NoDataText;
        var latest = refreshes.Max(refresh => refresh.SnapshotDate);
        var imported = refreshes.Max(refresh => AsUtc(refresh.ImportedAtUtc)).ToLocalTime();
        return ServiceDisplay.Text($"Service data as at {latest:dd MMM yyyy} (refreshed {imported:dd MMM yyyy})");
    }

    private void ShowFreshness(IReadOnlyList<ServiceFreshnessChip> chips, string note = "")
    {
        Freshness = chips;
        FreshnessNote = note;
        freshnessStrip.Children.Clear();
        freshnessSummary.Children.Clear();
        freshnessStrip.Visibility = Visibility.Collapsed;
        foreach (var chip in Freshness) freshnessStrip.Children.Add(ServiceFreshnessStrip.CreateChip(chip));
        var gap = ServiceUi.Space("Space8", 8);
        freshnessSummary.Children.Add(asAt);
        if (Freshness.Count > 0)
        {
            var summary = ServiceFreshnessStrip.CreateSummaryChip(Freshness);
            summary.Margin = ServiceUi.Gap(0, 0, gap, 0);
            freshnessSummary.Children.Add(summary);
            // Only the chips that need a look stay inline; a never-exported family counts as one (SVC-01, SVC-29).
            foreach (var chip in Freshness.Where(candidate => ServiceFreshnessStrip.NeedsLook(candidate)))
            {
                var inline = ServiceFreshnessStrip.CreateChip(chip, includeState: true);
                inline.Margin = ServiceUi.Gap(0, 0, gap, 0);
                freshnessSummary.Children.Add(inline);
            }
            allGroupsLink.Content = $"All {Freshness.Count} groups";
            allGroupsLink.Visibility = Visibility.Visible;
            freshnessSummary.Children.Add(allGroupsLink);
        }
        if (note.Length > 0)
        {
            var text = ServiceUi.Caption(note);
            text.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
            freshnessSummary.Children.Add(text);
        }
    }

    /// <summary>
    /// R-UI-18 (1.10.0, with the 1.9.9 diagnostics references): records the failure and returns
    /// "{prefix}{plain reason} Ref: XXXXXXXXXXXX", so a screenshot or a phone call can be matched to the log entry.
    /// </summary>
    internal static string FailureText(string prefix, Exception exception, string source, string eventId) =>
        DesktopDiagnostics.WithReference(prefix + DesktopFriendlyError.Describe(exception), DesktopDiagnostics.Record(exception, source, eventId));

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    /// <summary>Returns a message instead of loading when the screen still needs input (job history).</summary>
    protected virtual string? PrepareLoad() => null;
    /// <summary>True for a screen that has something to show before the first Service import (the money check's manual side, SD-08).</summary>
    protected virtual bool LoadsWithoutReadings => false;
    protected abstract Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source);
    protected virtual Task LoadExtrasAsync(ServiceReportQuery source, Func<bool> isCurrent) => Task.CompletedTask;
    protected virtual void ClearExtras() { }
    protected virtual string EmptyRowsText => "No rows for this choice.";
    protected virtual string Summarise(int count) => ServiceDisplay.Text($"{count:N0} rows.");
    protected virtual string ExportName => title;
    /// <summary>The export's period (SD-09: the screen's chosen or shown range, not today).</summary>
    protected virtual (DateOnly From, DateOnly To) ExportPeriod => (DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today));
    /// <summary>The export's message line: the as-at line, plus whatever the screen shows above its grid.</summary>
    protected virtual string ExportMessage => asAt.Text;

    /// <summary>The visible columns and rows, as the export writes them.</summary>
    public ExcelReportData BuildExportData() =>
        new(columns.Select(column => new ExcelReportColumn(column.Header, column.NumberFormat)).ToArray(),
            Rows.Select(row => row.Cells).ToArray());

    public virtual async Task ExportToPathAsync(string path)
    {
        var (from, to) = ExportPeriod;
        var metadata = new ExcelReportMetadata(ExportName, from, to, "Read only", ExportRuleVersion,
            ExportMessage, DateTimeOffset.UtcNow, CentreLabel);
        await export(path, metadata, BuildExportData());
    }

    private async Task ExportWithDialogAsync()
    {
        if (Rows.Count == 0) return;
        var dialog = ExportSaveDialog.Create(ExportSaveDialog.ExcelFilter, $"{ExportName.Replace(' ', '_')}_{DateTime.Today:yyyyMMdd}.xlsx");
        if (!ExportSaveDialog.ShowDialog(dialog, this)) return;
        try
        {
            await ExportToPathAsync(dialog.FileName);
            StatusText = ServiceDisplay.Text($"Exported {Rows.Count:N0} rows to {System.IO.Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception exception)
        {
            StatusText = FailureText("The export could not be saved. ", exception, diagnosticsSource, "SERVICE_EXPORT_FAILED");
        }
    }

    protected static string Joined(IEnumerable<string>? values) =>
        values is null ? "" : string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
}

