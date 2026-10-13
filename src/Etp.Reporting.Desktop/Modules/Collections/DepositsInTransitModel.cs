extern alias EtpApplication;

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
    public string AgeWord => Deposit.AgeDays switch { <= 0 => "Today", 1 => "1 day", var days => $"{days} days" };

    /// <summary>The age chip: quiet while the bank is likely still to credit it (R-DEP-1's window), then amber, then red.</summary>
    public StatusTone AgeTone => Deposit.AgeDays > CriticalAfterDays ? StatusTone.Critical : Deposit.AgeDays > WarnAfterDays ? StatusTone.Warning : StatusTone.Neutral;

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
    private readonly Func<ICashDepositService> service;
    private int revision;

    public DepositsInTransitModel(Func<ICashDepositService> service, ShellAccess access, string? storeCode)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        Access = access ?? throw new ArgumentNullException(nameof(access));
        StoreCode = string.IsNullOrWhiteSpace(storeCode) ? null : storeCode.Trim().ToUpperInvariant();
    }

    public ShellAccess Access { get; }
    public string? StoreCode { get; }
    public bool IsAllowed => CollectionsScreens.CanSeeDeposits(Access);
    public bool CanAccept => CollectionsScreens.CanConfirmDeposits(Access);
    public string? RefusalText => IsAllowed ? null : Access.HasAssignedRole ? CollectionsScreens.DepositsRefusedText : CollectionsScreens.NotSignedInText;
    public IReadOnlyList<DepositRow> Rows { get; private set; } = [];
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
            if (mine != revision) return;
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

    private void Say(StatusTone tone, string text)
    {
        NoticeTone = tone; NoticeText = text; Changed?.Invoke();
    }

    private void Fail(string text)
    {
        Rows = []; HasLoaded = false;
        StatusText = text; StatusTone = StatusTone.Critical;
        Changed?.Invoke();
    }
}
