extern alias EtpApplication;

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ServicePendingBoardRules = EtpApplication::Etp.Reporting.Application.Service.ServicePendingBoardRules;
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
    public const string CashBookLabel = "Cash book entry";
    public const string ManualEnteredValue = "Entered";
    public const string ManualMissingValue = "Not entered";

    private readonly DatePicker businessDate = new() { MinWidth = 150 };
    private readonly WrapPanel cards = new() { Orientation = Orientation.Horizontal, Margin = ServiceUi.Gap(-ServiceUi.S4, ServiceUi.S4, 0, ServiceUi.S4) };
    private readonly Action<ServiceDrillDown>? navigate;
    private DateOnly? chosen;
    private DateOnly? latestSnapshot;
    private DateOnly? loadedDate;

    public ServiceTodayView(Func<ServiceReportQuery> query, ServiceExcelExport export, Action<ServiceDrillDown>? navigate = null)
        : base("Service today",
            "What came in, what went out, what is waiting and whether the cash book entry was made, for the business date. The date starts at the latest Service export; select a card to open its list.",
            "Service.Today", "SERVICE_TODAY_LOAD_FAILED", query, export)
    {
        this.navigate = navigate;
        AutomationProperties.SetName(businessDate, "Business date");
        FilterBar.Children.Add(new TextBlock { Text = "Business date", VerticalAlignment = VerticalAlignment.Center, Margin = ServiceUi.Gap(0, 0, ServiceUi.S8, 0) });
        FilterBar.Children.Add(businessDate);
        var show = ServiceUi.MakeButton("Show");
        show.Margin = ServiceUi.Gap(ServiceUi.S8, 0, 0, 0);
        show.Click += async (_, _) => { chosen = businessDate.SelectedDate is { } picked ? DateOnly.FromDateTime(picked) : null; await ActivateAsync(); };
        FilterBar.Children.Add(show);
        AutomationProperties.SetName(cards, "Service today cards");
        Summary.Children.Add(cards);
        SetColumns([new("Card", Width: 200), new("Value", Width: 120), new("Detail", Width: 420)]);
        // The grid repeated the cards' values only so Export had rows (SVC-10); Export still writes it, the screen shows the cards once.
        HideGrid();
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
        if (DateOnly.TryParseExact(argument, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) BusinessDate = date;
    }

    protected override async Task<IReadOnlyList<ServiceGridRow>> LoadRowsAsync(ServiceReportQuery source)
    {
        latestSnapshot = Refreshes.Count == 0 ? null : Refreshes.Max(refresh => refresh.SnapshotDate);
        // R-UI-16: a date picked but not yet shown (Refresh instead of Show) is the date to load, so the picker and the cards agree.
        if (businessDate.SelectedDate is { } picked && DateOnly.FromDateTime(picked) != loadedDate) chosen = DateOnly.FromDateTime(picked);
        // Q15 / R-SQL-01: without a chosen date the query picks the latest day the exports hold data for (a raw pack is
        // named by its export day and holds data to the day before), never the calendar day.
        var summary = await source.LoadTodayAsync(chosen);
        loadedDate = summary.BusinessDate;
        businessDate.SelectedDate = summary.BusinessDate.ToDateTime(TimeOnly.MinValue);
        Cards = BuildCards(summary);
        cards.Children.Clear();
        foreach (var card in Cards) cards.Children.Add(CreateCardButton(card));
        return Cards.Select(card => new ServiceGridRow(card, [card.Label, card.Value, card.Detail])).ToArray();
    }

    protected override void ClearExtras() { cards.Children.Clear(); Cards = []; }

    /// <summary>
    /// The eight cards: design 3.2's seven, with the money split into the amount collected and whether the cash book entry
    /// was made (SVC-08), so a money card is never red while the money is right. Pure, so the values and targets are testable without WPF.
    /// Stripe = state only (SVC-06): <see cref="ServiceUi.StripeNone"/> is information, Success is checked and fine, Warning is look this week, Critical is act today.
    /// Sub-lines are words; export codes stay in grids and tooltips (module rule 8).
    /// </summary>
    public static IReadOnlyList<ServiceTodayCard> BuildCards(ServiceToday summary)
    {
        // Route arguments are culture-invariant (R-UI-17). Each card opens the list its number counts (design 3.2, R-UI-04).
        var date = summary.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var month = summary.BusinessDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var longDate = ServiceDisplay.Date(summary.BusinessDate);
        var manualState = summary.ManualEntered ? ManualEnteredValue : ManualMissingValue;
        var manualDetail = summary.ManualEntered
            ? ServiceDisplay.Text($"Service cash, card and UPI for {longDate} ({summary.ManualAmount ?? 0:N0})")
            : $"Service cash, card and UPI for {longDate}";
        return
        [
            new(BookedLabel, N(summary.BookedToday), $"this month {N(summary.BookedThisMonth)} · {N(summary.BookedThisMonthBooking)} bookings, {N(summary.BookedThisMonthQuickBilling)} quick bills",
                ServiceUi.StripeNone, new(ServiceScreens.JobsTask, ServiceJobsView.BookedOnPrefix + date)),
            new(DeliveredLabel, N(summary.DeliveredToday), $"this month {N(summary.DeliveredThisMonth)} delivered · {N(summary.RwrToday)} returned unrepaired today",
                ServiceUi.StripeNone, new(ServiceScreens.JobsTask, ServiceJobsView.DeliveredOnPrefix + date)),
            new(OnBenchLabel, N(summary.OnBench), $"{N(summary.IndentRaised)} waiting for parts · {N(summary.EddPassed)} past promised date",
                summary.EddPassed > 0 ? ServiceUi.StripeLookThisWeek : ServiceUi.StripeNone, new(ServiceScreens.PendingTask, ServiceStages.OnBench + "," + ServiceStages.IndentRaised)),
            new(ReadyLabel, N(summary.ReadyAtCentre), $"{N(summary.InTransit)} more on the way back",
                ServiceUi.StripeNone, new(ServiceScreens.PendingTask, ServiceStages.ReadyForDelivery)),
            new(CollectionLabel, summary.CollectionToday is { } collection ? collection.ToString("N0", ServiceDisplay.Culture) : ServiceScreenView.NotApplicable,
                summary.CollectionToday is null ? "no collection reported for the date" : "cash, card and UPI collected at the Service Centre",
                ServiceUi.StripeNone, new(ServiceScreens.MoneyTask, date)),
            new(CashBookLabel, manualState, manualDetail,
                summary.ManualEntered ? ServiceUi.StripeFine : ServiceUi.StripeActToday, new(ServiceScreens.MoneyTask, date)),
            new(Over15Label, N(summary.JobsOver15Days), "open jobs booked more than 15 days ago", summary.JobsOver15Days > 0 ? ServiceUi.StripeLookThisWeek : ServiceUi.StripeNone,
                new(ServiceScreens.PendingTask, ServicePendingBoardRules.Over15Days)),
            new(ClaimsLabel, N(summary.ClaimsRaisedThisMonth),
                (summary.ClaimsValueThisMonth is { } value ? ServiceDisplay.Text($"{value:N0} net incl. tax · ") : "") + "documents raised to the brand · settlement not known",
                ServiceUi.StripeNone, new(ServiceScreens.ClaimsTask, month))
        ];
    }

    /// <summary>The words each card's footer says: where a click goes.</summary>
    public static string OpenText(ServiceDrillDown target) =>
        target.TaskId == ServiceScreens.PendingTask ? "Open board"
        : target.TaskId == ServiceScreens.MoneyTask ? "Open money check"
        : target.TaskId == ServiceScreens.ClaimsTask ? "Open claims" : "Open list";

    /// <summary>Fixed width of a card, so a row of cards lines up (SVC-09).</summary>
    public const double CardWidth = 250;

    private Button CreateCardButton(ServiceTodayCard card)
    {
        var face = new StackPanel();
        face.Children.Add(new KpiCard(card.Label, card.Value, card.Detail, card.Accent) { Margin = ServiceUi.Gap(0) });
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = ServiceUi.Gap(ServiceUi.S12, ServiceUi.S4, 0, 0) };
        footer.Children.Add(ServiceUi.Text(OpenText(card.Target), "CaptionText", "Accent"));
        if (System.Windows.Application.Current?.TryFindResource("IconChevronRight") is Geometry chevron)
        {
            var icon = new System.Windows.Shapes.Path { Data = chevron, Width = 20, Height = 20, StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Accent");
            footer.Children.Add(icon);
        }
        face.Children.Add(footer);
        var button = new Button { Content = face, Tag = card, Width = CardWidth, Margin = ServiceUi.Gap(ServiceUi.S4), Cursor = System.Windows.Input.Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        // K3: the TileButton style owns the border at rest and the Accent hover/focus border; without it (older theme) the button is flat.
        if (System.Windows.Application.Current?.TryFindResource("TileButton") is Style tile) button.Style = tile;
        else { button.Padding = ServiceUi.Gap(0); button.BorderThickness = ServiceUi.Gap(0); button.Background = Brushes.Transparent; }
        AutomationProperties.SetName(button, $"{card.Label}: {card.Value}. {card.Detail}. Open the list.");
        button.Click += (_, _) => navigate?.Invoke(card.Target);
        return button;
    }

    private static string N(int value) => ServiceDisplay.Whole(value);

    protected override string EmptyRowsText => "No Service Today values.";

    protected override string Summarise(int count)
    {
        var date = BusinessDate ?? latestSnapshot;
        var quiet = Cards.Count > 0 && Cards.Take(2).All(card => card.Value == "0");
        var prefix = quiet ? ServiceDisplay.Text($"Nothing booked or delivered on {date:dd MMM yyyy}.") : ServiceDisplay.Text($"Counts for {date:dd MMM yyyy}.");
        if (latestSnapshot is null) return prefix;
        if (date == latestSnapshot) return prefix + " This is the latest Service export.";
        if (chosen is null && date < latestSnapshot)
            return prefix + ServiceDisplay.Text($" This is the latest day the Service exports hold (latest export {latestSnapshot:dd MMM yyyy}).");
        return date > latestSnapshot
            ? prefix + ServiceDisplay.Text($" The latest Service export is {latestSnapshot:dd MMM yyyy}; no file covers this date yet.")
            : prefix + ServiceDisplay.Text($" Latest Service export {latestSnapshot:dd MMM yyyy}.");
    }

    protected override string ExportName => "Service today";
    protected override (DateOnly From, DateOnly To) ExportPeriod
    {
        get { var date = BusinessDate ?? latestSnapshot ?? DateOnly.FromDateTime(DateTime.Today); return (date, date); }
    }
}

