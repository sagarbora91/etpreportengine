namespace Etp.Reporting.Application.Accounting;

public sealed record AccountingScope(string StoreCode, DateOnly BusinessDate);

public sealed record AccountingBusinessEvent(
    string EventCode,
    decimal Amount,
    string SourceReference,
    string Description);

public sealed record AccountingSource(
    long ReportGenerationId,
    IReadOnlyList<AccountingBusinessEvent> Events);

public sealed record ApprovedAccountingMapping(
    string BusinessEvent,
    string DebitLedger,
    string CreditLedger,
    string NarrationTemplate,
    string? CostCentre = null);

public sealed record AccountingEntry(
    int LineNumber,
    string BusinessEvent,
    string LedgerName,
    decimal DebitAmount,
    decimal CreditAmount,
    string Narration,
    string? CostCentre,
    string SourceReference);

public sealed record AccountingBatchDraft(
    IReadOnlyList<AccountingEntry> Entries,
    decimal DebitTotal,
    decimal CreditTotal,
    bool IsBalanced,
    IReadOnlyList<string> MissingMappings);

public sealed record AccountingPreview(
    long ReportGenerationId,
    AccountingBatchDraft Batch);

public sealed record AccountingBatchSummary(
    long Id,
    string StoreCode,
    DateOnly BusinessDate,
    long ReportGenerationId,
    int AccountingGeneration,
    decimal DebitTotal,
    decimal CreditTotal,
    string Status,
    string? ApprovedBy,
    DateTime? ExportedUtc,
    DateTime CreatedUtc,
    string? BlockingReason = null);

public sealed record SaveAccountingBatch(
    AccountingScope Scope,
    long ReportGenerationId,
    AccountingBatchDraft Batch);

public sealed record ApproveAccountingBatch(long BatchId, string Reason);
public sealed record RejectAccountingBatch(long BatchId, string Reason);

public sealed record ApproveAccountingMapping(
    AccountingScope Scope,
    string BusinessEvent,
    string DebitLedger,
    string CreditLedger,
    string NarrationTemplate,
    string Reason);

public sealed record ExportAccountingBatch(
    long BatchId,
    string CompanyName,
    string OutputPath);

public sealed record AccountingExportReceipt(
    long BatchId,
    string OutputPath,
    string Sha256,
    string CompanyName = "",
    string EnvironmentLabel = "TEST",
    DateTime? ExportedUtc = null,
    string? ExportedBy = null);

public sealed record AccountingDestination(string? CompanyName, string EnvironmentLabel);
public sealed record SaveAccountingDestination(string? CompanyName, string EnvironmentLabel, string CompanyConfirmation, string Reason);

public static class AccountingBatchControls
{
    public static bool IsBalancedAndComplete(AccountingBatchDraft? batch)
    {
        if (batch?.Entries is null || batch.MissingMappings is null) return false;
        if (!batch.IsBalanced || batch.MissingMappings.Count != 0 || batch.DebitTotal != batch.CreditTotal) return false;
        if (batch.Entries.Any(entry => entry.DebitAmount < 0 || entry.CreditAmount < 0 ||
                                       entry.DebitAmount > 0 && entry.CreditAmount > 0)) return false;
        return batch.Entries.Sum(entry => entry.DebitAmount) == batch.DebitTotal &&
               batch.Entries.Sum(entry => entry.CreditAmount) == batch.CreditTotal;
    }

    public static void EnsureBalancedAndComplete(AccountingBatchDraft? batch)
    {
        if (!IsBalancedAndComplete(batch))
            throw new InvalidOperationException(
                "The accounting batch must be balanced and fully mapped before it can be saved or exported.");
    }
}

public interface IAccountingService
{
    Task<AccountingDestination> LoadDestinationAsync(CancellationToken cancellationToken = default);
    Task SaveDestinationAsync(SaveAccountingDestination command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AccountingExportReceipt>> LoadExportHistoryAsync(CancellationToken cancellationToken = default);
    Task<AccountingSource> LoadSourceAsync(
        AccountingScope scope,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovedAccountingMapping>> LoadApprovedMappingsAsync(
        AccountingScope scope,
        CancellationToken cancellationToken = default);

    Task<AccountingPreview> PreviewAsync(
        AccountingScope scope,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountingBatchSummary>> LoadBatchesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountingEntry>> LoadEntriesAsync(
        long batchId,
        CancellationToken cancellationToken = default);

    Task<long> SaveAsync(
        SaveAccountingBatch command,
        CancellationToken cancellationToken = default);

    Task ApproveAsync(
        ApproveAccountingBatch command,
        CancellationToken cancellationToken = default);

    Task RejectAsync(RejectAccountingBatch command, CancellationToken cancellationToken = default);

    Task ApproveMappingAsync(
        ApproveAccountingMapping command,
        CancellationToken cancellationToken = default);

    Task<AccountingExportReceipt> ExportAsync(
        ExportAccountingBatch command,
        CancellationToken cancellationToken = default);
}

/// <summary>One ledger line of a Tally Sales voucher as the file carries it (1.12 D1, master plan 7a task 8).
/// Exactly one of <paramref name="Debit"/> and <paramref name="Credit"/> is above zero.</summary>
public sealed record AccountingVoucherLine(string LedgerName, decimal Debit, decimal Credit, string? CostCentre);

/// <summary>One PLANNED voucher of an approved Tally batch, ready for the Tally file. The first debit line is the
/// party (the tender ledger: D14 one retail ledger, D16 payment inside the voucher).</summary>
public sealed record AccountingVoucherDraft(
    int Sequence,
    string CorrespondenceKey,
    string VoucherType,
    DateOnly VoucherDate,
    string DocumentNumber,
    string Narration,
    IReadOnlyList<AccountingVoucherLine> Lines)
{
    public string PartyLedger => Lines.FirstOrDefault(line => line.Debit > 0)?.LedgerName
        ?? throw new InvalidOperationException($"Voucher {DocumentNumber} has no debit line, so it has no party ledger.");

    /// <summary>The voucher total: what the customer paid, GST included.</summary>
    public decimal Total => Lines.Sum(line => line.Debit);
}

/// <summary>What "Write Tally file" produced for one batch: <c>payload.xml</c> in the batch's evidence folder.</summary>
public sealed record TallyVoucherFileReceipt(
    long BatchId,
    string CompanyName,
    string RelativePath,
    string Sha256,
    int VoucherCount,
    decimal VoucherTotal,
    DateTime ExportedUtc);

/// <summary>Plan task 8 for the Owner: writes an approved Tally batch as one Sales voucher per invoice, keeps the file as
/// evidence, re-reads it, and only then marks the batch exported. Nothing is sent to Tally.</summary>
public interface ITallySalesVoucherExportService
{
    Task<TallyVoucherFileReceipt> ExportAsync(long batchId, CancellationToken cancellationToken = default);
}

/// <summary>A ledger group ETP asks Tally to create. <paramref name="Parent"/> is one of Tally's own groups.</summary>
public sealed record TallyMasterGroup(string Name, string Parent, string Purpose);

/// <summary>A cost centre for one store (D12, migration 0049). <paramref name="Proposed"/>: ETP made the name up because
/// Settings → Tally companies has none for the store yet.</summary>
public sealed record TallyMasterCostCentre(string Name, string StoreCode, bool Proposed);

/// <summary>A ledger ETP asks Tally to create. <paramref name="BusinessEvent"/> is the voucher line it serves, or null for a
/// ledger the vouchers do not use yet (a bank ledger for later deposits). <paramref name="FromApprovedMapping"/>: the name is
/// the Owner's approved ledger mapping; otherwise it is ETP's proposal and becomes a mapping only when the Owner saves it.</summary>
public sealed record TallyMasterLedger(
    string Name,
    string Parent,
    string Purpose,
    string? BusinessEvent,
    IReadOnlyList<string> StoreCodes,
    string? GstDutyHead,
    decimal? GstRate,
    bool CostCentresOn,
    bool FromApprovedMapping);

/// <summary>A voucher type ETP uses. <paramref name="BuiltIn"/>: Tally already has it, so nothing is created.</summary>
public sealed record TallyMasterVoucherType(string Name, string Parent, bool BuiltIn, string Note);

/// <summary>A ledger mapping the Owner still has to approve (Settings → Tally ledgers) before vouchers can use the proposal.</summary>
public sealed record ProposedLedgerMapping(string StoreCode, string BusinessEvent, string LedgerName);

/// <summary>Everything a brand-new Tally company needs before ETP's vouchers can be imported (decision 49 (3)): ledger
/// groups, cost centres per store, sales, GST output, round-off, tender, cash and bank ledgers, and the voucher type.
/// Reviewed with the accountant before it is used on a real company.</summary>
public sealed record TallyMastersPlan(
    string CompanyName,
    string Environment,
    IReadOnlyList<TallyMasterGroup> Groups,
    IReadOnlyList<TallyMasterCostCentre> CostCentres,
    IReadOnlyList<TallyMasterLedger> Ledgers,
    IReadOnlyList<TallyMasterVoucherType> VoucherTypes,
    IReadOnlyList<string> SetupSteps,
    IReadOnlyList<string> ReviewPoints,
    IReadOnlyList<ProposedLedgerMapping> MappingsToApprove);

/// <summary>The two files "Write Tally masters" produced: the importable XML and the plain-English list for the accountant.</summary>
public sealed record TallyMastersExportReceipt(
    string XmlPath,
    string ReviewPath,
    string XmlSha256,
    int GroupCount,
    int CostCentreCount,
    int LedgerCount);

/// <summary>Decision 49 (3) for the Owner: the masters of a new Tally company, made from ETP's stores, payment modes, GST rates
/// and approved ledger mappings.</summary>
public interface ITallyMastersService
{
    Task<TallyMastersPlan> PlanAsync(int tallyProfileId, CancellationToken cancellationToken = default);

    /// <summary>Writes <c>tally-masters-&lt;profile&gt;.xml</c> and <c>tally-masters-&lt;profile&gt;-review.txt</c> into
    /// <paramref name="folder"/>; existing files are never overwritten.</summary>
    Task<TallyMastersExportReceipt> ExportAsync(int tallyProfileId, string folder, CancellationToken cancellationToken = default);
}
