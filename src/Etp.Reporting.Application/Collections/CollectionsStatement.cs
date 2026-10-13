using System.Globalization;

namespace Etp.Reporting.Application.Collections;

/// <summary>How a statement line reads. Every state is a fact about the evidence, never a guess: UPI and card lines are
/// "totals only, not matched yet" until line-by-line matching (8b) exists.</summary>
public enum CollectionsLineState
{
    DepositInTransit,
    DepositCredited,
    DepositCreditedDifferentAmount,
    CreditedNotOnSheet,
    NoDepositRecorded,
    TotalsOnly,
    NoSettlementLinesYet,
    SettlementWithoutSales,
    NoOutsideEvidenceExpected,
    ProviderNotSetUp,
    TenderCodeNotMapped
}

/// <summary>One line of "Collections: expected vs evidence" (store x business date x mode).</summary>
/// <param name="Expected">What should have reached the bank or provider: the deposit the sheet says was sent (Cash) or the
/// tender total (other modes); null when nothing is expected.</param>
/// <param name="Evidence">The outside evidence: the credited deposit, or the provider's settlement total for that date.</param>
/// <param name="Difference">Evidence minus expected, when both are known.</param>
/// <param name="TenderTotal">Cash: the day's cash tenders, for information (the deposit is what is compared).</param>
public sealed record CollectionsStatementLine(
    string StoreCode,
    DateOnly BusinessDate,
    string Mode,
    string? ProviderKey,
    decimal? Expected,
    decimal? Evidence,
    decimal? Difference,
    CollectionsLineState State,
    string ShownAs,
    decimal? TenderTotal = null,
    DateOnly? CreditedDate = null,
    string? BankReference = null,
    int? AgeDays = null);

public sealed record CollectionsStatement(IReadOnlyList<CollectionsStatementLine> Lines, bool WithoutBankReferences);

/// <summary>A row of dbo.v_collection_expected.</summary>
public sealed record ExpectedCollection(
    string StoreCode, DateOnly BusinessDate, string Mode, string? ProviderKey, string? UnmappedTenderCode, string EvidenceKind, decimal Amount);

/// <summary>A row of dbo.v_collection_evidence.</summary>
public sealed record SettlementEvidence(string StoreCode, DateOnly BusinessDate, string ProviderKey, decimal? GrossTotal, decimal? NetTotal, long LineCount);

/// <summary>A row of dbo.v_cash_deposit_status.</summary>
public sealed record CashDepositStatus(
    string StoreCode, DateOnly DepositDate, decimal? SentAmount, decimal? CreditedAmount, DateOnly? CreditedDate, string? BankReference);

/// <summary>Builds the statement from the three object views (pure, so the wording and the arithmetic are unit-tested).
/// Text constants are the words the report shows (U4 renders them as they are).</summary>
public static class CollectionsStatementBuilder
{
    public const string TotalsOnlyText = "Totals only, not matched yet";
    public const string NoOutsideEvidenceText = "No outside evidence expected";
    public const string ProviderNotSetUpText = "Provider not set up (D19)";

    // Evidence kinds of dbo.v_collection_expected.
    public const string CashDepositKind = "CASH_DEPOSIT";
    public const string NoEvidenceKind = "NONE";
    public const string ProviderSettlementKind = "PROVIDER_SETTLEMENT";
    public const string ProviderNotSetKind = "PROVIDER_NOT_SET";
    public const string UnmappedKind = "UNMAPPED";

    private static readonly string[] ModeOrder = ["Cash", "UPI", "Card", "Bank", "Service UPI", "Service Card", "Service Cash", "CN", "Gift Card", "TC"];

    public static IReadOnlyList<CollectionsStatementLine> Build(
        IEnumerable<ExpectedCollection> expected,
        IEnumerable<SettlementEvidence> evidence,
        IEnumerable<CashDepositStatus> deposits,
        DateOnly asOf,
        bool withoutBankReferences = false)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(deposits);
        var expectedRows = expected.ToList();
        var lines = new List<CollectionsStatementLine>();

        // Cash: the deposit is the evidence; the day's cash tenders are shown for information.
        var cashTenders = expectedRows.Where(row => row.EvidenceKind == CashDepositKind)
            .GroupBy(row => (row.StoreCode, BusinessDate: row.BusinessDate))
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
        var depositByDay = deposits.GroupBy(row => (row.StoreCode, BusinessDate: row.DepositDate)).ToDictionary(group => group.Key, group => group.First());
        foreach (var key in cashTenders.Keys.Union(depositByDay.Keys))
        {
            var tender = cashTenders.TryGetValue(key, out var total) ? total : (decimal?)null;
            depositByDay.TryGetValue(key, out var deposit);
            lines.Add(CashLine(key.StoreCode, key.BusinessDate, tender, deposit, asOf, withoutBankReferences));
        }

        foreach (var row in expectedRows.Where(row => row.EvidenceKind is NoEvidenceKind or ProviderNotSetKind or UnmappedKind))
        {
            var (state, text) = row.EvidenceKind switch
            {
                NoEvidenceKind => (CollectionsLineState.NoOutsideEvidenceExpected, NoOutsideEvidenceText),
                ProviderNotSetKind => (CollectionsLineState.ProviderNotSetUp, ProviderNotSetUpText),
                _ => (CollectionsLineState.TenderCodeNotMapped,
                    $"Payment code {row.UnmappedTenderCode ?? "(blank)"} is not mapped to a mode. Map it in Settings > Stores & masters > Tender modes.")
            };
            lines.Add(new(row.StoreCode, row.BusinessDate, row.Mode, row.ProviderKey, row.Amount, null, null, state, text));
        }

        // UPI, card and the like: provider totals for the store and date, one line per provider.
        var providerExpected = expectedRows.Where(row => row.EvidenceKind == ProviderSettlementKind && row.ProviderKey is not null)
            .GroupBy(row => (row.StoreCode, row.BusinessDate, ProviderKey: row.ProviderKey!))
            .ToDictionary(group => group.Key, group => (Amount: group.Sum(row => row.Amount),
                Mode: string.Join(" + ", group.Select(row => row.Mode).Distinct(StringComparer.Ordinal).OrderBy(ModeRank))));
        var providerEvidence = evidence.GroupBy(row => (row.StoreCode, row.BusinessDate, row.ProviderKey))
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var key in providerExpected.Keys.Union(providerEvidence.Keys))
        {
            var hasExpected = providerExpected.TryGetValue(key, out var due);
            providerEvidence.TryGetValue(key, out var proof);
            var amount = proof is null ? null : proof.GrossTotal ?? proof.NetTotal;
            var netOnly = proof is not null && proof.GrossTotal is null && proof.NetTotal is not null;
            if (!hasExpected)
            {
                lines.Add(new(key.StoreCode, key.BusinessDate, "Provider " + key.ProviderKey, key.ProviderKey, null, amount, null,
                    CollectionsLineState.SettlementWithoutSales, "Settlement lines with no sales tender on this day. " + TotalsOnlyText + "."));
                continue;
            }
            if (proof is null || amount is null)
            {
                lines.Add(new(key.StoreCode, key.BusinessDate, due.Mode, key.ProviderKey, due.Amount, null, null,
                    CollectionsLineState.NoSettlementLinesYet, "No settlement lines for this day yet. " + TotalsOnlyText + "."));
                continue;
            }
            lines.Add(new(key.StoreCode, key.BusinessDate, due.Mode, key.ProviderKey, due.Amount, amount, amount.Value - due.Amount,
                CollectionsLineState.TotalsOnly, netOnly ? TotalsOnlyText + " (the file gives amounts after fees)." : TotalsOnlyText + "."));
        }

        return lines
            .OrderBy(line => line.StoreCode, StringComparer.Ordinal)
            .ThenBy(line => line.BusinessDate)
            .ThenBy(line => ModeRank(line.Mode.Split(" + ")[0]))
            .ThenBy(line => line.Mode, StringComparer.Ordinal)
            .ThenBy(line => line.ProviderKey, StringComparer.Ordinal)
            .ToList();
    }

    private static CollectionsStatementLine CashLine(string store, DateOnly date, decimal? tender, CashDepositStatus? deposit, DateOnly asOf, bool hideReference)
    {
        var reference = hideReference ? null : deposit?.BankReference;
        if (deposit?.SentAmount is { } sent)
        {
            if (deposit.CreditedAmount is { } credited && deposit.CreditedDate is { } on)
            {
                var same = credited == sent;
                return new(store, date, "Cash", null, sent, credited, credited - sent,
                    same ? CollectionsLineState.DepositCredited : CollectionsLineState.DepositCreditedDifferentAmount,
                    same ? $"Bank credited on {Day(on)}" : $"Bank credited on {Day(on)}, a different amount from the deposit sent",
                    tender, on, reference);
            }
            var age = Math.Max(0, asOf.DayNumber - date.DayNumber);
            return new(store, date, "Cash", null, sent, null, null, CollectionsLineState.DepositInTransit,
                age == 1 ? "Deposit in transit, 1 day" : $"Deposit in transit, {age.ToString(CultureInfo.InvariantCulture)} days",
                tender, AgeDays: age);
        }
        if (deposit?.CreditedAmount is { } orphan && deposit.CreditedDate is { } creditedOn)
            return new(store, date, "Cash", null, null, orphan, null, CollectionsLineState.CreditedNotOnSheet,
                $"Bank credited on {Day(creditedOn)}, but the daily sheet has no deposit for this day", tender, creditedOn, reference);
        return new(store, date, "Cash", null, null, null, null, CollectionsLineState.NoDepositRecorded,
            "No deposit on the daily sheet for this day", tender);
    }

    private static string Day(DateOnly date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    private static int ModeRank(string mode)
    {
        var index = Array.IndexOf(ModeOrder, mode);
        return index < 0 ? ModeOrder.Length : index;
    }
}

/// <summary>R-DEP-1 (112-WAVE-PLAN D-b): a deposit in transit and a bank credit line of the same amount, on an account that
/// takes cash deposits for that store (or a shared one), dated 0-3 days after the deposit. It only proposes; the Owner accepts.</summary>
public static class DepositProposalRule
{
    public const string RuleId = "R-DEP-1";
    public const int WindowDays = 3;

    /// <param name="lines">Credit lines of the cash-deposit accounts that no active confirmation uses yet.</param>
    public static IReadOnlyList<DepositProposal> Propose(IEnumerable<DepositInTransit> deposits, IEnumerable<DepositCandidateLine> lines)
    {
        ArgumentNullException.ThrowIfNull(deposits);
        ArgumentNullException.ThrowIfNull(lines);
        var candidates = lines.ToList();
        var pairs = new List<(DepositInTransit Deposit, DepositCandidateLine Line)>();
        foreach (var deposit in deposits)
            pairs.AddRange(candidates.Where(line => Fits(deposit, line)).Select(line => (deposit, line)));

        var perDeposit = pairs.GroupBy(pair => (pair.Deposit.StoreCode, pair.Deposit.DepositDate)).ToDictionary(group => group.Key, group => group.Count());
        var perLine = pairs.GroupBy(pair => pair.Line.BankLineId).ToDictionary(group => group.Key, group => group.Count());
        return pairs
            .Select(pair => new DepositProposal(pair.Deposit, pair.Line,
                perDeposit[(pair.Deposit.StoreCode, pair.Deposit.DepositDate)] > 1 || perLine[pair.Line.BankLineId] > 1))
            .OrderBy(proposal => proposal.Deposit.DepositDate)
            .ThenBy(proposal => proposal.Deposit.StoreCode, StringComparer.Ordinal)
            .ThenBy(proposal => proposal.Line.LineDate)
            .ThenBy(proposal => proposal.Line.BankLineId)
            .ToList();
    }

    public static bool Fits(DepositInTransit deposit, DepositCandidateLine line)
    {
        ArgumentNullException.ThrowIfNull(deposit);
        ArgumentNullException.ThrowIfNull(line);
        var days = line.LineDate.DayNumber - deposit.DepositDate.DayNumber;
        return line.CreditAmount == deposit.Amount
            && days is >= 0 and <= WindowDays
            && (line.AccountStoreCode is null || string.Equals(line.AccountStoreCode, deposit.StoreCode, StringComparison.Ordinal));
    }
}
