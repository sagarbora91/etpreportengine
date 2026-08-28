using Etp.Reporting.Application.Registers;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class SqlServerDigitalRegisterService : IDigitalRegisterService
{
    private readonly Func<string?, int, CancellationToken, Task<IReadOnlyList<RegisterEntryRow>>> load;
    private readonly Func<RegisterEntryRow, string, CancellationToken, Task<long>> save;

    public SqlServerDigitalRegisterService(string connectionString)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(
            connectionString,
            nameof(connectionString));
        var repository = new ProductisationRepository(validated);
        load = repository.LoadRegisterEntriesAsync;
        save = repository.SaveRegisterEntryAsync;
    }

    internal SqlServerDigitalRegisterService(
        Func<string?, int, CancellationToken, Task<IReadOnlyList<RegisterEntryRow>>> load,
        Func<RegisterEntryRow, string, CancellationToken, Task<long>> save)
    {
        this.load = load ?? throw new ArgumentNullException(nameof(load));
        this.save = save ?? throw new ArgumentNullException(nameof(save));
    }

    public async Task<IReadOnlyList<DigitalRegisterEntry>> LoadAsync(
        string? search = null,
        int limit = 500,
        CancellationToken cancellationToken = default) =>
        (await load(search, limit, cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();

    public Task<long> SaveAsync(
        DigitalRegisterEntryDraft entry,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return save(Map(entry), reason, cancellationToken);
    }

    internal static DigitalRegisterEntry Map(RegisterEntryRow row) =>
        new(
            row.Id,
            row.RegisterType,
            row.SourceDocumentId,
            row.StoreCode,
            row.BusinessDate,
            row.DocumentNumber,
            row.DocumentDate,
            row.Counterparty,
            row.Quantity,
            row.Amount,
            row.Reference,
            row.ReceivedBy,
            row.VerificationStatus,
            row.Remarks,
            row.ModifiedBy,
            row.ModifiedUtc);

    internal static RegisterEntryRow Map(DigitalRegisterEntryDraft draft) =>
        new(
            0,
            draft.RegisterType,
            draft.SourceDocumentId,
            draft.StoreCode,
            draft.BusinessDate,
            draft.DocumentNumber,
            draft.DocumentDate,
            draft.Counterparty,
            draft.Quantity,
            draft.Amount,
            draft.Reference,
            draft.ReceivedBy,
            draft.VerificationStatus,
            draft.Remarks,
            string.Empty,
            DateTime.MinValue);
}
