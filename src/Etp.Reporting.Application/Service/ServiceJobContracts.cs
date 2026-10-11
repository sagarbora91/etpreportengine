namespace Etp.Reporting.Application.Service;

// 1.10.0 Service UI wave (design review 10 Oct 2026, sections 3.4, 4.1-4.3; decision 25). The job model read contract
// over the 0050 views dbo.v_service_job (one row per job, stage per section 4.2) and dbo.v_service_job_timeline
// (one row per reading row of every family that holds the job). Lane history wrote these records before lane sql
// committed its contract file (lane brief rule 6); the coordinator reconciles names at merge.
// Privacy: no phone, e-mail or address field. The customer name is on the Jobs list, its export and the Job history header
// (decision 37, R-UI-21); migration 0056 takes it from the job's most recent reading and returns NULL to a Viewer.

/// <summary>The stage codes of <c>dbo.v_service_job</c> (design 4.2, first rule wins) and their screen labels.</summary>
public static class ServiceJobStages
{
    // The codes, order, labels and rank are lane sql's ServiceStages (one source; integration 10 Oct 2026).
    public const string Booked = ServiceStages.Booked;
    public const string OnBench = ServiceStages.OnBench;
    public const string IndentRaised = ServiceStages.IndentRaised;
    public const string SrnOut = ServiceStages.SrnOut;
    public const string ReadyForDelivery = ServiceStages.ReadyForDelivery;
    public const string InTransitBack = ServiceStages.InTransitBack;
    public const string DcIssued = ServiceStages.DcIssued;
    public const string RaIssued = ServiceStages.RaIssued;
    public const string Rwr = ServiceStages.Rwr;
    public const string Delivered = ServiceStages.Delivered;

    /// <summary>Lifecycle order (design 3.3 group order, then the closed stages) = <see cref="ServiceStages.Order"/>.</summary>
    public static IReadOnlyList<string> InOrder => ServiceStages.Order;

    public static string Label(string? stage) => string.IsNullOrEmpty(stage) ? "Unknown" : ServiceStages.Label(stage);

    public static int Rank(string? stage) => ServiceStages.Rank(stage ?? "");

    /// <summary>
    /// Closed = delivered or returned without repair (Q8); a DC/RA-issued job whose claim document exists is
    /// "closed by claim" (Q3) so the board does not carry it forever. Everything else is open.
    /// </summary>
    public static bool IsClosed(string? stage, bool claimRaised) =>
        stage is Delivered or Rwr || (stage is DcIssued or RaIssued && claimRaised);
}

/// <summary>S002 <c>jotype_booking_quickbilling</c> normalised to the two screen values (design 4.3, Q2).</summary>
public static class ServiceJobTypes
{
    public const string Booking = "Booking";
    public const string QuickBilling = "Quick Billing";

    /// <summary>A job without an S002 row, or with a blank type, is a Booking (design 4.3).</summary>
    public static string Normalise(string? exported) =>
        exported is not null && exported.Contains("quick", StringComparison.OrdinalIgnoreCase) ? QuickBilling : Booking;
}

/// <summary>TAT arithmetic for the Jobs list (design 4.3, Q2): the median over closed jobs, per job type.</summary>
public static class ServiceJobTat
{
    /// <summary>
    /// The one TAT median of the Service screens (R-SQL-07 / R-UI-06): <see cref="ServiceTat.Median"/>, the true median
    /// (the mean of the two middle values for an even count); null for no values. Kept so the Jobs list and Today agree.
    /// </summary>
    public static double? Median(IEnumerable<int> values) => ServiceTat.Median(values);
}

/// <summary>
/// One row of <c>dbo.v_service_job</c>: the job's identity, stage and ages under the 0048 read rules. <c>JoType</c> is the
/// exported S002 value (<see cref="ServiceJobTypes.Normalise"/> gives the screen value). <c>TatDays</c> is set for closed
/// jobs (booking to delivered / RWR date); <c>AgeDays</c> and <c>DaysInStage</c> for open jobs, measured to <c>AsAt</c>
/// (the latest Service snapshot date). <c>ClaimRaised</c> says a WDC/WRA claim document exists for a DC/RA job.
/// 1.10.1 (R-SQL-16, R-UI-07): <c>TatRepairDays</c> (booking to repaired, delivered jobs), <c>Open</c> (the view's is_open),
/// <c>ClosedDate</c> (delivery / RWR date, or the claim date of a DC/RA job closed by its claim) and <c>ClaimDate</c> (the
/// first WDC/WRA claim of a DC/RA job). They default to null so a header built by hand before 1.10.1 still compiles.
/// </summary>
public sealed record ServiceJobHeader(
    string JobOrderNumber,
    DateOnly? BookingDate,
    string? JoType,
    string? ExportedStatus,
    string? Brand,
    string? Model,
    string? ProductCategory,
    string? Guarantee,
    string? CustomerType,
    string? CustomerName,
    DateOnly? Edd,
    string Stage,
    DateOnly? StageDate,
    string? PendingAt,
    string? SpareRequired,
    bool ClaimRaised,
    decimal? SpareValue,
    decimal? LabourCharge,
    int? TatDays,
    int? AgeDays,
    int? DaysInStage,
    bool IsOverdue,
    DateOnly AsAt,
    int? TatRepairDays = null,
    bool? Open = null,
    DateOnly? ClosedDate = null,
    DateOnly? ClaimDate = null)
{
    /// <summary>
    /// Open = not delivered, not RWR and not a DC/RA job closed by its claim (Q3); the view's is_open when the row came
    /// from v_service_job (<c>Open</c>), else the same rule on <c>Stage</c> and <c>ClaimRaised</c> (R-SQL-16).
    /// </summary>
    public bool IsOpen => Open ?? !ServiceJobStages.IsClosed(Stage, ClaimRaised);
}

/// <summary>
/// One row of <c>dbo.v_service_job_timeline</c>: one reading row of one family for the job. <c>EventDate</c> is the
/// family's own date (jodate, indentdate, srn_date, jorepairdate, deliverydate, normalrwrdate, wdcdate/wradate,
/// transaction_date, running_test_date); <c>StatusText</c> the status as exported (jostatus, to_status, current_status,
/// result); <c>DocumentNumber</c> for claims and S003 lines; <c>Amount</c> where the family has a money column;
/// <c>SourceKind</c> is CONSOLIDATED or RAW.
/// R-SQL-12: every live reading of a DateLog family is listed, so one claim or revenue line repeats once per overlapping
/// reading. <c>Amount</c> is for display per row and must never be summed or totalled; money totals come from
/// v_service_job (revenue_*) and v_service_claims, which apply the DateLog rule. ServiceUiRulesTests guards the screens.
/// </summary>
public sealed record ServiceJobTimelineRow(
    string JobOrderNumber,
    DateOnly SnapshotDate,
    string ReportCode,
    string FamilyLabel,
    DateOnly? EventDate,
    string? StatusText,
    string? PendingStore,
    string? DocumentNumber,
    decimal? Amount,
    string SourceKind,
    long ImportFileId);

/// <summary>A job's header and its timeline (newest reading first; the screen re-sorts defensively).</summary>
public sealed record ServiceJobDetail(ServiceJobHeader Header, IReadOnlyList<ServiceJobTimelineRow> Timeline);
