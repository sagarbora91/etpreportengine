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

/// <summary>
/// The stage codes dbo.v_service_job (migration 0050, design review 4.2) gives a job; the first rule that fires wins.
/// The Pending board (design 3.3, Q3) lists the first eight; DELIVERED and RWR never reach it, and a DC/RA job with a
/// claim document is "closed by claim" and leaves it too. The board labels and limits are <see cref="ServicePendingStages"/>.
/// </summary>
public static class ServiceJobStages
{
    public const string Booked = "BOOKED";
    public const string OnBench = "ON_BENCH";
    public const string IndentRaised = "INDENT_RAISED";
    public const string SrnOut = "SRN_OUT";
    public const string InTransitBack = "IN_TRANSIT_BACK";
    public const string ReadyForDelivery = "READY_FOR_DELIVERY";
    public const string DcIssued = "DC_ISSUED";
    public const string RaIssued = "RA_ISSUED";
    public const string Delivered = "DELIVERED";
    public const string Rwr = "RWR";
}

/// <summary>
/// One open job of dbo.v_service_job for the Pending board (design review 3.3 and 4.3, 1.10.0 U1 contract).
/// <c>Stage</c> is a <see cref="ServiceJobStages"/> code; <c>StageDate</c> the date that stage started; <c>JoType</c> is
/// "Booking" or "Quick Billing" (S002, a job without an S002 row is Booking); <c>AgeDays</c> = booking date to <c>AsAt</c>
/// (equals S009 pendingnoofdays); <c>DaysInStage</c> = stage date to <c>AsAt</c>; <c>ClaimRaised</c> is true for a DC/RA
/// job that has a WDC/WRA claim document; <c>LastReadingDate</c> is the latest reading that holds the job; <c>AsAt</c> is
/// the latest Service snapshot date. Overdue and the age bands are computed in C# (<see cref="ServicePendingBoard"/>),
/// never here. No customer name, phone, e-mail or address.
/// </summary>
public sealed record ServicePendingBoardRow(
    string JobOrderNumber,
    string Stage,
    DateOnly? StageDate,
    DateOnly? BookingDate,
    string JoType,
    DateOnly? Edd,
    string? Brand,
    string? Model,
    string? ProductCategory,
    string? Guarantee,
    string? CustomerType,
    string? PendingAt,
    string? SpareRequired,
    bool ClaimRaised,
    int? AgeDays,
    int? DaysInStage,
    DateOnly? LastReadingDate,
    DateOnly AsAt);

public interface IServiceReportQuery
{
    /// <summary>Every Service reading, newest first.</summary>
    Task<IReadOnlyList<ServiceRefresh>> LoadRefreshesAsync(CancellationToken cancellationToken = default);

    /// <summary>One row per job; <paramref name="statusView"/> is a status view report code (S014-S018, S031-S035), or null for all.</summary>
    Task<IReadOnlyList<ServiceJobRow>> LoadJobsByStatusAsync(string? statusView, CancellationToken cancellationToken = default);

    /// <summary><paramref name="list"/> is one of <see cref="ServicePendingLists"/>.</summary>
    Task<IReadOnlyList<ServicePendingRow>> LoadPendingAsync(string list, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceJobEvent>> LoadJobHistoryAsync(string jobOrderNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every job of dbo.v_service_job whose stage is not DELIVERED or RWR (design review 3.3, Q3), one row per job, any
    /// order. The board itself (<see cref="ServicePendingBoard"/>) drops claimed DC/RA jobs, groups, bands and sorts.
    /// The default throws until lane sql's SqlServerServiceReportQuery (1.10.0 U1) overrides it, so the screen says why.
    /// </summary>
    Task<IReadOnlyList<ServicePendingBoardRow>> LoadPendingBoardAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The Pending board needs the 1.10.0 Service job model (migration 0050). Install the 1.10.0 update.");

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
}
