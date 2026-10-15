extern alias EtpApplication;

using System.Globalization;
using Etp.Reporting.Reporting;
using EtpApplication::Etp.Reporting.Application.Collections;

namespace Etp.Reporting.Desktop.Modules.Collections;

/// <summary>One row of Deposits in transit: a deposit the daily sheet says was sent and the bank has not been confirmed to
/// credit, and (for the Owner) one bank line R-DEP-1 proposes for it. A deposit with several candidate lines has one row per
/// line, each marked "Several fit", so the Owner chooses by accepting a specific line.</summary>
public sealed class DepositRow(DepositInTransit deposit, DepositProposal? proposal)
{
    public const int WarnAfterDays = 3;
    public const int CriticalAfterDays = 7;

    public DepositInTransit Deposit { get; } = deposit;
    public DepositProposal? Proposal { get; } = proposal;
    public string Store => Deposit.StoreCode;
    public string Date => IndianFormat.Date(Deposit.DepositDate);
    public string Amount => IndianFormat.MoneyCell(Deposit.Amount);
    public string AgeWord => (Deposit.IsOverdue ? "Overdue, " : string.Empty) + Deposit.AgeDays switch { <= 0 => "Today", 1 => "1 day", var days => $"{days} days" };

    /// <summary>The age chip. With the Owner's Cash settlement rule (1.12.1 P2, Settings > Collections setup > Settlement rules):
    /// red and "Overdue" once the deposit is older than the rule's "Overdue after", quiet before. Without a rule nothing is
    /// overdue (D20) and the chip only shows age: quiet while the bank is likely still to credit it (R-DEP-1's window), then
    /// amber, then red.</summary>
    public StatusTone AgeTone => Deposit.OverdueAfterDays is not null
        ? (Deposit.IsOverdue ? StatusTone.Critical : StatusTone.Neutral)
        : Deposit.AgeDays > CriticalAfterDays ? StatusTone.Critical : Deposit.AgeDays > WarnAfterDays ? StatusTone.Warning : StatusTone.Neutral;

    public bool IsAmbiguous => Proposal?.IsAmbiguous == true;

    /// <summary>"₹12,500.00 on 01 Aug 2026 · account name · ref DEP0001" - the account name, never its number; the bank reference is shown
    /// as it is (it is how the Owner finds the line on the statement). An em dash without a proposal.</summary>
    public string BankLine => Proposal is null ? CollectionsScreens.MissingValue
        : $"{IndianFormat.MoneyExact(Proposal.Line.CreditAmount)} on {IndianFormat.Date(Proposal.Line.LineDate)} · {CollectionsScreens.MaskDigits(Proposal.Line.AccountName)}"
          + (string.IsNullOrWhiteSpace(Proposal.Line.BankReference) ? string.Empty : $" · ref {Proposal.Line.BankReference}");

    /// <summary>The bank's narration for the line. Only the Owner's rows can carry it; the Store Manager's never do.</summary>
    public string Narration => string.IsNullOrWhiteSpace(Proposal?.Line.Narration) ? CollectionsScreens.MissingValue : Proposal!.Line.Narration!;

    public string ProposalWord => Proposal is null ? "No bank line yet" : IsAmbiguous ? "Several fit" : "One bank line fits";
    public StatusTone ProposalTone => Proposal is null ? StatusTone.Neutral : IsAmbiguous ? StatusTone.Warning : StatusTone.Information;
}

/// <summary>1.12.1 P2: one active bank credit on the Owner's "Marked as credited" list, where a mistake is withdrawn with a reason.
/// The account is named, never numbered (digit runs masked).</summary>
public sealed class CreditedRow(CreditedDeposit credit)
{
    public CreditedDeposit Credit { get; } = credit;
    public string Store => Credit.StoreCode;
    public string DepositDate => IndianFormat.Date(Credit.DepositDate);
    public string Sent => IndianFormat.MoneyCell(Credit.SentAmount);
    public string Credited => IndianFormat.MoneyCell(Credit.CreditedAmount);
    public string CreditedOn => IndianFormat.Date(Credit.CreditedDate);
    public string Account => CollectionsScreens.MaskDigits(Credit.AccountName);
    public string BankReference => string.IsNullOrWhiteSpace(Credit.BankReference) ? CollectionsScreens.MissingValue : Credit.BankReference;
    public string Reason => Credit.Reason;
    public string HowWord => Credit.RecordedByHand ? "Recorded by hand" : "From the bank line";
    public StatusTone HowTone => Credit.RecordedByHand ? StatusTone.Information : StatusTone.Success;

    /// <summary>The sheet's deposit and the credit differ (or the sheet has none): said in words, not only by the numbers.</summary>
    public string Agreement => Credit.SentAmount is null ? "Not on the daily sheet"
        : Credit.SentAmount.Value == Credit.CreditedAmount ? "Same amount" : "Different amount";
}

/// <summary>What the Owner typed in "Record by hand": the bank credited a deposit that has no bank line in ETP (the statement
/// layout is not set up yet, or the credit is on a passbook). The amount is text so the model can say what is wrong with it.</summary>
public sealed record ManualCreditEntry(DateOnly? CreditedDate, int? BankAccountId, string AmountText, string? BankReference, string Reason);

/// <summary>
/// Reports > Deposits in transit (List template; plan U4), oldest first, with an age chip. The Owner also sees R-DEP-1's
/// proposed bank line for each deposit and presses "Bank credited" to accept it (a reason is required, the service writes the
/// audit row; allowed on a finalised day). A Store Manager sees the same deposits without any proposal or narration (the
/// column is denied to the role in SQL). A Viewer has no access. Pure presentation logic over <see cref="ICashDepositService"/>.
/// </summary>
public sealed class DepositsInTransitModel
{
    public const string DefaultReason = "Seen on the bank statement";
    public const string NothingInTransitText = "No deposits in transit. Every cash deposit on a daily sheet has been marked as credited by the bank.";
    public const string OwnerAcceptsText = "Only the Owner can mark a deposit as credited by the bank.";
    public const string DefaultManualReason = "Seen on the bank passbook";
    public const string DefaultWithdrawReason = "Marked as credited by mistake";
    public const string NoAccountText = "There is no active bank account for this store. Add one in Settings > Collections setup first.";

    /// <summary>The "Marked as credited" list covers deposits of the last 62 days (two months), newest first.</summary>
    public const int CreditedListDays = 62;
    private readonly Func<ICashDepositService> service;
    private readonly Func<DateOnly> today;
    private int revision;

    public DepositsInTransitModel(Func<ICashDepositService> service, ShellAccess access, string? storeCode, Func<DateOnly>? today = null)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        Access = access ?? throw new ArgumentNullException(nameof(access));
        StoreCode = string.IsNullOrWhiteSpace(storeCode) ? null : storeCode.Trim().ToUpperInvariant();
        this.today = today ?? (() => DateOnly.FromDateTime(DateTime.Today));
    }

    public ShellAccess Access { get; }
    public string? StoreCode { get; }
    public bool IsAllowed => CollectionsScreens.CanSeeDeposits(Access);
    public bool CanAccept => CollectionsScreens.CanConfirmDeposits(Access);
    public string? RefusalText => IsAllowed ? null : Access.HasAssignedRole ? CollectionsScreens.DepositsRefusedText : CollectionsScreens.NotSignedInText;
    public IReadOnlyList<DepositRow> Rows { get; private set; } = [];

    /// <summary>1.12.1 P2 (Owner only): the active bank credits of the last <see cref="CreditedListDays"/> days.</summary>
    public IReadOnlyList<CreditedRow> Credited { get; private set; } = [];
    public bool HasLoaded { get; private set; }
    public string StatusText { get; private set; } = string.Empty;
    public StatusTone StatusTone { get; private set; } = StatusTone.Neutral;
    public event Action? Changed;

    /// <summary>The Notice under the toolbar: why nothing can be accepted for this role, or the result of the last accept.</summary>
    public string NoticeText { get; private set; } = string.Empty;
    public StatusTone NoticeTone { get; private set; } = StatusTone.Neutral;

    public string FooterText
    {
        get
        {
            if (!HasLoaded) return string.Empty;
            var deposits = Rows.Select(row => row.Deposit).Distinct().ToList();
            return $"{IndianFormat.Plural(deposits.Count, "deposit")} in transit · {IndianFormat.MoneyExact(deposits.Sum(deposit => deposit.Amount))} · oldest first";
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAllowed) { Fail(RefusalText!); return; }
        var mine = ++revision;
        StatusText = "Loading..."; StatusTone = StatusTone.Neutral; NoticeText = string.Empty; Changed?.Invoke();
        try
        {
            var stores = StoreCode is null ? null : (IReadOnlyList<string>)[StoreCode];
            var transit = await service().LoadInTransitAsync(stores, cancellationToken).ConfigureAwait(true);
            // Proposals read the bank's narration, which is the Owner's alone: a Store Manager never asks for them.
            var proposals = CanAccept ? await service().LoadProposalsAsync(stores, cancellationToken).ConfigureAwait(true) : [];
            // The credits already marked are the Owner's too (the table is the Owner's; withdrawing is Owner-only).
            var now = today();
            var credited = CanAccept ? await service().LoadCreditedAsync(stores, now.AddDays(-CreditedListDays), now, cancellationToken).ConfigureAwait(true) : [];
            if (mine != revision) return;
            Credited = credited.Select(credit => new CreditedRow(credit)).ToList();
            var rows = new List<DepositRow>();
            foreach (var deposit in transit.OrderBy(item => item.DepositDate).ThenBy(item => item.StoreCode, StringComparer.Ordinal))
            {
                var mineProposals = proposals.Where(proposal => proposal.Deposit.StoreCode == deposit.StoreCode && proposal.Deposit.DepositDate == deposit.DepositDate).ToList();
                if (mineProposals.Count == 0) rows.Add(new DepositRow(deposit, null));
                else rows.AddRange(mineProposals.Select(proposal => new DepositRow(deposit, proposal)));
            }
            Rows = rows;
            HasLoaded = true;
            StatusText = rows.Count == 0 ? NothingInTransitText : string.Empty;
            StatusTone = StatusTone.Neutral;
            if (!CanAccept && rows.Count > 0) { NoticeText = OwnerAcceptsText; NoticeTone = StatusTone.Information; }
            Changed?.Invoke();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            if (mine == revision) Fail(CollectionsScreens.Sentence(exception));
        }
    }

    /// <summary>"Bank credited" is available for a row that has a proposed bank line, to the Owner only.</summary>
    public bool CanAcceptRow(DepositRow? row) => CanAccept && row?.Proposal is not null;

    /// <summary>Why the button is off, for the disabled button's tooltip (B6).</summary>
    public string AcceptDisabledReason(DepositRow? row) =>
        !CanAccept ? OwnerAcceptsText :
        row is null ? "Select a deposit first." :
        row.Proposal is null ? "No bank line fits this deposit yet. Import the bank statement first." : string.Empty;

    /// <summary>Accepts the row's proposed bank line. The reason is required; a refused or stale accept shows the service's
    /// sentence and the list is read again.</summary>
    public async Task<bool> AcceptAsync(DepositRow? row, string? reason, CancellationToken cancellationToken = default)
    {
        if (!CanAcceptRow(row)) return false;
        if (string.IsNullOrWhiteSpace(reason)) { Say(StatusTone.Warning, "Give a reason, for example: " + DefaultReason + "."); return false; }
        var proposal = row!.Proposal!;
        try
        {
            await service().AcceptProposalAsync(new DepositProposalAcceptance(row.Store, row.Deposit.DepositDate, proposal.Line.BankLineId, reason.Trim()), cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            // The list is read again (a stale line is the usual cause); the service's sentence is the news, so it is shown after.
            try { await LoadAsync(cancellationToken).ConfigureAwait(true); } catch (OperationCanceledException) { throw; } catch (Exception) { /* the sentence below is the news */ }
            Say(StatusTone.Critical, CollectionsScreens.Sentence(exception));
            return false;
        }
        await LoadAsync(cancellationToken).ConfigureAwait(true);
        Say(StatusTone.Success, $"Marked as credited: {row.Store}, {row.Date}, {IndianFormat.MoneyExact(row.Deposit.Amount)}.");
        return true;
    }

    // ---- 1.12.1 P2: record a bank credit by hand, and withdraw one ------------------------------------------------------

    /// <summary>"Record by hand" is the Owner's, for any deposit in transit (with or without a proposed line).</summary>
    public bool CanRecordRow(DepositRow? row) => CanAccept && row is not null;

    public string RecordDisabledReason(DepositRow? row) => !CanAccept ? OwnerAcceptsText : row is null ? "Select a deposit first." : string.Empty;

    /// <summary>The accounts the credit can be recorded on (the store's own or shared, active).</summary>
    public async Task<IReadOnlyList<DepositAccount>> LoadAccountsAsync(DepositRow row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!CanAccept) return [];
        try
        {
            var accounts = await service().LoadDepositAccountsAsync(row.Store, cancellationToken).ConfigureAwait(true);
            if (accounts.Count == 0) Say(StatusTone.Warning, NoAccountText);
            return accounts;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { Say(StatusTone.Critical, CollectionsScreens.Sentence(exception)); return []; }
    }

    /// <summary>What the "Record by hand" form starts with: the sheet's amount, credited the day after (not after today), the
    /// first account, the usual reason.</summary>
    public ManualCreditEntry Suggestion(DepositRow row, IReadOnlyList<DepositAccount> accounts)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(accounts);
        var next = row.Deposit.DepositDate.AddDays(1);
        var now = today();
        return new(next > now ? row.Deposit.DepositDate : next, accounts.FirstOrDefault()?.BankAccountId,
            row.Deposit.Amount.ToString("0.00", CultureInfo.InvariantCulture), null, DefaultManualReason);
    }

    /// <summary>Records the bank credit the Owner saw for the row's deposit. Every field is checked here first, in a plain
    /// sentence; the service checks again and writes the audit row (CashDepositConfirmed).</summary>
    public async Task<bool> RecordByHandAsync(DepositRow? row, ManualCreditEntry? entry, CancellationToken cancellationToken = default)
    {
        if (!CanRecordRow(row) || entry is null) return false;
        if (ManualProblem(row!, entry) is { } problem) { Say(StatusTone.Warning, problem); return false; }
        var amount = ParseAmount(entry.AmountText)!.Value;
        try
        {
            await service().ConfirmManuallyAsync(new ManualDepositConfirmation(row!.Store, row.Deposit.DepositDate, entry.CreditedDate!.Value, entry.BankAccountId!.Value,
                amount, string.IsNullOrWhiteSpace(entry.BankReference) ? null : entry.BankReference.Trim(), entry.Reason.Trim()), cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            try { await LoadAsync(cancellationToken).ConfigureAwait(true); } catch (OperationCanceledException) { throw; } catch (Exception) { /* the sentence below is the news */ }
            Say(StatusTone.Critical, CollectionsScreens.Sentence(exception));
            return false;
        }
        await LoadAsync(cancellationToken).ConfigureAwait(true);
        var different = amount != row.Deposit.Amount ? $" The bank credited {IndianFormat.MoneyExact(amount)}, not the {IndianFormat.MoneyExact(row.Deposit.Amount)} on the sheet." : string.Empty;
        Say(different.Length > 0 ? StatusTone.Warning : StatusTone.Success,
            $"Recorded as credited by hand: {row.Store}, {row.Date}, on {IndianFormat.Date(entry.CreditedDate!.Value)}.{different}");
        return true;
    }

    /// <summary>The first thing wrong with a "Record by hand" entry, as one sentence; null when it can be sent.</summary>
    public string? ManualProblem(DepositRow row, ManualCreditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.CreditedDate is not { } credited) return "Enter the date the bank credited it.";
        if (credited < row.Deposit.DepositDate) return "The bank cannot credit a deposit before it was sent.";
        if (credited > today()) return "The bank cannot have credited it on a day that has not come yet. Check the credited date.";
        if (entry.BankAccountId is null) return "Choose the bank account that was credited.";
        if (ParseAmount(entry.AmountText) is not { } amount || amount <= 0) return "Enter the amount the bank credited, for example 12500 or 12,500.00.";
        if (decimal.Round(amount, 2) != amount) return "Enter the amount in rupees and paise (two decimals at most).";
        if (entry.BankReference is { Length: > 100 }) return "Keep the bank reference to 100 characters.";
        if (string.IsNullOrWhiteSpace(entry.Reason)) return "Give a reason, for example: " + DefaultManualReason + ".";
        if (entry.Reason.Trim().Length > 500) return "Keep the reason to 500 characters.";
        return null;
    }

    /// <summary>"12500", "12,500.00" or "1,25,000" (Indian grouping): commas are only grouping.</summary>
    public static decimal? ParseAmount(string? text)
    {
        var value = text?.Trim().Replace(",", string.Empty, StringComparison.Ordinal).Replace("₹", string.Empty, StringComparison.Ordinal).Trim();
        return !string.IsNullOrEmpty(value) && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ? amount : null;
    }

    public bool CanWithdraw(CreditedRow? row) => CanAccept && row is not null;

    public string WithdrawDisabledReason(CreditedRow? row) => !CanAccept ? OwnerAcceptsText : row is null ? "Select a bank credit first." : string.Empty;

    /// <summary>Withdraws a bank credit marked by mistake (a reason is required; nothing is deleted, the history keeps both). The
    /// deposit is in transit again and a bank line it used is free for another proposal.</summary>
    public async Task<bool> WithdrawAsync(CreditedRow? row, string? reason, CancellationToken cancellationToken = default)
    {
        if (!CanWithdraw(row)) return false;
        if (string.IsNullOrWhiteSpace(reason)) { Say(StatusTone.Warning, "Give a reason, for example: " + DefaultWithdrawReason + "."); return false; }
        try { await service().WithdrawAsync(row!.Credit.ConfirmationId, reason.Trim(), cancellationToken).ConfigureAwait(true); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            try { await LoadAsync(cancellationToken).ConfigureAwait(true); } catch (OperationCanceledException) { throw; } catch (Exception) { /* the sentence below is the news */ }
            Say(StatusTone.Critical, CollectionsScreens.Sentence(exception));
            return false;
        }
        await LoadAsync(cancellationToken).ConfigureAwait(true);
        Say(StatusTone.Success, $"Withdrawn: {row.Store}, {row.DepositDate}. The deposit is in transit again.");
        return true;
    }

    private void Say(StatusTone tone, string text)
    {
        NoticeTone = tone; NoticeText = text; Changed?.Invoke();
    }

    private void Fail(string text)
    {
        Rows = []; Credited = []; HasLoaded = false;
        StatusText = text; StatusTone = StatusTone.Critical;
        Changed?.Invoke();
    }
}
