namespace Etp.Reporting.Application.Collections;

// 1.12 wave 1, lane D2 "collections engine" (112-WAVE-PLAN D-b; master plan Phase 8a; decision 44 #7). Contracts only: the
// screens (Settings > Collections setup, Import > Bank and settlement files, Today > Bank credited, Reports > Collections and
// Deposits in transit) are wave 2 (U4). Matching, cases and periods (8b-8d) wait for the D19 samples.

/// <summary>A bank account that receives shop money. Only the last four digits are ever stored.</summary>
public sealed record BankAccountRow(
    int Id, string AccountKey, string DisplayName, string BankName, string? AccountLast4, string? StoreCode,
    bool TakesCashDeposits, bool Active, string ModifiedBy, DateTime ModifiedUtc);

/// <summary>A collection provider (Airpay, PhonePe, the card acquirer...) and the D11 mode it settles.</summary>
public sealed record CollectionProviderRow(
    string ProviderKey, string DisplayName, string Mode, string SettlesVia, string StatementKind, string Granularity,
    int? BankAccountId, decimal? RoundingTolerance, bool Active, string ModifiedBy, DateTime ModifiedUtc);

/// <summary>A merchant (and optional terminal) id that routes a provider's settlement lines to a store.</summary>
public sealed record MerchantIdRow(int Id, string ProviderKey, string MerchantId, string? TerminalId, string StoreCode, bool Active);

/// <summary>A settlement rule (D20). None exist until the Owner adds them; no rule means nothing becomes overdue.</summary>
public sealed record CollectionPolicyRow(
    int Id, string? StoreCode, string Mode, string? ProviderKey, int? SettlementWindowDays, int? OverdueAfterDays,
    decimal? CashTolerance, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

/// <summary>An Owner-made column map for one bank account's or provider's export.</summary>
public sealed record StatementLayoutRow(
    int Id, string LayoutKey, string DisplayName, string LayoutKind, int? BankAccountId, string? ProviderKey,
    string HeaderSignatureSha256, string? SheetName, int HeaderRow, int SkipFooterRows, string MappingJson, int Version, bool Active);

/// <summary>A tender code's provider and whether it is physical till cash.</summary>
public sealed record TenderProviderRow(string SourceTenderCode, string Mode, string? ProviderKey, bool IsTillCash, bool Active);

/// <summary>Everything Settings > Collections setup shows. Every role may read it; only the Owner may change it.</summary>
public sealed record CollectionsSetup(
    IReadOnlyList<BankAccountRow> BankAccounts,
    IReadOnlyList<CollectionProviderRow> Providers,
    IReadOnlyList<MerchantIdRow> MerchantIds,
    IReadOnlyList<CollectionPolicyRow> Policies,
    IReadOnlyList<StatementLayoutRow> Layouts,
    IReadOnlyList<TenderProviderRow> TenderCodes,
    string EvidenceRoot);

public sealed record BankAccountDraft(
    int? Id, string AccountKey, string DisplayName, string BankName, string? AccountLast4, string? StoreCode,
    bool TakesCashDeposits, bool Active);

public sealed record ProviderDraft(
    string ProviderKey, string DisplayName, string Mode, string SettlesVia, string StatementKind, string Granularity,
    int? BankAccountId, decimal? RoundingTolerance, bool Active);

public sealed record MerchantIdDraft(int? Id, string ProviderKey, string MerchantId, string? TerminalId, string StoreCode, bool Active);

public sealed record CollectionPolicyDraft(
    int? Id, string? StoreCode, string Mode, string? ProviderKey, int? SettlementWindowDays, int? OverdueAfterDays,
    decimal? CashTolerance, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

/// <summary>A layout to save. <see cref="HeaderSignatureSha256"/> comes from <see cref="ICollectionsSetupService.ReadSampleHeaderAsync"/>
/// on a sample file, never typed. <see cref="MappingJson"/> is validated by Etp.Reporting.Import.Collections.StatementLayout.</summary>
public sealed record StatementLayoutDraft(
    int? Id, string LayoutKey, string DisplayName, string LayoutKind, int? BankAccountId, string? ProviderKey,
    string HeaderSignatureSha256, string? SheetName, int HeaderRow, int SkipFooterRows, string MappingJson, bool Active);

public sealed record TenderProviderDraft(string SourceTenderCode, string? ProviderKey, bool IsTillCash);

/// <summary>The header row of a sample file as a layout would see it.</summary>
public sealed record SampleFileHeader(IReadOnlyList<string> SheetNames, string SheetName, int HeaderRow, IReadOnlyList<string> Headers, string HeaderSignatureSha256);

/// <summary>One line of "Test on this file": the normalised values, or the reason the line could not be read.</summary>
public sealed record LayoutTestLine(
    int RowNumber, DateOnly? LineDate, string? Reference, decimal? Credit, decimal? Debit, string? LineKind, decimal? Gross, decimal? Net,
    string? MerchantId, string? ParseError);

/// <summary>The result of "Test on this file" (the first lines only).</summary>
public sealed record LayoutTestResult(bool SignatureMatches, IReadOnlyList<string> Problems, int ReadableLines, int UnreadableLines, IReadOnlyList<LayoutTestLine> FirstLines);

/// <summary>Settings > Collections setup (master plan 8a tasks 1-2). Saving is Owner-only and audited (CollectionsSetup).</summary>
public interface ICollectionsSetupService
{
    Task<CollectionsSetup> LoadAsync(CancellationToken cancellationToken = default);
    Task<int> SaveBankAccountAsync(BankAccountDraft draft, string reason, CancellationToken cancellationToken = default);
    Task SaveProviderAsync(ProviderDraft draft, string reason, CancellationToken cancellationToken = default);
    Task<int> SaveMerchantIdAsync(MerchantIdDraft draft, string reason, CancellationToken cancellationToken = default);
    Task<int> SavePolicyAsync(CollectionPolicyDraft draft, string reason, CancellationToken cancellationToken = default);
    Task<int> SaveLayoutAsync(StatementLayoutDraft draft, string reason, CancellationToken cancellationToken = default);
    Task SaveTenderProviderAsync(TenderProviderDraft draft, string reason, CancellationToken cancellationToken = default);
    Task SaveEvidenceRootAsync(string path, string reason, CancellationToken cancellationToken = default);

    /// <summary>Reads the header of a sample file (Owner): the sheet names, the header cells at <paramref name="headerRow"/>
    /// and their signature.</summary>
    Task<SampleFileHeader> ReadSampleHeaderAsync(string samplePath, string? sheetName, int headerRow, CancellationToken cancellationToken = default);

    /// <summary>"Test on this file" (Owner): reads the sample with the draft layout without saving anything.</summary>
    Task<LayoutTestResult> TestLayoutAsync(string samplePath, StatementLayoutDraft draft, CancellationToken cancellationToken = default);
}

/// <summary>A layout a file matches by its header signature.</summary>
public sealed record MatchedLayout(int LayoutId, string LayoutKey, string DisplayName, string LayoutKind, string AccountOrProviderKey, string AccountOrProviderName);

/// <summary>What Import > Bank and settlement files shows before the import. When <see cref="Layouts"/> has more than one
/// entry the user must choose ("Which account is this statement for?") and Import stays disabled until then.</summary>
public sealed record CollectionsFilePreview(string FileName, string Sha256, IReadOnlyList<MatchedLayout> Layouts, string? Problem);

public enum CollectionsOverlapChoice { Stop, ImportAsNew, Restate }

public sealed record CollectionsImportRequest(string FilePath, int LayoutId, CollectionsOverlapChoice OverlapChoice = CollectionsOverlapChoice.Stop, string? Reason = null);

public enum CollectionsImportOutcome { Imported, AlreadyImported, DuplicateContent, PossibleDuplicates }

/// <summary>The per-file result. <see cref="Message"/> is the plain sentence the import list shows.</summary>
public sealed record CollectionsImportResult(
    CollectionsImportOutcome Outcome, long? ImportFileId, int NewLines, int AlreadyPresentLines, int UnreadableLines,
    int PossibleDuplicateLines, int SupersededFiles, string Message);

/// <summary>Import > Bank and settlement files (master plan 8a tasks 3-4). Owner and Store Manager; audited (CollectionsImport).</summary>
public interface ICollectionsImportService
{
    Task<CollectionsFilePreview> PreviewAsync(string filePath, CancellationToken cancellationToken = default);
    Task<CollectionsImportResult> ImportAsync(CollectionsImportRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A deposit the daily sheet says was sent and the bank has not been confirmed to credit yet.</summary>
/// <param name="OverdueAfterDays">1.12.1 P2: the "Overdue after" days of the Owner's Cash settlement rule in force for this store
/// on the deposit date (<see cref="CollectionPolicyResolver"/>); null when no rule sets it, and then nothing is ever overdue (D20).</param>
public sealed record DepositInTransit(string StoreCode, DateOnly DepositDate, decimal Amount, int AgeDays, int? OverdueAfterDays = null)
{
    /// <summary>Older than the rule allows. Without a rule a deposit is never overdue.</summary>
    public bool IsOverdue => OverdueAfterDays is { } limit && AgeDays > limit;
}

/// <summary>A bank credit line that R-DEP-1 offers for a deposit in transit. <see cref="Narration"/> is shown to the Owner only.</summary>
public sealed record DepositCandidateLine(long BankLineId, int BankAccountId, string AccountKey, string AccountName, string? AccountStoreCode,
    DateOnly LineDate, decimal CreditAmount, string? BankReference, string? Narration);

/// <summary>An R-DEP-1 proposal. <see cref="IsAmbiguous"/>: the deposit has more than one candidate line, or the line fits more
/// than one deposit; the Owner must choose.</summary>
public sealed record DepositProposal(DepositInTransit Deposit, DepositCandidateLine Line, bool IsAmbiguous);

public sealed record DepositProposalAcceptance(string StoreCode, DateOnly DepositDate, long BankLineId, string Reason);

public sealed record ManualDepositConfirmation(
    string StoreCode, DateOnly DepositDate, DateOnly CreditedDate, int BankAccountId, decimal Amount, string? BankReference, string Reason);

/// <summary>1.12.1 P2: an active bank credit confirmation (accepted from a proposal or recorded by hand), for the Owner's
/// "Marked as credited" list, where a mistake is withdrawn with a reason. <see cref="SentAmount"/> is what the daily sheet says
/// was sent (null when the sheet has no deposit for that day). The account is named, never numbered.</summary>
public sealed record CreditedDeposit(
    long ConfirmationId, string StoreCode, DateOnly DepositDate, DateOnly CreditedDate, decimal CreditedAmount, decimal? SentAmount,
    int BankAccountId, string AccountName, string? BankReference, bool RecordedByHand, string ConfirmedBy, DateTime ConfirmedUtc, string Reason);

/// <summary>1.12.1 P2: a bank account the Owner may record a credit on for a store (active, the store's own or shared).</summary>
public sealed record DepositAccount(int BankAccountId, string AccountKey, string DisplayName, string? StoreCode, bool TakesCashDeposits);

/// <summary>Today > "Bank credited" (decision 44 #7: deposit sent and credited are two states). Proposals only propose;
/// accepting, recording by hand and withdrawing are Owner-only and audited (CashDepositConfirmed). Allowed on a LOCKED day.</summary>
public interface ICashDepositService
{
    Task<IReadOnlyList<DepositInTransit>> LoadInTransitAsync(IReadOnlyList<string>? storeCodes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DepositProposal>> LoadProposalsAsync(IReadOnlyList<string>? storeCodes, CancellationToken cancellationToken = default);
    Task<long> AcceptProposalAsync(DepositProposalAcceptance acceptance, CancellationToken cancellationToken = default);
    Task<long> ConfirmManuallyAsync(ManualDepositConfirmation confirmation, CancellationToken cancellationToken = default);
    Task WithdrawAsync(long confirmationId, string reason, CancellationToken cancellationToken = default);

    /// <summary>1.12.1 P2 (Owner): the active confirmations whose deposit date is in the range, newest first.</summary>
    Task<IReadOnlyList<CreditedDeposit>> LoadCreditedAsync(IReadOnlyList<string>? storeCodes, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>1.12.1 P2 (Owner): the accounts a credit can be recorded on by hand for this store.</summary>
    Task<IReadOnlyList<DepositAccount>> LoadDepositAccountsAsync(string storeCode, CancellationToken cancellationToken = default);
}

public sealed record CollectionsStatementRequest(IReadOnlyList<string>? StoreCodes, DateOnly From, DateOnly To, DateOnly AsOf);

/// <summary>Reports > "Collections: expected vs evidence" and Reports > Deposits in transit. Every role may open the statement
/// (a Viewer gets it without bank references); the in-transit list is Owner and Store Manager.</summary>
public interface ICollectionsStatementQuery
{
    Task<CollectionsStatement> LoadStatementAsync(CollectionsStatementRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DepositInTransit>> LoadDepositsInTransitAsync(IReadOnlyList<string>? storeCodes, CancellationToken cancellationToken = default);
}
