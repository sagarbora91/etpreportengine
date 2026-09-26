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
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
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
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
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
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
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
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        return (await gateway.LoadBatchesAsync(cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<App.AccountingEntry>> LoadEntriesAsync(
        long batchId,
        CancellationToken cancellationToken = default)
    {
        ValidateBatchId(batchId);
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
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
        if (command.Batch.Entries.Any(e => e.DebitAmount < 0 || e.CreditAmount < 0 || e.DebitAmount > 0 && e.CreditAmount > 0)
            || command.Batch.DebitTotal != command.Batch.CreditTotal
            || command.Batch.Entries.Sum(e => e.DebitAmount) != command.Batch.DebitTotal
            || command.Batch.Entries.Sum(e => e.CreditAmount) != command.Batch.CreditTotal)
            throw new InvalidOperationException("Accounting entry totals must balance before saving.");
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
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

    public async Task RejectAsync(App.RejectAccountingBatch command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateBatchId(command.BatchId);
        ValidateReason(command.Reason, "Enter an accounting rejection reason.");
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        await gateway.RejectBatchAsync(command.BatchId, command.Reason, cancellationToken).ConfigureAwait(false);
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

        await gateway.ApproveMappingAsync(command with { BusinessEvent = command.BusinessEvent.Trim().ToUpperInvariant() },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<App.AccountingExportReceipt> ExportAsync(
        App.ExportAccountingBatch command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateBatchId(command.BatchId);
        ValidateRequired(command.OutputPath, "An export path is required.");
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);

        var destination = await gateway.LoadDestinationAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(destination.CompanyName)) throw new InvalidOperationException("Tally company not decided (D12). Set it in Settings.");
        if (destination.EnvironmentLabel != "TEST") throw new InvalidOperationException("Live Tally export is not enabled (D18 / Phase 7). Select TEST in Settings.");
        if (!string.IsNullOrWhiteSpace(command.CompanyName) && !string.Equals(command.CompanyName, destination.CompanyName, StringComparison.Ordinal))
            throw new InvalidOperationException("The requested company differs from Settings. Refresh the accounting screen.");
        var batch = (await gateway.LoadBatchesAsync(cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(row => row.Id == command.BatchId)
            ?? throw new InvalidOperationException("The accounting batch was not found.");
        if (!string.Equals(batch.Status, "APPROVED_READY", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Approve the accounting batch before exporting it.");

        var entries = await gateway.LoadEntriesAsync(command.BatchId, cancellationToken).ConfigureAwait(false);
        var draft = new App.AccountingBatchDraft(
            entries.Select(Map).ToArray(),
            batch.DebitTotal,
            batch.CreditTotal,
            batch.DebitTotal == batch.CreditTotal,
            []);
        App.AccountingBatchControls.EnsureBalancedAndComplete(draft);
        return await gateway.ExportBatchAsync(command.BatchId, command.OutputPath, destination,
            token => exportTally(command.OutputPath, destination.CompanyName, batch.BusinessDate, Map(draft), token),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<App.AccountingDestination> LoadDestinationAsync(CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        return await gateway.LoadDestinationAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveDestinationAsync(App.SaveAccountingDestination command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        ValidateReason(command.Reason, "Enter a Tally settings change reason.");
        if (command.EnvironmentLabel is not ("TEST" or "PRODUCTION")) throw new ArgumentException("Choose TEST or PRODUCTION.");
        if (command.EnvironmentLabel == "PRODUCTION" && (string.IsNullOrWhiteSpace(command.CompanyName) || command.CompanyConfirmation != command.CompanyName.Trim()))
            throw new ArgumentException("Type the exact company name to confirm PRODUCTION.");
        await gateway.SaveDestinationAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<App.AccountingExportReceipt>> LoadExportHistoryAsync(CancellationToken cancellationToken = default)
    {
        await RequireOwnerAsync(cancellationToken).ConfigureAwait(false);
        return await gateway.LoadExportHistoryAsync(cancellationToken).ConfigureAwait(false);
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
            source.ApprovedBy, source.ExportedUtc, source.CreatedUtc, source.BlockingReason);

    internal static AccountingEntryDraft Map(App.AccountingEntry source) =>
        new(source.LineNumber, source.BusinessEvent, source.LedgerName, source.DebitAmount,
            source.CreditAmount, source.Narration, source.CostCentre, source.SourceReference);

    internal static AccountingBatchDraft Map(App.AccountingBatchDraft source) =>
        new(source.Entries.Select(Map).ToArray(), source.DebitTotal, source.CreditTotal,
            source.IsBalanced, source.MissingMappings);

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
    Task RejectBatchAsync(long batchId, string reason, CancellationToken cancellationToken);
    Task ApproveMappingAsync(App.ApproveAccountingMapping command, CancellationToken cancellationToken);
    Task<App.AccountingDestination> LoadDestinationAsync(CancellationToken cancellationToken);
    Task SaveDestinationAsync(App.SaveAccountingDestination command, CancellationToken cancellationToken);
    Task<IReadOnlyList<App.AccountingExportReceipt>> LoadExportHistoryAsync(CancellationToken cancellationToken);
    Task<App.AccountingExportReceipt> ExportBatchAsync(long batchId, string outputPath, App.AccountingDestination destination,
        Func<CancellationToken,Task<string>> writeFile, CancellationToken cancellationToken);
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

    public Task RejectBatchAsync(long batchId, string reason, CancellationToken cancellationToken) =>
        repository.RejectAccountingBatchAsync(batchId, reason, cancellationToken);

    public Task ApproveMappingAsync(App.ApproveAccountingMapping command, CancellationToken cancellationToken) =>
        repository.ApproveAccountingMappingAsync(command, cancellationToken);

    public Task<App.AccountingDestination> LoadDestinationAsync(CancellationToken token) => repository.LoadAccountingDestinationAsync(token);
    public Task SaveDestinationAsync(App.SaveAccountingDestination command, CancellationToken token) => repository.SaveAccountingDestinationAsync(command,token);
    public Task<IReadOnlyList<App.AccountingExportReceipt>> LoadExportHistoryAsync(CancellationToken token) => repository.LoadAccountingExportHistoryAsync(token);
    public Task<App.AccountingExportReceipt> ExportBatchAsync(long batchId, string outputPath, App.AccountingDestination destination,
        Func<CancellationToken,Task<string>> writeFile, CancellationToken token) => repository.ExportAccountingBatchAsync(batchId,outputPath,destination,writeFile,token);
}
