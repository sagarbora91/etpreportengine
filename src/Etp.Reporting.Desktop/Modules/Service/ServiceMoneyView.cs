extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using ServiceMoneyDay = EtpApplication::Etp.Reporting.Application.Service.ServiceMoneyDay;
using ServiceRefresh = EtpApplication::Etp.Reporting.Application.Service.ServiceRefresh;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;
using ServiceUnmatchedMoneyEntry = EtpApplication::Etp.Reporting.Application.Service.ServiceUnmatchedMoneyEntry;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>
/// Service money check: the Service centre's S004 tender collection per date and tender beside the
/// manual Service cash, card and UPI entries of the Service-money shop, which stay the cash-book source
/// (decision 16). Below it, the Service entries made at other shops (listed, never added in), then the
/// dates whose money total changed since the previous refresh (information, never an approval item).
/// 1.10.0 (design 3.7): the manual side loads even before any Service file is imported (SD-08), and the date range
/// defaults to the latest S004 snapshot date minus 30 days until the user chooses one.
/// </summary>
public sealed class ServiceMoneyView : ServiceScreenView
{
    private readonly DatePicker fromDate = new() { MinHeight = 44, MinWidth = 140 };
    private readonly DatePicker toDate = new() { MinHeight = 44, MinWidth = 140 };
    private readonly TextBlock manualSources = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock changesStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock unmatchedStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private bool rangeChosen;
    private bool applyingDefault;

    /// <summary>The title of the grid of Service entries made at other shops.</summary>
    public const string UnmatchedTitle = "Service entries at other shops (not added)";

    public ServiceMoneyView(Func<ServiceReportQuery> query, ServiceExcelExport export)
        : base("Service money check",
            "S004 tender collection from the Service centre beside the manual Service cash, card and UPI entries. The manual entries stay the cash-book figures; this screen only compares them.",
            "Service.Money", "SERVICE_MONEY_LOAD_FAILED", query, export)
    {
        var today = DateTime.Today;
        toDate.SelectedDate = today;
        fromDate.SelectedDate = today.AddDays(-30);
        fromDate.SelectedDateChanged += (_, _) => { if (!applyingDefault) rangeChosen = true; };
        toDate.SelectedDateChanged += (_, _) => { if (!applyingDefault) rangeChosen = true; };
        AutomationProperties.SetName(fromDate, "From date");
        AutomationProperties.SetName(toDate, "To date");
        FilterBar.Children.Add(new TextBlock { Text = "From", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        FilterBar.Children.Add(fromDate);
        FilterBar.Children.Add(new TextBlock { Text = "To", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) });
        FilterBar.Children.Add(toDate);
        var show = new Button { Content = "Show", MinHeight = 44, Margin = new Thickness(8, 0, 0, 0) };
        show.Click += async (_, _) => await ActivateAsync();
        FilterBar.Children.Add(show);
        SetColumns(
        [
            new("Date", DisplayFormat: "dd MMM yyyy"), new("Tender", Width: 110), new("S004 amount", "#,##0.00", "N2", 130),
            new("Manual amount", "#,##0.00", "N2", 130), new("Difference", "#,##0.00", "N2", 120), new("Manual entries from", Width: 200)
        ]);

        Footer.Children.Add(manualSources);
        Footer.Children.Add(new TextBlock { Text = UnmatchedTitle, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        Footer.Children.Add(unmatchedStatus);
        string[] unmatchedHeaders = ["Date", "Shop", "Field", "Amount", "Note"];
        for (var index = 0; index < unmatchedHeaders.Length; index++)
            Unmatched.Columns.Add(new DataGridTextColumn
            {
                Header = unmatchedHeaders[index],
                Binding = new Binding($"Cells[{index}]") { StringFormat = index == 0 ? "dd MMM yyyy" : index == 3 ? "N2" : null },
                Width = index == 4 ? 320 : 130
            });
        TablePresentation.Configure(Unmatched);
        AutomationProperties.SetName(Unmatched, UnmatchedTitle);
        Footer.Children.Add(Unmatched);
        Footer.Children.Add(new TextBlock { Text = "Money changed since the previous refresh", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        Footer.Children.Add(new TextBlock { Text = "A date is listed when a newer Service file gives it a different total. It is information for checking, not an error.", TextWrapping = TextWrapping.Wrap });
        Footer.Children.Add(changesStatus);
        string[] changeHeaders = ["Date", "Report", "Previous refresh", "Previous amount", "Current refresh", "Current amount", "Change"];
        for (var index = 0; index < changeHeaders.Length; index++)
            Changes.Columns.Add(new DataGridTextColumn
            {
                Header = changeHeaders[index],
                Binding = new Binding($"Cells[{index}]") { StringFormat = index is 0 or 2 or 4 ? "dd MMM yyyy" : index == 1 ? null : "N2" },
                Width = 130
            });
        TablePresentation.Configure(Changes);
        AutomationProperties.SetName(Changes, "Money changed since the previous refresh");
        Footer.Children.Add(Changes);
    }

    public DataGrid Changes { get; } = new() { AutoGenerateColumns = false, IsReadOnly = true, MaxHeight = 220 };
    public IReadOnlyList<ServiceGridRow> ChangeRows { get; private set; } = [];
    public DataGrid Unmatched { get; } = new() { AutoGenerateColumns = false, IsReadOnly = true, MaxHeight = 180 };
    public IReadOnlyList<ServiceGridRow> UnmatchedRows { get; private set; } = [];
    public string UnmatchedStatusText => unmatchedStatus.Text;
    public string ManualSourcesText => manualSources.Text;
    public string ChangesStatusText => changesStatus.Text;

    public DateOnly From
    {
        get => DateOnly.FromDateTime(fromDate.SelectedDate ?? DateTime.Today.AddDays(-30));
        set => fromDate.SelectedDate = value.ToDateTime(TimeOnly.MinValue);
    }

    public DateOnly To
    {
        get => DateOnly.FromDateTime(toDate.SelectedDate ?? DateTime.Today);
        set => toDate.SelectedDate = value.ToDateTime(TimeOnly.MinValue);
    }

    /// <summary>True once the user (or a caller) has set a date; until then each refresh applies <see cref="DefaultRange"/>.</summary>
    public bool RangeChosen => rangeChosen;

    /// <summary>
    /// The default range: the latest S004 snapshot date and the 30 days before it, so the screen opens on the days the
    /// Service centre last reported rather than on a weekend with no rows (design 3.7, Q15). Today when no S004
    /// reading exists yet.
    /// </summary>
    public static (DateOnly From, DateOnly To) DefaultRange(IReadOnlyList<ServiceRefresh> refreshes, DateOnly today)
    {
        var s004 = refreshes.Where(refresh => string.Equals(refresh.ReportCode, "S004", StringComparison.OrdinalIgnoreCase)).ToArray();
        var to = s004.Length == 0 ? today : s004.Max(refresh => refresh.SnapshotDate);
        return (to.AddDays(-30), to);
    }

    /// <summary>SD-08: the manual Service entries are worth checking before the first Service import, so the screen loads without readings.</summary>
    protected override bool LoadsWithoutReadings => true;

    protected override string? PrepareLoad()
    {
        if (!rangeChosen)
        {
            var (from, to) = DefaultRange(Refreshes, DateOnly.FromDateTime(DateTime.Today));
            applyingDefault = true;
            try { From = from; To = to; }
            finally { applyingDefault = false; }
        }
        return From > To ? "The From date is after the To date. Choose a valid range." : null;
    }

    private bool HasS004Reading => Refreshes.Any(refresh => string.Equals(refresh.ReportCode, "S004", StringComparison.OrdinalIgnoreCase));
    private string S004Note => HasS004Reading ? "" : " No S004 reading is imported yet, so only the manual entries are shown.";

    protected override async Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source)
    {
        var days = await source.LoadMoneyCheckAsync(From, To);
        var unmatched = await source.LoadUnmatchedServiceEntriesAsync(From, To);
        manualSources.Text = DescribeManualSources(days);
        ShowUnmatched(unmatched);
        return days.OrderBy(day => day.BusinessDate).ThenBy(day => day.Tender, StringComparer.OrdinalIgnoreCase)
            .Select(day => new ServiceGridRow(day,
            [
                day.BusinessDate, day.Tender, day.S004Amount, day.ManualAmount, day.Difference, Joined((day.ManualStores ?? []).Order(StringComparer.OrdinalIgnoreCase))
            ])).ToArray();
    }

    protected override async Task LoadExtrasAsync(ServiceReportQuery source, Func<bool> isCurrent)
    {
        changesStatus.Text = "Loading money changes…";
        var changes = await source.LoadMoneyChangesAsync();
        if (!isCurrent()) return;
        ChangeRows = changes.OrderByDescending(change => change.BusinessDate).ThenBy(change => change.ReportCode, StringComparer.Ordinal)
            .Select(change => new ServiceGridRow(change,
            [
                change.BusinessDate, change.ReportCode, change.PreviousSnapshotDate, change.PreviousAmount,
                change.CurrentSnapshotDate, change.CurrentAmount, change.CurrentAmount - change.PreviousAmount
            ])).ToArray();
        Changes.ItemsSource = ChangeRows;
        changesStatus.Text = ChangeRows.Count == 0
            ? "No money changed since the previous refresh."
            : $"{ChangeRows.Count:N0} date{(ChangeRows.Count == 1 ? "" : "s")} changed since the previous refresh.";
    }

    protected override void ClearExtras()
    {
        ChangeRows = []; Changes.ItemsSource = null; changesStatus.Text = ""; manualSources.Text = "";
        UnmatchedRows = []; Unmatched.ItemsSource = null; unmatchedStatus.Text = "";
    }

    private void ShowUnmatched(IReadOnlyList<ServiceUnmatchedMoneyEntry> entries)
    {
        UnmatchedRows = entries.OrderBy(entry => entry.BusinessDate).ThenBy(entry => entry.StoreCode, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new ServiceGridRow(entry, [entry.BusinessDate, entry.StoreCode, entry.FieldCode, entry.Amount, entry.Note])).ToArray();
        Unmatched.ItemsSource = UnmatchedRows;
        unmatchedStatus.Text = UnmatchedRows.Count == 0
            ? "No Service entries at other shops in this range."
            : $"{UnmatchedRows.Count:N0} Service entr{(UnmatchedRows.Count == 1 ? "y" : "ies")} at other shops. They are shown for checking and are not added to the money check.";
    }

    /// <summary>Whose manual Service entries were compared (decision 16, Q1 and Q3).</summary>
    public static string DescribeManualSources(IReadOnlyList<ServiceMoneyDay> days)
    {
        var stores = Joined(days.SelectMany(day => day.ManualStores ?? []).Order(StringComparer.OrdinalIgnoreCase));
        const string rule = "Only the Titan World shop's Service cash, card and UPI entries are compared (decision 16); Service WDC is not compared.";
        return stores.Length == 0
            ? $"Manual entries from: none in this range. {rule}"
            : $"Manual entries from: {stores}. {rule}";
    }

    protected override string EmptyRowsText => "No Service money in this date range." + S004Note;
    protected override string Summarise(int count) => $"{count:N0} date and tender rows · {From:dd MMM yyyy} – {To:dd MMM yyyy}." + S004Note;
    protected override string ExportName => "Service money check";
    protected override (DateOnly From, DateOnly To) ExportPeriod => (From, To);
}
