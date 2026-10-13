extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;
using ServiceGitLine = EtpApplication::Etp.Reporting.Application.Service.ServiceGitLine;
using ServiceParts = EtpApplication::Etp.Reporting.Application.Service.ServiceParts;
using ServicePartsInvoice = EtpApplication::Etp.Reporting.Application.Service.ServicePartsInvoice;
using ServiceWaitingJob = EtpApplication::Etp.Reporting.Application.Service.ServiceWaitingJob;
using ServiceRefresh = EtpApplication::Etp.Reporting.Application.Service.ServiceRefresh;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>A month on the Parts screen's month filter; a null month is "All months".</summary>
public sealed record ServiceMonthChoice(DateOnly? Month, string Label)
{
    public override string ToString() => Label;
    public static ServiceMonthChoice All { get; } = new(null, "All months");
    public static ServiceMonthChoice For(DateOnly month) => new(new DateOnly(month.Year, month.Month, 1), ServiceDisplay.Month(month));
}

/// <summary>The five numbers of the Parts screen (design 3.6) as shown: the contract's counts plus the oldest open invoice's number.</summary>
public sealed record ServicePartsNumbers(
    int OpenInvoices,
    decimal OpenValue,
    int? OldestOpenDays,
    string? OldestOpenInvoice,
    int ReceivedThisMonth,
    int JobsWaitingForParts,
    int GitLinesLast30Days,
    DateOnly AsOf);

/// <summary>
/// Service parts and purchases (1.10.0, design 3.6): purchase invoices created (S007) against received (S008) with
/// their status and age, each invoice's lines, the jobs waiting for parts (S009), the goods in transit of the last
/// 30 days (S013) and the latest closing stock count and value (S006). No column links a pending job to a purchase
/// invoice (design 1.7), so the two panels sit side by side and are never joined.
/// </summary>
public sealed class ServicePartsView : ServiceScreenView
{
    /// <summary>The caption above the jobs-waiting grid (SVC-26); the full explanation is <see cref="NoLinkExplanation"/>, behind its tooltip.</summary>
    public const string NoLinkText = "Not linked to purchase invoices";
    public const string NoLinkExplanation = "No data link exists between a pending job and a purchase invoice: the Service exports carry none. The two lists are shown side by side and are not joined.";
    public const string WaitingTitle = "Jobs waiting for parts";
    public const string LinesTitle = "Invoice lines";
    public const string GitTitle = "Goods in transit, last 30 days";
    public const string NoLinesText = "Select an invoice to see its lines.";

    private readonly ServiceExcelExport export;
    private readonly Action<string>? openJob;
    private readonly CheckBox openOnly = new() { Content = "Open only", VerticalContentAlignment = VerticalAlignment.Center, Margin = ServiceUi.Gap(0, 0, ServiceUi.S12, 0) };
    private readonly ComboBox month = new() { MinWidth = 150, ItemsSource = new List<ServiceMonthChoice> { ServiceMonthChoice.All }, SelectedIndex = 0 };
    private readonly TextBox item = new() { MinWidth = 180, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock linesStatus = ServiceUi.Caption("");
    private readonly TextBlock waitingStatus = ServiceUi.Caption("");
    private readonly TextBlock gitStatus = ServiceUi.Caption("");
    private readonly TextBlock closingStock = new() { TextWrapping = TextWrapping.Wrap, Margin = ServiceUi.Gap(0, ServiceUi.S12, 0, ServiceUi.S4) };
    private readonly Button exportWaiting = new() { Content = "Export jobs waiting to Excel", Margin = ServiceUi.Gap(0, ServiceUi.S4, ServiceUi.S8, ServiceUi.S4), IsEnabled = false };
    private readonly Button openHistory = new() { Content = "Open job history", Margin = ServiceUi.Gap(0, ServiceUi.S4, ServiceUi.S8, ServiceUi.S4), IsEnabled = false };
    private DateOnly? chosenMonth;
    private bool settingMonths;
    private ServiceParts parts = EmptyParts;

    /// <summary>The contract's empty result (what a query without parts data returns).</summary>
    public static ServiceParts EmptyParts { get; } = new([], [], null, [], 0, 0m, null, 0, 0, 0, null);

    public ServicePartsView(Func<ServiceReportQuery> query, ServiceExcelExport export, Action<string>? openJob = null)
        : base("Service parts and purchases",
            "Purchase invoices created against received: open until a GRN or received date is exported. Days open count from the invoice date to the Service snapshot while open, and to the received date once closed.",
            "Service.Parts", "SERVICE_PARTS_LOAD_FAILED", query, export)
    {
        this.export = export;
        this.openJob = openJob;
        AutomationProperties.SetName(openOnly, "Open invoices only");
        AutomationProperties.SetName(month, "Invoice month");
        AutomationProperties.SetName(item, "Item");
        openOnly.Checked += async (_, _) => { if (IsLoaded) await ActivateAsync(); };
        openOnly.Unchecked += async (_, _) => { if (IsLoaded) await ActivateAsync(); };
        month.SelectionChanged += async (_, _) =>
        {
            if (settingMonths) return;
            chosenMonth = (month.SelectedItem as ServiceMonthChoice)?.Month;
            if (IsLoaded) await ActivateAsync();
        };
        item.KeyDown += async (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; await ActivateAsync(); } };
        FilterBar.Children.Add(openOnly);
        FilterBar.Children.Add(new TextBlock { Text = "Month", VerticalAlignment = VerticalAlignment.Center, Margin = ServiceUi.Gap(0, 0, ServiceUi.S8, 0) });
        FilterBar.Children.Add(month);
        FilterBar.Children.Add(new TextBlock { Text = "Item", VerticalAlignment = VerticalAlignment.Center, Margin = ServiceUi.Gap(ServiceUi.S12, 0, ServiceUi.S8, 0) });
        FilterBar.Children.Add(item);
        var show = ServiceUi.MakeButton("Show");
        show.Margin = ServiceUi.Gap(ServiceUi.S8, 0, 0, 0);
        show.Click += async (_, _) => await ActivateAsync();
        FilterBar.Children.Add(show);
        SetColumns(
        [
            new("Invoice", Width: 140), new("Invoice date", DisplayFormat: "dd MMM yyyy"), new("GRN", Width: 140), new("GRN date", DisplayFormat: "dd MMM yyyy"),
            new("Items", "#,##0", Width: 70), new("Shipped qty", "#,##0.##", "N0", 100), new("Received qty", "#,##0.##", "N0", 100),
            new("Net amount", "#,##0.00", "N2", 120), new("Status", Width: 80), new("Days open", "#,##0", Width: 90), new("From location", Width: 120)
        ]);
        Table.SelectionChanged += (_, _) => ShowLines((Table.SelectedItem as ServiceGridRow)?.Source as ServicePartsInvoice);

        Footer.Children.Add(ServiceUi.CardTitle(LinesTitle, ServiceUi.Gap(0, ServiceUi.S12, 0, 0)));
        Footer.Children.Add(linesStatus);
        Configure(Lines, LinesTitle, [("Item", null, 200), ("Shipped qty", "N0", 100), ("Received qty", "N0", 100), ("Net amount", "N2", 120), ("GRN", null, 140), ("GRN date", "dd MMM yyyy", 120)]);
        Footer.Children.Add(Lines);

        Footer.Children.Add(ServiceUi.CardTitle(WaitingTitle, ServiceUi.Gap(0, ServiceUi.S12, 0, 0)));
        var noLink = ServiceUi.Caption(NoLinkText);
        noLink.ToolTip = NoLinkExplanation;
        Footer.Children.Add(noLink);
        Footer.Children.Add(waitingStatus);
        Configure(Waiting, WaitingTitle, [("Job number", null, 150), ("Part required", null, 280), ("Indent date", "dd MMM yyyy", 120),
            ("Days waiting", "N0", 100), ("Brand", null, 120), ("Model", null, 150), ("Pending at", null, 110)]);
        Waiting.SelectionChanged += (_, _) => openHistory.IsEnabled = Waiting.SelectedItem is ServiceGridRow;
        Waiting.MouseDoubleClick += (_, args) => { if (IsOnRow(args.OriginalSource)) OpenSelectedJobHistory(); };
        Waiting.KeyDown += (_, args) => { if (args.Key == Key.Enter && Waiting.SelectedItem is not null) { args.Handled = true; OpenSelectedJobHistory(); } };
        Footer.Children.Add(Waiting);
        var waitingActions = new WrapPanel { Orientation = Orientation.Horizontal };
        openHistory.Click += (_, _) => OpenSelectedJobHistory();
        exportWaiting.Click += async (_, _) => await ExportWaitingWithDialogAsync();
        waitingActions.Children.Add(openHistory);
        waitingActions.Children.Add(exportWaiting);
        Footer.Children.Add(waitingActions);

        Configure(Git, GitTitle, [("Date", "dd MMM yyyy", 120), ("STM number", null, 140), ("Item", null, 160),
            ("Quantity shipped", "N0", 120), ("UCP", "N2", 110), ("From", null, 100), ("To", null, 100)]);
        // SVC-24: goods in transit and the closing stock line sit behind a collapsed section with the count in its heading.
        Footer.Children.Add(ServiceUi.CollapsedSection(ServiceUi.CardTitle(GitTitle), gitStatus, Git, closingStock));
    }

    public DataGrid Lines { get; } = new() { AutoGenerateColumns = false, IsReadOnly = true, MaxHeight = 180 };
    public IReadOnlyList<ServiceGridRow> LineRows { get; private set; } = [];
    public DataGrid Waiting { get; } = new() { AutoGenerateColumns = false, IsReadOnly = true, MaxHeight = 220, SelectionMode = DataGridSelectionMode.Single };
    public IReadOnlyList<ServiceGridRow> WaitingRows { get; private set; } = [];
    public DataGrid Git { get; } = new() { AutoGenerateColumns = false, IsReadOnly = true, MaxHeight = 180 };
    public IReadOnlyList<ServiceGridRow> GitRows { get; private set; } = [];
    public ServicePartsNumbers? NumbersShown { get; private set; }
    public string LinesStatusText => linesStatus.Text;
    public string WaitingStatusText => waitingStatus.Text;
    public string GitStatusText => gitStatus.Text;
    public string ClosingStockText => closingStock.Text;
    public IReadOnlyList<string> WaitingColumnHeaders => Waiting.Columns.Select(column => (string)column.Header).ToArray();

    public bool OpenOnly { get => openOnly.IsChecked == true; set => openOnly.IsChecked = value; }
    public string ItemFilter { get => item.Text; set => item.Text = value; }

    /// <summary>The first day of the chosen invoice month; null for all months.</summary>
    public DateOnly? SelectedMonth
    {
        get => chosenMonth;
        set
        {
            chosenMonth = value is { } chosen ? new DateOnly(chosen.Year, chosen.Month, 1) : null;
            SelectMonthItem();
        }
    }

    public IReadOnlyList<ServiceMonthChoice> MonthChoices => (IReadOnlyList<ServiceMonthChoice>)month.ItemsSource;

    protected override async Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source)
    {
        parts = await source.LoadPartsAsync() ?? EmptyParts;
        OfferMonths(parts.Invoices);
        var asOf = AsOf(parts, Refreshes, DateOnly.FromDateTime(DateTime.Today));
        var chosen = Filter(parts.Invoices, OpenOnly, chosenMonth, item.Text);
        ShowNumbers(Summarise(parts, asOf));
        ShowWaiting(parts.WaitingJobs);
        ShowGit(parts.Git, asOf);
        closingStock.Text = DescribeClosingStock(parts);
        ShowLines(null);
        return Sort(chosen).Select(invoice => new ServiceGridRow(invoice,
        [
            invoice.InvoiceNumber, invoice.InvoiceDate, invoice.GrnNumber, invoice.GrnDate, invoice.Items, invoice.ShippedQuantity,
            invoice.ReceivedQuantity, invoice.NetAmount, StatusLabel(invoice), invoice.DaysOpen, invoice.FromLocation
        ])).ToArray();
    }

    /// <summary>Open / Closed as design 3.6 words it (the view's own status is Open / Received).</summary>
    public static string StatusLabel(ServicePartsInvoice invoice) => invoice.IsOpen ? "Open" : "Closed";

    protected override void ClearExtras()
    {
        parts = EmptyParts;
        NumbersPanel.Children.Clear(); NumbersShown = null;
        LineRows = []; Lines.ItemsSource = null; linesStatus.Text = "";
        WaitingRows = []; Waiting.ItemsSource = null; waitingStatus.Text = ""; exportWaiting.IsEnabled = false; openHistory.IsEnabled = false;
        GitRows = []; Git.ItemsSource = null; gitStatus.Text = ""; closingStock.Text = "";
    }

    /// <summary>The date the panels count from: the contract's as-at, else the latest Service reading, else today (Q15).</summary>
    public static DateOnly AsOf(ServiceParts parts, IReadOnlyList<ServiceRefresh> refreshes, DateOnly today)
    {
        if (parts.AsAt is { } asAt) return asAt;
        return refreshes.Count > 0 ? refreshes.Max(refresh => refresh.SnapshotDate) : today;
    }

    /// <summary>Open only, invoice month, and an item text matched against the item of any line of the invoice.</summary>
    public static IReadOnlyList<ServicePartsInvoice> Filter(IReadOnlyList<ServicePartsInvoice> invoices, bool openOnly, DateOnly? month, string? itemText)
    {
        var text = (itemText ?? "").Trim();
        return invoices
            .Where(invoice => !openOnly || invoice.IsOpen)
            .Where(invoice => month is not { } chosen || invoice.InvoiceDate is { } date && date.Year == chosen.Year && date.Month == chosen.Month)
            .Where(invoice => text.Length == 0 || invoice.Lines.Any(line => (line.ItemId ?? "").Contains(text, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>Open first, then oldest invoice date first (no date last), then invoice number.</summary>
    public static IReadOnlyList<ServicePartsInvoice> Sort(IEnumerable<ServicePartsInvoice> invoices) =>
        invoices.OrderBy(invoice => invoice.IsOpen ? 0 : 1)
            .ThenBy(invoice => invoice.InvoiceDate.HasValue ? 0 : 1).ThenBy(invoice => invoice.InvoiceDate)
            .ThenBy(invoice => invoice.InvoiceNumber, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>The numbers of design 3.6: the contract's counts (the query computes them over the unfiltered data) plus the oldest open invoice's number.</summary>
    public static ServicePartsNumbers Summarise(ServiceParts parts, DateOnly asOf)
    {
        var oldest = parts.Invoices.Where(invoice => invoice.IsOpen && invoice.DaysOpen.HasValue).OrderByDescending(invoice => invoice.DaysOpen).FirstOrDefault();
        return new ServicePartsNumbers(parts.OpenInvoices, parts.OpenValue, parts.OldestOpenDays ?? oldest?.DaysOpen, oldest?.InvoiceNumber,
            parts.ReceivedThisMonth, parts.JobsWaiting, parts.GitLinesLast30Days, asOf);
    }

    /// <summary>S013 lines dated within the 30 days up to <paramref name="asOf"/>, newest first (a no-op when the query already limited them).</summary>
    public static IReadOnlyList<ServiceGitLine> RecentGit(IReadOnlyList<ServiceGitLine> git, DateOnly asOf)
    {
        // The same 30 days as the "GIT lines, last 30 days" card (ServicePartsRules.Build: date > asOf - 30), R-UI-12.
        var from = asOf.AddDays(-30);
        return git.Where(line => line.BusinessDate > from && line.BusinessDate <= asOf)
            .OrderByDescending(line => line.BusinessDate).ThenBy(line => line.StmNumber, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string DescribeClosingStock(ServiceParts parts) => parts.Stock is { } stock
        ? ServiceDisplay.Text($"Latest closing stock as at {stock.SnapshotDate:dd MMM yyyy}: {stock.Items:N0} items") +
          (stock.Quantity is { } quantity ? ServiceDisplay.Text($", quantity {quantity:N0}") : "") + (stock.Value is { } value ? ServiceDisplay.Text($", value {value:N2}") : "") + "."
        : "No closing stock reading imported yet.";

    /// <summary>Shows the lines of one invoice below the grid. Selecting a row does it; tests call it by number.</summary>
    public void SelectInvoice(string invoiceNumber)
    {
        var row = Rows.FirstOrDefault(candidate => string.Equals(((ServicePartsInvoice)candidate.Source).InvoiceNumber, invoiceNumber, StringComparison.OrdinalIgnoreCase));
        Table.SelectedItem = row;
        ShowLines(row?.Source as ServicePartsInvoice);
    }

    private void ShowLines(ServicePartsInvoice? invoice)
    {
        if (invoice is null) { LineRows = []; Lines.ItemsSource = null; linesStatus.Text = parts.Invoices.Count == 0 ? "" : NoLinesText; return; }
        LineRows = invoice.Lines.OrderBy(line => line.ItemId, StringComparer.OrdinalIgnoreCase)
            .Select(line => new ServiceGridRow(line, [line.ItemId, line.ShippedQuantity, line.ReceivedQuantity, line.NetAmount, line.GrnNumber, line.GrnDate])).ToArray();
        Lines.ItemsSource = LineRows;
        linesStatus.Text = LineRows.Count == 0
            ? $"Invoice {invoice.InvoiceNumber}: no lines in the export."
            : ServiceDisplay.Text($"Invoice {invoice.InvoiceNumber}: {LineRows.Count:N0} line{(LineRows.Count == 1 ? "" : "s")}.");
    }

    private void ShowNumbers(ServicePartsNumbers numbers)
    {
        NumbersShown = numbers;
        NumbersPanel.Children.Clear();
        NumbersPanel.Children.Add(new KpiCard("Open invoices", ServiceDisplay.Whole(numbers.OpenInvoices), ServiceDisplay.Text($"net {numbers.OpenValue:N2}"), numbers.OpenInvoices == 0 ? ServiceUi.StripeFine : ServiceUi.StripeLookThisWeek));
        NumbersPanel.Children.Add(new KpiCard("Oldest open", numbers.OldestOpenDays is { } days ? ServiceDisplay.Text($"{days:N0} days") : "—",
            numbers.OldestOpenInvoice is { } invoice ? $"invoice {invoice}" : "no open invoice", numbers.OldestOpenDays > 30 ? ServiceUi.StripeActToday : ServiceUi.StripeNone));
        NumbersPanel.Children.Add(new KpiCard("Received this month", ServiceDisplay.Whole(numbers.ReceivedThisMonth), ServiceDisplay.Month(numbers.AsOf), ServiceUi.StripeNone));
        NumbersPanel.Children.Add(new KpiCard("Jobs waiting for parts", ServiceDisplay.Whole(numbers.JobsWaitingForParts), "latest pending list", numbers.JobsWaitingForParts == 0 ? ServiceUi.StripeFine : ServiceUi.StripeLookThisWeek));
        NumbersPanel.Children.Add(new KpiCard("GIT lines, last 30 days", ServiceDisplay.Whole(numbers.GitLinesLast30Days), ServiceDisplay.Text($"to {numbers.AsOf:dd MMM yyyy}"), ServiceUi.StripeNone));
    }

    private void ShowWaiting(IReadOnlyList<ServiceWaitingJob> jobs)
    {
        WaitingRows = jobs.OrderByDescending(job => job.DaysWaiting.HasValue).ThenByDescending(job => job.DaysWaiting)
            .ThenBy(job => job.JobOrderNumber, StringComparer.OrdinalIgnoreCase)
            .Select(job => new ServiceGridRow(job, [job.JobOrderNumber, job.SpareRequired, job.IndentDate, job.DaysWaiting, job.Brand, job.Model, job.PendingAt]))
            .ToArray();
        Waiting.ItemsSource = WaitingRows;
        exportWaiting.IsEnabled = WaitingRows.Count > 0;
        openHistory.IsEnabled = false;
        waitingStatus.Text = WaitingRows.Count == 0
            ? "No job on the latest Pending repair list is waiting for a part."
            : ServiceDisplay.Text($"{WaitingRows.Count:N0} job{(WaitingRows.Count == 1 ? "" : "s")} waiting for parts · longest wait first. Select a job and Open job history.");
    }

    private void ShowGit(IReadOnlyList<ServiceGitLine> git, DateOnly asOf)
    {
        GitRows = RecentGit(git, asOf)
            .Select(line => new ServiceGridRow(line, [line.BusinessDate, line.StmNumber, line.ItemId, line.QuantityShipped, line.Ucp, line.FromLocation, line.ToLocation]))
            .ToArray();
        Git.ItemsSource = GitRows;
        gitStatus.Text = GitRows.Count == 0
            ? ServiceDisplay.Text($"No goods in transit lines in the 30 days to {asOf:dd MMM yyyy}.")
            : ServiceDisplay.Text($"{GitRows.Count:N0} line{(GitRows.Count == 1 ? "" : "s")} in the 30 days to {asOf:dd MMM yyyy}.");
    }

    /// <summary>Opens Job history for the selected waiting job (double-click or the button).</summary>
    public void OpenSelectedJobHistory()
    {
        if (Waiting.SelectedItem is ServiceGridRow { Source: ServiceWaitingJob job }) OpenJobHistory(job.JobOrderNumber);
    }

    /// <summary>Opens Job history through the shell's opener (TaskNavigator.NavigateServiceJob); nothing without one.</summary>
    public void OpenJobHistory(string jobOrderNumber) => openJob?.Invoke(jobOrderNumber);

    /// <summary>The jobs-waiting panel as its export writes it (both panels export, design 3.6).</summary>
    public ExcelReportData BuildWaitingExportData() =>
        new(Waiting.Columns.Select(column => new ExcelReportColumn((string)column.Header, WaitingNumberFormat((string)column.Header))).ToArray(),
            WaitingRows.Select(row => row.Cells).ToArray());

    private static string WaitingNumberFormat(string header) => header == "Days waiting" ? "#,##0" : "General";

    /// <summary>
    /// R-UI-13 (SD-09): the jobs-waiting export spans the earliest indent date of the waiting rows to the as-at date, not the
    /// invoice grid's span. Rows with no indent date are ignored; with none dated the span is the as-at day.
    /// </summary>
    public (DateOnly From, DateOnly To) WaitingExportPeriod
    {
        get
        {
            var asOf = AsOf(parts, Refreshes, DateOnly.FromDateTime(DateTime.Today));
            var dated = WaitingRows.Select(row => ((ServiceWaitingJob)row.Source).IndentDate).Where(date => date.HasValue).Select(date => date!.Value).ToArray();
            return (dated.Length == 0 || dated.Min() > asOf ? asOf : dated.Min(), asOf);
        }
    }

    public async Task ExportWaitingToPathAsync(string path)
    {
        var (from, to) = WaitingExportPeriod;
        var metadata = new ExcelReportMetadata("Service jobs waiting for parts", from, to, "Read only", ExportRuleVersion,
            AsAtText, DateTimeOffset.UtcNow, CentreLabel);
        await export(path, metadata, BuildWaitingExportData());
    }

    private async Task ExportWaitingWithDialogAsync()
    {
        if (WaitingRows.Count == 0) return;
        var dialog = ExportSaveDialog.Create(ExportSaveDialog.ExcelFilter, $"Service_jobs_waiting_for_parts_{DateTime.Today:yyyyMMdd}.xlsx");
        if (!ExportSaveDialog.ShowDialog(dialog, this)) return;
        try
        {
            await ExportWaitingToPathAsync(dialog.FileName);
            waitingStatus.Text = ServiceDisplay.Text($"Exported {WaitingRows.Count:N0} jobs to {System.IO.Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception exception)
        {
            waitingStatus.Text = FailureText("The export could not be saved. ", exception, "Service.Parts", "SERVICE_EXPORT_FAILED");
        }
    }

    private void OfferMonths(IReadOnlyList<ServicePartsInvoice> invoices)
    {
        var choices = new List<ServiceMonthChoice> { ServiceMonthChoice.All };
        choices.AddRange(invoices.Where(invoice => invoice.InvoiceDate.HasValue)
            .Select(invoice => new DateOnly(invoice.InvoiceDate!.Value.Year, invoice.InvoiceDate.Value.Month, 1))
            .Distinct().OrderByDescending(first => first).Select(ServiceMonthChoice.For));
        if (chosenMonth is { } chosen && !choices.Any(choice => choice.Month == chosen)) choices.Add(ServiceMonthChoice.For(chosen));
        settingMonths = true;
        try { month.ItemsSource = choices; SelectMonthItem(); }
        finally { settingMonths = false; }
    }

    private void SelectMonthItem()
    {
        var choices = (List<ServiceMonthChoice>)month.ItemsSource;
        if (chosenMonth is { } chosen && !choices.Any(choice => choice.Month == chosen))
        {
            settingMonths = true;
            try { choices = [.. choices, ServiceMonthChoice.For(chosen)]; month.ItemsSource = choices; }
            finally { settingMonths = false; }
        }
        settingMonths = true;
        try { month.SelectedItem = choices.First(choice => choice.Month == chosenMonth); }
        finally { settingMonths = false; }
    }

    private static void Configure(DataGrid grid, string name, (string Header, string? Format, double Width)[] columns)
    {
        foreach (var (header, format, width) in columns)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding($"Cells[{grid.Columns.Count}]") { StringFormat = format, ConverterCulture = ServiceDisplay.Culture },
                Width = width
            });
        TablePresentation.Configure(grid);
        AutomationProperties.SetName(grid, name);
    }

    protected override string EmptyRowsText => parts.Invoices.Count == 0
        ? "No purchase invoices imported yet."
        : "No purchase invoice matches these filters.";
    protected override string Summarise(int count) =>
        ServiceDisplay.Text($"{count:N0} invoice{(count == 1 ? "" : "s")} · open first, oldest first") + (OpenOnly ? " · open only" : "") +
        (chosenMonth is { } chosen ? " · " + ServiceDisplay.Month(chosen) : "") + (string.IsNullOrWhiteSpace(item.Text) ? "" : $" · item \"{item.Text.Trim()}\"") + ".";
    protected override string ExportName => "Service parts and purchases";
    protected override (DateOnly From, DateOnly To) ExportPeriod
    {
        get
        {
            var asOf = AsOf(parts, Refreshes, DateOnly.FromDateTime(DateTime.Today));
            var dated = Rows.Select(row => ((ServicePartsInvoice)row.Source).InvoiceDate).Where(date => date.HasValue).Select(date => date!.Value).ToArray();
            return (dated.Length == 0 ? asOf : dated.Min(), asOf);
        }
    }
}

