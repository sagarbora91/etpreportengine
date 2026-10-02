namespace Etp.Reporting.Application.Accounting;

public static class TallyRecoveryAction
{
    /// <summary>Read back again from the right company and compare; nothing in Tally is touched.</summary>
    public const string ReadBackAgain = "READ_BACK_AGAIN";
    /// <summary>The accountant corrects the voucher in Tally by hand; ETP never edits a Tally voucher.</summary>
    public const string ManualCorrectionInTally = "MANUAL_CORRECTION_IN_TALLY";
    /// <summary>A new batch with a Credit Note reversing the voucher Tally holds and a fresh voucher at the next revision.</summary>
    public const string ReverseAndReissue = "REVERSE_AND_REISSUE";
    /// <summary>The Owner accepts the warnings with a reason.</summary>
    public const string AcceptWithReason = "ACCEPT_WITH_REASON";
}

/// <param name="NeedsSeparateApproval">True for a reversal, which also needs its own revision approval (plan task 20).</param>
public sealed record TallyRecoveryStep(int VoucherSequence, string Action, string Reason, IReadOnlyList<string> DifferenceTypes, bool NeedsSeparateApproval);

/// <summary>A proposal only. The Owner approves the whole plan before any step is taken; the whole batch is never
/// resent, and ETP never deletes, alters or re-dates a voucher in Tally.</summary>
public sealed record TallyRecoveryPlan(long? RunId, string RuleSetVersion, IReadOnlyList<TallyRecoveryStep> Steps)
{
    public bool ResendsWholeBatch => false;
}

/// <summary>Plan task 22: one step per voucher that is not RECONCILED, chosen from the differences of a run.
/// Resending a voucher (RESEND_VOUCHER) needs proven absence and the connected mode of Slice 7d, so it is never proposed here.</summary>
public static class TallyRecoveryPlanBuilder
{
    private static readonly string[] CoverageProblems = ["MISSING", "WRONG_COMPANY", "NOT_VERIFIABLE"];
    private static readonly string[] TallySideProblems = ["DUPLICATE", "AMBIGUOUS_MATCH", "STATUS_MISMATCH", "LEDGER_MISMATCH", "AMOUNT_MISMATCH", "TAX_MISMATCH", "QUANTITY_MISMATCH", "ACTUAL_CHANGED"];

    public static TallyRecoveryPlan Build(TallyReconciliationResult result, long? runId = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        var steps = new List<TallyRecoveryStep>();
        foreach (var voucher in result.Vouchers.Where(v => v.Status != TallyVoucherStatus.Reconciled).OrderBy(v => v.Sequence))
        {
            var mine = result.Differences.Where(d => d.VoucherSequence == voucher.Sequence).ToArray();
            var types = mine.Select(d => d.DifferenceType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var failures = mine.Where(d => d.Severity == "FAIL").Select(d => d.DifferenceType).ToHashSet(StringComparer.Ordinal);

            TallyRecoveryStep step;
            if (failures.Contains("SOURCE_CHANGED"))
                step = new(voucher.Sequence, TallyRecoveryAction.ReverseAndReissue,
                    "ETP's source figures changed after the voucher was written. Reverse the voucher Tally holds with a Credit Note and issue a fresh one at the next revision.", types, true);
            else if (!voucher.CoveredByReadback || failures.Overlaps(CoverageProblems) && !failures.Overlaps(TallySideProblems) || voucher.Status == TallyVoucherStatus.ActualLocated)
                step = new(voucher.Sequence, TallyRecoveryAction.ReadBackAgain,
                    "What Tally holds for this voucher is not proven yet. Export the full Day Book for its date from the right company and compare again.", types, false);
            else if (failures.Overlaps(TallySideProblems))
                step = new(voucher.Sequence, TallyRecoveryAction.ManualCorrectionInTally,
                    "Tally's voucher differs from ETP's plan. With the accountant, correct it in Tally by hand, then read back and compare again.", types, false);
            else if (voucher.Status == TallyVoucherStatus.ReconciledWithWarnings)
                step = new(voucher.Sequence, TallyRecoveryAction.AcceptWithReason,
                    "Only warnings remain. The Owner can accept each with a reason.", types, false);
            else
                step = new(voucher.Sequence, TallyRecoveryAction.ReadBackAgain,
                    "Nothing has been proven for this voucher yet. Read back and compare again.", types, false);
            steps.Add(step);
        }
        return new(runId, result.RuleSetVersion, steps);
    }
}
