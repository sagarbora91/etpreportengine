extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using ServiceMoneyDay = EtpApplication::Etp.Reporting.Application.Service.ServiceMoneyDay;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>
/// Service money check: the Service centre's S004 tender collection per date and tender beside the
/// manual Service cash, card and UPI entries, which stay the cash-book source. Below it, the dates
/// whose money total changed since the previous refresh (information, never an approval item).
/// </summary>
public sealed class ServiceMoneyView : ServiceScreenView
{
    private readonly DatePicker fromDate = new() { MinHeight = 44, MinWidth = 140 };
    private readonly DatePicker toDate = new() { MinHeight = 44, MinWidth = 140 };
    private readonly TextBlock manualSources = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock changesStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };

    public ServiceMoneyView(Func<ServiceReportQuery> query, ServiceExcelExport export)
        : base("Service money check",
            "S004 tender collection from the Service centre beside the manual Service cash, card and UPI entries. The manual entries stay the cash-book figures; this screen only compares them.",
            "Service.Money", "SERVICE_MONEY_LOAD_FAILED", query, export)
    {
        var today = DateTime.Today;
        toDate.SelectedDate = today;
        fromDate.SelectedDate = today.AddDays(-30);
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

    protected override string? PrepareLoad() =>
        From > To ? "The From date is after the To date. Choose a valid range." : null;

    protected override async Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source)
    {
        var days = await source.LoadMoneyCheckAsync(From, To);
        manualSources.Text = DescribeManualSources(days);
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
                change.CurrentSnapshotDate, change.CurrentAmount, (change.CurrentAmount ?? 0m) - (change.PreviousAmount ?? 0m)
            ])).ToArray();
        Changes.ItemsSource = ChangeRows;
        changesStatus.Text = ChangeRows.Count == 0
            ? "No money changed since the previous refresh."
            : $"{ChangeRows.Count:N0} date{(ChangeRows.Count == 1 ? "" : "s")} changed since the previous refresh.";
    }

    protected override void ClearExtras()
    {
        ChangeRows = []; Changes.ItemsSource = null; changesStatus.Text = ""; manualSources.Text = "";
    }

    /// <summary>Which shops' manual Service entries were summed (design question Q2: all shops until Sagar decides).</summary>
    public static string DescribeManualSources(IReadOnlyList<ServiceMoneyDay> days)
    {
        var stores = Joined(days.SelectMany(day => day.ManualStores ?? []).Order(StringComparer.OrdinalIgnoreCase));
        return stores.Length == 0
            ? "Manual entries from: none in this range."
            : $"Manual entries from: {stores}. Every shop's manual Service entries are summed until the Owner names the Service centre's shop.";
    }

    protected override string EmptyRowsText => "No Service money in this date range.";
    protected override string Summarise(int count) => $"{count:N0} date and tender rows · {From:dd MMM yyyy} – {To:dd MMM yyyy}.";
    protected override string ExportName => "Service money check";
    protected override (DateOnly From, DateOnly To) ExportPeriod => (From, To);
}
