extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Etp.Reporting.Reporting;
using ServiceGitLine = EtpApplication::Etp.Reporting.Application.Service.ServiceGitLine;
using ServiceJobWaitingForParts = EtpApplication::Etp.Reporting.Application.Service.ServiceJobWaitingForParts;
using ServiceParts = EtpApplication::Etp.Reporting.Application.Service.ServiceParts;
using ServicePartsRules = EtpApplication::Etp.Reporting.Application.Service.ServicePartsRules;
using ServicePurchaseInvoice = EtpApplication::Etp.Reporting.Application.Service.ServicePurchaseInvoice;
using ServicePurchaseInvoiceLine = EtpApplication::Etp.Reporting.Application.Service.ServicePurchaseInvoiceLine;
using ServiceRefresh = EtpApplication::Etp.Reporting.Application.Service.ServiceRefresh;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>A month on the Parts screen's month filter; a null month is "All months".</summary>
public sealed record ServiceMonthChoice(DateOnly? Month, string Label)
{
    public override string ToString() => Label;
    public static ServiceMonthChoice All { get; } = new(null, "All months");
    public static ServiceMonthChoice For(DateOnly month) => new(new DateOnly(month.Year, month.Month, 1), month.ToString("MMM yyyy"));
}

/// <summary>The five numbers of the Parts screen (design 3.6), computed by <see cref="ServicePartsView.Summarise(ServiceParts, DateOnly)"/>.</summary>
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
    public const string NoLinkText = "No data link exists between a pending job and a purchase invoice: the Service exports carry none. The two lists are shown side by side and are not joined.";
    public const string WaitingTitle = "Jobs waiting for parts (S009)";
    public const string LinesTitle = "Invoice lines";
    public const string GitTitle = "Goods in transit (S013), last 30 days";
    public const string NoLinesText = "Select an invoice to see its lines.";

    private readonly ServiceExcelExport export;
    private readonly ServiceNavigate? navigate;
    private readonly CheckBox openOnly = new() { Content = "Open only", MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
    private readonly ComboBox month = new() { MinWidth = 150, MinHeight = 44, ItemsSource = new List<ServiceMonthChoice> { ServiceMonthChoice.All }, SelectedIndex = 0 };
    private readonly TextBox item = new() { MinWidth = 180, MinHeight = 44, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock linesStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock waitingStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock gitStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock closingStock = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 4) };
    private readonly Button exportWaiting = new() { Content = "Export jobs waiting to Excel", MinHeight = 44, Margin = new Thickness(0, 4, 8, 4), IsEnabled = false };
    private readonly Button openHistory = new() { Content = "Open job history", MinHeight = 44, Margin = new Thickness(0, 4, 8, 4), IsEnabled = false };
    private DateOnly? chosenMonth;
    private bool settingMonths;
    private ServiceParts parts = ServiceParts.Empty;
    private IReadOnlyList<ServicePurchaseInvoiceLine> lines = [];

    public ServicePartsView(Func<ServiceReportQuery> query, ServiceExcelExport export, ServiceNavigate? navigate = null)
        : base("Service parts and purchases",
            "Purchase invoices created (S007) against received (S008): open until a GRN or received date is exported. Days open count from the invoice date to the Service snapshot while open, and to the received date once closed.",
            "Service.Parts", "SERVICE_PARTS_LOAD_FAILED", query, export)
    {
        this.export = export;
        this.navigate = navigate;
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
        FilterBar.Children.Add(new TextBlock { Text = "Month", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        FilterBar.Children.Add(month);
        FilterBar.Children.Add(new TextBlock { Text = "Item", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) });
        FilterBar.Children.Add(item);
        var show = new Button { Content = "Show", MinHeight = 44, Margin = new Thickness(8, 0, 0, 0) };
        show.Click += async (_, _) => await ActivateAsync();
        FilterBar.Children.Add(show);
        SetColumns(
        [
            new("Invoice", Width: 140), new("Invoice date", DisplayFormat: "dd MMM yyyy"), new("GRN", Width: 140), new("GRN date", DisplayFormat: "dd MMM yyyy"),
            new("Items", "#,##0", Width: 70), new("Shipped qty", "#,##0.##", "N0", 100), new("Received qty", "#,##0.##", "N0", 100),
            new("Net amount", "#,##0.00", "N2", 120), new("Status", Width: 80), new("Days open", "#,##0", Width: 90), new("From location", Width: 120)
        ]);
        Table.SelectionChanged += (_, _) => ShowLines((Table.SelectedItem as ServiceGridRow)?.Source as ServicePurchaseInvoice);

        Footer.Children.Add(new TextBlock { Text = LinesTitle, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        Footer.Children.Add(linesStatus);
        Configure(Lines, LinesTitle, [("Item code", null, 140), ("Item", null, 320), ("Shipped qty", "N0", 100), ("Received qty", "N0", 100), ("Net amount", "N2", 120)]);
        Footer.Children.Add(Lines);

        Footer.Children.Add(new TextBlock { Text = WaitingTitle, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        Footer.Children.Add(new TextBlock { Text = NoLinkText, TextWrapping = TextWrapping.Wrap });
        Footer.Children.Add(waitingStatus);
        Configure(Waiting, WaitingTitle, [("Job number", null, 150), ("Part required", null, 260), ("Spare code", null, 120), ("Indent", null, 130),
            ("Indent date", "dd MMM yyyy", 120), ("Days waiting", "N0", 100), ("Brand", null, 120), ("Model", null, 150), ("As at", "dd MMM yyyy", 120)]);
        Waiting.SelectionChanged += (_, _) => openHistory.IsEnabled = Waiting.SelectedItem is ServiceGridRow;
        Waiting.MouseDoubleClick += (_, _) => OpenSelectedJobHistory();
        Footer.Children.Add(Waiting);
        var waitingActions = new WrapPanel { Orientation = Orientation.Horizontal };
        openHistory.Click += (_, _) => OpenSelectedJobHistory();
        exportWaiting.Click += async (_, _) => await ExportWaitingWithDialogAsync();
        waitingActions.Children.Add(openHistory);
        waitingActions.Children.Add(exportWaiting);
        Footer.Children.Add(waitingActions);

        Footer.Children.Add(new TextBlock { Text = GitTitle, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        Footer.Children.Add(gitStatus);
        Configure(Git, GitTitle, [("Date", "dd MMM yyyy", 120), ("Document", null, 140), ("Item code", null, 140), ("Item", null, 260),
            ("Quantity", "N0", 90), ("Value", "N2", 120), ("From", null, 100), ("To", null, 100)]);
        Footer.Children.Add(Git);
        Footer.Children.Add(closingStock);
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
        parts = await source.LoadPartsAsync() ?? ServiceParts.Empty;
        lines = parts.Lines;
        OfferMonths(parts.Invoices);
        var asOf = AsOf(parts, Refreshes, DateOnly.FromDateTime(DateTime.Today));
        var chosen = Filter(parts.Invoices, parts.Lines, OpenOnly, chosenMonth, item.Text);
        ShowNumbers(Summarise(parts, asOf));
        ShowWaiting(parts.JobsWaitingForParts);
        ShowGit(parts.GitLines, asOf);
        closingStock.Text = DescribeClosingStock(parts);
        ShowLines(null);
        return Sort(chosen).Select(invoice => new ServiceGridRow(invoice,
        [
            invoice.InvoiceNumber, invoice.InvoiceDate, invoice.GrnNumber, invoice.GrnDate, invoice.Items, invoice.ShippedQuantity,
            invoice.ReceivedQuantity, invoice.NetAmount, ServicePartsRules.Status(invoice), ServicePartsRules.DaysOpen(invoice), invoice.FromLocation
        ])).ToArray();
    }

    protected override void ClearExtras()
    {
        parts = ServiceParts.Empty; lines = [];
        Numbers.Children.Clear(); NumbersShown = null;
        LineRows = []; Lines.ItemsSource = null; linesStatus.Text = "";
        WaitingRows = []; Waiting.ItemsSource = null; waitingStatus.Text = ""; exportWaiting.IsEnabled = false; openHistory.IsEnabled = false;
        GitRows = []; Git.ItemsSource = null; gitStatus.Text = ""; closingStock.Text = "";
    }

    /// <summary>The date the numbers count from: the latest Parts snapshot, else the latest Service reading, else today (Q15).</summary>
    public static DateOnly AsOf(ServiceParts parts, IReadOnlyList<ServiceRefresh> refreshes, DateOnly today)
    {
        var dates = parts.Invoices.Select(invoice => invoice.SnapshotDate)
            .Concat(parts.JobsWaitingForParts.Select(job => job.SnapshotDate))
            .Concat(parts.GitLines.Select(line => line.SnapshotDate))
            .Concat(parts.ClosingStock is { } stock ? new[] { stock.SnapshotDate } : Array.Empty<DateOnly>())
            .ToArray();
        if (dates.Length > 0) return dates.Max();
        return refreshes.Count > 0 ? refreshes.Max(refresh => refresh.SnapshotDate) : today;
    }

    /// <summary>Open only, invoice month, and an item text matched against the item code or description of any line of the invoice.</summary>
    public static IReadOnlyList<ServicePurchaseInvoice> Filter(IReadOnlyList<ServicePurchaseInvoice> invoices, IReadOnlyList<ServicePurchaseInvoiceLine> lines,
        bool openOnly, DateOnly? month, string? itemText)
    {
        var text = (itemText ?? "").Trim();
        var matching = text.Length == 0 ? null : lines
            .Where(line => (line.ItemCode ?? "").Contains(text, StringComparison.OrdinalIgnoreCase) || (line.ItemDescription ?? "").Contains(text, StringComparison.OrdinalIgnoreCase))
            .Select(line => line.InvoiceNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return invoices
            .Where(invoice => !openOnly || ServicePartsRules.IsOpen(invoice))
            .Where(invoice => month is not { } chosen || invoice.InvoiceDate is { } date && date.Year == chosen.Year && date.Month == chosen.Month)
            .Where(invoice => matching is null || matching.Contains(invoice.InvoiceNumber))
            .ToArray();
    }

    /// <summary>Open first, then oldest invoice date first (no date last), then invoice number.</summary>
    public static IReadOnlyList<ServicePurchaseInvoice> Sort(IEnumerable<ServicePurchaseInvoice> invoices) =>
        invoices.OrderBy(invoice => ServicePartsRules.IsOpen(invoice) ? 0 : 1)
            .ThenBy(invoice => invoice.InvoiceDate.HasValue ? 0 : 1).ThenBy(invoice => invoice.InvoiceDate)
            .ThenBy(invoice => invoice.InvoiceNumber, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>The numbers of design 3.6. "This month" and "last 30 days" count back from <paramref name="asOf"/>.</summary>
    public static ServicePartsNumbers Summarise(ServiceParts parts, DateOnly asOf)
    {
        var open = parts.Invoices.Where(ServicePartsRules.IsOpen).ToArray();
        var oldest = open.Where(invoice => ServicePartsRules.DaysOpen(invoice).HasValue).OrderByDescending(invoice => ServicePartsRules.DaysOpen(invoice)).FirstOrDefault();
        var receivedThisMonth = parts.Invoices.Count(invoice => !ServicePartsRules.IsOpen(invoice)
            && (invoice.ReceivedDate ?? invoice.GrnDate) is { } received && received.Year == asOf.Year && received.Month == asOf.Month);
        return new ServicePartsNumbers(open.Length, open.Sum(invoice => invoice.NetAmount ?? 0m),
            oldest is null ? null : ServicePartsRules.DaysOpen(oldest), oldest?.InvoiceNumber,
            receivedThisMonth, parts.JobsWaitingForParts.Count, RecentGit(parts.GitLines, asOf).Count, asOf);
    }

    /// <summary>S013 lines dated within the 30 days up to <paramref name="asOf"/> (undated lines are left out), newest first.</summary>
    public static IReadOnlyList<ServiceGitLine> RecentGit(IReadOnlyList<ServiceGitLine> git, DateOnly asOf)
    {
        var from = asOf.AddDays(-30);
        return git.Where(line => line.TransactionDate is { } date && date >= from && date <= asOf)
            .OrderByDescending(line => line.TransactionDate).ThenBy(line => line.DocumentNumber, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string DescribeClosingStock(ServiceParts parts) => parts.ClosingStock is { } stock
        ? $"Latest closing stock (S006) as at {stock.SnapshotDate:dd MMM yyyy}: {stock.Items:N0} items" +
          (stock.Quantity is { } quantity ? $", quantity {quantity:N0}" : "") + (stock.Value is { } value ? $", value {value:N2}" : "") + "."
        : "No S006 closing stock reading imported yet.";

    /// <summary>Shows the lines of one invoice below the grid. Selecting a row does it; tests call it by number.</summary>
    public void SelectInvoice(string invoiceNumber)
    {
        var row = Rows.FirstOrDefault(candidate => string.Equals(((ServicePurchaseInvoice)candidate.Source).InvoiceNumber, invoiceNumber, StringComparison.OrdinalIgnoreCase));
        Table.SelectedItem = row;
        ShowLines(row?.Source as ServicePurchaseInvoice);
    }

    private void ShowLines(ServicePurchaseInvoice? invoice)
    {
        if (invoice is null) { LineRows = []; Lines.ItemsSource = null; linesStatus.Text = parts.Invoices.Count == 0 ? "" : NoLinesText; return; }
        LineRows = lines.Where(line => string.Equals(line.InvoiceNumber, invoice.InvoiceNumber, StringComparison.OrdinalIgnoreCase))
            .OrderBy(line => line.ItemCode, StringComparer.OrdinalIgnoreCase)
            .Select(line => new ServiceGridRow(line, [line.ItemCode, line.ItemDescription, line.ShippedQuantity, line.ReceivedQuantity, line.NetAmount])).ToArray();
        Lines.ItemsSource = LineRows;
        linesStatus.Text = LineRows.Count == 0
            ? $"Invoice {invoice.InvoiceNumber}: no lines in the export."
            : $"Invoice {invoice.InvoiceNumber}: {LineRows.Count:N0} line{(LineRows.Count == 1 ? "" : "s")}.";
    }

    private void ShowNumbers(ServicePartsNumbers numbers)
    {
        NumbersShown = numbers;
        Numbers.Children.Clear();
        Numbers.Children.Add(new KpiCard("Open invoices", numbers.OpenInvoices.ToString("N0"), $"net {numbers.OpenValue:N2}", numbers.OpenInvoices == 0 ? "Success" : "Warning"));
        Numbers.Children.Add(new KpiCard("Oldest open", numbers.OldestOpenDays is { } days ? $"{days:N0} days" : "—",
            numbers.OldestOpenInvoice is { } invoice ? $"invoice {invoice}" : "no open invoice", numbers.OldestOpenDays > 30 ? "Critical" : "Information"));
        Numbers.Children.Add(new KpiCard("Received this month", numbers.ReceivedThisMonth.ToString("N0"), numbers.AsOf.ToString("MMM yyyy"), "Success"));
        Numbers.Children.Add(new KpiCard("Jobs waiting for parts", numbers.JobsWaitingForParts.ToString("N0"), "latest S009 list", numbers.JobsWaitingForParts == 0 ? "Success" : "Warning"));
        Numbers.Children.Add(new KpiCard("GIT lines, last 30 days", numbers.GitLinesLast30Days.ToString("N0"), $"to {numbers.AsOf:dd MMM yyyy}", "Information"));
    }

    private void ShowWaiting(IReadOnlyList<ServiceJobWaitingForParts> jobs)
    {
        WaitingRows = jobs.OrderByDescending(job => job.DaysWaiting.HasValue).ThenByDescending(job => job.DaysWaiting)
            .ThenBy(job => job.JobOrderNumber, StringComparer.OrdinalIgnoreCase)
            .Select(job => new ServiceGridRow(job, [job.JobOrderNumber, job.SpareRequired, job.SpareCode, job.IndentNumber, job.IndentDate, job.DaysWaiting, job.Brand, job.Model, job.SnapshotDate]))
            .ToArray();
        Waiting.ItemsSource = WaitingRows;
        exportWaiting.IsEnabled = WaitingRows.Count > 0;
        openHistory.IsEnabled = false;
        waitingStatus.Text = WaitingRows.Count == 0
            ? "No job on the latest Pending repair list is waiting for a part."
            : $"{WaitingRows.Count:N0} job{(WaitingRows.Count == 1 ? "" : "s")} waiting for parts · longest wait first. Select a job and Open job history.";
    }

    private void ShowGit(IReadOnlyList<ServiceGitLine> git, DateOnly asOf)
    {
        GitRows = RecentGit(git, asOf)
            .Select(line => new ServiceGridRow(line, [line.TransactionDate, line.DocumentNumber, line.ItemCode, line.ItemDescription, line.Quantity, line.Value, line.FromLocation, line.ToLocation]))
            .ToArray();
        Git.ItemsSource = GitRows;
        gitStatus.Text = GitRows.Count == 0
            ? $"No goods in transit lines in the 30 days to {asOf:dd MMM yyyy}."
            : $"{GitRows.Count:N0} line{(GitRows.Count == 1 ? "" : "s")} in the 30 days to {asOf:dd MMM yyyy}.";
    }

    /// <summary>Opens Job history for the selected waiting job (double-click or the button).</summary>
    public void OpenSelectedJobHistory()
    {
        if (Waiting.SelectedItem is ServiceGridRow { Source: ServiceJobWaitingForParts job }) OpenJobHistory(job.JobOrderNumber);
    }

    public void OpenJobHistory(string jobOrderNumber)
    {
        if (navigate is null) return;
        navigate(ServiceScreens.JobHistoryTask, jobOrderNumber);
    }

    /// <summary>The jobs-waiting panel as its export writes it (both panels export, design 3.6).</summary>
    public ExcelReportData BuildWaitingExportData() =>
        new(Waiting.Columns.Select(column => new ExcelReportColumn((string)column.Header, WaitingNumberFormat((string)column.Header))).ToArray(),
            WaitingRows.Select(row => row.Cells).ToArray());

    private static string WaitingNumberFormat(string header) => header == "Days waiting" ? "#,##0" : "General";

    public async Task ExportWaitingToPathAsync(string path)
    {
        var (from, to) = ExportPeriod;
        var metadata = new ExcelReportMetadata("Service jobs waiting for parts", from, to, "Read only", "service-interim-1",
            AsAtText, DateTimeOffset.UtcNow, ServiceCentreLabel);
        await export(path, metadata, BuildWaitingExportData());
    }

    private async Task ExportWaitingWithDialogAsync()
    {
        if (WaitingRows.Count == 0) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            FileName = $"Service_jobs_waiting_for_parts_{DateTime.Today:yyyyMMdd}.xlsx",
            AddExtension = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            await ExportWaitingToPathAsync(dialog.FileName);
            waitingStatus.Text = $"Exported {WaitingRows.Count:N0} jobs to {System.IO.Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception exception)
        {
            DesktopDiagnostics.Record(exception, "Service.Parts", "SERVICE_EXPORT_FAILED");
            waitingStatus.Text = "The export could not be saved. " + DesktopFriendlyError.Describe(exception);
        }
    }

    private void OfferMonths(IReadOnlyList<ServicePurchaseInvoice> invoices)
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
                Binding = new Binding($"Cells[{grid.Columns.Count}]") { StringFormat = format },
                Width = width
            });
        TablePresentation.Configure(grid);
        AutomationProperties.SetName(grid, name);
    }

    protected override string EmptyRowsText => parts.Invoices.Count == 0
        ? "No purchase invoices (S007/S008) imported yet."
        : "No purchase invoice matches these filters.";
    protected override string Summarise(int count) =>
        $"{count:N0} invoice{(count == 1 ? "" : "s")} · open first, oldest first" + (OpenOnly ? " · open only" : "") +
        (chosenMonth is { } chosen ? $" · {chosen:MMM yyyy}" : "") + (string.IsNullOrWhiteSpace(item.Text) ? "" : $" · item \"{item.Text.Trim()}\"") + ".";
    protected override string ExportName => "Service parts and purchases";
    protected override (DateOnly From, DateOnly To) ExportPeriod
    {
        get
        {
            var asOf = AsOf(parts, Refreshes, DateOnly.FromDateTime(DateTime.Today));
            var dated = Rows.Select(row => ((ServicePurchaseInvoice)row.Source).InvoiceDate).Where(date => date.HasValue).Select(date => date!.Value).ToArray();
            return (dated.Length == 0 ? asOf : dated.Min(), asOf);
        }
    }
}
