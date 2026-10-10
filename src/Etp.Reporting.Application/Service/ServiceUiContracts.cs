namespace Etp.Reporting.Application.Service;

// Service Centre UI wave (1.10.0, decision 25, 10 Oct 2026): the read contract over the 0050 views
// (v_service_job, v_service_job_timeline, v_service_claims, v_service_parts and the amended v_service_pending_current).
// Lane sql owns this file and SqlServerServiceReportQuery; the UI lanes copy it verbatim (same path, same content) and
// fake IServiceReportQuery against it. Design: docs/roadmap/SERVICE-CENTRE-UI-DESIGN-REVIEW-2026-10-10.md sections 3, 4, 6.
// Privacy: no phone, e-mail or address anywhere; the customer name only where the Job history header shows it.
// Every new member of the interface has a default so a query or fake written before 1.10.0 still compiles
// (the decision-16 precedent); SqlServerServiceReportQuery overrides them all.
// The pure rules (age bands, overdue, TAT median, freshness colours) are ServiceUiRules.cs beside this file.

/// <summary>The lifecycle stages of dbo.v_service_job (design 4.2), as the view spells them, in lifecycle order.</summary>
public static class ServiceStages
{
    public const string Booked = "BOOKED";
    public const string OnBench = "ON_BENCH";
    public const string IndentRaised = "INDENT_RAISED";
    public const string SrnOut = "SRN_OUT";
    public const string ReadyForDelivery = "READY_FOR_DELIVERY";
    public const string InTransitBack = "IN_TRANSIT_BACK";
    public const string DcIssued = "DC_ISSUED";
    public const string RaIssued = "RA_ISSUED";
    public const string Rwr = "RWR";
    public const string Delivered = "DELIVERED";

    /// <summary>Board order (design 3.3): booked first, delivered last.</summary>
    public static IReadOnlyList<string> Order { get; } =
        [Booked, OnBench, IndentRaised, SrnOut, ReadyForDelivery, InTransitBack, DcIssued, RaIssued, Rwr, Delivered];

    /// <summary>The stages the Pending board never shows (Q3/Q8).</summary>
    public static IReadOnlyList<string> Closed { get; } = [Delivered, Rwr];

    public static string Label(string stage) => stage switch
    {
        Booked => "Booked, no status yet",
        OnBench => "On the bench",
        IndentRaised => "Indent raised, parts awaited",
        SrnOut => "SRN out for repair",
        ReadyForDelivery => "Repaired, awaiting delivery",
        InTransitBack => "Sent back after repair, in transit",
        DcIssued => "DC issued",
        RaIssued => "RA issued",
        Rwr => "Returned without repair",
        Delivered => "Delivered",
        _ => stage,
    };

    public static int Rank(string stage) { var i = Order.IndexOf(stage); return i < 0 ? Order.Count : i; }
}

/// <summary>S002 jotype_booking_quickbilling as the export spells it (Q2): everything that is not Quick Billing is Booking.</summary>
public static class ServiceJobTypes
{
    public const string Booking = "Booking";
    public const string QuickBilling = "Quick Billing";

    public static bool IsQuickBilling(string? exported) =>
        exported is not null && exported.Replace(" ", "").Replace("_", "").Equals("QuickBilling", StringComparison.OrdinalIgnoreCase);

    public static string Normalise(string? exported) => IsQuickBilling(exported) ? QuickBilling : Booking;
}

/// <summary>The claim types of dbo.v_service_claims (design 3.5): GPRC cell, Module Bank, WDC, WRA.</summary>
public static class ServiceClaimTypes
{
    public const string Gprc = "GPRC";
    public const string ModuleBank = "MB";
    public const string Wdc = "WDC";
    public const string Wra = "WRA";
    public static IReadOnlyList<string> All { get; } = [Gprc, ModuleBank, Wdc, Wra];

    public static string Label(string claimType) => claimType switch
    {
        Gprc => "GPRC cell", ModuleBank => "Module Bank", Wdc => "WDC (depreciation)", Wra => "WRA (replacement)", _ => claimType,
    };
}

/// <summary>
/// One row of dbo.v_service_job: one Service job with its stage (design 4.2), dates, money and ageing (4.3). Dates are the
/// family's own dates; <c>AsAt</c> is the latest Service snapshot date, against which <c>AgeDays</c> and <c>DaysInStage</c>
/// are counted for open jobs; <c>TatDays</c> (booking to delivered or RWR) and <c>TatRepairDays</c> (booking to repaired)
/// are set for closed jobs only. <c>IsOpen</c> is false for DELIVERED, RWR and a DC/RA job whose claim was raised (Q3).
/// <c>OverdueBy</c> is the full rule (Q4): EDD passed, else days in stage over the stage limit; null when not overdue.
/// </summary>
public sealed record ServiceJobSummary(
    string JobOrderNumber,
    DateOnly? BookingDate,
    string JoType,
    bool IsQuickBilling,
    string? ExportedStatus,
    string? Brand,
    string? Model,
    string? ProductCategory,
    string? CustomerName,
    string? Guarantee,
    string? CustomerType,
    DateOnly? Edd,
    string Stage,
    DateOnly? StageDate,
    string? PendingAt,
    string? SpareRequired,
    DateOnly? IndentDate,
    DateOnly? SrnDate,
    string? SrnToStore,
    DateOnly? RepairDate,
    DateOnly? DeliveryDate,
    DateOnly? RwrDate,
    string? RwrReason,
    DateOnly? WdcDate,
    string? WdcNumber,
    DateOnly? WraDate,
    string? RadcNumber,
    bool ClaimRaised,
    decimal? SpareValue,
    decimal? LabourCharge,
    decimal? RevenueLabourCharge,
    decimal? RevenueSpareCharge,
    decimal? RevenueNetInclTax,
    int RevenueDocuments,
    int? TatDays,
    int? TatRepairDays,
    int? AgeDays,
    int? DaysInStage,
    int? OverdueBy,
    bool IsOpen,
    DateOnly? LastReadingDate,
    DateOnly AsAt)
{
    public string StageLabel => ServiceStages.Label(Stage);
    public string? AgeBand => ServiceAgeing.Band(DaysInStage);
}

/// <summary>Service Today (design 3.2) for one business date; counts come from v_service_job, money from the S004/manual views.</summary>
public sealed record ServiceToday(
    DateOnly BusinessDate,
    DateOnly? AsAt,
    int BookedToday,
    int BookedTodayBooking,
    int BookedTodayQuickBilling,
    int BookedThisMonth,
    int BookedThisMonthBooking,
    int BookedThisMonthQuickBilling,
    int DeliveredToday,
    int DeliveredThisMonth,
    int RwrToday,
    int OnBench,
    int IndentRaised,
    int EddPassed,
    int InTransit,
    int ReadyAtCentre,
    decimal? CollectionToday,
    decimal? CollectionCash,
    decimal? CollectionCard,
    decimal? CollectionUpi,
    bool ManualEntered,
    decimal? ManualAmount,
    int JobsOver15Days,
    int ClaimsRaisedThisMonth,
    decimal? ClaimsValueThisMonth);

/// <summary>One open job on the Pending board (design 3.3), grouped by <c>Stage</c> in <see cref="ServiceStages.Order"/>.</summary>
public sealed record ServicePendingBoardRow(
    string JobOrderNumber,
    string Stage,
    DateOnly? BookingDate,
    int? DaysSinceBooking,
    int? DaysInStage,
    DateOnly? Edd,
    int? OverdueBy,
    string? Brand,
    string? Model,
    string? ProductCategory,
    string? Guarantee,
    string? CustomerType,
    string? PendingAt,
    string? SpareRequired,
    string JoType,
    bool IsQuickBilling,
    bool ClaimRaised,
    DateOnly? LastReadingDate)
{
    public string StageLabel => ServiceStages.Label(Stage);
    public string? AgeBand => ServiceAgeing.Band(DaysInStage);
    public bool IsOverdue => OverdueBy is > 0;
}

/// <summary>The Pending board: its rows (stage order, then days in stage descending) and the numbers on the screen.</summary>
public sealed record ServicePendingBoard(
    IReadOnlyList<ServicePendingBoardRow> Rows,
    int OpenJobs,
    int Overdue,
    int Over30Days,
    int InTransit,
    int PartsAwaited,
    DateOnly? AsAt);

/// <summary>
/// One row of dbo.v_service_job_timeline: one reading of one family that holds the job, with the family's own event date,
/// the status text as exported, the pending store, the document number and the amount where a money column exists.
/// </summary>
public sealed record ServiceJobTimelineRow(
    string JobOrderNumber,
    DateOnly SnapshotDate,
    string SourceKind,
    string ReportCode,
    string ListLabel,
    DateOnly? EventDate,
    string? StatusText,
    string? PendingStore,
    string? DocumentNumber,
    decimal? Amount,
    int Lines);

/// <summary>Job history (design 3.4): the header card, the timeline newest first and the job's claim lines. <c>Header</c> is null when no family holds the job.</summary>
public sealed record ServiceJobDetail(
    string JobOrderNumber,
    ServiceJobSummary? Header,
    IReadOnlyList<ServiceJobTimelineRow> Timeline,
    IReadOnlyList<ServiceClaimLine> Claims);

/// <summary>Jobs list (design 3.4): open jobs plus jobs closed since <c>ClosedSince</c>, with the TAT summary of the closed ones.</summary>
public sealed record ServiceJobList(IReadOnlyList<ServiceJobSummary> Rows, ServiceTatSummary Tat, DateOnly? ClosedSince, DateOnly? AsAt);

/// <summary>TAT of closed jobs (Q2): Booking and Quick Billing apart; medians of booking to delivered and booking to repaired.</summary>
public sealed record ServiceTatSummary(
    int ClosedJobs,
    int BookingJobs,
    int QuickBillingJobs,
    double? BookingMedianDays,
    double? BookingRepairMedianDays,
    double? BookingAverageDays,
    double? QuickBillingMedianDays,
    int Over15Days);

/// <summary>One line of dbo.v_service_claims. <c>ClaimMonth</c> is the first day of the business date's month.</summary>
public sealed record ServiceClaimLine(
    string ClaimType,
    DateOnly BusinessDate,
    string? DocumentNumber,
    string? JobOrderNumber,
    string? ItemId,
    decimal? Quantity,
    decimal? NetAmountInclTax,
    decimal? UcpValue,
    string? AccountNumber,
    string ReportCode,
    DateOnly SnapshotDate)
{
    public DateOnly ClaimMonth => new(BusinessDate.Year, BusinessDate.Month, 1);
}

/// <summary>Claims by month and type (design 3.5): documents, lines, jobs, net incl. tax and UCP value.</summary>
public sealed record ServiceClaimsSummaryRow(DateOnly ClaimMonth, string ClaimType, int Documents, int Lines, int Jobs, decimal NetAmountInclTax, decimal UcpValue);

/// <summary>A DC or RA job with no claim document yet ("not yet claimed"): <c>ClaimType</c> is WDC for DC, WRA for RA.</summary>
public sealed record ServiceUnclaimedJob(
    string JobOrderNumber,
    string Stage,
    string ClaimType,
    DateOnly? StageDate,
    int? DaysSince,
    string? Brand,
    string? Model,
    string? DcNumber);

/// <summary>
/// The Claims screen (raised only, Q9 = A): the summary, every line in the range, the DC/RA jobs not yet claimed and the
/// GPRC gap warning (the latest GPRC reading is older than the latest DC/RA status reading).
/// </summary>
public sealed record ServiceClaims(
    IReadOnlyList<ServiceClaimsSummaryRow> Summary,
    IReadOnlyList<ServiceClaimLine> Lines,
    IReadOnlyList<ServiceUnclaimedJob> NotYetClaimed,
    bool GprcGapWarning,
    DateOnly? LatestGprcReading,
    DateOnly? LatestDcRaReading,
    DateOnly? AsAt);

/// <summary>One line (item) of a purchase invoice, from S007 (created) and S008 (received).</summary>
public sealed record ServicePartsLine(string? ItemId, decimal? ShippedQuantity, decimal? ReceivedQuantity, decimal? NetAmount, string? GrnNumber, DateOnly? GrnDate);

/// <summary>
/// One purchase invoice (design 3.6): <c>Status</c> is Open or Received; <c>DaysOpen</c> is snapshot minus invoice date while
/// open, received minus invoice date once received. Lines are the invoice's items.
/// </summary>
public sealed record ServicePartsInvoice(
    string InvoiceNumber,
    DateOnly? InvoiceDate,
    string? GrnNumber,
    DateOnly? GrnDate,
    DateOnly? ReceivedDate,
    int Items,
    decimal? ShippedQuantity,
    decimal? ReceivedQuantity,
    decimal? NetAmount,
    string Status,
    int? DaysOpen,
    string? FromLocation,
    DateOnly SnapshotDate,
    IReadOnlyList<ServicePartsLine> Lines)
{
    public bool IsOpen => Status == "Open";
}

/// <summary>One S013 goods-in-transit line (spares), item level.</summary>
public sealed record ServiceGitLine(string? StmNumber, DateOnly BusinessDate, string? ItemId, decimal? QuantityShipped, string? FromLocation, string? ToLocation, decimal? Ucp, DateOnly SnapshotDate);

/// <summary>The latest S006 closing stock as count and value only (no item detail on the screen).</summary>
public sealed record ServiceStockSummary(DateOnly SnapshotDate, int Items, decimal? Quantity, decimal? Value);

/// <summary>A job waiting for parts (stage INDENT_RAISED): the part the S009 export names and the days since the indent.</summary>
public sealed record ServiceWaitingJob(string JobOrderNumber, string? SpareRequired, DateOnly? IndentDate, int? DaysWaiting, string? Brand, string? Model, string? PendingAt);

/// <summary>The Parts screen (design 3.6): invoices (open first, oldest first), GIT, stock summary, jobs waiting for parts and the numbers.</summary>
public sealed record ServiceParts(
    IReadOnlyList<ServicePartsInvoice> Invoices,
    IReadOnlyList<ServiceGitLine> Git,
    ServiceStockSummary? Stock,
    IReadOnlyList<ServiceWaitingJob> WaitingJobs,
    int OpenInvoices,
    decimal OpenValue,
    int? OldestOpenDays,
    int ReceivedThisMonth,
    int JobsWaiting,
    int GitLinesLast30Days,
    DateOnly? AsAt);

public enum ServiceFreshnessColour { NoData, Fresh, Amber, Red }

/// <summary>
/// One chip of the freshness strip (design 3.7): a family group, its latest snapshot date (the oldest of the group's
/// families, so a stale family shows), the source kind of that reading and the colour (amber over 7 days, red over 14, Q14).
/// </summary>
public sealed record ServiceFreshnessChip(string Group, IReadOnlyList<string> ReportCodes, DateOnly? LatestSnapshotDate, string? SourceKind, ServiceFreshnessColour Colour, string Text);

public partial interface IServiceReportQuery
{
    /// <summary>Service Today for <paramref name="businessDate"/>, or for the latest Service snapshot date when null (Q15).</summary>
    Task<ServiceToday> LoadTodayAsync(DateOnly? businessDate = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServiceToday(businessDate ?? DateOnly.MinValue, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null, null, null, false, null, 0, 0, null));

    /// <summary>Every open job (v_service_job where is_open = 1) in stage order, then days in stage descending.</summary>
    Task<ServicePendingBoard> LoadPendingBoardAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServicePendingBoard([], 0, 0, 0, 0, 0, null));

    /// <summary>One job: header, timeline (newest snapshot first, then event date descending) and claim lines. Any job number, trimmed.</summary>
    Task<ServiceJobDetail> LoadJobAsync(string jobOrderNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServiceJobDetail(jobOrderNumber?.Trim() ?? "", null, [], []));

    /// <summary>Open jobs plus jobs closed on or after <paramref name="closedSince"/> (null: all), with the TAT summary (Q2, Q8).</summary>
    Task<ServiceJobList> LoadJobListAsync(DateOnly? closedSince, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServiceJobList([], ServiceTat.Summarise([]), closedSince, null));

    /// <summary>Claims raised with a business date in [<paramref name="from"/>, <paramref name="to"/>] (null bounds are open), plus the not-yet-claimed DC/RA jobs.</summary>
    Task<ServiceClaims> LoadClaimsAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServiceClaims([], [], [], false, null, null, null));

    /// <summary>The Parts screen.</summary>
    Task<ServiceParts> LoadPartsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ServiceParts([], [], null, [], 0, 0m, null, 0, 0, 0, null));

    /// <summary>The freshness strip, one chip per family group, from the refresh log (no SQL of its own, design 3.7). <paramref name="asOf"/> defaults to today.</summary>
    async Task<IReadOnlyList<ServiceFreshnessChip>> LoadFreshnessAsync(DateOnly? asOf = null, CancellationToken cancellationToken = default) =>
        ServiceFreshness.Build(await LoadRefreshesAsync(cancellationToken), asOf ?? DateOnly.FromDateTime(DateTime.Today));
}
