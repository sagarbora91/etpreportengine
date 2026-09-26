using Etp.Reporting.Application.Registers;

namespace Etp.Reporting.Infrastructure.SqlServer;

public sealed class SqlServerDigitalRegisterService : IDigitalRegisterService
{
    private readonly Func<string?, int, string?, CancellationToken, Task<IReadOnlyList<RegisterEntryRow>>> load;
    private readonly Func<RegisterEntryRow, string, CancellationToken, Task<long>> save;
    private readonly Func<CancellationToken, Task<ApplicationAccess>> loadAccess;

    public SqlServerDigitalRegisterService(string connectionString)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(
            connectionString,
            nameof(connectionString));
        var repository = new ProductisationRepository(validated);
        load = (search, limit, type, token) => repository.LoadRegisterEntriesAsync(search, limit, token, registerType: type);
        save = repository.SaveRegisterEntryAsync;
        loadAccess = new Phase2OperationsRepository(validated).LoadCurrentAccessAsync;
    }

    internal SqlServerDigitalRegisterService(
        Func<string?, int, string?, CancellationToken, Task<IReadOnlyList<RegisterEntryRow>>> load,
        Func<RegisterEntryRow, string, CancellationToken, Task<long>> save,
        Func<CancellationToken, Task<ApplicationAccess>> loadAccess)
    {
        this.load = load ?? throw new ArgumentNullException(nameof(load));
        this.save = save ?? throw new ArgumentNullException(nameof(save));
        this.loadAccess = loadAccess ?? throw new ArgumentNullException(nameof(loadAccess));
    }

    public static async Task<IReadOnlyList<DigitalRegisterEntry>> LoadDayAsync(string connectionString, string storeCode, DateOnly date, CancellationToken token = default)
    {
        var validated = SqlAdapterConnection.RequireWindowsIntegrated(connectionString, nameof(connectionString));
        await RequireViewAsync(new Phase2OperationsRepository(validated).LoadCurrentAccessAsync, token).ConfigureAwait(false);
        return (await new ProductisationRepository(validated)
            .LoadRegisterEntriesAsync(null, 2000, token, storeCode, date).ConfigureAwait(false)).Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<DigitalRegisterEntry>> LoadAsync(
        string? search = null,
        int limit = 500,
        CancellationToken cancellationToken = default,
        string? registerType = null)
    {
        await RequireViewAsync(loadAccess, cancellationToken).ConfigureAwait(false);
        return (await load(search, limit, registerType, cancellationToken).ConfigureAwait(false)).Select(Map).ToArray();
    }

    public async Task<long> SaveAsync(
        DigitalRegisterEntryDraft entry,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var status = entry.VerificationStatus?.Trim().ToUpperInvariant();
        if (status is not ("DRAFT" or "VERIFIED")) throw new ArgumentException("Choose Draft or Verified.", nameof(entry));
        var access = await loadAccess(cancellationToken).ConfigureAwait(false);
        if (!access.CanImport) throw new UnauthorizedAccessException("Owner or Store Manager permission is required.");
        if (status == "VERIFIED" && !access.CanAdminister)
            throw new UnauthorizedAccessException("Owner permission is required to verify a register entry.");
        return await save(Map(entry with { VerificationStatus = status }), reason, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RequireViewAsync(Func<CancellationToken, Task<ApplicationAccess>> loadAccess, CancellationToken token)
    {
        if (!(await loadAccess(token).ConfigureAwait(false)).CanView)
            throw new UnauthorizedAccessException("This Windows account does not have application access.");
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
