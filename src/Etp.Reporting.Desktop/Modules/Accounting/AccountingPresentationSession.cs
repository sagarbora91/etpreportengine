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
    private readonly Func<string, AccountingService> serviceFactory;

    public AccountingPresentationSession(Func<string, AccountingService> serviceFactory) =>
        this.serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));

    public AccountingPresentationSnapshot Current { get; private set; } = new(null, null, null);

    public async Task<IReadOnlyList<AccountingBatchSummary>> RefreshAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        Current = new(null, null, null);
        return await serviceFactory(connectionString).LoadBatchesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AccountingPreview> PreviewAsync(
        string connectionString,
        AccountingScope scope,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var preview = await serviceFactory(connectionString).PreviewAsync(scope, cancellationToken).ConfigureAwait(false);
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
        string connectionString,
        AccountingScope expectedScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedScope);
        if (Current.Scope is null || Current.Draft is null || Current.ReportGenerationId is null)
            throw new InvalidOperationException("Preview a balanced accounting batch first.");
        if (Current.Scope != expectedScope)
            throw new InvalidOperationException("The accounting scope changed. Preview this store and business date again before saving.");
        AccountingBatchControls.EnsureBalancedAndComplete(Current.Draft);
        return serviceFactory(connectionString).SaveAsync(
            new SaveAccountingBatch(Current.Scope, Current.ReportGenerationId.Value, Current.Draft), cancellationToken);
    }

    public Task ApproveAsync(
        string connectionString,
        AccountingBatchSummary batch,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return serviceFactory(connectionString).ApproveAsync(new ApproveAccountingBatch(batch.Id, reason), cancellationToken);
    }

    public Task ApproveMappingAsync(
        string connectionString,
        ApproveAccountingMapping command,
        CancellationToken cancellationToken = default) =>
        serviceFactory(connectionString).ApproveMappingAsync(command, cancellationToken);

    public Task<AccountingExportReceipt> ExportAsync(
        string connectionString,
        AccountingBatchSummary batch,
        string companyName,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!string.Equals(batch.Status, "APPROVED", StringComparison.Ordinal))
            throw new InvalidOperationException("Approve the accounting batch before exporting it.");
        return serviceFactory(connectionString).ExportAsync(
            new ExportAccountingBatch(batch.Id, companyName, outputPath), cancellationToken);
    }
}
