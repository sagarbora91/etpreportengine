using System.Globalization;

namespace Etp.Reporting.Application.Accounting;

/// <summary>A planned ledger line (A). Signed amount = debit minus credit.</summary>
public sealed record ExpectedLedgerLine(string BusinessEvent, string LedgerName, decimal Debit, decimal Credit);

/// <summary>A planned voucher (A), from <c>accounting_vouchers</c> and its entries.</summary>
public sealed record ExpectedVoucher(
    int Sequence,
    string CorrespondenceKey,
    string VoucherType,
    DateOnly VoucherDate,
    string DocumentNumber,
    string Status,
    IReadOnlyList<ExpectedLedgerLine> Lines);

/// <summary>A ledger line as a Tally XML file carries it. <paramref name="Amount"/> keeps Tally's sign
/// (a debit is negative) and is null when the amount could not be read.</summary>
public sealed record ParsedLedgerLine(string LedgerName, decimal? Amount, bool? IsDeemedPositive);

/// <summary>A voucher read from a Tally XML file: the payload ETP wrote (B) or a read-back (C).
/// Any field Tally did not return is null, never a default.</summary>
public sealed record ParsedVoucher(
    int Index,
    string? VoucherType,
    DateOnly? VoucherDate,
    string? VoucherNumber,
    string? Reference,
    string? Narration,
    bool? IsCancelled,
    bool? IsOptional,
    string? TallyGuid,
    long? MasterId,
    long? AlterId,
    string FragmentSha256,
    IReadOnlyList<ParsedLedgerLine> Lines)
{
    /// <summary>Correspondence keys found in the narration, or failing that in the reference.</summary>
    public IReadOnlyList<string> Keys
    {
        get
        {
            var keys = TallyCorrespondenceKey.FindAll(Narration);
            return keys.Count > 0 ? keys : TallyCorrespondenceKey.FindAll(Reference);
        }
    }
}

/// <summary>What a read-back returned (C). <paramref name="CompanyNameReported"/> is only what Tally itself said.</summary>
public sealed record ReadbackSnapshot(
    long? ReadbackId,
    string? CompanyNameReported,
    bool IsComplete,
    string? IncompleteReason,
    DateOnly FromDate,
    DateOnly ToDate,
    IReadOnlyList<ParsedVoucher> Vouchers);

/// <summary>Absolute tolerances per check level. The default is none: money must match to the paisa.</summary>
public sealed record ToleranceSet(IReadOnlyDictionary<string, decimal> AbsoluteByCheckLevel)
{
    public static ToleranceSet None { get; } = new(new Dictionary<string, decimal>(StringComparer.Ordinal));
    public decimal For(string checkLevel) => AbsoluteByCheckLevel.TryGetValue(checkLevel, out var value) ? value : 0m;
}

/// <param name="VoucherNumberVerifiable">D18: the TEST company's voucher type numbers vouchers manually, so Tally keeps ETP's number.</param>
/// <param name="PayloadRecorded">A written Tally file (B) is registered for the batch. Without it A = B cannot be checked.</param>
public sealed record ReconciliationInput(
    string CompanyName,
    IReadOnlyList<ExpectedVoucher> Expected,
    bool SourceUnchanged,
    bool PayloadUnchanged,
    bool VoucherNumberVerifiable,
    bool HasHttpAttempt,
    bool PayloadRecorded = true);

public sealed record ReconciliationDifference(
    int? VoucherSequence,
    int? ActualIndex,
    string CheckLevel,
    string DifferenceType,
    string Severity,
    string? ValueA,
    string? ValueB,
    string? ValueC,
    decimal? Delta,
    string RuleId,
    string MatchRationale,
    string RequiredAction);

public sealed record VoucherReconciliation(int Sequence, string Status, int? ActualIndex, bool CoveredByReadback);

/// <param name="BatchStatus">The batch status the voucher outcomes derive to (plan task 3).</param>
/// <param name="RunOutcome">The run's recorded outcome: one of the four values <c>tally_reconciliation_runs</c> allows.</param>
public sealed record TallyReconciliationResult(
    string RuleSetVersion,
    string BatchStatus,
    string RunOutcome,
    IReadOnlyList<VoucherReconciliation> Vouchers,
    IReadOnlyList<ReconciliationDifference> Differences);

/// <summary>Three-way comparison of plan task 10: A = what ETP planned, B = the file ETP wrote,
/// C = what Tally reports it holds. Pure, no I/O. A voucher is RECONCILED only when A = B = C on every check
/// with exactly one actual voucher located by its correspondence key; equal amount or date alone never matches.</summary>
public static class TallyReconciliationEngine
{
    public const string RuleSetVersion = "7a.1";
    public const int RuleVersion = 1;

    private static readonly string[] SentStatuses =
    [
        TallyVoucherStatus.Exported, TallyVoucherStatus.Submitted, TallyVoucherStatus.OutcomeUnknown, TallyVoucherStatus.ActualLocated,
        TallyVoucherStatus.Reconciled, TallyVoucherStatus.ReconciledWithWarnings, TallyVoucherStatus.Difference
    ];

    public static IReadOnlyDictionary<string, string> RequiredActions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MISSING"] = "Voucher not found in Tally. Do not write the file again; check Tally's import log, then read back again.",
        ["EXTRA"] = "Tally holds an ETP voucher this batch did not plan. Check which batch it belongs to before changing anything.",
        ["DUPLICATE"] = "Tally holds this voucher more than once. With the accountant, remove the extra copy in Tally, then read back again.",
        ["WRONG_COMPANY"] = "The read-back came from another Tally company. Open the right company in Tally and read back again.",
        ["AMOUNT_MISMATCH"] = "Tally's amount differs from ETP's. Check the voucher in Tally with the accountant; do not change ETP's figures.",
        ["TAX_MISMATCH"] = "Tally's GST amount differs from ETP's. Check the voucher's tax lines in Tally with the accountant.",
        ["LEDGER_MISMATCH"] = "The ledger lines in Tally differ from ETP's plan. Check the voucher and the ledger mapping with the accountant.",
        ["QUANTITY_MISMATCH"] = "Tally's quantity differs from ETP's. Check the voucher's stock lines in Tally.",
        ["STATUS_MISMATCH"] = "The voucher's details in Tally (type, date, number or cancellation) differ from ETP's. Check it in Tally.",
        ["SOURCE_CHANGED"] = "ETP's source figures changed after the file was written. Do not import again; reject and prepare a corrected batch.",
        ["ACTUAL_CHANGED"] = "The voucher changed in Tally since the last check. Check who changed it, then compare again.",
        ["AMBIGUOUS_MATCH"] = "One Tally voucher carries two ETP keys. Correct its narration in Tally, then read back again.",
        ["NOT_VERIFIABLE"] = "This could not be checked. Read back again from the right company and dates; the batch stays incomplete until it is checked."
    };

    public static TallyReconciliationResult Run(ReconciliationInput a, IReadOnlyList<ParsedVoucher>? b, ReadbackSnapshot c, ToleranceSet t)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(t);
        var differences = new List<ReconciliationDifference>();
        var outcomes = new List<VoucherReconciliation>();
        void Add(int? sequence, int? actual, string level, string type, string severity, string? valueA, string? valueB, string? valueC, decimal? delta, string rule, string rationale) =>
            differences.Add(new(sequence, actual, level, type, severity, valueA, valueB, valueC, delta, rule, rationale, RequiredActions[type]));

        var sent = a.Expected.Where(voucher => SentStatuses.Contains(voucher.Status, StringComparer.Ordinal)).ToArray();
        var reported = c.CompanyNameReported?.Trim();
        var companyKnown = reported is not null;
        var rightCompany = companyKnown && string.Equals(reported, a.CompanyName.Trim(), StringComparison.Ordinal);
        var usable = rightCompany && c.IsComplete;

        // Keys found in the read-back. A voucher with two different keys never matches either of them.
        var byKey = new Dictionary<string, List<ParsedVoucher>>(StringComparer.Ordinal);
        var ambiguous = new Dictionary<string, List<ParsedVoucher>>(StringComparer.Ordinal);
        if (usable)
            foreach (var actual in c.Vouchers)
            {
                var keys = actual.Keys;
                var target = keys.Count == 1 ? byKey : ambiguous;
                foreach (var key in keys)
                {
                    if (!target.TryGetValue(key, out var list)) target[key] = list = [];
                    list.Add(actual);
                }
            }
        var payloadByKey = b?.SelectMany(voucher => voucher.Keys.Count == 1 ? new[] { (Key: voucher.Keys[0], Voucher: voucher) } : Array.Empty<(string Key, ParsedVoucher Voucher)>())
            .GroupBy(item => item.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Select(item => item.Voucher).ToArray(), StringComparer.Ordinal);

        foreach (var voucher in sent)
        {
            var seq = voucher.Sequence;
            var covered = usable && voucher.VoucherDate >= c.FromDate && voucher.VoucherDate <= c.ToDate;
            var before = differences.Count;
            int? located = null;
            var absenceUnproven = false;

            if (!a.SourceUnchanged)
                Add(seq, null, "INTEGRITY", "SOURCE_CHANGED", "FAIL", "changed", null, null, null, "RECO-INT-001", "The invoice's source facts no longer hash to the prepared value.");
            if (!a.PayloadRecorded)
                Add(seq, null, "INTEGRITY", "NOT_VERIFIABLE", "FAIL", null, "no file recorded", null, null, "RECO-INT-006", "No written Tally file is registered for this batch, so the file cannot be compared.");
            else if (!a.PayloadUnchanged)
                Add(seq, null, "INTEGRITY", "NOT_VERIFIABLE", "FAIL", null, "file changed", null, null, "RECO-INT-002", "The written Tally file no longer matches its recorded SHA-256.");

            if (!companyKnown)
                Add(seq, null, "COVERAGE", "NOT_VERIFIABLE", "FAIL", a.CompanyName, null, null, null, "RECO-COV-004", "Tally did not say which company answered (COMPANY_NOT_REPORTED).");
            else if (!rightCompany)
                Add(seq, null, "COVERAGE", "WRONG_COMPANY", "FAIL", a.CompanyName, null, reported, null, "RECO-COV-003", "The read-back names another company; nothing in it is matched.");
            else if (!c.IsComplete)
                Add(seq, null, "COVERAGE", "NOT_VERIFIABLE", "FAIL", null, null, c.IncompleteReason, null, "RECO-COV-005", "The read-back is incomplete, so absence cannot be proven.");
            else if (covered)
            {
                if (ambiguous.TryGetValue(voucher.CorrespondenceKey, out var mixed))
                    foreach (var actual in mixed)
                        Add(seq, actual.Index, "COVERAGE", "AMBIGUOUS_MATCH", "FAIL", voucher.CorrespondenceKey, null, string.Join(" ", actual.Keys), null, "RECO-COV-006", "This Tally voucher carries more than one ETP key.");
                var matches = byKey.TryGetValue(voucher.CorrespondenceKey, out var found) ? found : new List<ParsedVoucher>();
                if (matches.Count == 0 && !c.Vouchers.Any(actual => actual.VoucherDate == voucher.VoucherDate))
                {
                    // The dates of a hand-exported file are typed by the operator. A file with nothing at all on this day
                    // may simply be the wrong day, so it never proves the voucher absent (a false MISSING invites a resend).
                    absenceUnproven = true;
                    Add(seq, null, "COVERAGE", "NOT_VERIFIABLE", "FAIL", Date(voucher.VoucherDate), null, "no vouchers on this date", null, "RECO-COV-008",
                        "The read-back holds no voucher dated this day, so it may not cover it; absence is not proven.");
                }
                else if (matches.Count == 0)
                    Add(seq, null, "COVERAGE", "MISSING", "FAIL", voucher.CorrespondenceKey, null, null, null, "RECO-COV-001", "No actual voucher in the company carries this key.");
                else if (matches.Count > 1)
                    Add(seq, null, "COVERAGE", "DUPLICATE", "FAIL", voucher.CorrespondenceKey, null, $"{matches.Count} vouchers", null, "RECO-COV-002", "More than one actual voucher carries this key; none is picked.");
                else
                {
                    located = matches[0].Index;
                    var payload = payloadByKey is null ? null
                        : payloadByKey.TryGetValue(voucher.CorrespondenceKey, out var written) && written.Length == 1 ? written[0] : null;
                    if (payloadByKey is not null && payload is null)
                        Add(seq, located, "INTEGRITY", "NOT_VERIFIABLE", "FAIL", voucher.CorrespondenceKey, "not in file", null, null, "RECO-INT-003", "The written file does not carry this voucher exactly once.");
                    CompareLocated(voucher, payload, matches[0], a.VoucherNumberVerifiable, t, Add);
                }
            }

            var mine = differences.Skip(before).ToArray();
            var status = Status(voucher.Status, covered && !absenceUnproven, located, mine);
            outcomes.Add(new(seq, status, located, covered && !absenceUnproven));
        }

        // Coverage the other way: ETP keys in the company that this batch did not plan.
        if (usable)
        {
            // Only keys this batch sent are expected in Tally. A blocked, excluded or still planned voucher found there is reported too.
            var sentKeys = sent.Select(voucher => voucher.CorrespondenceKey).ToHashSet(StringComparer.Ordinal);
            var unsent = a.Expected.Where(voucher => !sentKeys.Contains(voucher.CorrespondenceKey))
                .GroupBy(voucher => voucher.CorrespondenceKey, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var stores = a.Expected.Select(voucher => StoreOf(voucher.CorrespondenceKey)).ToHashSet(StringComparer.Ordinal);
            foreach (var (key, actuals) in byKey.Where(pair => !sentKeys.Contains(pair.Key) && stores.Contains(StoreOf(pair.Key))).OrderBy(pair => pair.Key, StringComparer.Ordinal))
                foreach (var actual in actuals.Where(item => item.VoucherDate is { } date && date >= c.FromDate && date <= c.ToDate))
                    if (unsent.TryGetValue(key, out var notSent))
                        Add(notSent.Sequence, actual.Index, "COVERAGE", "EXTRA", "WARN", notSent.Status, null, key, null, "RECO-COV-009",
                            $"Tally holds a voucher this batch did not send (it is {notSent.Status} here).");
                    else
                        Add(null, actual.Index, "COVERAGE", "EXTRA", "WARN", null, null, key, null, "RECO-COV-007", "An actual voucher carries an ETP key for this store that this batch did not plan.");
        }

        var states = a.Expected.Where(voucher => voucher.Status is TallyVoucherStatus.Excluded or TallyVoucherStatus.Cancelled)
            .Select(voucher => new VoucherState(voucher.Status))
            .Concat(outcomes.Select(outcome => new VoucherState(outcome.Status, outcome.CoveredByReadback,
                differences.Any(d => d.VoucherSequence == outcome.Sequence && d.DifferenceType == "NOT_VERIFIABLE") && outcome.ActualIndex is not null)))
            .ToArray();
        var batch = AccountingBatchStatusRules.Derive(states, a.HasHttpAttempt);
        var run = batch is TallyBatchStatus.Reconciled or TallyBatchStatus.ReconciledWithAcceptedWarnings or TallyBatchStatus.FailedReconciliation
            ? batch : TallyBatchStatus.ReconciliationIncomplete;
        return new(RuleSetVersion, batch, run, outcomes, differences);
    }

    private static void CompareLocated(ExpectedVoucher voucher, ParsedVoucher? payload, ParsedVoucher actual, bool numberVerifiable, ToleranceSet tolerances,
        Action<int?, int?, string, string, string, string?, string?, string?, decimal?, string, string> add)
    {
        var seq = voucher.Sequence;
        var index = actual.Index;

        if (actual.IsCancelled == true)
            add(seq, index, "HEADER", "STATUS_MISMATCH", "FAIL", "active", payload is null ? null : "active", "cancelled", null, "RECO-HDR-004", "The voucher is cancelled in Tally.");
        if (actual.VoucherType is null)
            add(seq, index, "HEADER", "NOT_VERIFIABLE", "FAIL", voucher.VoucherType, payload?.VoucherType, null, null, "RECO-HDR-001", "Tally did not return the voucher type.");
        else if (!string.Equals(actual.VoucherType, voucher.VoucherType, StringComparison.Ordinal) || payload is not null && !string.Equals(payload.VoucherType, voucher.VoucherType, StringComparison.Ordinal))
            add(seq, index, "HEADER", "STATUS_MISMATCH", "FAIL", voucher.VoucherType, payload?.VoucherType, actual.VoucherType, null, "RECO-HDR-001", "The voucher type differs.");
        if (actual.VoucherDate is null)
            add(seq, index, "HEADER", "NOT_VERIFIABLE", "FAIL", Date(voucher.VoucherDate), Date(payload?.VoucherDate), null, null, "RECO-HDR-002", "Tally did not return the voucher date.");
        else if (actual.VoucherDate != voucher.VoucherDate || payload is not null && payload.VoucherDate != voucher.VoucherDate)
            add(seq, index, "HEADER", "STATUS_MISMATCH", "FAIL", Date(voucher.VoucherDate), Date(payload?.VoucherDate), Date(actual.VoucherDate), null, "RECO-HDR-002", "The voucher date differs.");
        if (numberVerifiable)
        {
            if (actual.VoucherNumber is null)
                add(seq, index, "HEADER", "NOT_VERIFIABLE", "FAIL", voucher.DocumentNumber, payload?.VoucherNumber, null, null, "RECO-HDR-003", "Tally did not return the voucher number.");
            else if (!string.Equals(actual.VoucherNumber, voucher.DocumentNumber, StringComparison.Ordinal))
                add(seq, index, "HEADER", "STATUS_MISMATCH", "FAIL", voucher.DocumentNumber, payload?.VoucherNumber, actual.VoucherNumber, null, "RECO-HDR-003", "The voucher number differs.");
        }

        var planned = voucher.Lines.GroupBy(line => line.LedgerName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => (Amount: Money(group.Sum(line => line.Debit - line.Credit)), Level: Level(group.First().BusinessEvent)), StringComparer.OrdinalIgnoreCase);
        var written = payload is null ? null : Signed(payload);
        var held = Signed(actual);

        foreach (var (ledger, plan) in planned.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var mismatch = plan.Level == "TAX" ? "TAX_MISMATCH" : "AMOUNT_MISMATCH";
            if (written is not null)
            {
                if (!written.TryGetValue(ledger, out var inFile))
                    add(seq, index, "INTEGRITY", "LEDGER_MISMATCH", "FAIL", $"{ledger} {Text(plan.Amount)}", "absent", null, null, "RECO-INT-004", "The written file lacks a planned ledger line.");
                else if (inFile is null || inFile.Value != plan.Amount)
                    add(seq, index, "INTEGRITY", inFile is null ? "NOT_VERIFIABLE" : mismatch, "FAIL", Text(plan.Amount), Text(inFile), null, inFile - plan.Amount, "RECO-INT-005", $"The written file's amount for {ledger} differs from the plan.");
            }
            if (!held.TryGetValue(ledger, out var inTally))
            {
                add(seq, index, plan.Level, "LEDGER_MISMATCH", "FAIL", $"{ledger} {Text(plan.Amount)}", null, "absent", null, "RECO-ACC-002", $"Tally's voucher has no line for {ledger}.");
                continue;
            }
            if (inTally is null)
            {
                add(seq, index, plan.Level, "NOT_VERIFIABLE", "FAIL", Text(plan.Amount), written?.GetValueOrDefault(ledger) is { } w ? Text(w) : null, null, null, "RECO-ACC-003", $"Tally's amount for {ledger} could not be read.");
                continue;
            }
            var delta = inTally.Value - plan.Amount;
            if (delta == 0) continue;
            var tolerance = tolerances.For(plan.Level);
            add(seq, index, plan.Level, mismatch, tolerance > 0 && Math.Abs(delta) <= tolerance ? "WARN" : "FAIL",
                Text(plan.Amount), written?.GetValueOrDefault(ledger) is { } v ? Text(v) : null, Text(inTally.Value), delta, "RECO-ACC-001", $"Ledger {ledger}: Tally differs from the plan by {Text(delta)}.");
        }
        foreach (var ledger in held.Keys.Where(ledger => !planned.ContainsKey(ledger)).Order(StringComparer.OrdinalIgnoreCase))
            add(seq, index, "ACCOUNTING", "LEDGER_MISMATCH", "FAIL", "absent", null, $"{ledger} {Text(held[ledger])}", null, "RECO-ACC-004", $"Tally's voucher has a line for {ledger} that ETP did not plan.");
    }

    private static string Status(string current, bool covered, int? located, IReadOnlyList<ReconciliationDifference> mine)
    {
        if (mine.Any(d => d.Severity == "FAIL" && d.DifferenceType != "NOT_VERIFIABLE")) return TallyVoucherStatus.Difference;
        if (located is not null)
            return mine.Any(d => d.DifferenceType == "NOT_VERIFIABLE") ? TallyVoucherStatus.ActualLocated
                : mine.Any(d => d.Severity == "WARN") ? TallyVoucherStatus.ReconciledWithWarnings
                : TallyVoucherStatus.Reconciled;
        // Not located and nothing proven: keep what it was, so Derive reports the batch as incomplete.
        return covered && current is TallyVoucherStatus.Reconciled or TallyVoucherStatus.ReconciledWithWarnings or TallyVoucherStatus.ActualLocated
            ? TallyVoucherStatus.Exported : current;
    }

    /// <summary>Debit-positive amount per ledger as Tally holds it; null when any line's amount could not be read.</summary>
    private static Dictionary<string, decimal?> Signed(ParsedVoucher voucher) =>
        voucher.Lines.GroupBy(line => line.LedgerName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Any(line => line.Amount is null) ? (decimal?)null : Money(group.Sum(line => -line.Amount!.Value)), StringComparer.OrdinalIgnoreCase);

    private static string Level(string businessEvent) =>
        businessEvent.StartsWith("OUTPUT_", StringComparison.Ordinal) ? "TAX"
        : businessEvent.StartsWith("TENDER_", StringComparison.Ordinal) || businessEvent is "ROUND_OFF" or "PARTY" ? "TENDER"
        : "ACCOUNTING";

    private static string StoreOf(string key) => key.Split(':') is { Length: > 1 } parts ? parts[1] : "";
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string? Text(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture);
    private static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
