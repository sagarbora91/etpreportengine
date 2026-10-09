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
/// The four claim types of <c>v_service_claims</c> (1.10.0 design 3.5). The code is what the Claims screen filters
/// on; the label is what it shows. GPRC cell = S023 + S041, Module Bank = S024, WDC = S025 + S039, WRA = S026 + S040;
/// where an old and a new header family hold the same document number the new-header family wins (Q12).
/// </summary>
public static class ServiceClaimTypes
{
    public const string GprcCell = "GPRC";
    public const string ModuleBank = "MODULE_BANK";
    public const string Wdc = "WDC";
    public const string Wra = "WRA";

    public static IReadOnlyList<string> All { get; } = [GprcCell, ModuleBank, Wdc, Wra];

    public static string Label(string code) => code switch
    {
        GprcCell => "GPRC cell", ModuleBank => "Module Bank", Wdc => "WDC", Wra => "WRA", _ => code
    };
}

/// <summary>
/// One claim line from <c>v_service_claims</c>: a line of a claim document raised with Titan. <c>ClaimDate</c> is the
/// document's transaction date, <c>ReportCode</c> the family the line came from after the union, <c>SnapshotDate</c>
/// that family's reading date. Claims are "raised" only: no export carries settlement (design 1.7, Q9 = A).
/// </summary>
public sealed record ServiceClaimLine(
    string ClaimType,
    DateOnly ClaimDate,
    string DocumentNumber,
    string? JobOrderNumber,
    string? ItemId,
    decimal? Quantity,
    decimal? NetAmountIncTax,
    decimal? UcpValue,
    string ReportCode,
    DateOnly SnapshotDate);

/// <summary>
/// A DC (S014) or RA (S016) job with no claim document in any claim family: a claim not yet raised. <c>ClaimType</c>
/// is the type due (WDC for a DC job, WRA for an RA job); <c>IssuedDate</c> and <c>IssuedReference</c> are the job's
/// DC/RA date and number; <c>SnapshotDate</c> is the status view's reading date, from which "days since" is counted.
/// </summary>
public sealed record ServiceUnclaimedJob(
    string JobOrderNumber,
    string StatusView,
    string StatusLabel,
    string ClaimType,
    DateOnly? IssuedDate,
    string? IssuedReference,
    string? Brand,
    string? Model,
    DateOnly SnapshotDate);

/// <summary>The Claims screen's read: every claim line after the union rule and the DC/RA jobs not yet claimed.</summary>
public sealed record ServiceClaims(IReadOnlyList<ServiceClaimLine> Lines, IReadOnlyList<ServiceUnclaimedJob> NotYetClaimed);

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
    /// Claims raised with Titan (<c>v_service_claims</c>, union by document number) and the DC/RA jobs without a claim
    /// document (1.10.0 design 3.5). The default refuses so a build whose query predates 1.10.0 says so on the screen;
    /// lane sql's query overrides it.
    /// </summary>
    Task<ServiceClaims> LoadClaimsAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<ServiceClaims>(new InvalidOperationException("The Service claims read is not available in this build."));
}
