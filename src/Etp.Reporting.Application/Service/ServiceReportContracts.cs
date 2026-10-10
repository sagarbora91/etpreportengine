namespace Etp.Reporting.Application.Service;

// Service Centre interim (S-2, decision 15, 3 Oct 2026): the read contract over the 0048 C_SERVICE_READ views.
// Lane L4 implements it (SqlServerServiceReportQuery); lane L5's four read-only screens consume it.
// Privacy: no customer phone, e-mail or address field anywhere; the customer name only where a screen shows it.
// A "reading" is one imported Service file; its SnapshotDate is the folder date (consolidated workbook) or the
// window end in the file name (raw export). Rows are chosen by snapshot date, never by import order.

/// <summary>How a job's membership of a list changed between readings. Information only: never a review item.</summary>
public enum ServiceJobEventKind { FirstSeen, StillListed, LeftList, Reappeared }

/// <summary>The list keys <see cref="IServiceReportQuery.LoadPendingAsync"/> accepts.</summary>
public static class ServicePendingLists
{
    /// <summary>S009 PendingRepair, latest state snapshot.</summary>
    public const string PendingRepair = "PENDING_REPAIR";

    /// <summary>S010 PendingDelivery, latest state snapshot.</summary>
    public const string PendingDelivery = "PENDING_DELIVERY";

    /// <summary>S011 SRNStatusReport, latest reading per job.</summary>
    public const string SrnStatus = "SRN_STATUS";

    public static IReadOnlyList<string> All { get; } = [PendingRepair, PendingDelivery, SrnStatus];
}

/// <summary>One reading of one Service family (the refresh log).</summary>
public sealed record ServiceRefresh(string ReportCode, DateOnly SnapshotDate, long Rows, long ImportFileId, DateTime ImportedAtUtc);

/// <summary>
/// One job's current status. <c>StatusView</c> is the report code of the status view (S014-S018, S031-S035) and
/// <c>StatusLabel</c> its label (DC, IR, RA, RWR, DELIVERED, PD, PR, SRN, REPAIRED, SRNINV). Money is summed over the
/// line rows of the winning reading; <c>Lines</c> counts them; <c>InOtherLists</c> is how many other status views ever held the job.
/// </summary>
public sealed record ServiceJobRow(
    string JobOrderNumber,
    string StatusView,
    string StatusLabel,
    DateOnly? JobDate,
    DateOnly? Edd,
    string? Brand,
    string? Model,
    string? ProductCategory,
    string? CustomerName,
    decimal? SpareValue,
    decimal? LabourCharge,
    int Lines,
    DateOnly SnapshotDate,
    int InOtherLists);

/// <summary>A job on a pending list. <c>List</c> is one of <see cref="ServicePendingLists"/>; <c>AgeDays</c> = snapshot date - job date.</summary>
public sealed record ServicePendingRow(
    string List,
    string JobOrderNumber,
    DateOnly? JobDate,
    int? AgeDays,
    string? Brand,
    string? Model,
    string? CustomerName,
    string? PendingStore,
    DateOnly SnapshotDate);

/// <summary>
/// One step of a job's history on one list. <c>LeftList</c> is dated at the first reading without the job, with the
/// last reading that held it in <c>PreviousSnapshotDate</c> ("left the list on or before <c>SnapshotDate</c>").
/// </summary>
public sealed record ServiceJobEvent(
    string JobOrderNumber,
    string ReportCode,
    string ListLabel,
    ServiceJobEventKind EventKind,
    DateOnly SnapshotDate,
    DateOnly? PreviousSnapshotDate);

/// <summary>
/// S004 tender amount for one business date and tender beside the manual SERVICE_CASH/CARD/UPI entry.
/// <c>ManualStores</c> lists the store codes whose manual entries were compared: since decision 16 (Q1) only the shop
/// that enters the Service centre's money; entries at other shops are <see cref="ServiceUnmatchedMoneyEntry"/> rows.
/// The rules are <see cref="ServiceMoneyCheck"/>.
/// </summary>
public sealed record ServiceMoneyDay(
    DateOnly BusinessDate,
    string Tender,
    decimal? S004Amount,
    decimal? ManualAmount,
    decimal? Difference,
    IReadOnlyList<string> ManualStores);

/// <summary>A business date whose S003 or S004 total changed between the previous covering reading and the winning one.</summary>
public sealed record ServiceMoneyChange(
    DateOnly BusinessDate,
    string ReportCode,
    DateOnly PreviousSnapshotDate,
    decimal PreviousAmount,
    DateOnly CurrentSnapshotDate,
    decimal CurrentAmount);


// 1.10.0 Service UI wave (design SERVICE-CENTRE-UI-DESIGN-REVIEW-2026-10-10.md, sections 3.2 and 3.7; decision 25).
// Lane shell-today wrote these records at the contract path because lane sql's contract file was not yet committed;
// the coordinator reconciles them at merge (110-LANE-BRIEF.md rule 6).

/// <summary>
/// The stage names of <c>dbo.v_service_job</c> (design 4.2, first rule wins). The Service Today cards and the Pending
/// board name their drill-down targets by stage; a screen maps a stage to the list it can show.
/// </summary>
public static class ServiceStages
{
    public const string Delivered = "DELIVERED";
    public const string ReturnedWithoutRepair = "RWR";
    public const string DcIssued = "DC_ISSUED";
    public const string RaIssued = "RA_ISSUED";
    public const string InTransitBack = "IN_TRANSIT_BACK";
    public const string ReadyForDelivery = "READY_FOR_DELIVERY";
    public const string SrnOut = "SRN_OUT";
    public const string IndentRaised = "INDENT_RAISED";
    public const string OnBench = "ON_BENCH";
    public const string Booked = "BOOKED";

    /// <summary>Stage order of the Pending board (design 3.3), open stages first, then the closed ones.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Booked, OnBench, IndentRaised, SrnOut, ReadyForDelivery, InTransitBack, DcIssued, RaIssued, ReturnedWithoutRepair, Delivered];
}

/// <summary>Source kinds of a Service reading, as <c>dbo.v_service_readings.source_kind</c> names them.</summary>
public static class ServiceSourceKinds
{
    public const string Consolidated = "CONSOLIDATED";
    public const string Raw = "RAW";
}

/// <summary>
/// The latest reading of one Service family (design 3.7, the freshness strip): its snapshot date, source kind
/// (<see cref="ServiceSourceKinds"/>; empty when unknown) and import time. One row per family that has a reading.
/// </summary>
public sealed record ServiceFamilyFreshness(string ReportCode, DateOnly SnapshotDate, string SourceKind, long Rows, DateTime ImportedAtUtc);

/// <summary>
/// Service Today (design 3.2): the counts for one business date and its calendar month, read from <c>dbo.v_service_job</c>,
/// <c>dbo.v_service_s004_daily</c>, <c>dbo.v_service_manual_money</c> and <c>dbo.v_service_claims</c>. "Today" means the
/// business date asked for, which defaults to the latest Service snapshot date (Q15), never the calendar day.
/// Booked counts S002 <c>created_date</c> split by job type (Q2); delivered counts S018 <c>deliverydate</c>; RWR counts S017.
/// On the bench is the ON_BENCH + INDENT_RAISED stages (of which indent raised, of which EDD passed); in transit and ready
/// for delivery are their stages at the latest snapshot. <c>CollectionToday</c> is the S004 cash + card + UPI total for
/// the date (null when the date has no S004 row); <c>ManualMoneyToday</c> is the Service-money shop's manual
/// SERVICE_CASH/CARD/UPI total for the date (null when nothing was entered: "manual entry: not entered").
/// </summary>
public sealed record ServiceTodaySummary(
    DateOnly BusinessDate,
    int BookedToday,
    int BookedTodayBooking,
    int BookedTodayQuickBilling,
    int BookedMonth,
    int BookedMonthBooking,
    int BookedMonthQuickBilling,
    int DeliveredToday,
    int DeliveredMonth,
    int RwrToday,
    int RwrMonth,
    int OnBench,
    int OnBenchIndentRaised,
    int OnBenchEddPassed,
    int InTransitBack,
    int ReadyForDelivery,
    decimal? CollectionToday,
    decimal? ManualMoneyToday,
    int OpenOver15Days,
    int ClaimsRaisedMonth);

public interface IServiceReportQuery
{
    /// <summary>Every Service reading, newest first.</summary>
    Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default);

    /// <summary>One row per job; <paramref name="statusView"/> is a status view report code (S014-S018, S031-S035), or null for all.</summary>
    Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default);

    /// <summary><paramref name="list"/> is one of <see cref="ServicePendingLists"/>.</summary>
    Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default);

    /// <summary>The decision-16 money check (<see cref="ServiceMoneyCheck.Compare"/>) for billing dates in the range.</summary>
    Task<IReadOnlyList<ServiceMoneyDay>> LoadMoneyCheckAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Manual Service entries made at a shop other than the Service-money shop, listed separately and never added to the
    /// check (<see cref="ServiceMoneyCheck.Unmatched"/>, decision 16 Q1). The default lists none, so an implementation
    /// written before decision 16 still compiles; lane L4's query overrides it.
    /// </summary>
    Task<IReadOnlyList<ServiceUnmatchedMoneyEntry>> LoadUnmatchedServiceEntriesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ServiceUnmatchedMoneyEntry>>([]);

    Task<IReadOnlyList<ServiceMoneyChange>> LoadMoneyChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The latest reading of every Service family, for the freshness strip (design 3.7). The default derives it from
    /// <see cref="LoadRefreshesAsync"/> with an unknown source kind, so a query written before 1.10.0 still compiles;
    /// lane sql's query reads <c>dbo.v_service_readings</c> (<c>is_latest = 1</c>) and fills the source kind.
    /// </summary>
    async Task<IReadOnlyList<ServiceFamilyFreshness>> LoadFreshnessAsync(CancellationToken cancellationToken = default)
    {
        var refreshes = await LoadRefreshesAsync(cancellationToken);
        return refreshes.GroupBy(refresh => refresh.ReportCode, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(refresh => refresh.SnapshotDate).ThenByDescending(refresh => refresh.ImportFileId).First())
            .Select(latest => new ServiceFamilyFreshness(latest.ReportCode, latest.SnapshotDate, "", latest.Rows, latest.ImportedAtUtc))
            .OrderBy(row => row.ReportCode, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Service Today (design 3.2) for <paramref name="businessDate"/>. Until the 1.10.0 read model (migration 0050,
    /// lane sql) implements it, the default says so through the screen's friendly error; it never returns zeros.
    /// </summary>
    Task<ServiceTodaySummary> LoadTodayAsync(DateOnly businessDate, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Service Today needs the 1.10.0 Service read model (migration 0050), which this build does not include yet.");
}
