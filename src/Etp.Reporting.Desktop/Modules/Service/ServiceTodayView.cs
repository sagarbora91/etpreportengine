extern alias EtpApplication;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ServiceReportQuery = EtpApplication::Etp.Reporting.Application.Service.IServiceReportQuery;
using ServiceStages = EtpApplication::Etp.Reporting.Application.Service.ServiceStages;
using ServiceToday = EtpApplication::Etp.Reporting.Application.Service.ServiceToday;

namespace Etp.Reporting.Desktop.Modules.Service;

/// <summary>One Service Today card: label, the number, the line under it, the stripe colour and where it drills to.</summary>
public sealed record ServiceTodayCard(string Label, string Value, string Detail, string Accent, ServiceDrillDown Target);

/// <summary>
/// Service Today (design 3.2): the morning view. What came in (Booking vs Quick Billing), what went out (with RWR),
/// what is on the bench, what is ready for delivery and whether the Service money was entered, for one business date
/// and its month. The date defaults to the latest Service snapshot date, never the calendar day (Q15), so a weekend
/// never shows zeros. Every card opens the matching Pending group or Jobs filter; the grid under the cards holds the
/// same values, so Export writes the cards as one sheet.
/// </summary>
public sealed class ServiceTodayView : ServiceScreenView, IServiceDrillDownTarget
{
    public const string BookedLabel = "Booked";
    public const string DeliveredLabel = "Delivered";
    public const string OnBenchLabel = "On the bench";
    public const string ReadyLabel = "Ready for delivery";
    public const string CollectionLabel = "Collection";
    public const string Over15Label = "Open over 15 days";
    public const string ClaimsLabel = "Claims raised this month";
    public const string ManualEnteredText = "manual entry: entered";
    public const string ManualMissingText = "manual entry: not entered";

    private readonly DatePicker businessDate = new() { MinHeight = 44, MinWidth = 150 };
    private readonly WrapPanel cards = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
    private readonly Action<ServiceDrillDown>? navigate;
    private DateOnly? chosen;
    private DateOnly? latestSnapshot;

    public ServiceTodayView(Func<ServiceReportQuery> query, ServiceExcelExport export, Action<ServiceDrillDown>? navigate = null)
        : base("Service today",
            "What came in, what went out, what is waiting and whether the money was entered, for the business date. The date starts at the latest Service export; select a card to open its list.",
            "Service.Today", "SERVICE_TODAY_LOAD_FAILED", query, export)
    {
        this.navigate = navigate;
        AutomationProperties.SetName(businessDate, "Business date");
        FilterBar.Children.Add(new TextBlock { Text = "Business date", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        FilterBar.Children.Add(businessDate);
        var show = new Button { Content = "Show", MinHeight = 44, Margin = new Thickness(8, 0, 0, 0) };
        show.Click += async (_, _) => { chosen = businessDate.SelectedDate is { } picked ? DateOnly.FromDateTime(picked) : null; await ActivateAsync(); };
        FilterBar.Children.Add(show);
        AutomationProperties.SetName(cards, "Service today cards");
        Summary.Children.Add(cards);
        SetColumns([new("Card", Width: 200), new("Value", Width: 120), new("Detail", Width: 420)]);
    }

    /// <summary>The business date the cards show; null until the first load picks the latest snapshot date.</summary>
    public DateOnly? BusinessDate
    {
        get => chosen ?? (businessDate.SelectedDate is { } picked ? DateOnly.FromDateTime(picked) : null);
        set { chosen = value; businessDate.SelectedDate = value?.ToDateTime(TimeOnly.MinValue); }
    }

    /// <summary>The cards of the last load, in display order.</summary>
    public IReadOnlyList<ServiceTodayCard> Cards { get; private set; } = [];

    /// <summary>The card buttons, one per card, in display order.</summary>
    public IReadOnlyList<Button> CardButtons => cards.Children.OfType<Button>().ToArray();

    /// <summary>A drill-down argument to this screen is a business date (yyyy-MM-dd).</summary>
    public void ApplyDrillDown(string argument)
    {
        if (DateOnly.TryParseExact(argument, "yyyy-MM-dd", out var date)) BusinessDate = date;
    }

    protected override async Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source)
    {
        latestSnapshot = Refreshes.Count == 0 ? null : Refreshes.Max(refresh => refresh.SnapshotDate);
        // Q15: the latest Service snapshot date, never the calendar day; the query also defaults a null date to it.
        var date = chosen ?? latestSnapshot;
        var summary = await source.LoadTodayAsync(date);
        businessDate.SelectedDate = summary.BusinessDate.ToDateTime(TimeOnly.MinValue);
        Cards = BuildCards(summary);
        cards.Children.Clear();
        foreach (var card in Cards) cards.Children.Add(CreateCardButton(card));
        return Cards.Select(card => new ServiceGridRow(card, [card.Label, card.Value, card.Detail])).ToArray();
    }

    protected override void ClearExtras() { cards.Children.Clear(); Cards = []; }

    /// <summary>The seven cards of design 3.2 from one summary. Pure, so the values and targets are testable without WPF.</summary>
    public static IReadOnlyList<ServiceTodayCard> BuildCards(ServiceToday summary)
    {
        var date = summary.BusinessDate.ToString("yyyy-MM-dd");
        var manual = summary.ManualEntered ? $"{ManualEnteredText} ({summary.ManualAmount ?? 0:N0})" : ManualMissingText;
        var collectionAccent = summary.CollectionToday is null ? "PrimaryText" : summary.ManualEntered ? "Success" : "Critical";
        return
        [
            new(BookedLabel, N(summary.BookedToday), $"Booking {N(summary.BookedTodayBooking)} · Quick Billing {N(summary.BookedTodayQuickBilling)} · this month {N(summary.BookedThisMonth)} ({N(summary.BookedThisMonthBooking)} / {N(summary.BookedThisMonthQuickBilling)})",
                "Success", new(ServiceScreens.JobsTask, ServiceStages.Booked)),
            new(DeliveredLabel, N(summary.DeliveredToday), $"RWR {N(summary.RwrToday)} · this month {N(summary.DeliveredThisMonth)} delivered",
                "Success", new(ServiceScreens.JobsTask, ServiceStages.Delivered)),
            new(OnBenchLabel, N(summary.OnBench), $"indent raised {N(summary.IndentRaised)} · EDD passed {N(summary.EddPassed)}",
                summary.EddPassed > 0 ? "Warning" : "PrimaryText", new(ServiceScreens.PendingTask, ServiceStages.OnBench)),
            new(ReadyLabel, N(summary.ReadyAtCentre), $"in transit back {N(summary.InTransit)}",
                "PrimaryText", new(ServiceScreens.PendingTask, ServiceStages.ReadyForDelivery)),
            new(CollectionLabel, summary.CollectionToday is { } collection ? collection.ToString("N0") : "—",
                summary.CollectionToday is null ? "no S004 collection for the date · " + manual : "S004 cash, card and UPI · " + manual,
                collectionAccent, new(ServiceScreens.MoneyTask, date)),
            new(Over15Label, N(summary.JobsOver15Days), "open jobs booked more than 15 days ago", summary.JobsOver15Days > 0 ? "Warning" : "PrimaryText",
                new(ServiceScreens.PendingTask)),
            new(ClaimsLabel, N(summary.ClaimsRaisedThisMonth),
                (summary.ClaimsValueThisMonth is { } value ? $"{value:N0} net incl. tax · " : "") + "GPRC, Module Bank, WDC and WRA documents raised (not settlement)",
                "PrimaryText", new(ServiceScreens.ClaimsTask))
        ];
    }

    private Button CreateCardButton(ServiceTodayCard card)
    {
        var button = new Button
        {
            Content = new KpiCard(card.Label, card.Value, card.Detail, card.Accent),
            Tag = card, Padding = new Thickness(0), Margin = new Thickness(0), BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, MinWidth = 220, MaxWidth = 340, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(button, $"{card.Label}: {card.Value}. {card.Detail}. Open the list.");
        button.Click += (_, _) => navigate?.Invoke(card.Target);
        return button;
    }

    private static string N(int value) => value.ToString("N0");

    protected override string EmptyRowsText => "No Service Today values.";

    protected override string Summarise(int count)
    {
        var date = BusinessDate ?? latestSnapshot;
        var quiet = Cards.Count > 0 && Cards.Take(2).All(card => card.Value == "0");
        var prefix = quiet ? $"Nothing booked or delivered on {date:dd MMM yyyy}." : $"Counts for {date:dd MMM yyyy}.";
        if (latestSnapshot is null) return prefix;
        if (date == latestSnapshot) return prefix + " This is the latest Service export.";
        return date > latestSnapshot
            ? prefix + $" The latest Service export is {latestSnapshot:dd MMM yyyy}; no file covers this date yet."
            : prefix + $" Latest Service export {latestSnapshot:dd MMM yyyy}.";
    }

    protected override string ExportName => "Service today";
    protected override (DateOnly From, DateOnly To) ExportPeriod
    {
        get { var date = BusinessDate ?? latestSnapshot ?? DateOnly.FromDateTime(DateTime.Today); return (date, date); }
    }
}
