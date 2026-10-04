using System.Collections.Frozen;
using Etp.Reporting.Application.Service;

namespace Etp.Reporting.Import.Service;

/// <summary>How the 0048 read views choose the rows of a Service family (design section 4).</summary>
public enum ServiceReadRuleKind
{
    /// <summary>Rows of the reading with the greatest snapshot date (ties: greatest import_file_id).</summary>
    StateSnapshot,

    /// <summary>For each business date D, rows dated D from the latest reading whose window [min row date, snapshot date] contains D.</summary>
    DateLog,

    /// <summary>Per job, rows of the latest reading that holds the job.</summary>
    JobList,
}

/// <summary>A catalogue column, by its source header and its frozen canonical name (scripts/service-centre/families.spec.json).</summary>
public sealed record ServiceColumn(string SourceHeader, string CanonicalField);

/// <summary>
/// The read rule of one importable family. <c>DateColumn</c> is set for <see cref="ServiceReadRuleKind.DateLog"/>;
/// <c>JobColumn</c> is set for <see cref="ServiceReadRuleKind.JobList"/>, and for the S009/S010 state snapshots,
/// whose membership the job-list events compare.
/// </summary>
public sealed record ServiceReadRule(string ReportCode, ServiceReadRuleKind Kind, ServiceColumn? DateColumn = null, ServiceColumn? JobColumn = null);

/// <summary>One of the ten RepairRegister status views. The lifecycle rank breaks ties only (L4 may correct it from data and must record the change).</summary>
public sealed record ServiceStatusView(string ReportCode, string StatusLabel, int LifecycleRank);

/// <summary>A pending list: the family, the contract list key (<see cref="ServicePendingLists"/>) and its Owner label.</summary>
public sealed record ServicePendingList(string ReportCode, string ListKey, string Label);

/// <summary>
/// Service Centre interim import (S-2, decision 15, 3 Oct 2026): which S families are imported, which are not and why,
/// and how the read views use each one. Static, no I/O. Lanes L1 (catalogue), L2 (landing tables), L3 (routing),
/// L4 (read views) and L5 (screens) build against these sets.
/// </summary>
public static class ServiceInterimFamilies
{
    /// <summary>The Service Centre store. It is an inactive store of the SERVICE business unit (0048 section A).</summary>
    public const string ServiceStoreCode = "AW330";

    /// <summary>Diagnostic codes of the Service interim (docs/service-centre/SERVICE-INTERIM-NUMBERS.md).</summary>
    public static class Codes
    {
        /// <summary>S001 RepairRegister is the builder's union of the ten status views: Not needed.</summary>
        public const string FamilyDerived = "FAMILY_DERIVED";

        /// <summary>S005 and S038: Not needed (Owner).</summary>
        public const string ServiceFamilyNotNeeded = "SERVICE_FAMILY_NOT_NEEDED";

        /// <summary>S027 and S028: deferred to P8, reported Not needed.</summary>
        public const string ServiceFamilyDeferred = "SERVICE_FAMILY_DEFERRED";

        /// <summary>A Service file could not be dated: a dated folder name or the snapshot-date override is needed.</summary>
        public const string ServiceSnapshotDateNeeded = "SERVICE_SNAPSHOT_DATE_NEEDED";

        /// <summary>Information: the folder date differs from the latest Snapshot_As_Of (S006, S009, S010). Never refuses.</summary>
        public const string ServiceSnapshotDateDiffersFromHistory = "SERVICE_SNAPSHOT_DATE_DIFFERS_FROM_HISTORY";

        /// <summary>Information: no store column and no sibling store, so the Service store AW330 was used.</summary>
        public const string ServiceStoreDefaulted = "SERVICE_STORE_DEFAULTED";
    }

    /// <summary>The 35 families that land in an etp_landing_snnn table.</summary>
    public static IReadOnlySet<string> Importable { get; } = Set(
        "S002", "S003", "S004",
        "S006", "S007", "S008", "S009", "S010", "S011", "S012", "S013", "S014", "S015", "S016", "S017", "S018",
        "S019", "S020", "S021", "S022", "S023", "S024", "S025", "S026",
        "S029", "S030", "S031", "S032", "S033", "S034", "S035", "S036", "S037",
        "S039", "S040");

    /// <summary>S001: derived by the consolidation builder; reported Not needed with <see cref="Codes.FamilyDerived"/>.</summary>
    public static IReadOnlySet<string> Derived { get; } = Set("S001");

    /// <summary>S005 (undatable tender summary) and S038 (retired SRN report name; its header equals S011's).</summary>
    public static IReadOnlySet<string> NotNeeded { get; } = Set("S005", "S038");

    /// <summary>S027 (TAT) and S028 (technician productivity): deferred to P8.</summary>
    public static IReadOnlySet<string> Deferred { get; } = Set("S027", "S028");

    /// <summary>The ten RepairRegister status views, with label and lifecycle rank (PR 1 ... DELIVERED 10).</summary>
    public static IReadOnlyList<ServiceStatusView> StatusViews { get; } =
    [
        new("S014", "DC", 5),
        new("S015", "IR", 2),
        new("S016", "RA", 6),
        new("S017", "RWR", 8),
        new("S018", "DELIVERED", 10),
        new("S031", "PD", 9),
        new("S032", "PR", 1),
        new("S033", "SRN", 3),
        new("S034", "REPAIRED", 7),
        new("S035", "SRNINV", 4),
    ];

    public static IReadOnlyList<ServicePendingList> PendingLists { get; } =
    [
        new("S009", ServicePendingLists.PendingRepair, "Pending repair"),
        new("S010", ServicePendingLists.PendingDelivery, "Pending delivery"),
        new("S011", ServicePendingLists.SrnStatus, "SRN status"),
    ];

    /// <summary>Money and claim logs (the money check and money-change list read S003 and S004).</summary>
    public static IReadOnlySet<string> MoneyLogs { get; } = Set("S003", "S004", "S019", "S023", "S024", "S025", "S026", "S039", "S040");

    private static readonly ServiceColumn JobOrderNumber = new("JobOrderNumber", "jobordernumber");
    private static readonly ServiceColumn JoNumber = new("JONumber", "jonumber");
    private static readonly ServiceColumn JobOrderNo = new("Job Order No", "job_order_no");
    private static readonly ServiceColumn TransDate = new("TransDate", "transdate");
    private static readonly ServiceColumn GrnDate = new("GRN_DATE", "grn_date");
    private static readonly ServiceColumn TransactionDate = new("Transaction Date", "transaction_date");

    /// <summary>
    /// One read rule per importable family (design section 4). Column names are the frozen canonical names of
    /// scripts/service-centre/families.spec.json; lane L4 relies on this table.
    /// </summary>
    public static IReadOnlyDictionary<string, ServiceReadRule> ReadRules { get; } = new[]
    {
        // StateSnapshot: the latest reading is the whole truth. S006 has no job column.
        new ServiceReadRule("S006", ServiceReadRuleKind.StateSnapshot),
        new ServiceReadRule("S009", ServiceReadRuleKind.StateSnapshot, JobColumn: JoNumber),
        new ServiceReadRule("S010", ServiceReadRuleKind.StateSnapshot, JobColumn: JoNumber),

        // DateLog: money and movement logs, by their business-date column.
        new ServiceReadRule("S003", ServiceReadRuleKind.DateLog, new("Trans Date", "trans_date")),
        new ServiceReadRule("S004", ServiceReadRuleKind.DateLog, new("BillingDate", "billingdate")),
        // S007 is the purchase register by CREATED date and S008 by RECEIVED date, but both are dated by GRN_DATE
        // here as listed (the goods-received date is the one both carry for every row); doubtful for S007, where
        // INVOICE_DATE is the alternative. L4 may revisit it from data and must record the change.
        new ServiceReadRule("S007", ServiceReadRuleKind.DateLog, GrnDate),
        new ServiceReadRule("S008", ServiceReadRuleKind.DateLog, GrnDate),
        new ServiceReadRule("S013", ServiceReadRuleKind.DateLog, new("STM Date", "stm_date")),
        new ServiceReadRule("S019", ServiceReadRuleKind.DateLog, new("RepairDate", "repairdate")),
        new ServiceReadRule("S022", ServiceReadRuleKind.DateLog, new("INVOICE DATE", "invoice_date")),
        new ServiceReadRule("S023", ServiceReadRuleKind.DateLog, TransDate),
        new ServiceReadRule("S024", ServiceReadRuleKind.DateLog, TransDate),
        new ServiceReadRule("S025", ServiceReadRuleKind.DateLog, TransDate),
        new ServiceReadRule("S026", ServiceReadRuleKind.DateLog, TransDate),
        new ServiceReadRule("S029", ServiceReadRuleKind.DateLog, new("Repair Date", "repair_date")),
        new ServiceReadRule("S039", ServiceReadRuleKind.DateLog, TransactionDate),
        new ServiceReadRule("S040", ServiceReadRuleKind.DateLog, TransactionDate),

        // JobList: job and status lists, by job order number (the column name differs per family).
        new ServiceReadRule("S002", ServiceReadRuleKind.JobList, JobColumn: JobOrderNo),
        new ServiceReadRule("S011", ServiceReadRuleKind.JobList, JobColumn: new("JOBORDER NUMBER", "joborder_number")),
        // S012 SRN history: one job can have more than one SRN; the job is the key the screens search by.
        new ServiceReadRule("S012", ServiceReadRuleKind.JobList, JobColumn: JoNumber),
        new ServiceReadRule("S014", ServiceReadRuleKind.JobList, JobColumn: JobOrderNumber),
        new ServiceReadRule("S015", ServiceReadRuleKind.JobList, JobColumn: JobOrderNumber),
        new ServiceReadRule("S016", ServiceReadRuleKind.JobList, JobColumn: JobOrderNumber),
        new ServiceReadRule("S017", ServiceReadRuleKind.JobList, JobColumn: JobOrderNumber),
        new ServiceReadRule("S018", ServiceReadRuleKind.JobList, JobColumn: JobOrderNumber),
        new ServiceReadRule("S020", ServiceReadRuleKind.JobList, JobColumn: new("JOBORDERNUMBER", "jobordernumber")),
        new ServiceReadRule("S021", ServiceReadRuleKind.JobList, JobColumn: JobOrderNumber),
        // S030 MIS export grid: rows are running-test runs of a job; the job is still the key.
        new ServiceReadRule("S030", ServiceReadRuleKind.JobList, JobColumn: new("Job order number", "job_order_number")),
        new ServiceReadRule("S031", ServiceReadRuleKind.JobList, JobColumn: JobOrderNumber),
        new ServiceReadRule("S032", ServiceReadRuleKind.JobList, JobColumn: JobOrderNumber),
        new ServiceReadRule("S033", ServiceReadRuleKind.JobList, JobColumn: JobOrderNumber),
        new ServiceReadRule("S034", ServiceReadRuleKind.JobList, JobColumn: JobOrderNumber),
        new ServiceReadRule("S035", ServiceReadRuleKind.JobList, JobColumn: JobOrderNumber),
        new ServiceReadRule("S036", ServiceReadRuleKind.JobList, JobColumn: JobOrderNo),
        new ServiceReadRule("S037", ServiceReadRuleKind.JobList, JobColumn: JobOrderNo),
    }.ToFrozenDictionary(rule => rule.ReportCode, StringComparer.Ordinal);

    public static bool IsServiceCode(string? reportCode) =>
        reportCode is not null && (Importable.Contains(reportCode) || Derived.Contains(reportCode)
            || NotNeeded.Contains(reportCode) || Deferred.Contains(reportCode));

    /// <summary>
    /// The Not-needed diagnostic code for a Service family that is not imported, or null when the family is importable
    /// (or not a Service family).
    /// </summary>
    public static string? NotImportedCode(string? reportCode) => reportCode switch
    {
        null => null,
        _ when Derived.Contains(reportCode) => Codes.FamilyDerived,
        _ when NotNeeded.Contains(reportCode) => Codes.ServiceFamilyNotNeeded,
        _ when Deferred.Contains(reportCode) => Codes.ServiceFamilyDeferred,
        _ => null,
    };

    private static IReadOnlySet<string> Set(params string[] codes) => codes.ToFrozenSet(StringComparer.Ordinal);
}
