using App = Etp.Reporting.Application.Accounting;

namespace Etp.Reporting.Infrastructure.SqlServer;

/// <summary>
/// Cohesive SQL adapter for controlled accounting preview, preparation,
/// Owner approval, mapping approval and audited Tally export.
/// </summary>
public sealed class SqlServerAccountingService : App.IAccountingService
{
    private readonly IAccountingSqlGateway gateway;
    private readonly Func<CancellationToken, Task<ApplicationAccess>> loadAccess;
    private readonly Func<string, string, DateOnly, AccountingBatchDraft, CancellationToken, Task<string>> exportTally;

    public SqlServerAccountingService(string connectionString)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(
            connectionString,
            nameof(connectionString));
        gateway = new ProductisationAccountingGateway(new ProductisationRepository(validated));
        loadAccess = new Phase2OperationsRepository(validated).LoadCurrentAccessAsync;
        var exporter = new TallyXmlExportService();
        exportTally = exporter.ExportAsync;
    }

    internal SqlServerAccountingService(
        IAccountingSqlGateway gateway,
        Func<CancellationToken, Task<ApplicationAccess>> loadAccess,
        Func<string, string, DateOnly, AccountingBatchDraft, CancellationToken, Task<string>> exportTally)
    {
        this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        this.loadAccess = loadAccess ?? throw new ArgumentNullException(nameof(loadAccess));
        this.exportTally = exportTally ?? throw new ArgumentNullException(nameof(exportTally));
    }

    public async Task<App.AccountingSource> LoadSourceAsync(
        App.AccountingScope scope,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        await RequireViewAsync(cancellationToken).ConfigureAwait(false);
        return Map(await gateway.LoadSourceAsync(
            scope.StoreCode,
            scope.BusinessDate,
            cancellationToken).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<App.ApprovedAccountingMapping>> LoadApprovedMappingsAsync(
        App.AccountingScope scope,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        await RequireViewAsync(cancellationToken).ConfigureAwait(false);
        var rows = await gateway.LoadMappingsAsync(
            scope.StoreCode,
            scope.BusinessDate,
            cancellationToken).ConfigureAwait(false);
        return rows.Select(Map).ToArray();
    }

    public async Task<App.AccountingPreview> PreviewAsync(
        App.AccountingScope scope,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        await RequireViewAsync(cancellationToken).ConfigureAwait(false);
        var source = await gateway.LoadSourceAsync(
            scope.StoreCode,
            scope.BusinessDate,
            cancellationToken).ConfigureAwait(false);
        var mappings = await gateway.LoadMappingsAsync(
            scope.StoreCode,
            scope.BusinessDate,
            cancellationToken).ConfigureAwait(false);
        var batch = new AccountingBatchComposer().Compose(source.Events, mappings);
        return new(source.GenerationId, Map(batch));
    }

    public async Task<IReadOnlyList<App.AccountingBatchSummary>> LoadBatchesAsync(
        CancellationToken cancellationToken = default)
    {
        await RequireViewAsync(cancellationToken).ConfigureAwait(false);
        return (await gateway.LoadBatchesAsync(cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<App.AccountingEntry>> LoadEntriesAsync(
        long batchId,
        CancellationToken cancellationToken = default)
    {
        ValidateBatchId(batchId);
        await RequireViewAsync(cancellationToken).ConfigureAwait(false);
        return (await gateway.LoadEntriesAsync(batchId, cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();
    }

    public async Task<long> SaveAsync(
        App.SaveAccountingBatch command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateScope(command.Scope);
        if (command.ReportGenerationId <= 0)
            throw new ArgumentOutOfRangeException(nameof(command), "A report generation is required.");
        App.AccountingBatchControls.EnsureBalancedAndComplete(command.Batch);
        await RequireImportAsync(cancellationToken).ConfigureAwait(false);
        return await gateway.SaveBatchAsync(
            command.Scope.StoreCode,
            command.Scope.BusinessDate,
            command.ReportGenerationId,
            Map(command.Batch),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ApproveAsync(
        App.ApproveAccountingBatch command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateBatchId(command.BatchId);
        ValidateReason(command.Reason, "Enter an accounting approval reason.");
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        await gateway.ApproveBatchAsync(command.BatchId, command.Reason, cancellationToken).ConfigureAwait(false);
    }

    public async Task ApproveMappingAsync(
        App.ApproveAccountingMapping command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateScope(command.Scope);
        ValidateRequired(command.BusinessEvent, "A business event is required.");
        ValidateRequired(command.DebitLedger, "A debit ledger is required.");
        ValidateRequired(command.CreditLedger, "A credit ledger is required.");
        ValidateRequired(command.NarrationTemplate, "A narration template is required.");
        ValidateReason(command.Reason, "Enter an accounting mapping approval reason.");
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);

        var eventCode = command.BusinessEvent.Trim().ToUpperInvariant();
        var payload = new
        {
            Event = eventCode,
            Debit = command.DebitLedger,
            Credit = command.CreditLedger,
            Narration = command.NarrationTemplate,
            Store = command.Scope.StoreCode
        };
        var approvalId = await gateway.CreateMappingApprovalAsync(
            eventCode,
            payload,
            command.Scope.StoreCode,
            command.Scope.BusinessDate,
            cancellationToken).ConfigureAwait(false);
        await gateway.DecideApprovalAsync(
            approvalId,
            command.Reason,
            cancellationToken).ConfigureAwait(false);
        await gateway.SaveMappingAsync(
            approvalId,
            eventCode,
            command.DebitLedger,
            command.CreditLedger,
            command.NarrationTemplate,
            command.Scope.StoreCode,
            command.Scope.BusinessDate,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<App.AccountingExportReceipt> ExportAsync(
        App.ExportAccountingBatch command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateBatchId(command.BatchId);
        ValidateRequired(command.CompanyName, "A Tally company name is required.");
        ValidateRequired(command.OutputPath, "An export path is required.");
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);

        var batch = (await gateway.LoadBatchesAsync(cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(row => row.Id == command.BatchId)
            ?? throw new InvalidOperationException("The accounting batch was not found.");
        if (!string.Equals(batch.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Approve the accounting batch before exporting it.");

        var entries = await gateway.LoadEntriesAsync(command.BatchId, cancellationToken).ConfigureAwait(false);
        var draft = new App.AccountingBatchDraft(
            entries.Select(Map).ToArray(),
            batch.DebitTotal,
            batch.CreditTotal,
            batch.DebitTotal == batch.CreditTotal,
            []);
        App.AccountingBatchControls.EnsureBalancedAndComplete(draft);
        var hash = await exportTally(
            command.OutputPath,
            command.CompanyName,
            batch.BusinessDate,
            Map(draft),
            cancellationToken).ConfigureAwait(false);
        await gateway.RecordExportAsync(command.BatchId, hash, cancellationToken).ConfigureAwait(false);
        return new(command.BatchId, Path.GetFullPath(command.OutputPath), hash);
    }

    internal static App.AccountingSource Map((long GenerationId, IReadOnlyList<AccountingBusinessEvent> Events) source) =>
        new(source.GenerationId, source.Events.Select(Map).ToArray());

    internal static App.AccountingBusinessEvent Map(AccountingBusinessEvent source) =>
        new(source.EventCode, source.Amount, source.SourceReference, source.Description);

    internal static App.ApprovedAccountingMapping Map(AccountingMapping source) =>
        new(source.BusinessEvent, source.DebitLedger, source.CreditLedger, source.NarrationTemplate, source.CostCentre);

    internal static App.AccountingEntry Map(AccountingEntryDraft source) =>
        new(source.LineNumber, source.BusinessEvent, source.LedgerName, source.DebitAmount, source.CreditAmount,
            source.Narration, source.CostCentre, source.SourceReference);

    internal static App.AccountingBatchDraft Map(AccountingBatchDraft source) =>
        new(source.Entries.Select(Map).ToArray(), source.DebitTotal, source.CreditTotal,
            source.IsBalanced, source.MissingMappings);

    internal static App.AccountingBatchSummary Map(AccountingBatchRow source) =>
        new(source.Id, source.StoreCode, source.BusinessDate, source.ReportGenerationId,
            source.AccountingGeneration, source.DebitTotal, source.CreditTotal, source.Status,
            source.ApprovedBy, source.ExportedUtc, source.TallyReference, source.CreatedUtc);

    internal static AccountingEntryDraft Map(App.AccountingEntry source) =>
        new(source.LineNumber, source.BusinessEvent, source.LedgerName, source.DebitAmount,
            source.CreditAmount, source.Narration, source.CostCentre, source.SourceReference);

    internal static AccountingBatchDraft Map(App.AccountingBatchDraft source) =>
        new(source.Entries.Select(Map).ToArray(), source.DebitTotal, source.CreditTotal,
            source.IsBalanced, source.MissingMappings);

    private async Task RequireViewAsync(CancellationToken cancellationToken)
    {
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanView)
            throw new UnauthorizedAccessException("This Windows account does not have application access.");
    }

    private async Task RequireImportAsync(CancellationToken cancellationToken)
    {
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanImport)
            throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
    }

    private async Task RequireOwnerAsync(CancellationToken cancellationToken)
    {
        if (!(await loadAccess(cancellationToken).ConfigureAwait(false)).CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required.");
    }

    private static void ValidateScope(App.AccountingScope? scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ValidateRequired(scope.StoreCode, "An accounting store is required.");
        if (scope.BusinessDate == default)
            throw new ArgumentException("An accounting business date is required.", nameof(scope));
    }

    private static void ValidateBatchId(long batchId)
    {
        if (batchId <= 0) throw new ArgumentOutOfRangeException(nameof(batchId));
    }

    private static void ValidateReason(string? reason, string message)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException(message, nameof(reason));
    }

    private static void ValidateRequired(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException(message);
    }
}

internal interface IAccountingSqlGateway
{
    Task<(long GenerationId, IReadOnlyList<AccountingBusinessEvent> Events)> LoadSourceAsync(
        string storeCode, DateOnly businessDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingMapping>> LoadMappingsAsync(
        string storeCode, DateOnly businessDate, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingBatchRow>> LoadBatchesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingEntryDraft>> LoadEntriesAsync(long batchId, CancellationToken cancellationToken);
    Task<long> SaveBatchAsync(
        string storeCode, DateOnly businessDate, long reportGenerationId,
        AccountingBatchDraft batch, CancellationToken cancellationToken);
    Task ApproveBatchAsync(long batchId, string reason, CancellationToken cancellationToken);
    Task<long> CreateMappingApprovalAsync(
        string eventCode, object payload, string storeCode, DateOnly businessDate,
        CancellationToken cancellationToken);
    Task DecideApprovalAsync(long approvalId, string reason, CancellationToken cancellationToken);
    Task SaveMappingAsync(
        long approvalId, string eventCode, string debitLedger, string creditLedger,
        string narration, string storeCode, DateOnly effectiveFrom,
        CancellationToken cancellationToken);
    Task RecordExportAsync(long batchId, string sha256, CancellationToken cancellationToken);
}

internal sealed class ProductisationAccountingGateway(ProductisationRepository repository) : IAccountingSqlGateway
{
    public Task<(long GenerationId, IReadOnlyList<AccountingBusinessEvent> Events)> LoadSourceAsync(
        string storeCode, DateOnly businessDate, CancellationToken cancellationToken) =>
        repository.LoadAccountingSourceAsync(storeCode, businessDate, cancellationToken);

    public Task<IReadOnlyList<AccountingMapping>> LoadMappingsAsync(
        string storeCode, DateOnly businessDate, CancellationToken cancellationToken) =>
        repository.LoadApprovedAccountingMappingsAsync(storeCode, businessDate, cancellationToken);

    public Task<IReadOnlyList<AccountingBatchRow>> LoadBatchesAsync(CancellationToken cancellationToken) =>
        repository.LoadAccountingBatchesAsync(cancellationToken);

    public Task<IReadOnlyList<AccountingEntryDraft>> LoadEntriesAsync(
        long batchId, CancellationToken cancellationToken) =>
        repository.LoadAccountingEntriesAsync(batchId, cancellationToken);

    public Task<long> SaveBatchAsync(
        string storeCode, DateOnly businessDate, long reportGenerationId,
        AccountingBatchDraft batch, CancellationToken cancellationToken) =>
        repository.SaveAccountingBatchAsync(storeCode, businessDate, reportGenerationId, batch, cancellationToken);

    public Task ApproveBatchAsync(long batchId, string reason, CancellationToken cancellationToken) =>
        repository.ApproveAccountingBatchAsync(batchId, reason, cancellationToken);

    public Task<long> CreateMappingApprovalAsync(
        string eventCode, object payload, string storeCode, DateOnly businessDate,
        CancellationToken cancellationToken) =>
        repository.CreateApprovalAsync(
            "ACCOUNTING_MAPPING", "AccountingMapping", eventCode, payload,
            storeCode, businessDate, cancellationToken);

    public Task DecideApprovalAsync(long approvalId, string reason, CancellationToken cancellationToken) =>
        repository.DecideApprovalAsync(approvalId, true, reason, cancellationToken);

    public Task SaveMappingAsync(
        long approvalId, string eventCode, string debitLedger, string creditLedger,
        string narration, string storeCode, DateOnly effectiveFrom,
        CancellationToken cancellationToken) =>
        repository.SaveAccountingMappingAsync(
            approvalId, eventCode, debitLedger, creditLedger, narration,
            storeCode, effectiveFrom, cancellationToken);

    public Task RecordExportAsync(long batchId, string sha256, CancellationToken cancellationToken) =>
        repository.RecordAccountingExportAsync(batchId, sha256, cancellationToken);
}
