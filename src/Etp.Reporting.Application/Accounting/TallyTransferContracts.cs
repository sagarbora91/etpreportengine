using System.Globalization;
using System.Text.RegularExpressions;

namespace Etp.Reporting.Application.Accounting;

/// <summary>Phase 7 batch statuses (plan task 3). Nothing here claims Tally imported anything:
/// IMPORT_REPORTED_AWAITING_RECONCILIATION means "claimed, not proven", and RECONCILED is set
/// only from a reconciliation run.</summary>
public static class TallyBatchStatus
{
    public const string Draft = "DRAFT";
    public const string Blocked = "BLOCKED";
    public const string ApprovedReady = "APPROVED_READY";
    public const string ExportedAwaitingImport = "EXPORTED_AWAITING_IMPORT";
    public const string SubmittedAwaitingResult = "SUBMITTED_AWAITING_RESULT";
    public const string OutcomeUnknown = "OUTCOME_UNKNOWN";
    public const string PartiallyApplied = "PARTIALLY_APPLIED";
    public const string ImportReportedAwaitingReconciliation = "IMPORT_REPORTED_AWAITING_RECONCILIATION";
    public const string ReconciliationIncomplete = "RECONCILIATION_INCOMPLETE";
    public const string FailedReconciliation = "FAILED_RECONCILIATION";
    public const string Reconciled = "RECONCILED";
    public const string ReconciledWithAcceptedWarnings = "RECONCILED_WITH_ACCEPTED_WARNINGS";
    public const string ReAuditRequired = "RE_AUDIT_REQUIRED";
    public const string Rejected = "REJECTED";
    public const string Cancelled = "CANCELLED";

    public static IReadOnlyList<string> All { get; } =
    [
        Draft, Blocked, ApprovedReady, ExportedAwaitingImport, SubmittedAwaitingResult, OutcomeUnknown,
        PartiallyApplied, ImportReportedAwaitingReconciliation, ReconciliationIncomplete, FailedReconciliation,
        Reconciled, ReconciledWithAcceptedWarnings, ReAuditRequired, Rejected, Cancelled
    ];
}

/// <summary>Phase 7 per-voucher statuses (plan task 2, <c>accounting_vouchers.voucher_status</c>).</summary>
public static class TallyVoucherStatus
{
    public const string Planned = "PLANNED";
    public const string Blocked = "BLOCKED";
    public const string Excluded = "EXCLUDED";
    public const string Exported = "EXPORTED";
    public const string Submitted = "SUBMITTED";
    public const string OutcomeUnknown = "OUTCOME_UNKNOWN";
    public const string ActualLocated = "ACTUAL_LOCATED";
    public const string Reconciled = "RECONCILED";
    public const string ReconciledWithWarnings = "RECONCILED_WITH_WARNINGS";
    public const string Difference = "DIFFERENCE";
    public const string Cancelled = "CANCELLED";

    public static IReadOnlyList<string> All { get; } =
    [
        Planned, Blocked, Excluded, Exported, Submitted, OutcomeUnknown,
        ActualLocated, Reconciled, ReconciledWithWarnings, Difference, Cancelled
    ];
}

/// <summary>The allowed batch status changes of plan task 3. <c>dbo.usp_accounting_batch_transition</c>
/// enforces the same table in SQL; this copy lets the screen explain a disabled action before it is tried.
/// Conditions that need data (no FAIL finding, every WARN waived, a reason for an exclusion) are checked
/// by the caller; this table only says whether the move can ever happen.</summary>
public static class AccountingBatchTransitions
{
    private static readonly IReadOnlyDictionary<string, string[]> Allowed = Build();

    public static bool IsAllowed(string fromStatus, string toStatus)
    {
        RequireKnown(fromStatus);
        RequireKnown(toStatus);
        return Allowed.TryGetValue(fromStatus, out var targets) && targets.Contains(toStatus, StringComparer.Ordinal);
    }

    public static IReadOnlyList<string> TargetsFrom(string fromStatus)
    {
        RequireKnown(fromStatus);
        return Allowed.TryGetValue(fromStatus, out var targets) ? targets : Array.Empty<string>();
    }

    private static void RequireKnown(string status)
    {
        if (!TallyBatchStatus.All.Contains(status, StringComparer.Ordinal))
            throw new ArgumentException($"Unknown accounting batch status '{status}'.", nameof(status));
    }

    private static Dictionary<string, string[]> Build()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Add(string[] from, params string[] to)
        {
            foreach (var source in from)
            {
                if (!map.TryGetValue(source, out var set)) map[source] = set = new HashSet<string>(StringComparer.Ordinal);
                set.UnionWith(to);
            }
        }

        const string draft = TallyBatchStatus.Draft, blocked = TallyBatchStatus.Blocked;
        const string ready = TallyBatchStatus.ApprovedReady, exported = TallyBatchStatus.ExportedAwaitingImport;
        const string submitted = TallyBatchStatus.SubmittedAwaitingResult, unknown = TallyBatchStatus.OutcomeUnknown;
        const string partial = TallyBatchStatus.PartiallyApplied, reported = TallyBatchStatus.ImportReportedAwaitingReconciliation;
        const string incomplete = TallyBatchStatus.ReconciliationIncomplete, failed = TallyBatchStatus.FailedReconciliation;
        const string reconciled = TallyBatchStatus.Reconciled, accepted = TallyBatchStatus.ReconciledWithAcceptedWarnings;
        const string reAudit = TallyBatchStatus.ReAuditRequired, rejected = TallyBatchStatus.Rejected;
        const string cancelled = TallyBatchStatus.Cancelled;

        Add([draft, blocked], blocked, ready, rejected, cancelled);
        Add([blocked], draft);
        Add([ready], exported, submitted, blocked, rejected, draft);
        Add([exported], reported, cancelled);
        Add([submitted], reported, unknown, partial, ready);
        Add([unknown], reported, partial, incomplete);
        Add([reported, partial, incomplete], reconciled, accepted, failed, incomplete, partial, unknown);
        Add([reconciled, accepted], reAudit);
        Add([reAudit, failed, accepted], reconciled, accepted, failed, incomplete);

        return map.ToDictionary(pair => pair.Key, pair => pair.Value.Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
    }
}

/// <summary>What Derive needs to know about one voucher after a read-back or reconciliation run.</summary>
/// <param name="Status">A <see cref="TallyVoucherStatus"/> value other than PLANNED or BLOCKED.</param>
/// <param name="CoveredByReadback">A complete read-back of the right company covers the voucher's date.</param>
/// <param name="HasNotVerifiableDifferenceOnLocatedVoucher">The voucher was located and at least one check came back NOT_VERIFIABLE.</param>
/// <param name="OnlyMissingWithTallyRejection">A DIFFERENCE voucher whose only rows are MISSING, with a per-object rejection
/// recorded in <c>tally_attempts.tally_diagnostics_json</c>.</param>
/// <param name="WarningsAccepted">Every warning row of a RECONCILED_WITH_WARNINGS voucher has <c>accepted_by</c>.</param>
public sealed record VoucherState(
    string Status,
    bool CoveredByReadback = false,
    bool HasNotVerifiableDifferenceOnLocatedVoucher = false,
    bool OnlyMissingWithTallyRejection = false,
    bool WarningsAccepted = false);

/// <summary>The seven-step batch outcome table of plan task 3, applied in order.
/// No HTTP status, Tally acknowledgement or import count ever reaches this method.</summary>
public static class AccountingBatchStatusRules
{
    public static string Derive(IReadOnlyList<VoucherState> vouchers, bool hasHttpAttempt)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        foreach (var voucher in vouchers)
        {
            if (!TallyVoucherStatus.All.Contains(voucher.Status, StringComparer.Ordinal))
                throw new ArgumentException($"Unknown voucher status '{voucher.Status}'.", nameof(vouchers));
            if (voucher.Status is TallyVoucherStatus.Planned or TallyVoucherStatus.Blocked)
                throw new ArgumentException("Planned and blocked vouchers are set by Save and Validate, not derived.", nameof(vouchers));
        }

        var active = vouchers.Where(v => v.Status is not (TallyVoucherStatus.Excluded or TallyVoucherStatus.Cancelled)).ToArray();

        if (active.Any(v => v.Status == TallyVoucherStatus.OutcomeUnknown))
            return TallyBatchStatus.OutcomeUnknown;

        if (active.Any(v => v.Status is TallyVoucherStatus.Exported or TallyVoucherStatus.Submitted && !v.CoveredByReadback) ||
            active.Any(v => v.HasNotVerifiableDifferenceOnLocatedVoucher))
            return TallyBatchStatus.ReconciliationIncomplete;

        if (hasHttpAttempt &&
            active.Any(v => v.Status is TallyVoucherStatus.ActualLocated or TallyVoucherStatus.Reconciled or TallyVoucherStatus.ReconciledWithWarnings) &&
            active.Any(v => v.Status == TallyVoucherStatus.Difference && v.OnlyMissingWithTallyRejection))
            return TallyBatchStatus.PartiallyApplied;

        if (active.Any(v => v.Status == TallyVoucherStatus.Difference))
            return TallyBatchStatus.FailedReconciliation;

        if (active.Length > 0 && active.All(v => v.Status == TallyVoucherStatus.Reconciled))
            return TallyBatchStatus.Reconciled;

        if (active.Length > 0 &&
            active.All(v => v.Status == TallyVoucherStatus.Reconciled ||
                            v.Status == TallyVoucherStatus.ReconciledWithWarnings && v.WarningsAccepted))
            return TallyBatchStatus.ReconciledWithAcceptedWarnings;

        if (active.Length == 0)
            return TallyBatchStatus.Cancelled;

        return TallyBatchStatus.ReconciliationIncomplete;
    }
}

/// <summary>Voucher component roles (plan task 2).</summary>
public static class TallyComponentRole
{
    public const string Sales = "SALES";
    public const string CreditNote = "CREDIT_NOTE";
    public const string Receipt = "RECEIPT";
    public static IReadOnlyList<string> All { get; } = [Sales, CreditNote, Receipt];
}

/// <summary>The business key ETP writes into a voucher's narration so the read-back can find it again:
/// <c>ETP:{store}:{invoice_year}:{document_number}:{component_role}:{revision}</c> (plan tasks 2 and 9).
/// Batch id, file hash, mapping version and workbook row never enter the key.</summary>
public static class TallyCorrespondenceKey
{
    public const int MaxDocumentNumberLength = 80;

    private static readonly Regex StoreCodePattern = new("^[A-Z0-9_-]{1,30}$", RegexOptions.CultureInvariant);

    private static readonly Regex KeyPattern = new(
        @"\bETP:[A-Z0-9_-]{1,30}:\d{4}:[^:|\s]{1,80}:(SALES|CREDIT_NOTE|RECEIPT):\d+\b",
        RegexOptions.CultureInvariant);

    /// <summary>True when the document number can be carried in the key and found again by the read-back regex.
    /// A number containing <c>:</c> or <c>|</c> is KEY_UNSAFE (plan task 2); so is one the regex could not
    /// match back: empty, longer than 80 characters, or containing white space.</summary>
    public static bool IsDocumentNumberSafe(string? documentNumber) =>
        !string.IsNullOrEmpty(documentNumber) &&
        documentNumber.Length <= MaxDocumentNumberLength &&
        !documentNumber.Any(c => c is ':' or '|' || char.IsWhiteSpace(c));

    public static string Build(string storeCode, int invoiceYear, string documentNumber, string componentRole, int revision)
    {
        if (storeCode is null || !StoreCodePattern.IsMatch(storeCode))
            throw new ArgumentException("The store code must be 1-30 upper-case letters, digits, '_' or '-'.", nameof(storeCode));
        if (invoiceYear is < 1000 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(invoiceYear), "The invoice year must have four digits.");
        if (!IsDocumentNumberSafe(documentNumber))
            throw new ArgumentException("KEY_UNSAFE: the document number cannot be carried in a Tally correspondence key.", nameof(documentNumber));
        if (!TallyComponentRole.All.Contains(componentRole, StringComparer.Ordinal))
            throw new ArgumentException($"Unknown voucher component role '{componentRole}'.", nameof(componentRole));
        if (revision < 1)
            throw new ArgumentOutOfRangeException(nameof(revision), "The revision starts at 1.");

        return string.Create(CultureInfo.InvariantCulture, $"ETP:{storeCode}:{invoiceYear}:{documentNumber}:{componentRole}:{revision}");
    }

    /// <summary>Every distinct key found in a narration or reference text, in order of first appearance.
    /// More than one key in a single voucher is AMBIGUOUS_MATCH for the caller.</summary>
    public static IReadOnlyList<string> FindAll(string? text) =>
        string.IsNullOrEmpty(text)
            ? Array.Empty<string>()
            : KeyPattern.Matches(text).Select(match => match.Value).Distinct(StringComparer.Ordinal).ToArray();
}

/// <summary>One Tally company that ETP may write vouchers for (plan task 1, <c>dbo.tally_profiles</c>).
/// The policy fields carry decisions D13, D14, D16 and D17; until the owner and accountant freeze them,
/// a profile records the assumption it was set up with. Live books are enabled only by a separate,
/// later action, never by saving a profile.</summary>
public sealed record TallyProfile(
    int? Id,
    string ProfileCode,
    string CompanyName,
    string Environment,
    string? EndpointUrl,
    string DefaultDeliveryMode,
    string PayloadFormat,
    string VoucherGranularity,
    string PartyPolicy,
    string? SinglePartyLedger,
    string TenderModel,
    string PostingModel,
    string VoucherView,
    DateOnly? PostingFromDate,
    DateOnly? PostingToDate,
    string? TallyBuildLabel,
    bool IsEnabled,
    IReadOnlyList<string> StoreCodes,
    DateTime? ProductionEnabledUtc = null,
    string? ModifiedBy = null,
    DateTime? ModifiedUtc = null)
{
    /// <summary>A TEST profile with the Slice 7a assumptions: one voucher per invoice, one retail ledger,
    /// tender inside the voucher, accounting only, file delivery.</summary>
    public static TallyProfile NewTest(string profileCode, string companyName, IReadOnlyList<string> storeCodes) => new(
        null, profileCode, companyName, "TEST", null, "FILE", "XML", "PER_INVOICE", "SINGLE_LEDGER", "Cash Sales",
        "IN_VOUCHER", "ACCOUNTING_ONLY", "ACCOUNTING", null, null, null, true, storeCodes);
}

public static class TallyProfileOptions
{
    public static IReadOnlyList<string> Environments { get; } = ["TEST", "PRODUCTION"];
    public static IReadOnlyList<string> DeliveryModes { get; } = ["FILE", "HTTP"];
    public static IReadOnlyList<string> PayloadFormats { get; } = ["XML", "JSON"];
    public static IReadOnlyList<string> VoucherGranularities { get; } = ["PER_INVOICE", "DAILY_SUMMARY"];
    public static IReadOnlyList<string> PartyPolicies { get; } = ["SINGLE_LEDGER", "NAMED_LEDGERS"];
    public static IReadOnlyList<string> TenderModels { get; } = ["IN_VOUCHER", "CLEARING_LEDGER"];
    public static IReadOnlyList<string> PostingModels { get; } = ["ACCOUNTING_ONLY", "INVENTORY"];
    public static IReadOnlyList<string> VoucherViews { get; } = ["ACCOUNTING", "INVOICE"];
}

/// <summary>The checks a profile must pass before it is saved. The database repeats them as CHECK
/// constraints; these give the Owner a plain sentence first.</summary>
public static class TallyProfileRules
{
    private static readonly Regex ProfileCodePattern = new("^[A-Z0-9][A-Z0-9_-]{0,29}$", RegexOptions.CultureInvariant);
    private static readonly Regex StoreCodePattern = new("^[A-Z0-9_-]{1,30}$", RegexOptions.CultureInvariant);
    private static readonly Regex LoopbackEndpoint = new(@"^http://(127\.0\.0\.1|localhost):[0-9]{1,5}/$", RegexOptions.CultureInvariant);

    public const string OnlyThisPc = "Only this PC's Tally can be used. Enter http://127.0.0.1:<port>/ or http://localhost:<port>/, or leave it blank.";

    public static bool IsLoopbackEndpoint(string? endpointUrl) =>
        endpointUrl is not null && LoopbackEndpoint.IsMatch(endpointUrl) &&
        int.TryParse(endpointUrl[(endpointUrl.LastIndexOf(':') + 1)..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var port) &&
        port is > 0 and <= 65535;

    /// <summary>Returns the profile with codes trimmed and upper-cased, or throws <see cref="ArgumentException"/>.</summary>
    public static TallyProfile Normalise(TallyProfile profile, string reason)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new ArgumentException("Enter a change reason of at most 500 characters.");

        var code = (profile.ProfileCode ?? "").Trim().ToUpperInvariant();
        if (!ProfileCodePattern.IsMatch(code))
            throw new ArgumentException("The short code must be 1-30 capital letters, digits, '_' or '-', starting with a letter or digit.");
        var company = (profile.CompanyName ?? "").Trim();
        if (company.Length is 0 or > 200)
            throw new ArgumentException("Enter the Tally company name exactly as Tally shows it (at most 200 characters).");

        Require(profile.Environment, TallyProfileOptions.Environments, "Choose test books or live books.");
        Require(profile.DefaultDeliveryMode, TallyProfileOptions.DeliveryModes, "Choose how vouchers reach Tally.");
        Require(profile.PayloadFormat, TallyProfileOptions.PayloadFormats, "Choose the file format.");
        Require(profile.VoucherGranularity, TallyProfileOptions.VoucherGranularities, "Choose one voucher per invoice or one daily summary.");
        Require(profile.PartyPolicy, TallyProfileOptions.PartyPolicies, "Choose how customers appear in Tally.");
        Require(profile.TenderModel, TallyProfileOptions.TenderModels, "Choose how payments are recorded.");
        Require(profile.PostingModel, TallyProfileOptions.PostingModels, "Choose accounting only or with stock items.");
        Require(profile.VoucherView, TallyProfileOptions.VoucherViews, "Choose the voucher view the sample voucher was keyed in.");

        var endpoint = string.IsNullOrWhiteSpace(profile.EndpointUrl) ? null : profile.EndpointUrl.Trim();
        if (endpoint is not null && !IsLoopbackEndpoint(endpoint)) throw new ArgumentException(OnlyThisPc);
        if (profile.DefaultDeliveryMode == "HTTP" && endpoint is null)
            throw new ArgumentException("Sending straight to Tally needs the address where Tally answers on this PC.");
        if (profile.PayloadFormat == "JSON")
            throw new ArgumentException("JSON files stay unavailable until the installed Tally build has been checked (plan task 24). Choose XML.");

        if (profile.PartyPolicy == "NAMED_LEDGERS" && profile.TenderModel == "IN_VOUCHER")
            throw new ArgumentException("Named customer ledgers need a clearing ledger per tender mode.");
        var ledger = string.IsNullOrWhiteSpace(profile.SinglePartyLedger) ? null : profile.SinglePartyLedger.Trim();
        if (profile.PartyPolicy == "SINGLE_LEDGER" && ledger is null)
            throw new ArgumentException("Enter the one Tally ledger that retail customers are posted to, for example \"Cash Sales\".");
        if (ledger?.Length > 200) throw new ArgumentException("The customer ledger name can have at most 200 characters.");

        if (profile.PostingFromDate is { } from && profile.PostingToDate is { } to && from > to)
            throw new ArgumentException("The first allowed voucher date must not be after the last.");
        var build = string.IsNullOrWhiteSpace(profile.TallyBuildLabel) ? null : profile.TallyBuildLabel.Trim();
        if (build?.Length > 100) throw new ArgumentException("The Tally build label can have at most 100 characters.");

        var stores = (profile.StoreCodes ?? []).Select(store => (store ?? "").Trim().ToUpperInvariant()).Where(store => store.Length > 0)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (stores.FirstOrDefault(store => !StoreCodePattern.IsMatch(store)) is { } bad)
            throw new ArgumentException($"'{bad}' is not a store code.");

        return profile with
        {
            ProfileCode = code, CompanyName = company, EndpointUrl = endpoint, SinglePartyLedger = ledger,
            TallyBuildLabel = build, StoreCodes = stores
        };
    }

    private static void Require(string? value, IReadOnlyList<string> allowed, string message)
    {
        if (value is null || !allowed.Contains(value, StringComparer.Ordinal)) throw new ArgumentException(message);
    }
}

public interface ITallyProfileService
{
    /// <summary>Every Tally company with the stores it covers. Owner only.</summary>
    Task<IReadOnlyList<TallyProfile>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates (Id null) or changes a Tally company and its stores with a reason; returns its id. Owner only.
    /// Never enables live books.</summary>
    Task<int> SaveAsync(TallyProfile profile, string reason, CancellationToken cancellationToken = default);
}
