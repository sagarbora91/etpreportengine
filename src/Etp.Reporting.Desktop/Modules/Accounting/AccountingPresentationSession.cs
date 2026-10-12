extern alias EtpApplication;

using AccountingBatchDraft = EtpApplication::Etp.Reporting.Application.Accounting.AccountingBatchDraft;
using AccountingBatchControls = EtpApplication::Etp.Reporting.Application.Accounting.AccountingBatchControls;
using AccountingBatchSummary = EtpApplication::Etp.Reporting.Application.Accounting.AccountingBatchSummary;
using AccountingExportReceipt = EtpApplication::Etp.Reporting.Application.Accounting.AccountingExportReceipt;
using AccountingPreview = EtpApplication::Etp.Reporting.Application.Accounting.AccountingPreview;
using AccountingScope = EtpApplication::Etp.Reporting.Application.Accounting.AccountingScope;
using AccountingService = EtpApplication::Etp.Reporting.Application.Accounting.IAccountingService;
using ApproveAccountingBatch = EtpApplication::Etp.Reporting.Application.Accounting.ApproveAccountingBatch;
using ApproveAccountingMapping = EtpApplication::Etp.Reporting.Application.Accounting.ApproveAccountingMapping;
using ExportAccountingBatch = EtpApplication::Etp.Reporting.Application.Accounting.ExportAccountingBatch;
using SaveAccountingBatch = EtpApplication::Etp.Reporting.Application.Accounting.SaveAccountingBatch;

namespace Etp.Reporting.Desktop.Modules.Accounting;

public sealed record AccountingPresentationSnapshot(
    AccountingScope? Scope,
    long? ReportGenerationId,
    AccountingBatchDraft? Draft);

public sealed class AccountingPresentationSession
{
    private readonly Func<AccountingService> serviceFactory;

    public AccountingPresentationSession(Func<AccountingService> serviceFactory) =>
        this.serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));

    public AccountingPresentationSnapshot Current { get; private set; } = new(null, null, null);
    public void InvalidatePreview() => Current = new(null, null, null);

    public async Task<IReadOnlyList<AccountingBatchSummary>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        Current = new(null, null, null);
        return await serviceFactory().LoadBatchesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountingPreview> PreviewAsync(
        AccountingScope scope,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var preview = await serviceFactory().PreviewAsync(scope, cancellationToken).ConfigureAwait(false);
            Current = new(scope, preview.ReportGenerationId, preview.Batch);
            return preview;
        }
        catch
        {
            Current = new(null, null, null);
            throw;
        }
    }

    public Task<long> SaveCurrentAsync(
        AccountingScope expectedScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedScope);
        if (Current.Scope is null || Current.Draft is null || Current.ReportGenerationId is null)
            throw new InvalidOperationException("Preview a balanced accounting batch first.");
        if (Current.Scope != expectedScope)
            throw new InvalidOperationException("The accounting scope changed. Preview this store and business date again before saving.");
        return serviceFactory().SaveAsync(
            new SaveAccountingBatch(Current.Scope, Current.ReportGenerationId.Value, Current.Draft), cancellationToken);
    }

    public Task ApproveAsync(
        AccountingBatchSummary batch,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return serviceFactory().ApproveAsync(new ApproveAccountingBatch(batch.Id, reason), cancellationToken);
    }

    public Task RejectAsync(AccountingBatchSummary batch, string reason, CancellationToken cancellationToken=default) =>
        serviceFactory().RejectAsync(new EtpApplication::Etp.Reporting.Application.Accounting.RejectAccountingBatch(batch.Id,reason),cancellationToken);

    public Task ApproveMappingAsync(
        ApproveAccountingMapping command,
        CancellationToken cancellationToken = default) =>
        serviceFactory().ApproveMappingAsync(command, cancellationToken);

    public Task<IReadOnlyList<EtpApplication::Etp.Reporting.Application.Accounting.AccountingEntry>> LoadEntriesAsync(long id) => serviceFactory().LoadEntriesAsync(id);
    public Task<EtpApplication::Etp.Reporting.Application.Accounting.AccountingDestination> LoadDestinationAsync() => serviceFactory().LoadDestinationAsync();
    public Task<IReadOnlyList<AccountingExportReceipt>> LoadExportHistoryAsync() => serviceFactory().LoadExportHistoryAsync();

    public Task<AccountingExportReceipt> ExportAsync(
        AccountingBatchSummary batch,
        string companyName,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!string.Equals(batch.Status, "APPROVED_READY", StringComparison.Ordinal))
            throw new InvalidOperationException("Approve the accounting batch before exporting it.");
        return serviceFactory().ExportAsync(
            new ExportAccountingBatch(batch.Id, companyName, outputPath), cancellationToken);
    }
}
