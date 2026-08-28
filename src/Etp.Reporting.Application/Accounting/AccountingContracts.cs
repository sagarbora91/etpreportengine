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
    string? TallyReference,
    DateTime CreatedUtc);

public sealed record SaveAccountingBatch(
    AccountingScope Scope,
    long ReportGenerationId,
    AccountingBatchDraft Batch);

public sealed record ApproveAccountingBatch(long BatchId, string Reason);

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
    string Sha256);

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

    Task ApproveMappingAsync(
        ApproveAccountingMapping command,
        CancellationToken cancellationToken = default);

    Task<AccountingExportReceipt> ExportAsync(
        ExportAccountingBatch command,
        CancellationToken cancellationToken = default);
}
